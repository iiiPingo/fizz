using System;
using System.Drawing;
using System.Globalization;

namespace Fizz
{
    internal enum Stat
    {
        Fps, Low1, FrameTime, GpuTemp, GpuLoad, GpuClock, GpuPower, Vram,
        CpuTemp, CpuLoad, CoreMax, CpuClock, CpuPower, Ram, Disk, Net, Clock, Game, PresentMode
    }

    /// <summary>Present modes as reported by PresentMon's PresentMode column.</summary>
    internal static class PresentModes
    {
        public const byte Unknown = 0, LegacyFlip = 1, LegacyCopy = 2, IndependentFlip = 3, ComposedFlip = 4,
            HwComposedIndependentFlip = 5, ComposedCopyGpu = 6, ComposedCopyCpu = 7;

        public static readonly string[] Full =
        {
            "", "Hardware: Legacy Flip", "Hardware: Legacy Copy to front buffer", "Hardware: Independent Flip",
            "Composed: Flip", "Hardware Composed: Independent Flip", "Composed: Copy with GPU GDI", "Composed: Copy with CPU GDI"
        };
        public static readonly string[] Short = { "--", "Exclusive", "Exclusive", "iFlip", "Composed", "MPO iFlip", "Copy", "Copy" };

        public static bool IsExclusive(byte m) { return m == LegacyFlip || m == LegacyCopy; }
    }

    /// <summary>Latest values. NaN = unavailable (shown as "--").</summary>
    internal sealed class Snapshot
    {
        public float Fps = float.NaN, Low1 = float.NaN, FrameTime = float.NaN;
        public byte PresentMode;
        public float GpuTemp = float.NaN, GpuLoad = float.NaN, GpuClock = float.NaN, GpuPower = float.NaN;
        public float VramUsedMb = float.NaN, VramTotalMb = float.NaN;
        public float CpuTemp = float.NaN, CpuLoad = float.NaN, CoreMax = float.NaN, CpuClockMhz = float.NaN, CpuPower = float.NaN;
        public float RamUsedMb = float.NaN, RamTotalMb = float.NaN;
        public float Disk = float.NaN, NetDownBps = float.NaN, NetUpBps = float.NaN;
        public string Game = "";

        public void CopyFrom(Snapshot o)
        {
            Fps = o.Fps; Low1 = o.Low1; FrameTime = o.FrameTime; PresentMode = o.PresentMode;
            GpuTemp = o.GpuTemp; GpuLoad = o.GpuLoad; GpuClock = o.GpuClock; GpuPower = o.GpuPower;
            VramUsedMb = o.VramUsedMb; VramTotalMb = o.VramTotalMb;
            CpuTemp = o.CpuTemp; CpuLoad = o.CpuLoad; CoreMax = o.CoreMax; CpuClockMhz = o.CpuClockMhz; CpuPower = o.CpuPower;
            RamUsedMb = o.RamUsedMb; RamTotalMb = o.RamTotalMb; Disk = o.Disk; NetDownBps = o.NetDownBps; NetUpBps = o.NetUpBps;
            Game = o.Game;
        }
    }

    internal sealed class StatDef
    {
        public Stat Id;
        public string Key, Name, Label, Template;
        public bool HasThreshold, LowerIsWorse, IsTemp, DefaultColors, DefaultUnit;
        public float DefaultAmber, DefaultRed;
        public string ThresholdHint;
    }

    internal enum Tone { Normal, Good, Warn, Bad, Dim }

