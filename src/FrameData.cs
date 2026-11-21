using System;
using System.Collections.Generic;
using System.Text;

namespace Fizz
{
    internal struct PmRow
    {
        public int Pid;
        public ulong Swap;
        public float FrameMs;
        public byte Mode;
    }

    /// <summary>
    /// Parses PresentMon CSV lines straight from bytes (no per-line string allocations).
    /// Columns are located by header name, so v1/v2/v3 metric layouts and future column additions all work.
    /// </summary>
    internal sealed class PmCsvParser
    {
        int idxPid = -1, idxSwap = -1, idxMode = -1, idxFt = -1, headerCount;
        readonly int[] starts = new int[160];
        readonly int[] ends = new int[160];
        public bool HasHeader { get { return idxPid >= 0 && idxFt >= 0; } }
        public string HeaderText { get; private set; }
        public long Rows, BadRows;

        static readonly byte[][] ModeBytes = BuildModeBytes();
        static byte[][] BuildModeBytes()
        {
            var r = new byte[PresentModes.Full.Length][];
            for (int i = 0; i < r.Length; i++) r[i] = Encoding.ASCII.GetBytes(PresentModes.Full[i]);
            return r;
        }

        static readonly byte[] AppPrefix = Encoding.ASCII.GetBytes("Application,");

        /// <summary>Parse one line (without newline). Returns true when a frame row was produced.</summary>
        public bool ParseLine(byte[] b, int start, int len, out PmRow row)
        {
            row = default(PmRow);
            while (len > 0 && (b[start + len - 1] == '\r' || b[start + len - 1] == 0)) len--;
            if (len <= 0) return false;

            if (StartsWith(b, start, len, AppPrefix)) { ReadHeader(Encoding.ASCII.GetString(b, start, len)); return false; }
            if (!HasHeader) return false;

            int n = Split(b, start, len);
            if (n < headerCount) { BadRows++; return false; }
            int shift = n - headerCount; // an exe name containing commas pushes everything right

            int pid;
            if (!ParseInt(b, F(idxPid, shift), out pid)) { BadRows++; return false; }
            float ft = ParseFloat(b, F(idxFt, shift));
            if (float.IsNaN(ft) || ft <= 0 || ft > 60000) { BadRows++; return false; }

            row.Pid = pid;
            row.FrameMs = ft;
            row.Swap = idxSwap >= 0 ? ParseHex(b, F(idxSwap, shift)) : 0;
            row.Mode = idxMode >= 0 ? MatchMode(b, F(idxMode, shift)) : PresentModes.Unknown;
            Rows++;
            return true;
        }

        int F(int idx, int shift) { return idx == 0 ? 0 : idx + shift; }

        void ReadHeader(string line)
        {
            HeaderText = line;
            var cols = line.Split(',');
            headerCount = cols.Length;
            idxPid = idxSwap = idxMode = idxFt = -1;
            int ftPriority = 99;
            for (int i = 0; i < cols.Length; i++)
            {
                string c = cols[i].Trim();
                if (c.Equals("ProcessID", StringComparison.OrdinalIgnoreCase)) idxPid = i;
                else if (c.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase)) idxSwap = i;
                else if (c.Equals("PresentMode", StringComparison.OrdinalIgnoreCase)) idxMode = i;
                else
                {
                    // Present-to-present time: v1/v3 "MsBetweenPresents", v2 "FrameTime", newer "MsBetweenAppStart".
                    int pr = Is(c, "MsBetweenPresents") ? 0 : Is(c, "FrameTime") ? 1 : Is(c, "MsBetweenAppStart") ? 2 : 99;
                    if (pr < ftPriority) { ftPriority = pr; idxFt = i; }
                }
            }
        }

