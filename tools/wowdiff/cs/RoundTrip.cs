namespace Glass
{
    public static class RoundTrip
    {
        public static void Write(string dir)
        {
            var t = typeof(Wow);
            var toc = (string)t.GetProperty("AddonToc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            var core = (string)t.GetProperty("AddonCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "GlassSetup.toc"), toc, new System.Text.UTF8Encoding(false));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Core.lua"), core, new System.Text.UTF8Encoding(false));
        }
    }
}
