// Saved state. The macOS build uses UserDefaults; here it is one JSON file, so a friend
// can delete it to start clean and can read it to see what Glass thinks it knows.
//
//   %APPDATA%\Glass\settings.json

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glass
{
    public static class Saved
    {
        /// The group sizes worth having one-click access to mid-pull.
        public static readonly string[] PresetNames = { "duo", "5", "10", "20" };

        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glass");
        public static string Path_ => System.IO.Path.Combine(Dir, "settings.json");

        static JsonObject d = new JsonObject();
        static readonly object gate = new object();

        public static void Load()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            if (!File.Exists(Path_)) return;
            // A virus scanner or a second copy can hold the file for a moment; wait it out
            // rather than start empty and overwrite every preset at the first save.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    d = JsonNode.Parse(File.ReadAllText(Path_)) as JsonObject ?? new JsonObject();
                    return;
                }
                catch (IOException e) when (attempt < 5)
                {
                    Log.Write("settings busy (" + e.Message + "), retrying");
                    System.Threading.Thread.Sleep(200);
                }
                catch (Exception e)
                {
                    // Unreadable: keep it beside the new one, so nothing is lost for good.
                    Log.Write("settings load failed: " + e.Message + " -- kept as settings.bad.json, starting fresh");
                    try { File.Copy(Path_, System.IO.Path.Combine(Dir, "settings.bad.json"), true); } catch { }
                    d = new JsonObject();
                    return;
                }
            }
        }

        /// Writes are debounced and happen on a timer thread, never under `gate`. The keyboard
        /// hook and the forwarding worker read settings too, and neither may ever wait on a
        /// disk -- a slow write, or a virus scanner holding the file, would stall the input
        /// path for as long as it lasted.
        static System.Threading.Timer flushTimer;
        static bool dirty;

        static void Flush()
        {
            dirty = true;
            if (flushTimer == null)
                flushTimer = new System.Threading.Timer(_ => FlushNow(), null, 250, System.Threading.Timeout.Infinite);
            else
                flushTimer.Change(250, System.Threading.Timeout.Infinite);
        }

        /// Write now if anything changed. Called by the timer, and on quit.
        public static void FlushNow()
        {
            // The snapshot is taken inside the write lock, so an older one can never be written
            // after a newer one.
            lock (writeGate)
            {
                string json;
                lock (gate)
                {
                    if (!dirty) return;
                    dirty = false;
                    json = d.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }
                try
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = Path_ + ".tmp";
                    File.WriteAllText(tmp, json);
                    // Replace rather than truncate in place, so a crash mid-write cannot leave
                    // an empty settings file behind.
                    if (File.Exists(Path_)) File.Replace(tmp, Path_, null); else File.Move(tmp, Path_);
                }
                catch (Exception e)
                {
                    // Still unsaved: try again shortly, and again on quit.
                    Log.Write("settings save failed: " + e.Message);
                    lock (gate) { dirty = true; }
                    flushTimer?.Change(2000, System.Threading.Timeout.Infinite);
                }
            }
        }

        static readonly object writeGate = new object();

        // MARK: - Primitives

        public static bool Has(string key) { lock (gate) return d.ContainsKey(key); }

        public static void Remove(string key)
        {
            lock (gate) { if (d.Remove(key)) Flush(); }
        }

        public static bool GetBool(string key, bool fallback)
        {
            lock (gate)
            {
                try { return d.TryGetPropertyValue(key, out var v) && v != null ? v.GetValue<bool>() : fallback; }
                catch { return fallback; }
            }
        }

        public static void SetBool(string key, bool value) { lock (gate) { d[key] = value; Flush(); } }

        public static double GetDouble(string key, double fallback)
        {
            lock (gate)
            {
                try { return d.TryGetPropertyValue(key, out var v) && v != null ? v.GetValue<double>() : fallback; }
                catch { return fallback; }
            }
        }

        public static void SetDouble(string key, double value) { lock (gate) { d[key] = value; Flush(); } }

        public static string GetString(string key, string fallback = null)
        {
            lock (gate)
            {
                try { return d.TryGetPropertyValue(key, out var v) && v != null ? v.GetValue<string>() : fallback; }
                catch { return fallback; }
            }
        }

        public static void SetString(string key, string value)
        {
            lock (gate) { if (value == null) d.Remove(key); else d[key] = value; Flush(); }
        }

        static double[] GetNumbers(string key, int n)
        {
            lock (gate)
            {
                try
                {
                    if (!d.TryGetPropertyValue(key, out var v) || v is not JsonArray a || a.Count != n) return null;
                    return a.Select(x => x.GetValue<double>()).ToArray();
                }
                catch { return null; }
            }
        }

        static void SetNumbers(string key, double[] v)
        {
            lock (gate)
            {
                if (v == null) d.Remove(key);
                else d[key] = new JsonArray(v.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());
                Flush();
            }
        }

        // MARK: - Rectangles

        /// Screen rectangles are stored as x,y,w,h in Windows virtual-desktop pixels, which
        /// already run y-down from the primary monitor's top-left — no flipping anywhere.
        static Rectangle? Rect(string key)
        {
            var v = GetNumbers(key, 4);
            if (v == null) return null;
            return new Rectangle((int)v[0], (int)v[1], (int)v[2], (int)v[3]);
        }

        static void Store(string key, Rectangle? r) =>
            SetNumbers(key, r.HasValue
                ? new double[] { r.Value.X, r.Value.Y, r.Value.Width, r.Value.Height } : null);

        public static Rectangle? Preset(string name) => Rect("preset." + name);
        public static void SetPreset(string name, Rectangle? r) => Store("preset." + name, r);

        public static Rectangle? Region
        {
            get => Rect("region");
            set => Store("region", value);
        }

        public static string ActivePreset
        {
            get => GetString("activePreset");
            set => SetString("activePreset", value);
        }

        public static Point? OverlayOrigin
        {
            get
            {
                var v = GetNumbers("overlay", 2);
                return v == null ? (Point?)null : new Point((int)v[0], (int)v[1]);
            }
            set => SetNumbers("overlay", value.HasValue
                ? new double[] { value.Value.X, value.Value.Y } : null);
        }

        // MARK: - Overlay behaviour

        /// The header reveals on hover by default, so nothing sits permanently in the place
        /// a missed raid-frame click would land.
        public static HeaderMode HeaderMode
        {
            get => HeaderModes.Parse(GetString("headerMode"));
            set => SetString("headerMode", value.ToString().ToLowerInvariant());
        }

        /// Number keys pressed while hovering the overlay act on the hovered frame.
        public static bool ForwardKeys
        {
            get => GetBool("forwardKeys", true);
            set => SetBool("forwardKeys", value);
        }

        /// Deliver forwarded keys straight to the target window with PostMessage instead of
        /// focusing it first. On by default: the focus-flip alternative hands the other
        /// character your keyboard for ~150ms, long enough for held movement keys to leak.
        public static bool KeysViaPid
        {
            get => GetBool("keysViaPid", true);
            set => SetBool("keysViaPid", value);
        }

        /// Route *mouse* clicks with PostMessage too, instead of warping the real cursor.
        /// Off by default, same as the macOS build's --pid: warp is the path known to work.
        /// Unlike macOS, posted mouse messages are not dead on arrival here, so this is
        /// worth trying if warping disturbs your camera.
        public static bool ClicksViaPid
        {
            get => GetBool("clicksViaPid", false);
            set => SetBool("clicksViaPid", value);
        }

        /// What the mirror shows: "window" (the game window, which keeps showing while another
        /// window covers it) or "screen" (whatever is at that spot on screen). Windows defaults
        /// to the window: most players Alt+Tab between clients on one monitor. The Mac build
        /// defaults to the screen. An old "auto" reads as the default.
        public static string MirrorMode
        {
            get => GetString("mirrorMode", "window") == "screen" ? "screen" : "window";
            set => SetString("mirrorMode", value);
        }

        /// How a click reaches a window that is covered: "post" sends it to the window directly,
        /// "front" brings the window forward for the moment of the click.
        public static string HiddenClicks
        {
            get => GetString("hiddenClicks", "post") == "front" ? "front" : "post";
            set => SetString("hiddenClicks", value);
        }

        /// The program of the window last mirrored, for finding it again
        /// after it restarts.
        public static string BoundExe
        {
            get => GetString("boundExe");
            set => SetString("boundExe", value);
        }

        public static bool GpuBoost
        {
            get => GetBool("gpuBoost", false);
            set => SetBool("gpuBoost", value);
        }

        /// Locked means clicks pass through to the source. Unlocked means drag to reposition.
        public static bool Locked
        {
            get => GetBool("locked", true);
            set => SetBool("locked", value);
        }

        public static double Scale
        {
            get => GetDouble("scale", 1.0);
            set => SetDouble("scale", value);
        }

        public static double? Opacity
        {
            get => Has("opacity") ? GetDouble("opacity", 1.0) : (double?)null;
            set { if (value.HasValue) SetDouble("opacity", value.Value); else Remove("opacity"); }
        }

        public static void Reset()
        {
            lock (gate)
            {
                foreach (var n in PresetNames) d.Remove("preset." + n);
                foreach (var k in new[] { "region", "overlay", "activePreset", "opacity", "scale", "headerMode" })
                    d.Remove(k);
                Flush();
            }
        }

        // MARK: - Game settings

        // These were stored under "wow." before Glass stopped naming one game. Reads fall back
        // to the old key so nobody's choices vanish on upgrade, and the next write moves them
        // over; the fallback can go once no old settings file is likely to be left.
        const string OldPrefix = "wow.";
        const string Prefix = "game.";

        /// Most are recommended on; a few that can surprise you default off. Unticking any
        /// of them restores the game's own default for that setting.
        public static bool GameSetting(GameSetting s) =>
            GetBool(Prefix + s.Key, GetBool(OldPrefix + s.Key, s.DefaultOn));
        public static void SetGameSetting(string key, bool on) => SetBool(Prefix + key, on);

        /// "Name-Realm" -> role.
        public static Dictionary<string, string> GameRoles
        {
            get
            {
                lock (gate)
                {
                    var outv = new Dictionary<string, string>();
                    try
                    {
                        if (!d.TryGetPropertyValue(Prefix + "roles", out var v) || !(v is JsonObject))
                            d.TryGetPropertyValue(OldPrefix + "roles", out v);
                        if (v is JsonObject o)
                            foreach (var kv in o) outv[kv.Key] = kv.Value?.GetValue<string>() ?? "";
                    }
                    catch { }
                    return outv;
                }
            }
            set
            {
                lock (gate)
                {
                    var o = new JsonObject();
                    foreach (var kv in value) o[kv.Key] = kv.Value;
                    d[Prefix + "roles"] = o;
                    Flush();
                }
            }
        }

        // MARK: - Shortcuts

        public static Shortcut GetShortcut(Command c)
        {
            var v = GetNumbers("key." + c.ToString(), 2);
            if (v == null) return Commands.DefaultShortcut(c);
            return new Shortcut((int)v[0], (uint)v[1]);
        }

        public static void SetShortcut(Command c, Shortcut s) =>
            SetNumbers("key." + c.ToString(), new double[] { s.KeyCode, s.Mods });

        public static void ResetShortcut(Command c) => Remove("key." + c.ToString());
    }
}