        static bool Is(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        int Split(byte[] b, int start, int len)
        {
            int n = 0, s = start, end = start + len;
            for (int i = start; i < end && n < starts.Length - 1; i++)
            {
                if (b[i] == ',') { starts[n] = s; ends[n] = i; n++; s = i + 1; }
            }
            starts[n] = s; ends[n] = end; n++;
            return n;
        }

        bool ParseInt(byte[] b, int field, out int v)
        {
            v = 0;
            int i = starts[field], e = ends[field];
            while (i < e && b[i] == ' ') i++;
            if (i >= e) return false;
            for (; i < e; i++)
            {
                int d = b[i] - '0';
                if (d < 0 || d > 9) return false;
                v = v * 10 + d;
            }
            return true;
        }

        ulong ParseHex(byte[] b, int field)
        {
            int i = starts[field], e = ends[field];
            if (e - i > 2 && b[i] == '0' && (b[i + 1] == 'x' || b[i + 1] == 'X')) i += 2;
            ulong v = 0;
            for (; i < e; i++)
            {
                int c = b[i], d;
                if (c >= '0' && c <= '9') d = c - '0';
                else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                else break;
                v = (v << 4) | (uint)d;
            }
            return v;
        }

        float ParseFloat(byte[] b, int field)
        {
            int i = starts[field], e = ends[field];
            while (i < e && b[i] == ' ') i++;
            if (i >= e) return float.NaN;
            bool neg = false;
            if (b[i] == '-') { neg = true; i++; }
            double v = 0, scale = 1;
            bool any = false, frac = false;
            for (; i < e; i++)
            {
                int c = b[i];
                if (c >= '0' && c <= '9')
                {
                    any = true;
                    if (frac) { scale *= 0.1; v += (c - '0') * scale; }
                    else v = v * 10 + (c - '0');
                }
                else if (c == '.' && !frac) frac = true;
                else if (c == 'e' || c == 'E')
                {
                    int ex = 0; bool en = false; i++;
                    if (i < e && (b[i] == '-' || b[i] == '+')) { en = b[i] == '-'; i++; }
                    for (; i < e && b[i] >= '0' && b[i] <= '9'; i++) ex = ex * 10 + (b[i] - '0');
                    v *= Math.Pow(10, en ? -ex : ex);
                    break;
                }
                else return float.NaN; // "NA" and anything unexpected
            }
            if (!any) return float.NaN;
            return (float)(neg ? -v : v);
        }

        byte MatchMode(byte[] b, int field)
        {
            int i = starts[field], len = ends[field] - i;
            for (byte m = 1; m < ModeBytes.Length; m++)
            {
                var mb = ModeBytes[m];
                if (mb.Length != len) continue;
                bool eq = true;
                for (int k = 0; k < len; k++) if (b[i + k] != mb[k]) { eq = false; break; }
                if (eq) return m;
            }
            return PresentModes.Unknown;
        }

        static bool StartsWith(byte[] b, int start, int len, byte[] prefix)
        {
            if (len < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (b[start + i] != prefix[i]) return false;
            return true;
        }
    }

    /// <summary>
    /// Splits a byte stream into lines. Handles CRLF, partial reads, and UTF-16LE output
    /// (PresentMon switches to UTF-16 when writing to a console or file).
    /// </summary>
    internal sealed class LineSplitter
    {
        readonly byte[] buf = new byte[1 << 17];
        int used;
        int utf16 = -1; // -1 unknown, 0 no, 1 yes
        int carry = -1;  // odd byte left over between UTF-16 reads
        public delegate void LineHandler(byte[] b, int start, int len);

        public void Feed(byte[] data, int count, LineHandler onLine)
        {
            int i = 0;
            if (utf16 < 0 && count >= 2)
            {
                bool bom = data[0] == 0xFF && data[1] == 0xFE;
                utf16 = bom || (data[1] == 0 && data[0] != 0) ? 1 : 0;
                if (bom) i = 2;
            }
            if (utf16 == 1)
            {
                // Keep the low byte of each UTF-16 code unit (all the fields we parse are ASCII).
                for (; i < count; i++)
                {
                    if (carry < 0) { carry = data[i]; continue; }
                    byte lo = (byte)carry, hi = data[i];
                    carry = -1;
                    Push(hi != 0 ? (byte)'?' : lo, onLine);
                }
                return;
            }
            for (; i < count; i++) Push(data[i], onLine);
        }

        void Push(byte c, LineHandler onLine)
        {
            if (c == (byte)'\n')
            {
                onLine(buf, 0, used);
                used = 0;
                return;
            }
            if (used < buf.Length) buf[used++] = c;
            else used = 0; // absurdly long line: drop it
        }
    }

    internal struct FrameResult
    {
        public float Fps, Low1, FrameTime;
        public byte Mode;
        public int Frames1s;
    }

    /// <summary>
    /// Rolling frame statistics for the target (foreground game) process.
    /// Fixed-size ring buffers: no allocations after construction.
    /// </summary>
    internal sealed class FrameStats
    {
        const int Cap = 16384;
        readonly float[] ft = new float[Cap];
        readonly long[] at = new long[Cap];
        readonly ulong[] sw = new ulong[Cap];
        readonly byte[] md = new byte[Cap];
        int head, count;
        readonly object gate = new object();
        readonly float[] scratch = new float[Cap];
        readonly ulong[] swIds = new ulong[8];
        readonly int[] swCounts = new int[8];
        int targetPid;
        public readonly long Freq = System.Diagnostics.Stopwatch.Frequency;

        // Which processes are presenting (for "is this foreground window a game?").
        const int PidSlots = 64;
        readonly int[] pids = new int[PidSlots];
        readonly long[] pidLast = new long[PidSlots];
        readonly int[] pidCount = new int[PidSlots];
        readonly long[] pidWindowStart = new long[PidSlots];
        readonly byte[] pidMode = new byte[PidSlots];
        readonly int[] pidPrevCount = new int[PidSlots];

        // Whole-session numbers for the target (session summary).
        public readonly SessionStats Session = new SessionStats();

        public int TargetPid { get { lock (gate) return targetPid; } }

        public void ResetSession(string game) { lock (gate) Session.Reset(game); }
        public void AddSessionTemps(float gpu, float cpu) { lock (gate) Session.AddTemps(gpu, cpu); }

        public SessionSummary GetSession()
        {
            lock (gate)
            {
                return new SessionSummary
                {
                    Game = Session.Game, Started = Session.Started, Frames = Session.Frames, AvgFps = Session.AvgFps, Low1 = Session.Low1,
                    MaxGpuTemp = Session.MaxGpuTemp, MaxCpuTemp = Session.MaxCpuTemp, AvgGpuTemp = Session.AvgGpuTemp, AvgCpuTemp = Session.AvgCpuTemp
                };
            }
        }

        public void SetTarget(int pid)
        {
            lock (gate)
            {
                if (pid == targetPid) return;
                targetPid = pid;
                head = count = 0;
            }
        }

        public void Add(PmRow r, long now)
        {
            lock (gate)
            {
                TouchPid(r.Pid, now, r.Mode);
                if (r.Pid != targetPid || targetPid == 0) return;
                ft[head] = r.FrameMs; at[head] = now; sw[head] = r.Swap; md[head] = r.Mode;
                head = (head + 1) % Cap;
                if (count < Cap) count++;
                Session.AddFrame(r.FrameMs);
            }
        }

        void TouchPid(int pid, long now, byte mode)
        {
            int free = -1, oldest = 0;
            for (int i = 0; i < PidSlots; i++)
            {
                if (pids[i] == pid)
                {
                    if (now - pidWindowStart[i] > Freq * 2)
                    {
                        pidPrevCount[i] = now - pidWindowStart[i] < Freq * 4 ? pidCount[i] : 0;
                        pidWindowStart[i] = now; pidCount[i] = 0;
                    }
                    pidCount[i]++; pidLast[i] = now; pidMode[i] = mode;
                    return;
                }
                if (pids[i] == 0 && free < 0) free = i;
                if (pidLast[i] < pidLast[oldest]) oldest = i;
            }
            int s = free >= 0 ? free : oldest;
            pids[s] = pid; pidLast[s] = now; pidWindowStart[s] = now; pidCount[s] = 1; pidMode[s] = mode; pidPrevCount[s] = 0;
        }

        /// <summary>True if <paramref name="pid"/> presented at least a few frames in the last ~2 s.</summary>
        public bool IsPresenting(int pid, long now)
        {
            lock (gate)
            {
                for (int i = 0; i < PidSlots; i++)
                    if (pids[i] == pid) return now - pidLast[i] < Freq * 2 && pidCount[i] >= 3;
                return false;
            }
        }

        /// <summary>Frames per second <paramref name="pid"/> has been presenting lately (0 if idle).</summary>
        public float PresentRate(int pid, long now)
        {
            lock (gate)
            {
                for (int i = 0; i < PidSlots; i++)
                {
                    if (pids[i] != pid) continue;
                    if (now - pidLast[i] > Freq) return 0;
                    double el = (now - pidWindowStart[i]) / (double)Freq;
                    float cur = el > 0.5 ? (float)(pidCount[i] / el) : 0;
                    return Math.Max(cur, pidPrevCount[i] / 2f);
                }
                return 0;
            }
        }

        public byte LastModeOf(int pid, long now)
        {
            lock (gate)
            {
                for (int i = 0; i < PidSlots; i++)
                    if (pids[i] == pid && now - pidLast[i] < Freq * 3) return pidMode[i];
                return PresentModes.Unknown;
            }
        }

        public FrameResult Compute(long now)
        {
            var res = new FrameResult { Fps = float.NaN, Low1 = float.NaN, FrameTime = float.NaN };
            lock (gate)
            {
                if (count == 0) return res;
                long t1 = now - Freq, t2 = now - Freq * 2, t10 = now - Freq * 10;

                // 1) dominant swap chain over the last 2 s (games with launcher/overlay swap chains)
                int nSw = 0;
                for (int k = 0, i = Prev(head); k < count; k++, i = Prev(i))
                {
                    if (at[i] < t2) break;
                    int j = 0;
                    while (j < nSw && swIds[j] != sw[i]) j++;
                    if (j == nSw) { if (nSw == swIds.Length) continue; swIds[nSw] = sw[i]; swCounts[nSw] = 0; nSw++; }
                    swCounts[j]++;
                }
                if (nSw == 0) return res; // nothing in 2 s: game paused / minimized
                int best = 0;
                for (int j = 1; j < nSw; j++) if (swCounts[j] > swCounts[best]) best = j;
                ulong dom = swIds[best];

                // 2) FPS + frametime over 1 s, 1% low over 10 s
                int n1 = 0, m = 0;
                double sum1 = 0;
                bool modeSet = false;
                for (int k = 0, i = Prev(head); k < count; k++, i = Prev(i))
                {
                    if (at[i] < t10) break;
                    if (sw[i] != dom) continue;
                    if (!modeSet) { res.Mode = md[i]; modeSet = true; }
                    if (at[i] >= t1) { n1++; sum1 += ft[i]; }
                    scratch[m++] = ft[i];
                }
                if (n1 == 0)
                {
                    // fewer than one frame per second: use the 2 s window
                    for (int k = 0, i = Prev(head); k < count; k++, i = Prev(i))
                    {
                        if (at[i] < t2) break;
                        if (sw[i] == dom) { n1++; sum1 += ft[i]; }
                    }
                }
                if (n1 > 0 && sum1 > 0)
                {
                    res.FrameTime = (float)(sum1 / n1);
                    res.Fps = (float)(1000.0 * n1 / sum1);
                    res.Frames1s = n1;
                }
                if (m > 0)
                {
                    // 1% low = average FPS of the slowest 1% of frames (so a few stutters actually show up)
                    Array.Sort(scratch, 0, m);
                    int worst = Math.Max(1, m / 100);
                    double sumWorst = 0;
                    for (int i = m - worst; i < m; i++) sumWorst += scratch[i];
                    if (sumWorst > 0) res.Low1 = (float)(1000.0 * worst / sumWorst);
                    if (!float.IsNaN(res.Fps) && res.Low1 > res.Fps) res.Low1 = res.Fps;
                }
            }
            return res;
        }

        int Prev(int i) { return i == 0 ? Cap - 1 : i - 1; }
    }

    internal sealed class SessionSummary
    {
        public string Game;
        public DateTime Started;
        public long Frames;
        public float AvgFps, Low1, MaxGpuTemp, MaxCpuTemp, AvgGpuTemp, AvgCpuTemp;
    }

    /// <summary>Whole-session frame stats with a fixed histogram (bounded memory however long you play).</summary>
    internal sealed class SessionStats
    {
        const float BinMs = 0.05f;
        const int Bins = 8000; // 0..400 ms
        readonly int[] hist = new int[Bins];
        public long Frames;
        public double TotalMs;
        public float MaxGpuTemp = float.NaN, MaxCpuTemp = float.NaN;
        double gpuSum, cpuSum;
        long gpuN, cpuN;
        public float AvgGpuTemp { get { return gpuN > 0 ? (float)(gpuSum / gpuN) : float.NaN; } }
        public float AvgCpuTemp { get { return cpuN > 0 ? (float)(cpuSum / cpuN) : float.NaN; } }
        public DateTime Started = DateTime.MinValue;
        public string Game = "";

        public void Reset(string game)
        {
            Array.Clear(hist, 0, Bins);
            Frames = 0; TotalMs = 0; MaxGpuTemp = MaxCpuTemp = float.NaN;
            gpuSum = cpuSum = 0; gpuN = cpuN = 0;
            Started = DateTime.Now; Game = game ?? "";
        }

        public void AddFrame(float ms)
        {
            Frames++; TotalMs += ms;
            int b = (int)(ms / BinMs);
            if (b >= Bins) b = Bins - 1;
            if (b < 0) b = 0;
            hist[b]++;
        }

        public void AddTemps(float gpu, float cpu)
        {
            if (!float.IsNaN(gpu)) { gpuSum += gpu; gpuN++; if (float.IsNaN(MaxGpuTemp) || gpu > MaxGpuTemp) MaxGpuTemp = gpu; }
            if (!float.IsNaN(cpu)) { cpuSum += cpu; cpuN++; if (float.IsNaN(MaxCpuTemp) || cpu > MaxCpuTemp) MaxCpuTemp = cpu; }
        }

        public float AvgFps { get { return Frames > 0 && TotalMs > 0 ? (float)(1000.0 * Frames / TotalMs) : float.NaN; } }

        /// <summary>1% low = average FPS of the slowest 1% of frames (same definition as the live overlay).</summary>
        public float Low1
        {
            get
            {
                if (Frames == 0) return float.NaN;
                long want = Math.Max(1, Frames / 100), got = 0;
                double sum = 0;
                for (int i = Bins - 1; i >= 0 && got < want; i--)
                {
                    if (hist[i] == 0) continue;
                    long take = Math.Min(hist[i], want - got);
                    sum += take * (i + 0.5) * BinMs;
                    got += take;
                }
                return sum > 0 ? (float)(1000.0 * got / sum) : float.NaN;
            }
        }
    }
}
