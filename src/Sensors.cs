using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware;

namespace Fizz
{
    internal sealed class SensorInfo
    {
        public string Identifier, Hardware, HardwareKind, Name;
        public float Value;
        public bool IsGpu;
    }

    [Flags]
    internal enum Need { None = 0, Cpu = 1, Gpu = 2, Ram = 4, Disk = 8, Net = 16, Catalog = 32 }

    /// <summary>
    /// Hardware sensors. Only ever touched from the sampler thread.
    /// LibreHardwareMonitor lives behind <see cref="LhmBackend"/> so a missing/broken DLL can't take Fizz down.
    /// </summary>
    internal sealed class SensorService : IDisposable
    {
        LhmBackend lhm;
        bool lhmBroken;
        int consecutiveErrors;
        DateTime reopenAtUtc = DateTime.MinValue;
        public string Problem { get; private set; }
        public volatile List<SensorInfo> LastCatalog;
        public string CpuName = "", GpuName = "";
        public string CpuTempSource = "", GpuTempSource = "";
        SensorPick cpuPick = new SensorPick(), gpuPick = new SensorPick();
        bool picksDirty = true;

        // fallbacks that need no driver
        long lastIdle, lastKernel, lastUser;
        PerformanceCounter diskIdle;
        bool diskBroken;
        NetworkInterface[] nics;
        DateTime nicsAtUtc = DateTime.MinValue;
        long lastRx = -1, lastTx = -1, lastNetTicks;

        public void SetPicks(SensorPick cpu, SensorPick gpu)
        {
            cpuPick = cpu ?? new SensorPick();
            gpuPick = gpu ?? new SensorPick();
            picksDirty = true;
        }

        public void RequestReopen(int delaySeconds)
        {
            reopenAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            CloseLhm();
        }

        public void Sample(Snapshot s, Need need)
        {
            // ---- LibreHardwareMonitor (CPU / GPU) ----
            if ((need & (Need.Cpu | Need.Gpu | Need.Catalog)) != 0 && !lhmBroken)
            {
                try { SampleLhm(s, need); }
                catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is TypeLoadException || ex is BadImageFormatException || ex is MissingMethodException)
                {
                    lhmBroken = true;
                    lhm = null;
                    Problem = "Sensor library missing or damaged: " + ex.Message;
                    Log.Error("Sensors.Load", ex);
                }
            }

            // ---- RAM ----
            if ((need & Need.Ram) != 0)
            {
                var m = new Native.MEMORYSTATUSEX { dwLength = 64 };
                if (Native.GlobalMemoryStatusEx(ref m))
                {
                    s.RamTotalMb = m.ullTotalPhys / 1048576f;
                    s.RamUsedMb = (m.ullTotalPhys - m.ullAvailPhys) / 1048576f;
                }
            }

            // ---- CPU load without LHM ----
            if ((need & Need.Cpu) != 0 && float.IsNaN(s.CpuLoad)) s.CpuLoad = CpuLoadFallback();

            if ((need & Need.Disk) != 0) s.Disk = DiskActivity(); else DisposeDisk();
            if ((need & Need.Net) != 0) SampleNet(s);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void SampleLhm(Snapshot s, Need need)
        {
            if (lhmBroken) return;
            if (lhm == null)
            {
                if (DateTime.UtcNow < reopenAtUtc) return;
                try
                {
                    lhm = OpenLhm();
                    picksDirty = true;
                    CpuName = lhm.CpuName; GpuName = lhm.GpuName;
                    Problem = null;
                }
                catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is TypeLoadException || ex is BadImageFormatException)
                {
                    lhmBroken = true;
                    Problem = "Sensor library missing or damaged: " + ex.Message;
                    Log.Error("Sensors.Open", ex);
                    return;
                }
                catch (Exception ex)
                {
                    lhm = null;
                    reopenAtUtc = DateTime.UtcNow.AddSeconds(30);
                    Problem = "Sensors failed to start, retrying: " + ex.Message;
                    Log.Error("Sensors.Open", ex);
                    return;
                }
            }

            try
            {
                if (picksDirty) { lhm.ApplyPicks(cpuPick, gpuPick); picksDirty = false; GpuName = lhm.GpuName; }
                int errs = lhm.Update((need & Need.Cpu) != 0, (need & Need.Gpu) != 0, (need & Need.Catalog) != 0);
                lhm.Read(s, (need & Need.Cpu) != 0, (need & Need.Gpu) != 0);
                CpuTempSource = lhm.CpuTempSource; GpuTempSource = lhm.GpuTempSource;
                if ((need & Need.Catalog) != 0) LastCatalog = lhm.Catalog();
                consecutiveErrors = errs > 0 ? consecutiveErrors + 1 : 0;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                Log.Error("Sensors.Sample", ex);
            }

