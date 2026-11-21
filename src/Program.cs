using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: System.Reflection.AssemblyTitle("Fizz")]
[assembly: System.Reflection.AssemblyProduct("Fizz")]
[assembly: System.Reflection.AssemblyDescription("Fizz - in-game performance overlay")]
[assembly: System.Reflection.AssemblyVersion("1.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.1.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Fizz
{
    internal static class Program
    {
        public const string HubTitle = "Fizz.Hub";
        public const string OpenSettingsMessage = "Fizz.OpenSettings";
        public const string ExitMessage = "Fizz.Exit";   // "Fizz.exe --exit": close cleanly (Minibar's Stop / updates)
        public const int ExitRestart = 3;

        [STAThread]
        static int Main(string[] args)
        {
            // Belt and braces with the manifest: render at the monitor's real DPI (crisp text, no bitmap stretching).
            try { Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
            try
            {
                if (args.Contains("--child")) return ChildMain(args);
                if (args.Contains("--dump-sensors")) return DumpSensors();
                return Launcher.Run(args);
            }
            catch (Exception ex)
            {
                Log.Crash("Main", ex);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int ChildMain(string[] args) { return ChildHost.Run(args); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int DumpSensors()
        {
            string path = Path.Combine(Log.Dir, "sensors.txt");
            string text;
            try
            {
                text = "Fizz sensor dump " + DateTime.Now + Environment.NewLine +
                       "Elevated: " + Startup.IsElevated + "   PawnIO installed: " + Startup.PawnIoInstalled + Environment.NewLine + Environment.NewLine +
                       LhmBackend.DumpAll();
            }
            catch (Exception ex) { text = "Sensor dump failed: " + ex; }
            File.WriteAllText(path, text);
            return 0;
        }

        public static IntPtr FindHub() { return Native.FindWindowEx(Native.HWND_MESSAGE, IntPtr.Zero, null, HubTitle); }
    }

    /// <summary>
    /// The process you start. Handles elevation, then babysits the real app (the "child"):
    /// restarts it if it crashes or hangs, gives up politely if it crash-loops.
    /// Deliberately uses no WinForms so it stays tiny.
    /// </summary>
    internal static class Launcher
    {
        public static int Run(string[] args)
        {
            if (args.Contains("--exit"))
            {
                // ask a running Fizz to close (the hub accepts this from a non-admin process); the watchdog then stops too
                IntPtr h = Program.FindHub();
                if (h != IntPtr.Zero) Native.PostMessage(h, Native.RegisterWindowMessage(Program.ExitMessage), IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            bool fromTask = args.Contains("--task");
            bool replace = args.Contains("--replace");

            if (replace)
            {
                for (int i = 0; i < 80 && Program.FindHub() != IntPtr.Zero; i++) Thread.Sleep(100);
            }

            // Already running? Just open its settings.
            IntPtr hub = Program.FindHub();
            if (hub != IntPtr.Zero)
            {
                Native.PostMessage(hub, Native.RegisterWindowMessage(Program.OpenSettingsMessage), IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            bool createdNew;
            Mutex wdMutex;
            try { wdMutex = new Mutex(true, @"Local\Fizz.Watchdog", out createdNew); }
            catch (UnauthorizedAccessException) { return 0; } // an elevated watchdog owns it
            if (!createdNew) return 0;

            try
            {
                if (!Startup.IsElevated && !fromTask && !args.Contains("--noelevate"))
                {
                    // Prefer the startup task: elevated with no UAC prompt.
                    wdMutex.ReleaseMutex(); wdMutex.Dispose(); wdMutex = null;
                    if (Startup.TaskExists() && Startup.RunTask())
                    {
                        for (int i = 0; i < 60; i++) { Thread.Sleep(100); if (Program.FindHub() != IntPtr.Zero) return 0; }
                    }
                    if (Startup.RelaunchElevated("--elevated")) return 0;
                    // UAC declined: carry on without admin (limited mode).
                    wdMutex = new Mutex(true, @"Local\Fizz.Watchdog", out createdNew);
                    if (!createdNew) return 0;
                }
                return Watch();
            }
            finally
            {
                if (wdMutex != null) { try { wdMutex.ReleaseMutex(); } catch { } wdMutex.Dispose(); }
            }
        }

        static int Watch()
        {
            string exe = Startup.ExePath;
            var crashes = new List<DateTime>();
            string hbName = @"Local\Fizz.Heartbeat." + Process.GetCurrentProcess().Id;
            using (var mmf = MemoryMappedFile.CreateNew(hbName, 16))
            using (var hb = mmf.CreateViewAccessor(0, 16))
            {
                while (true)
                {
                    hb.Write(0, DateTime.UtcNow.Ticks);
                    Process child;
                    try
                    {
                        child = Process.Start(new ProcessStartInfo(exe, "--child --hb " + hbName)
                        {
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(exe)
                        });
                    }
                    catch (Exception ex) { Log.Crash("Watchdog.Start", ex); return 1; }

                    int code = WaitForChild(child, hb);
                    if (code == 0) return 0;              // user chose Exit
                    if (code == Program.ExitRestart) { Thread.Sleep(300); continue; }

                    DateTime now = DateTime.UtcNow;
                    crashes.Add(now);
                    crashes.RemoveAll(t => (now - t).TotalMinutes > 10);
                    Log.Crash("Watchdog", "Fizz stopped unexpectedly (exit code " + code + "); restart #" + crashes.Count);
                    if (crashes.Count >= 5)
                    {
                        Native.MessageBox(IntPtr.Zero,
                            "Fizz crashed 5 times in 10 minutes, so it stopped restarting itself.\n\nDetails are in:\n" + Log.CrashPath,
                            "Fizz", 0x30);
                        return 1;
                    }
                    Thread.Sleep(1000 * Math.Min(10, 2 * crashes.Count));
                }
            }
        }

        /// <summary>Waits for the child. Kills it if its UI thread stops updating the heartbeat (frozen).</summary>
        static int WaitForChild(Process child, MemoryMappedViewAccessor hb)
        {
            var started = Stopwatch.StartNew();
            var loop = Stopwatch.StartNew();
            int staleStrikes = 0;
            while (!child.WaitForExit(5000))
            {
                long gapMs = loop.ElapsedMilliseconds;
                loop.Restart();
                if (gapMs > 15000) { staleStrikes = 0; continue; } // we were asleep (sleep/hibernate): give it time

                double ageS = (DateTime.UtcNow - new DateTime(hb.ReadInt64(0), DateTimeKind.Utc)).TotalSeconds;
                if (started.Elapsed.TotalSeconds > 60 && ageS > 45) staleStrikes++;
                else staleStrikes = 0;

                if (staleStrikes >= 3)
                {
                    Log.Crash("Watchdog", "Fizz UI froze (no heartbeat for " + (int)ageS + " s); restarting it");
                    try { child.Kill(); child.WaitForExit(5000); } catch { }
                    return 99;
                }
            }
            int code;
            try { code = child.ExitCode; } catch { code = -1; }
            child.Dispose();
            return code;
        }
    }
}
