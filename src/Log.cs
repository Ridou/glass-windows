// %LOCALAPPDATA%\Glass\Glass.log -- the only way to see what happened when Glass is started
// from Explorer. Records forwarding decisions and timings; it never records keys pressed
// away from the overlay.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Glass
{
    public static class Log
    {
        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glass");
        public static string File_ => Path.Combine(Dir, "Glass.log");

        static readonly BlockingCollection<string> queue = new BlockingCollection<string>(4096);
        static Thread writer;

        public static void Start()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                // Keep one previous log, so a long-lived install never grows without bound.
                var f = new FileInfo(File_);
                if (f.Exists && f.Length > 5 * 1024 * 1024)
                    System.IO.File.Move(File_, Path.Combine(Dir, "Glass.old.log"), true);
            }
            catch { }
            writer = new Thread(Drain) { IsBackground = true, Name = "glass.log" };
            writer.Start();
            Write("---- Glass started (" + Environment.OSVersion.VersionString + ", "
                  + (Environment.Is64BitProcess ? "x64" : "x86") + ") ----");
        }

        /// The only thread that touches the disk or the console. A console can block its writer
        /// -- selecting text in one pauses output -- and the writer must never be the hook thread.
        static void Drain()
        {
            var batch = new System.Text.StringBuilder();
            foreach (var first in queue.GetConsumingEnumerable())
            {
                batch.Clear().Append(first);
                while (batch.Length < 65536 && queue.TryTake(out var more)) batch.Append(more);
                var text = batch.ToString();
                if (Echo) { try { Console.Write(text); } catch { } }
                try { System.IO.File.AppendAllText(File_, text); } catch { }
            }
        }

        /// Copy every line to the console, as the Mac build prints its log to stdout. Only while
        /// Glass runs from a console window; a one-shot command prints its own output instead.
        public static volatile bool Echo;

        /// Never blocks the caller: a full queue drops the line rather than stall the thread
        /// that wrote it. That thread may be the one running the keyboard hook.
        public static void Write(string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine;
            if (!queue.IsAddingCompleted) queue.TryAdd(line);
        }

        /// Give queued lines a moment to reach the disk before the process exits.
        public static void Flush()
        {
            var sw = Stopwatch.StartNew();
            while (queue.Count > 0 && writer != null && writer.IsAlive && sw.ElapsedMilliseconds < 500)
                Thread.Sleep(5);
            Thread.Sleep(writer != null && writer.IsAlive ? 20 : 0);      // the last batch is written after it is taken
        }

        // MARK: - Main thread watchdog

        /// Pings the UI thread every 100ms and logs if it took too long to answer. Every click
        /// on the overlay arrives through that thread, so a stall there is a heal that lands
        /// late. (The keyboard hook has a thread of its own, watched in Hooks.) This makes a
        /// stall visible, not inferred.
        static System.Threading.Timer watchdog;
        static Control pump;

        public static void StartWatchdog(Control uiThreadControl)
        {
            pump = uiThreadControl;
            watchdog = new System.Threading.Timer(_ =>
            {
                var sent = Stopwatch.GetTimestamp();
                try
                {
                    if (pump == null || pump.IsDisposed || !pump.IsHandleCreated) return;
                    pump.BeginInvoke((Action)(() =>
                    {
                        var lag = (Stopwatch.GetTimestamp() - sent) * 1000.0 / Stopwatch.Frequency;
                        if (lag > 250) Write(string.Format("UI thread stalled {0:F0}ms", lag));
                    }));
                }
                catch { }
            }, null, 1000, 100);
        }
    }
}