            // Driver reset / resume can leave LHM handles dead: rebuild it.
            if (consecutiveErrors >= 10)
            {
                Log.Warn("Sensors failing repeatedly; reopening in 15 s");
                consecutiveErrors = 0;
                RequestReopen(15);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LhmBackend OpenLhm() { var b = new LhmBackend(); b.Open(); return b; }

        void CloseLhm()
        {
            var l = lhm;
            lhm = null;
            if (l != null) { try { l.Close(); } catch (Exception ex) { Log.Error("Sensors.Close", ex); } }
        }

        float CpuLoadFallback()
        {
            long idle, kernel, user;
            if (!Native.GetSystemTimes(out idle, out kernel, out user)) return float.NaN;
            long di = idle - lastIdle, dk = kernel - lastKernel, du = user - lastUser;
            bool first = lastKernel == 0;
            lastIdle = idle; lastKernel = kernel; lastUser = user;
            long total = dk + du;
            if (first || total <= 0) return float.NaN;
            return Math.Max(0, Math.Min(100, (total - di) * 100f / total));
        }

        float DiskActivity()
        {
            if (diskBroken) return float.NaN;
            try
            {
                if (diskIdle == null) { diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", true); diskIdle.NextValue(); return float.NaN; }
                return Math.Max(0, Math.Min(100, 100f - diskIdle.NextValue()));
            }
            catch (Exception ex) { diskBroken = true; Log.Error("Sensors.Disk", ex); return float.NaN; }
        }

        void DisposeDisk()
        {
            if (diskIdle != null) { try { diskIdle.Dispose(); } catch { } diskIdle = null; }
        }

        void SampleNet(Snapshot s)
        {
            try
            {
                if (nics == null || (DateTime.UtcNow - nicsAtUtc).TotalSeconds > 15)
                {
                    nicsAtUtc = DateTime.UtcNow;
                    nics = NetworkInterface.GetAllNetworkInterfaces().Where(IsRealNic).ToArray();
                    lastRx = lastTx = -1; // adapter set may have changed
                }
                long rx = 0, tx = 0;
                foreach (var n in nics)
                {
                    try { var st = n.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
                }
                long now = Stopwatch.GetTimestamp();
                if (lastRx >= 0)
                {
                    double dt = (now - lastNetTicks) / (double)Stopwatch.Frequency;
                    if (dt > 0.05)
                    {
                        s.NetDownBps = (float)Math.Max(0, (rx - lastRx) / dt);
                        s.NetUpBps = (float)Math.Max(0, (tx - lastTx) / dt);
                    }
                }
                lastRx = rx; lastTx = tx; lastNetTicks = now;
            }
            catch (Exception ex) { Log.Error("Sensors.Net", ex); }
        }

        static bool IsRealNic(NetworkInterface n)
        {
            if (n.OperationalStatus != OperationalStatus.Up) return false;
            if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return false;
            string d = (n.Description + " " + n.Name).ToLowerInvariant();
            string[] skip = { "virtual", "hyper-v", "vmware", "vbox", "virtualbox", "loopback", "pseudo", "wan miniport", "bluetooth", "vethernet", "tap-", "wintun", "wireguard", "npcap", "filter" };
            foreach (var k in skip) if (d.Contains(k)) return false;
            return true;
        }

        public void Dispose()
        {
            CloseLhm();
            DisposeDisk();
        }
    }

    /// <summary>All LibreHardwareMonitor types are confined to this class.</summary>
    internal sealed class LhmBackend
    {
        Computer computer;
        IHardware cpu, gpu;
        readonly List<IHardware> gpus = new List<IHardware>();
        ISensor cpuTemp, cpuLoad, coreMax, cpuClock, cpuPower;
        readonly List<ISensor> coreLoads = new List<ISensor>(), coreClocks = new List<ISensor>();
        ISensor gpuTemp, gpuLoad, gpuClock, gpuPower, vramUsed, vramTotal;
        int updatesSinceDiscover;
        public string CpuName = "", GpuName = "", CpuTempSource = "", GpuTempSource = "";
        SensorPick cpuPick = new SensorPick(), gpuPick = new SensorPick();

        public void Open()
        {
            computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            computer.Open();
            foreach (var h in computer.Hardware)
            {
                if (h.HardwareType == HardwareType.Cpu && cpu == null) cpu = h;
                else if (h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel) gpus.Add(h);
            }
            foreach (var h in computer.Hardware) SafeUpdate(h); // sensors appear after the first update
            CpuName = cpu != null ? cpu.Name : "";
            Log.Info("Sensors opened: CPU=" + CpuName + "; GPUs=" + string.Join(" | ", gpus.Select(g => g.Name + " (" + g.HardwareType + ")")));
            Discover();
        }

        public void Close()
        {
            var c = computer;
            computer = null;
            if (c != null) c.Close();
        }

        public void ApplyPicks(SensorPick cpuP, SensorPick gpuP)
        {
            cpuPick = cpuP; gpuPick = gpuP;
            Discover();
        }

        static int SafeUpdate(IHardware h)
        {
            try { h.Update(); return 0; }
            catch (Exception ex) { Log.Error("Sensors.Update." + h.HardwareType, ex); return 1; }
        }

        public int Update(bool needCpu, bool needGpu, bool all)
        {
            if (computer == null) return 1;
            int errs = 0;
            if (all) { foreach (var h in computer.Hardware) errs += SafeUpdate(h); }
            else
            {
                if (needCpu && cpu != null) errs += SafeUpdate(cpu);
                if (needGpu && gpu != null) errs += SafeUpdate(gpu);
            }
            // Some sensors only show up after a few updates; look again now and then.
            if (++updatesSinceDiscover == 6 || updatesSinceDiscover % 120 == 0) Discover();
            return errs;
        }

        void Discover()
        {
            // ---- GPU device: the user's pick, else the one with the most dedicated memory ----
            gpu = null;
            ISensor pickedGpuTemp = FindById(gpuPick);
            if (pickedGpuTemp != null) gpu = pickedGpuTemp.Hardware;
            if (gpu == null && gpus.Count > 0)
            {
                gpu = gpus.OrderByDescending(g => Val(Find(g, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")) ?? -1)
                          .ThenBy(g => g.HardwareType == HardwareType.GpuIntel ? 1 : 0).First();
            }
            GpuName = gpu != null ? gpu.Name : "";

            // ---- CPU ----
            cpuTemp = FindById(cpuPick);
            CpuTempSource = cpuTemp != null ? "picked: " + cpuTemp.Name : "";
            if (cpuTemp == null && cpu != null)
            {
                cpuTemp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "Core (Tctl)", "CPU Package", "Core (Tdie)", "Core Max", "Core Average")
                          ?? cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) < 0
                                                              && !x.Name.StartsWith("CCD", StringComparison.OrdinalIgnoreCase));
                CpuTempSource = cpuTemp != null ? "auto: " + cpuTemp.Name : "none";
            }
            if (cpu != null)
            {
                cpuLoad = Find(cpu, SensorType.Load, "CPU Total");
                coreMax = Find(cpu, SensorType.Load, "CPU Core Max");
                cpuClock = Find(cpu, SensorType.Clock, "Cores (Average Effective)", "Cores (Average)");
                cpuPower = Find(cpu, SensorType.Power, "Package", "CPU Package") ?? cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power);
                coreLoads.Clear(); coreClocks.Clear();
                foreach (var x in cpu.Sensors)
                {
                    if (x.SensorType == SensorType.Load && x.Name.StartsWith("CPU Core #", StringComparison.Ordinal)) coreLoads.Add(x);
                    if (x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #", StringComparison.Ordinal)) coreClocks.Add(x);
                }
            }

            // ---- GPU sensors ----
            gpuTemp = pickedGpuTemp;
            GpuTempSource = gpuTemp != null ? "picked: " + gpuTemp.Name : "";
            if (gpu != null)
            {
                if (gpuTemp == null)
                {
                    gpuTemp = Find(gpu, SensorType.Temperature, "GPU Core", "GPU")
                              ?? gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Name.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) < 0
                                                                  && x.Name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) < 0);
                    GpuTempSource = gpuTemp != null ? "auto: " + gpuTemp.Name : "none";
                }
                gpuLoad = Find(gpu, SensorType.Load, "GPU Core", "D3D 3D") ?? gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load);
                gpuClock = Find(gpu, SensorType.Clock, "GPU Core");
                gpuPower = Find(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU PPT", "GPU Core") ?? gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power);
                vramUsed = Find(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
                vramTotal = Find(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            }
            else { gpuLoad = gpuClock = gpuPower = vramUsed = vramTotal = null; }
        }

        ISensor FindById(SensorPick p)
        {
            if (p == null || p.IsAuto || computer == null) return null;
            foreach (var h in computer.Hardware)
                foreach (var x in h.Sensors)
                    if (x.Identifier.ToString() == p.Identifier) return x;
            // identifiers can shift between versions: fall back to hardware + sensor name
            foreach (var h in computer.Hardware)
                if (h.Name == p.Hardware)
                    foreach (var x in h.Sensors)
                        if (x.SensorType == SensorType.Temperature && x.Name == p.Name) return x;
            return null;
        }

        static ISensor Find(IHardware h, SensorType t, params string[] names)
        {
            foreach (var n in names)
                foreach (var x in h.Sensors)
                    if (x.SensorType == t && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }

        static float? Val(ISensor x) { return x == null ? null : x.Value; }

        static float Get(ISensor x, float min, float max)
        {
            if (x == null) return float.NaN;
            float? v = x.Value;
            if (!v.HasValue || float.IsNaN(v.Value) || v.Value < min || v.Value > max) return float.NaN;
            return v.Value;
        }

        public void Read(Snapshot s, bool needCpu, bool needGpu)
        {
            if (needCpu)
            {
                s.CpuTemp = Get(cpuTemp, 1, 125);
                s.CpuLoad = Get(cpuLoad, 0, 100);
                s.CoreMax = Get(coreMax, 0, 100);
                if (float.IsNaN(s.CoreMax) && coreLoads.Count > 0)
                {
                    float m = float.NaN;
                    foreach (var c in coreLoads) { float v = Get(c, 0, 100); if (!float.IsNaN(v) && (float.IsNaN(m) || v > m)) m = v; }
                    s.CoreMax = m;
                }
                s.CpuClockMhz = Get(cpuClock, 50, 10000);
                if (float.IsNaN(s.CpuClockMhz) && coreClocks.Count > 0)
                {
                    float sum = 0; int n = 0;
                    foreach (var c in coreClocks) { float v = Get(c, 50, 10000); if (!float.IsNaN(v)) { sum += v; n++; } }
                    if (n > 0) s.CpuClockMhz = sum / n;
                }
                s.CpuPower = Get(cpuPower, 0, 1000);
            }
            if (needGpu)
            {
                s.GpuTemp = Get(gpuTemp, 1, 125);
                s.GpuLoad = Get(gpuLoad, 0, 100);
                s.GpuClock = Get(gpuClock, 1, 10000);
                s.GpuPower = Get(gpuPower, 0, 2000);
                s.VramUsedMb = Get(vramUsed, 0, 1 << 20);
                s.VramTotalMb = Get(vramTotal, 1, 1 << 20);
            }
        }

        public List<SensorInfo> Catalog()
        {
            var list = new List<SensorInfo>();
            if (computer == null) return list;
            foreach (var h in computer.Hardware)
                foreach (var x in h.Sensors)
                    if (x.SensorType == SensorType.Temperature)
                        list.Add(new SensorInfo
                        {
                            Identifier = x.Identifier.ToString(), Hardware = h.Name, HardwareKind = h.HardwareType.ToString(), Name = x.Name,
                            Value = x.Value.HasValue ? x.Value.Value : float.NaN,
                            IsGpu = h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel
                        });
            return list;
        }

        /// <summary>Full dump of every sensor (diagnostics only).</summary>
        public static string DumpAll()
        {
            var sb = new System.Text.StringBuilder();
            var c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true }; // same groups Fizz uses: never motherboard/EC, SMBus or storage
            c.Open();
            try
            {
                foreach (var h in c.Hardware) { SafeUpdate(h); }
                System.Threading.Thread.Sleep(600);
                foreach (var h in c.Hardware)
                {
                    SafeUpdate(h);
                    sb.AppendLine("== " + h.Name + " [" + h.HardwareType + "] " + h.Identifier);
                    foreach (var x in h.Sensors.OrderBy(x => x.SensorType).ThenBy(x => x.Index))
                        sb.AppendLine(string.Format("   {0,-11} {1,-34} {2,10}   {3}", x.SensorType, x.Name,
                            x.Value.HasValue ? x.Value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "null", x.Identifier));
                }
            }
            finally { c.Close(); }
            return sb.ToString();
        }
    }
}
