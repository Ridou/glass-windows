using System;
using System.Collections.Generic;
using System.Linq;

namespace Glass
{
    // Stand-ins for the app's Saved, Log and Wnd: defaults unless a scenario overrides them.
    public static class Saved
    {
        public static Dictionary<string, bool> Overrides = new Dictionary<string, bool>();
        public static Dictionary<string, string> GameRoles = new Dictionary<string, string>();
        /// The macOS defaults, which differ from the Windows build's for exactly two settings.
        public static bool MacDefault(GameSetting s) =>
            s.Key == "shadowLow" || s.Key == "secondaryLightingFair" ? true : s.DefaultOn;
        public static bool GameSetting(GameSetting s) => Overrides.TryGetValue(s.Key, out var v) ? v : MacDefault(s);
    }
    public static class Log { public static void Write(string m) { } }
    public static class Wnd { public static string ProcessPath(uint pid) => null; }

    public static class HarnessMain
    {
        static void P(string s) { Console.Out.Write(s + "\n"); }

        static void Dump(string name)
        {
            P("== scenario " + name + " ==");
            var tabs = new List<(string, List<GameSetting>)> { ("Everyone", Game.Shared) };
            tabs.AddRange(GameRoles.All.Select(r => (GameRoles.Title(r), Game.For(r))));
            foreach (var (t, list) in tabs)
            {
                P("-- tab " + t);
                P(string.Join("\n", Game.ConsoleCommands(list)));
                P("-- macros " + t);
                P(string.Join("\n\n---\n\n", Game.MacroChunks(Game.ConsoleCommands(list))));
            }
            P("-- per setting");
            foreach (var s in Game.Settings)
                P(s.Key + " [" + s.Group + "] shared=" + (s.IsShared ? "true" : "false") + ": "
                  + string.Join(" | ", Game.CommandsFor(s)));
            P("-- end");
        }

        public static int Main()
        {
            if (System.Environment.GetEnvironmentVariable("ROUNDTRIP") is string d) { RoundTrip.Write(d); return 0; }
            Dump("defaults");
            Saved.GameRoles = new Dictionary<string, string>
                { ["Jaysonheal-Forever"] = "healer", ["Ridou-Forever"] = "tank", ["Alt-Forever"] = "dps" };
            foreach (var s in Game.Settings) Saved.Overrides[s.Key] = !Saved.MacDefault(s);
            Dump("inverted");
            Saved.Overrides.Clear();
            for (int i = 0; i < Game.Settings.Count; i++) if (i % 3 == 0) Saved.Overrides[Game.Settings[i].Key] = false;
            Dump("every third off");
            return 0;
        }
    }
}
