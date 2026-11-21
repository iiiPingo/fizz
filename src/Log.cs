using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fizz
{
    /// <summary>Tiny thread-safe logger. Never throws. Rate-limits repeated messages.</summary>
    internal static class Log
    {
        static readonly object Gate = new object();
        static readonly Dictionary<string, DateTime> LastByKey = new Dictionary<string, DateTime>();
        const long MaxBytes = 1024 * 1024;
        static readonly int Pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fizz");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string LogPath { get { return Path.Combine(Dir, "fizz.log"); } }
        public static string CrashPath { get { return Path.Combine(Dir, "crash.log"); } }

        public static void Info(string msg) { Write(LogPath, "INFO", msg); }
        public static void Warn(string msg) { Write(LogPath, "WARN", msg); }

        /// <summary>Logs at most once per <paramref name="seconds"/> for the same key.</summary>
        public static void Throttled(string key, string msg, int seconds = 60)
        {
            DateTime now = DateTime.UtcNow, last;
            lock (Gate)
            {
                if (LastByKey.TryGetValue(key, out last) && (now - last).TotalSeconds < seconds) return;
                LastByKey[key] = now;
                if (LastByKey.Count > 500) LastByKey.Clear();
            }
            Write(LogPath, "WARN", msg);
        }

        public static void Error(string where, Exception ex)
        {
            Throttled("err:" + where + ":" + (ex == null ? "" : ex.GetType().Name + ex.Message),
                where + ": " + (ex == null ? "(null)" : ex.ToString()), 30);
        }

        public static void Crash(string where, object ex)
        {
            string text = where + ": " + (ex == null ? "(null)" : ex.ToString());
            Write(CrashPath, "CRASH", text);
            Write(LogPath, "CRASH", text);
        }

        static void Write(string path, string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] [" +
                        Pid + "] " + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
