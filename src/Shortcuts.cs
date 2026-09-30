// Header modes, rebindable commands, and the global hotkeys that drive them.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Glass
{
    /// How the header behaves when the pointer is not near the overlay.
    public enum HeaderMode
    {
        Auto,      // hidden, revealed on hover -- the default
        Pinned,    // always visible
        Hidden,    // stays away; hovering offers only a small dot to bring it back
    }

    public static class HeaderModes
    {
        public static HeaderMode Parse(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "pinned": return HeaderMode.Pinned;
                case "hidden": return HeaderMode.Hidden;
                default: return HeaderMode.Auto;
            }
        }

        public static HeaderMode Next(this HeaderMode m)
        {
            switch (m)
            {
                case HeaderMode.Auto: return HeaderMode.Pinned;
                case HeaderMode.Pinned: return HeaderMode.Hidden;
                default: return HeaderMode.Auto;
            }
        }

        public static string Label(this HeaderMode m)
        {
            switch (m)
            {
                case HeaderMode.Pinned: return "Always shown";
                case HeaderMode.Hidden: return "Hidden";
                default: return "Dots on hover";
            }
        }
    }

    public struct Shortcut : IEquatable<Shortcut>
    {
        public int KeyCode;      // a Windows virtual-key code
        public uint Mods;        // MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_WIN

        public Shortcut(int keyCode, uint mods) { KeyCode = keyCode; Mods = mods; }

        public bool Equals(Shortcut o) => o.KeyCode == KeyCode && (o.Mods & 0xF) == (Mods & 0xF);
        public override bool Equals(object o) => o is Shortcut s && Equals(s);
        public override int GetHashCode() => KeyCode * 31 + (int)(Mods & 0xF);

        public string Display
        {
            get
            {
                var s = "";
                if ((Mods & Native.MOD_CONTROL) != 0) s += "Ctrl+";
                if ((Mods & Native.MOD_ALT) != 0) s += "Alt+";
                if ((Mods & Native.MOD_SHIFT) != 0) s += "Shift+";
                if ((Mods & Native.MOD_WIN) != 0) s += "Win+";
                return s + Name(KeyCode);
            }
        }

        /// Enough of the keyboard to name anything someone would sensibly bind.
        public static string Name(int vk)
        {
            if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
            if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x6A: return "Num*";
                case 0x6B: return "Num+";
                case 0x6D: return "Num-";
                case 0x6E: return "Num.";
                case 0x6F: return "Num/";
                case 0x90: return "NumLock";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
                default: return "key" + vk;
            }
        }

        public static uint ModsFrom(Keys modifiers)
        {
            uint m = 0;
            if ((modifiers & Keys.Control) != 0) m |= Native.MOD_CONTROL;
            if ((modifiers & Keys.Alt) != 0) m |= Native.MOD_ALT;
            if ((modifiers & Keys.Shift) != 0) m |= Native.MOD_SHIFT;
            return m;
        }

        /// What is physically held right now. The low-level hook carries no modifier flags of
        /// its own, and GetKeyState would answer for the hook thread's own keyboard state, which
        /// never sees a key. GetAsyncKeyState is the physical state, and a modifier pressed before
        /// the key being decided on is already reflected in it.
        public static uint LiveMods()
        {
            uint m = 0;
            if ((Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0) m |= Native.MOD_CONTROL;
            if ((Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0) m |= Native.MOD_ALT;
            if ((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0) m |= Native.MOD_SHIFT;
            return m;
        }
    }

    /// Every rebindable action, with its default key.
    public enum Command { Preset1, Preset2, Preset3, Preset4, Pick, Overlay, Bar, Lock }

    public static class Commands
    {
        public static readonly Command[] All = (Command[])Enum.GetValues(typeof(Command));

        public static string Label(Command c)
        {
            switch (c)
            {
                case Command.Preset1: return "Use preset “" + Saved.PresetNames[0] + "”";
                case Command.Preset2: return "Use preset “" + Saved.PresetNames[1] + "”";
                case Command.Preset3: return "Use preset “" + Saved.PresetNames[2] + "”";
                case Command.Preset4: return "Use preset “" + Saved.PresetNames[3] + "”";
                case Command.Pick: return "Pick a new region";
                case Command.Overlay: return "Show / hide overlay";
                case Command.Bar: return "Cycle header visibility";
                default: return "Lock / unlock overlay";
            }
        }

        public static Shortcut DefaultShortcut(Command c)
        {
            const uint ctrlAlt = Native.MOD_CONTROL | Native.MOD_ALT;
            switch (c)
            {
                case Command.Preset1: return new Shortcut('1', ctrlAlt);
                case Command.Preset2: return new Shortcut('2', ctrlAlt);
                case Command.Preset3: return new Shortcut('3', ctrlAlt);
                case Command.Preset4: return new Shortcut('4', ctrlAlt);
                case Command.Pick: return new Shortcut('P', ctrlAlt);
                case Command.Overlay: return new Shortcut('H', ctrlAlt);
                case Command.Bar: return new Shortcut('B', ctrlAlt);
                default: return new Shortcut('L', ctrlAlt);
            }
        }

        public static void Run(Command c)
        {
            switch (c)
            {
                case Command.Preset1: App.UsePreset(Saved.PresetNames[0]); break;
                case Command.Preset2: App.UsePreset(Saved.PresetNames[1]); break;
                case Command.Preset3: App.UsePreset(Saved.PresetNames[2]); break;
                case Command.Preset4: App.UsePreset(Saved.PresetNames[3]); break;
                case Command.Pick: App.PickRegion(); break;
                case Command.Overlay: App.ToggleOverlay(); break;
                case Command.Bar: App.ToggleBar(); break;
                case Command.Lock: App.ToggleLock(); break;
            }
        }
    }

    /// RegisterHotKey rather than watching the low-level hook: a hotkey is *consumed*, so a
    /// shortcut never also reaches the game underneath. MOD_NOREPEAT means holding one down
    /// fires it once.
    ///
    /// This hidden window is also how a second launch of Glass finds the first: it has a fixed
    /// caption, and a posted WM_SHOW_SETTINGS asks the running copy to open Settings -- the
    /// Windows version of clicking a running app's Dock icon.
    public sealed class Hotkeys : NativeWindow
    {
        public const string Caption = "Glass.Main.7c1e0b52";
        public const int WM_SHOW_SETTINGS = Native.WM_APP + 10;
        const int EscId = 0x47FF;

        readonly Dictionary<int, Command> bound = new Dictionary<int, Command>();
        int nextId = 0x4700;   // 'G'
        bool suspended;
        Action onEsc;

        public Hotkeys()
        {
            CreateHandle(new CreateParams { Caption = Caption, X = -32000, Y = -32000, Width = 1, Height = 1 });
        }

        /// Drop every binding, then install the current ones. Called on any rebind.
        public void Reload()
        {
            foreach (var id in bound.Keys.ToList()) Native.UnregisterHotKey(Handle, id);
            bound.Clear();
            if (suspended) return;

            foreach (var c in Commands.All)
            {
                var s = Saved.GetShortcut(c);
                int id = nextId++;
                if (nextId > 0x47F0) nextId = 0x4700;
                if (Native.RegisterHotKey(Handle, id, s.Mods | Native.MOD_NOREPEAT, (uint)s.KeyCode))
                    bound[id] = c;
                else
                    Log.Write("could not register " + s.Display + " for " + Commands.Label(c)
                              + " -- another app owns it");
            }
        }

        /// While a new shortcut is being recorded, the old ones must not fire: pressing
        /// Ctrl+Alt+1 to record it would otherwise switch preset instead.
        public void Suspend() { suspended = true; Reload(); }
        public void Resume() { suspended = false; Reload(); }

        /// Esc as a global hotkey for as long as the region picker is up, so it cancels even
        /// when a game kept the keyboard focus the picker could not take.
        public void GrabEsc(Action cancel)
        {
            onEsc = cancel;
            Native.RegisterHotKey(Handle, EscId, Native.MOD_NOREPEAT, Native.VK_ESCAPE);
        }

        public void ReleaseEsc()
        {
            onEsc = null;
            Native.UnregisterHotKey(Handle, EscId);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                try
                {
                    if (id == EscId) onEsc?.Invoke();
                    else if (bound.TryGetValue(id, out var c)) Commands.Run(c);
                }
                catch (Exception e) { Log.Write("hotkey: " + e.Message); }
                return;
            }
            if (m.Msg == WM_SHOW_SETTINGS)
            {
                // wParam is a 1-based tab from a second launch's --settings TAB, or 0.
                int tab = m.WParam.ToInt32();
                var name = tab >= 1 && tab <= SettingsForm.TabNames.Length ? SettingsForm.TabNames[tab - 1] : null;
                try { App.ShowSettings(name); } catch (Exception e) { Log.Write("show settings: " + e.Message); }
                return;
            }
            base.WndProc(ref m);
        }
    }
}
