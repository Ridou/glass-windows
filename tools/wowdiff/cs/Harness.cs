using System;
using System.Collections.Generic;
using System.Linq;

namespace Glass
{
    // Stand-ins for the app's Saved, Log and Wnd: defaults unless a scenario overrides them.
    public static class Saved
    {
        public static Dictionary<string, bool> Overrides = new Dictionary<string, bool>();
        public static Dictionary<string, string> WowRoles = new Dictionary<string, string>();
        /// The macOS defaults, which differ from the Windows build's for exactly two settings.
        public static bool MacDefault(WoWSetting s) =>
            s.Key == "shadowLow" || s.Key == "secondaryLightingFair" ? true : s.DefaultOn;
        public static bool WowSetting(WoWSetting s) => Overrides.TryGetValue(s.Key, out var v) ? v : MacDefault(s);
    }
    public static class Log { public static void Write(string m) { } }
    public static class Wnd { public static string ProcessPath(uint pid) => null; }

    public static class HarnessMain
    {
        static void P(string s) { Console.Out.Write(s + "\n"); }

        static void Dump(string name)
        {
            P("== scenario " + name + " ==");
            var tabs = new List<(string, List<WoWSetting>)> { ("Everyone", Wow.Shared) };
            tabs.AddRange(WoWRoles.All.Select(r => (WoWRoles.Title(r), Wow.For(r))));
            foreach (var (t, list) in tabs)
            {
                P("-- tab " + t);
                P(string.Join("\n", Wow.ConsoleCommands(list)));
                P("-- macros " + t);
                P(string.Join("\n\n---\n\n", Wow.MacroChunks(Wow.ConsoleCommands(list))));
            }
            P("-- per setting");
            foreach (var s in Wow.Settings)
                P(s.Key + " [" + s.Group + "] shared=" + (s.IsShared ? "true" : "false") + ": "
                  + string.Join(" | ", Wow.CommandsFor(s)));
            P("-- config");
            Console.Out.Write(Wow.AddonConfigLua());
            P("-- end");
        }

        public static int Main()
        {
            if (System.Environment.GetEnvironmentVariable("ROUNDTRIP") is string d) { RoundTrip.Write(d); return 0; }
            Dump("defaults");
            Saved.WowRoles = new Dictionary<string, string>
                { ["Jaysonheal-Doomhowl"] = "healer", ["Ridou-Doomhowl"] = "tank", ["Alt-Doomhowl"] = "dps" };
            foreach (var s in Wow.Settings) Saved.Overrides[s.Key] = !Saved.MacDefault(s);
            Dump("inverted");
            Saved.Overrides.Clear();
            for (int i = 0; i < Wow.Settings.Count; i++) if (i % 3 == 0) Saved.Overrides[Wow.Settings[i].Key] = false;
            Dump("every third off");
            return 0;
        }
    }
}