    internal static class Stats
    {
        public static readonly StatDef[] All =
        {
            D(Stat.Fps, "fps", "FPS", "FPS", "000", true, true, false, true, false, 60, 30, "FPS below"),
            D(Stat.Low1, "low1", "1% low FPS", "1% LOW", "000", true, true, false, true, false, 60, 30, "FPS below"),
            D(Stat.FrameTime, "frametime", "Frametime", "FT", "00.0 ms", true, false, false, false, true, 16.7f, 33.3f, "ms above"),
            D(Stat.GpuTemp, "gputemp", "GPU temperature", "GPU", "00°C", true, false, true, true, true, 75, 85, "°C at or above"),
            D(Stat.GpuLoad, "gpuload", "GPU load", "GPU", "100%", true, false, false, false, true, 95, 100, "% at or above"),
            D(Stat.GpuClock, "gpuclock", "GPU clock", "GPU", "0000 MHz", false, false, false, false, true, 0, 0, ""),
            D(Stat.GpuPower, "gpupower", "GPU power draw", "GPU", "000 W", false, false, false, false, true, 0, 0, ""),
            D(Stat.Vram, "vram", "VRAM used / total", "VRAM", "0.0/0.0 GB", true, false, false, true, true, 90, 97, "% used at or above"),
            D(Stat.CpuTemp, "cputemp", "CPU temperature", "CPU", "00°C", true, false, true, true, true, 85, 95, "°C at or above"),
            D(Stat.CpuLoad, "cpuload", "CPU load", "CPU", "100%", true, false, false, false, true, 90, 100, "% at or above"),
            D(Stat.CoreMax, "coremax", "CPU busiest core", "CORE", "100%", true, false, false, false, true, 95, 100, "% at or above"),
            D(Stat.CpuClock, "cpuclock", "CPU clock", "CPU", "0.0 GHz", false, false, false, false, true, 0, 0, ""),
            D(Stat.CpuPower, "cpupower", "CPU power draw", "CPU", "00 W", false, false, false, false, true, 0, 0, ""),
            D(Stat.Ram, "ram", "RAM used / total", "RAM", "00.0/00.0 GB", true, false, false, true, true, 85, 95, "% used at or above"),
            D(Stat.Disk, "disk", "Disk activity", "DISK", "100%", true, false, false, false, true, 90, 100, "% at or above"),
            D(Stat.Net, "net", "Network down / up", "NET", "↓000 ↑000 KB/s", false, false, false, false, true, 0, 0, ""),
            D(Stat.Clock, "clock", "Time", "", "00:00", false, false, false, false, true, 0, 0, ""),
            D(Stat.Game, "game", "Game name", "", "", false, false, false, false, true, 0, 0, ""),
            D(Stat.PresentMode, "presentmode", "Present mode (latency check)", "MODE", "Composed", false, false, false, true, true, 0, 0, ""),
        };

        static StatDef D(Stat id, string key, string name, string label, string template, bool thr, bool lowerWorse,
            bool isTemp, bool colorsOn, bool unit, float amber, float red, string hint)
        {
            return new StatDef
            {
                Id = id, Key = key, Name = name, Label = label, Template = template, HasThreshold = thr, LowerIsWorse = lowerWorse,
                IsTemp = isTemp, DefaultColors = colorsOn && thr, DefaultUnit = unit, DefaultAmber = amber, DefaultRed = red, ThresholdHint = hint
            };
        }

        public static StatDef Get(Stat id) { return All[(int)id]; }

