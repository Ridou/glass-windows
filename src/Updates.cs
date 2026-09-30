// Is there a newer Glass? A copy handed to a friend is never revisited on GitHub, so Glass
// asks for itself: once, a few seconds after it starts, it reads the latest release's version
// from GitHub's public API. Nothing is sent but the request. If a newer version exists, a tray
// balloon says so, and Settings > Help shows it with a button to download it.

using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Glass
{
    public static class Updates
    {
        public const string DownloadUrl = "https://github.com/Ridou/glass-windows/releases/latest/download/Glass-Windows.zip";
        public const string ReleasesUrl = "https://github.com/Ridou/glass-windows/releases";
        const string ApiUrl = "https://api.github.com/repos/Ridou/glass-windows/releases/latest";

        /// The running version, "1.2.0".
        public static string Current =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? "0.0.0";

        /// The newest release, once asked; null before that or if GitHub could not be reached.
        public static volatile string Latest;
        public static bool Newer => Latest != null && IsNewer(Latest, Current);

        /// --no-update-check, for test runs.
        public static bool Disabled;

        static bool IsNewer(string latest, string current) =>
            Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c;

        /// Ask GitHub, off the UI thread. `quiet`: no balloon, just refresh what Help shows.
        public static void Check(bool quiet = false, Action done = null)
        {
            if (Disabled) return;
            Task.Run(async () =>
            {
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("Glass-Windows/" + Current);
                        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                        var json = await http.GetStringAsync(ApiUrl);
                        using (var doc = JsonDocument.Parse(json))
                        {
                            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                            Latest = tag.TrimStart('v', 'V');
                        }
                    }
                    Log.Write("update check: running " + Current + ", newest " + Latest + (Newer ? " -- update available" : ""));
                    if (Newer && !quiet)
                        App.Defer(() => App.Tray?.Balloon("Glass " + Latest + " is available",
                            "You have " + Current + ". Open Settings > Help and click Download the Latest."));
                }
                catch (Exception e) { Log.Write("update check failed: " + e.Message); }
                if (done != null) App.Defer(done);
            });
        }
    }
}
