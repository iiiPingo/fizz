using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Fizz
{
    /// <summary>
    /// Runs Intel PresentMon hidden in the background and feeds its CSV output into <see cref="FrameStats"/>.
    /// Self-healing: restarts with backoff if it exits, restarts if it stalls, and dies with Fizz (job object).
    /// </summary>
    internal sealed class PresentMonService : IDisposable
    {
        public const string SessionName = "Fizz_PresentMon";
        readonly FrameStats stats;
        readonly object gate = new object();
        Process proc;
        Thread reader;
        IntPtr job;
        long lastDataTicks;
        DateTime nextStartUtc = DateTime.MinValue, startedUtc, lastStallRestartUtc = DateTime.MinValue;
        int failures;
        volatile bool accessDenied;
        bool disposed;
        public string Header { get; private set; }
        public long Rows { get; private set; }
        public string Problem { get; private set; }   // null when fine
        public bool Unavailable { get; private set; }  // won't retry (missing exe / no permission)

        public PresentMonService(FrameStats stats) { this.stats = stats; }

        public static string ExePath
        {
            get
            {
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
                    if (!Directory.Exists(dir)) return null;
                    return Directory.GetFiles(dir, "PresentMon*.exe").OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                }
                catch { return null; }
            }
        }

        public bool Running
        {
            get { lock (gate) { try { return proc != null && !proc.HasExited; } catch { return false; } } }
        }

        /// <summary>Called ~2x/s from the sampler thread.</summary>
        public void Ensure(bool wanted, bool gameInFront)
        {
            lock (gate)
            {
                if (disposed) return;
                if (!wanted) { if (proc != null) StopLocked("idle"); return; }
                if (Unavailable) return;

                bool alive;
                try { alive = proc != null && !proc.HasExited; } catch { alive = false; }

                if (proc != null && !alive)
                {
                    int code = -1;
                    try { code = proc.ExitCode; } catch { }
                    CleanupLocked();
                    if (accessDenied)
                    {
                        Unavailable = true;
                        Problem = "FPS needs Fizz to run as admin (PresentMon: access denied)";
                        Log.Warn(Problem);
                        return;
                    }
                    failures++;
                    int delay = Math.Min(60, 1 << Math.Min(failures, 6));
                    nextStartUtc = DateTime.UtcNow.AddSeconds(delay);
                    Problem = "PresentMon exited (code " + code + "), retrying in " + delay + " s";
                    Log.Throttled("pm-exit", Problem, 30);
                    return;
                }

                if (alive)
                {
                    if ((DateTime.UtcNow - startedUtc).TotalSeconds > 120) failures = 0;
                    long now = Stopwatch.GetTimestamp();
                    double silentS = (now - Interlocked.Read(ref lastDataTicks)) / (double)Stopwatch.Frequency;
                    // A game is in front but PresentMon has said nothing for 20 s: assume it's wedged.
                    if (gameInFront && silentS > 20 && (DateTime.UtcNow - startedUtc).TotalSeconds > 20 &&
                        (DateTime.UtcNow - lastStallRestartUtc).TotalSeconds > 60)
                    {
                        lastStallRestartUtc = DateTime.UtcNow;
                        Log.Warn("PresentMon silent for " + (int)silentS + " s with a game in front; restarting it");
                        StopLocked("stall");
                        nextStartUtc = DateTime.UtcNow;
                    }
                    return;
                }

                if (DateTime.UtcNow < nextStartUtc) return;
                StartLocked();
            }
        }

        void StartLocked()
        {
            string exe = ExePath;
            if (exe == null)
            {
                Unavailable = true;
                Problem = "PresentMon is missing (expected in the tools folder next to Fizz.exe)";
                Log.Warn(Problem);
                return;
            }
            try
            {
                accessDenied = false;
                var psi = new ProcessStartInfo(exe,
                    "--output_stdout --no_console_stats --session_name " + SessionName +
                    " --stop_existing_session --exclude dwm.exe --no_track_input --no_track_gpu")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = false };
                p.ErrorDataReceived += OnStdErr;
                p.Start();
                AddToJob(p);
                try { p.PriorityClass = ProcessPriorityClass.Normal; } catch { }
                p.BeginErrorReadLine();
                proc = p;
                startedUtc = DateTime.UtcNow;
                Interlocked.Exchange(ref lastDataTicks, Stopwatch.GetTimestamp());
                var stream = p.StandardOutput.BaseStream;
                reader = new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "Fizz.PresentMon.Reader", Priority = ThreadPriority.AboveNormal };
                reader.Start();
                Problem = null;
                Log.Info("PresentMon started: " + Path.GetFileName(exe) + " pid " + p.Id);
            }
            catch (Exception ex)
            {
                CleanupLocked();
                failures++;
                nextStartUtc = DateTime.UtcNow.AddSeconds(Math.Min(60, 1 << Math.Min(failures, 6)));
                Problem = "Couldn't start PresentMon: " + ex.Message;
                Log.Error("PresentMon.Start", ex);
            }
        }

        void OnStdErr(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            if (e.Data.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0) accessDenied = true;
            Log.Throttled("pm-stderr:" + (e.Data.Length > 40 ? e.Data.Substring(0, 40) : e.Data), "PresentMon: " + e.Data, 300);
        }

        void ReadLoop(Stream s)
        {
            var parser = new PmCsvParser();
            var splitter = new LineSplitter();
            var buf = new byte[1 << 16];
            LineSplitter.LineHandler onLine = (b, start, len) =>
            {
                PmRow row;
                try
                {
                    if (parser.ParseLine(b, start, len, out row)) stats.Add(row, Stopwatch.GetTimestamp());
                    else if (parser.HeaderText != null && Header == null) { Header = parser.HeaderText; Log.Info("PresentMon header: " + Header); }
                }
                catch (Exception ex) { Log.Error("PresentMon.Parse", ex); }
            };
            try
            {
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    Interlocked.Exchange(ref lastDataTicks, Stopwatch.GetTimestamp());
                    splitter.Feed(buf, n, onLine);
                    Rows = parser.Rows;
                }
            }
            catch (Exception ex) { Log.Throttled("pm-read", "PresentMon reader ended: " + ex.Message, 60); }
        }

        void AddToJob(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = Native.CreateJobObject(IntPtr.Zero, null);
                    var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref info,
                        Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
                }
                Native.AssignProcessToJobObject(job, p.Handle);
            }
            catch (Exception ex) { Log.Error("PresentMon.Job", ex); }
        }

        void StopLocked(string why)
        {
            if (proc == null) return;
            try { if (!proc.HasExited) { proc.Kill(); proc.WaitForExit(1500); } } catch { }
            CleanupLocked();
            try { Native.StopEtwSession(SessionName); } catch { }
            Log.Info("PresentMon stopped (" + why + ")");
        }

        void CleanupLocked()
        {
            try { if (proc != null) { proc.ErrorDataReceived -= OnStdErr; proc.Dispose(); } } catch { }
            proc = null;
            reader = null;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                StopLocked("exit");
                if (job != IntPtr.Zero) { Native.CloseHandle(job); job = IntPtr.Zero; }
            }
        }
    }
}