        public static bool TryParse(string key, out Stat id)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Key, key, StringComparison.OrdinalIgnoreCase)) { id = All[i].Id; return true; }
            id = Stat.Fps;
            return false;
        }

        // ---------- value → (key, text, tone) ----------

        /// <summary>A number that changes only when the displayed text would change. Lets us skip formatting/redraw.</summary>
        public static long ValueKey(Stat id, Snapshot s, bool unit)
        {
            long u = unit ? 1 : 0;
            switch (id)
            {
                case Stat.Fps: return Q(s.Fps, 1) * 2 + u;
                case Stat.Low1: return Q(s.Low1, 1) * 2 + u;
                case Stat.FrameTime: return Q(s.FrameTime, 10) * 2 + u;
                case Stat.GpuTemp: return Q(s.GpuTemp, 1) * 2 + u;
                case Stat.GpuLoad: return Q(s.GpuLoad, 1) * 2 + u;
                case Stat.GpuClock: return Q(s.GpuClock, 1) * 2 + u;
                case Stat.GpuPower: return Q(s.GpuPower, 1) * 2 + u;
                case Stat.Vram: return (Q(s.VramUsedMb / 1024f, 10) * 100000 + Q(s.VramTotalMb / 1024f, 10)) * 2 + u;
                case Stat.CpuTemp: return Q(s.CpuTemp, 1) * 2 + u;
                case Stat.CpuLoad: return Q(s.CpuLoad, 1) * 2 + u;
                case Stat.CoreMax: return Q(s.CoreMax, 1) * 2 + u;
                case Stat.CpuClock: return Q(s.CpuClockMhz / 1000f, 10) * 2 + u;
                case Stat.CpuPower: return Q(s.CpuPower, 1) * 2 + u;
                case Stat.Ram: return (Q(s.RamUsedMb / 1024f, 10) * 100000 + Q(s.RamTotalMb / 1024f, 10)) * 2 + u;
                case Stat.Disk: return Q(s.Disk, 1) * 2 + u;
                case Stat.Net:
                    {
                        bool mb = NetUsesMb(s);
                        float div = mb ? 1048576f : 1024f;
                        return ((Q(s.NetDownBps / div, mb ? 10 : 1) * 1000000 + Q(s.NetUpBps / div, mb ? 10 : 1)) * 2 + (mb ? 1 : 0)) * 2 + u;
                    }
                case Stat.Clock: { var n = DateTime.Now; return n.Hour * 60 + n.Minute; }
                case Stat.Game: return s.Game == null ? 0 : s.Game.GetHashCode();
                case Stat.PresentMode: return s.PresentMode;
            }
            return 0;
        }

        static long Q(float v, float scale)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return -1;
            return (long)Math.Round(v * scale);
        }

        static bool NetUsesMb(Snapshot s)
        {
            float m = Math.Max(Nz(s.NetDownBps), Nz(s.NetUpBps));
            return m >= 1048576f * 0.95f;
        }
        static float Nz(float v) { return float.IsNaN(v) ? 0 : v; }

        const string Dash = "--";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Format(Stat id, Snapshot s, bool unit)
        {
            switch (id)
            {
                case Stat.Fps:
                case Stat.Low1:
                    {
                        float v = id == Stat.Fps ? s.Fps : s.Low1;
                        if (float.IsNaN(v)) return Dash;
                        return Int(v) + (unit ? " fps" : "");
                    }
                case Stat.FrameTime: return float.IsNaN(s.FrameTime) ? Dash : Tenths(Q(s.FrameTime, 10)) + (unit ? " ms" : "");
                case Stat.GpuTemp: return Temp(s.GpuTemp, unit);
                case Stat.CpuTemp: return Temp(s.CpuTemp, unit);
                case Stat.GpuLoad: return Pct(s.GpuLoad, unit);
                case Stat.CpuLoad: return Pct(s.CpuLoad, unit);
                case Stat.CoreMax: return Pct(s.CoreMax, unit);
                case Stat.Disk: return Pct(s.Disk, unit);
                case Stat.GpuClock: return float.IsNaN(s.GpuClock) ? Dash : Int(s.GpuClock) + (unit ? " MHz" : "");
                case Stat.CpuClock: return float.IsNaN(s.CpuClockMhz) ? Dash : Tenths(Q(s.CpuClockMhz / 1000f, 10)) + (unit ? " GHz" : "");
                case Stat.GpuPower: return float.IsNaN(s.GpuPower) ? Dash : Int(s.GpuPower) + (unit ? " W" : "");
                case Stat.CpuPower: return float.IsNaN(s.CpuPower) ? Dash : Int(s.CpuPower) + (unit ? " W" : "");
                case Stat.Vram: return UsedTotal(s.VramUsedMb, s.VramTotalMb, unit);
                case Stat.Ram: return UsedTotal(s.RamUsedMb, s.RamTotalMb, unit);
                case Stat.Net:
                    {
                        if (float.IsNaN(s.NetDownBps) && float.IsNaN(s.NetUpBps)) return Dash;
                        bool mb = NetUsesMb(s);
                        float div = mb ? 1048576f : 1024f;
                        string down = mb ? Tenths(Q(Nz(s.NetDownBps) / div, 10)) : Whole(Q(Nz(s.NetDownBps) / div, 1));
                        string up = mb ? Tenths(Q(Nz(s.NetUpBps) / div, 10)) : Whole(Q(Nz(s.NetUpBps) / div, 1));
                        return "↓" + down + " ↑" + up + (unit ? (mb ? " MB/s" : " KB/s") : "");
                    }
                case Stat.Clock: return DateTime.Now.ToString(ShortTimeNoSeconds(), CultureInfo.CurrentCulture);
                case Stat.Game: return string.IsNullOrEmpty(s.Game) ? Dash : s.Game;
                case Stat.PresentMode: return PresentModes.Short[s.PresentMode < PresentModes.Short.Length ? s.PresentMode : 0];
            }
            return Dash;
        }

        static string cachedTimePattern;
        static string ShortTimeNoSeconds()
        {
            if (cachedTimePattern == null)
            {
                string p = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
                cachedTimePattern = p.Replace(":ss", "").Replace(".ss", "");
            }
            return cachedTimePattern;
        }

        // Small integer → string cache so steady values never allocate.
        static readonly string[] IntCache = BuildIntCache();
        static string[] BuildIntCache()
        {
            var a = new string[10000];
            for (int i = 0; i < a.Length; i++) a[i] = i.ToString(Inv);
            return a;
        }
        public static string Int(float v) { return Whole(Q(v, 1)); }

        static string Whole(long q) { return q >= 0 && q < IntCache.Length ? IntCache[q] : q.ToString(Inv); }

        /// <summary>Formats a value stored in tenths, e.g. 69 → "6.9". Uses the same rounding as the cache key.</summary>
        static string Tenths(long q)
        {
            if (q < 0) return "0.0";
            return Whole(q / 10) + "." + IntCache[q % 10];
        }

        static string Temp(float v, bool unit) { return float.IsNaN(v) ? Dash : Int(v) + (unit ? "°C" : "°"); }
        static string Pct(float v, bool unit) { return float.IsNaN(v) ? Dash : Int(v) + (unit ? "%" : ""); }

        static string UsedTotal(float usedMb, float totalMb, bool unit)
        {
            if (float.IsNaN(usedMb)) return Dash;
            string t = Tenths(Q(usedMb / 1024f, 10));
            if (!float.IsNaN(totalMb) && totalMb > 0) t += "/" + Tenths(Q(totalMb / 1024f, 10));
            return t + (unit ? " GB" : "");
        }

        /// <summary>The number thresholds are compared against (NaN = no coloring).</summary>
        public static float ThresholdValue(Stat id, Snapshot s)
        {
            switch (id)
            {
                case Stat.Fps: return s.Fps;
                case Stat.Low1: return s.Low1;
                case Stat.FrameTime: return s.FrameTime;
                case Stat.GpuTemp: return s.GpuTemp;
                case Stat.GpuLoad: return s.GpuLoad;
                case Stat.CpuTemp: return s.CpuTemp;
                case Stat.CpuLoad: return s.CpuLoad;
                case Stat.CoreMax: return s.CoreMax;
                case Stat.Disk: return s.Disk;
                case Stat.Vram: return s.VramTotalMb > 0 ? s.VramUsedMb * 100f / s.VramTotalMb : float.NaN;
                case Stat.Ram: return s.RamTotalMb > 0 ? s.RamUsedMb * 100f / s.RamTotalMb : float.NaN;
            }
            return float.NaN;
        }

        public static Tone ToneFor(Stat id, Snapshot s, ThresholdSetting t)
        {
            if (id == Stat.PresentMode)
            {
                if (t != null && !t.Enabled) return Tone.Normal;
                switch (s.PresentMode)
                {
                    case PresentModes.IndependentFlip:
                    case PresentModes.HwComposedIndependentFlip:
                    case PresentModes.LegacyFlip:
                    case PresentModes.LegacyCopy: return Tone.Good;
                    case PresentModes.ComposedFlip: return Tone.Warn;
                    case PresentModes.ComposedCopyCpu:
                    case PresentModes.ComposedCopyGpu: return Tone.Bad;
                }
                return Tone.Dim;
            }
            var def = Get(id);
            float v = ThresholdValue(id, s);
            if (float.IsNaN(v)) return Tone.Dim;
            if (!def.HasThreshold || t == null || !t.Enabled) return Tone.Normal;
            if (def.LowerIsWorse)
            {
                if (v < t.Red) return Tone.Bad;
                if (v < t.Amber) return Tone.Warn;
                return Tone.Normal;
            }
            if (v >= t.Red) return Tone.Bad;
            if (v >= t.Amber) return Tone.Warn;
            return def.IsTemp ? Tone.Good : Tone.Normal;
        }
    }
}
