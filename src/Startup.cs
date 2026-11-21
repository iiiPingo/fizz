using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace Fizz
{
    /// <summary>Elevation and "start with Windows" (a logon scheduled task, so no UAC prompt every boot).</summary>
    internal static class Startup
    {
        public const string TaskName = "Fizz";

        public static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        public static bool IsElevated
        {
            get
            {
                try { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        public static bool PawnIoInstalled
        {
            get
            {
                const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
                foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                {
                    try
                    {
                        using (var b = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
                        using (var k = b.OpenSubKey(key))
                            if (k != null && k.GetValue("DisplayVersion") != null) return true;
                    }
                    catch { }
                }
                return false;
            }
        }

        /// <summary>Relaunch elevated (UAC prompt). Returns false if the user said no.</summary>
        public static bool RelaunchElevated(string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ExePath, args) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(ExePath) });
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; } // user cancelled UAC
            catch (Exception ex) { Log.Error("RelaunchElevated", ex); return false; }
        }

        static int Schtasks(string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        public static bool TaskExists()
        {
            string o;
            return Schtasks("/Query /TN \"" + TaskName + "\"", out o) == 0;
        }

        /// <summary>Starts Fizz through the task (elevated, no UAC prompt). Works from a non-admin process.</summary>
        public static bool RunTask()
        {
            string o;
            int rc = Schtasks("/Run /TN \"" + TaskName + "\"", out o);
            if (rc != 0) Log.Warn("schtasks /Run failed: " + o.Trim());
            return rc == 0;
        }

        /// <summary>Creates/updates the logon task. Needs admin. Returns an error message or null.</summary>
        public static string EnableStartWithWindows()
        {
            if (!IsElevated) return "Fizz needs to run as admin once to set this up.";
            string user = WindowsIdentity.GetCurrent().Name;
            string exe = ExePath;
            string xml =
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Fizz in-game performance overlay</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>" + Esc(user) + @"</UserId><Delay>PT8S</Delay></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author""><UserId>" + Esc(user) + @"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec><Command>" + Esc(exe) + @"</Command><Arguments>--task</Arguments><WorkingDirectory>" + Esc(Path.GetDirectoryName(exe)) + @"</WorkingDirectory></Exec>
  </Actions>
</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "fizz-task.xml");
            try
            {
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                string o;
                int rc = Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F", out o);
                if (rc != 0) { Log.Warn("schtasks /Create failed: " + o.Trim()); return "Couldn't create the startup task: " + o.Trim(); }
                Log.Info("Startup task created/updated");
                return null;
            }
            catch (Exception ex) { Log.Error("EnableStartWithWindows", ex); return ex.Message; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static string DisableStartWithWindows()
        {
            if (!TaskExists()) return null;
            string o;
            int rc = Schtasks("/Delete /TN \"" + TaskName + "\" /F", out o);
            if (rc != 0) { Log.Warn("schtasks /Delete failed: " + o.Trim()); return IsElevated ? o.Trim() : "Run Fizz as admin to remove the startup task."; }
            return null;
        }

        static string Esc(string s) { return System.Security.SecurityElement.Escape(s); }
    }
}
