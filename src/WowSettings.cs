// WoW client settings, offered as console commands you copy and run yourself. Every CVar name
// here was checked against the Classic Era client binary before being offered. Glass does not
// touch the game: it only produces the text.
//
// This table is a straight port of the macOS build's, so the two cannot drift apart. Two
// entries are labelled "(Mac)" and are kept as-is rather than dropped: they are harmless on
// Windows, they document what the Mac client needed, and removing them would make the two
// builds disagree about what a setting key means.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Glass
{
    /// What a character does in the group. Settings are offered per role rather than per
    /// class: a second melee alt should get the Warrior's settings without Glass learning a
    /// new class.
    public enum WoWRole { Tank, Healer, DPS }

    public static class WoWRoles
    {
        public static readonly WoWRole[] All = { WoWRole.Tank, WoWRole.Healer, WoWRole.DPS };
        public static string Title(WoWRole r) => r == WoWRole.DPS ? "DPS" : r.ToString();
        public static string Raw(WoWRole r) => r.ToString().ToLowerInvariant();
    }

    public class WoWSetting
    {
        public string Key;
        public string Title;
        public string Detail = "";
        /// name -> value when enabled.
        public Dictionary<string, string> CVars = new Dictionary<string, string>();
        /// key -> binding command, applied when ticked. WoW names the backtick key TILDE and
        /// the middle mouse button BUTTON3.
        public Dictionary<string, string> Bindings = new Dictionary<string, string>();
        /// What those keys go back to when unticked. A key missing here is cleared instead.
        public Dictionary<string, string> OffBindings = new Dictionary<string, string>();
        public HashSet<WoWRole> Roles = new HashSet<WoWRole>(WoWRoles.All);
        public string Group = "";
        public bool DefaultOn = true;
        /// The self-cast modifier is a binding, not a CVar.
        public bool SelfCastNone = false;

        /// Applies to everybody, so it lives under Everyone rather than a role tab.
        public bool IsShared => Roles.Count == WoWRoles.All.Length;
        public bool AppliesTo(WoWRole? role) => IsShared || (role.HasValue && Roles.Contains(role.Value));
    }

    public static class Wow
    {
        static HashSet<WoWRole> Melee => new HashSet<WoWRole> { WoWRole.Tank, WoWRole.DPS };
        static HashSet<WoWRole> Healer => new HashSet<WoWRole> { WoWRole.Healer };

        static Dictionary<string, string> D(params string[] pairs)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
            return d;
        }

        public static readonly List<WoWSetting> Settings = new List<WoWSetting>
        {
            // MARK: Both characters

            new WoWSetting { Key = "autoLoot", Title = "Auto loot",
                Detail = "Looting takes everything at once, no loot window.",
                CVars = D("autoLootDefault", "1"), Group = "Looting and interacting" },
            new WoWSetting { Key = "interact", Title = "Interact with what's in front of you",
                Detail = "Bind Interact With Target; one key talks, loots or gathers. An icon shows the target.",
                CVars = D("SoftTargetInteract", "3", "SoftTargetIconInteract", "1"),
                Group = "Looting and interacting" },
            // The client's own help text: "2 = Can be anywhere in targeting area". Range is
            // left alone -- it is capped by each object's interact range anyway.
            new WoWSetting { Key = "interactArc", Title = "Interact reaches all around you",
                Detail = "Not just what you face: quest givers and loot beside or behind you count too.",
                CVars = D("SoftTargetInteractArc", "2"), Group = "Looting and interacting" },
            new WoWSetting { Key = "autoLootRate", Title = "Loot as fast as the client allows",
                Detail = "Tick rate for auto loot, in milliseconds. 0 asks for every frame; the client may clamp it.",
                CVars = D("autoLootRate", "0"), Group = "Looting and interacting" },
            new WoWSetting { Key = "interactQuestItems", Title = "Interact key uses quest items",
                Detail = "Quest items count as something the interact key can act on.",
                CVars = D("interactQuestItems", "1"), Group = "Looting and interacting" },
            new WoWSetting { Key = "questItemLeftClick", Title = "Left click uses quest items on creatures",
                Detail = "Click a relevant creature to use the quest item on it.",
                CVars = D("canUseQuestItemWithLeftClick", "1"), Group = "Looting and interacting" },

            new WoWSetting { Key = "selfCast", Title = "No self-cast modifier",
                Detail = "Otherwise Alt casts on yourself, so Clique alt-binds never reach the frame.",
                CVars = D(), Group = "Casting and targeting", SelfCastNone = true },
            new WoWSetting { Key = "keyDown", Title = "Cast on key press, not release",
                Detail = "Saves the time you hold the key down.",
                CVars = D("ActionButtonUseKeyDown", "1"), Group = "Casting and targeting" },
            new WoWSetting { Key = "sticky", Title = "Keep target when clicking the ground",
                Detail = "A stray click on terrain no longer drops your target.",
                CVars = D("deselectOnClick", "0"), Group = "Casting and targeting" },
            new WoWSetting { Key = "secureAbility", Title = "Ignore an accidental double click on an aura",
                Detail = "A doubled click cannot toggle an aura off. You forward clicks, so this can happen.",
                CVars = D("secureAbilityToggle", "1"), Group = "Casting and targeting" },

            new WoWSetting { Key = "strafe", Title = "Q and E strafe instead of turning",
                Detail = "Turning with the keyboard is slower than the mouse; strafing is what you want bound.",
                CVars = D(), Bindings = D("Q", "STRAFELEFT", "E", "STRAFERIGHT"),
                OffBindings = D("Q", "TURNLEFT", "E", "TURNRIGHT"), Group = "Movement keys" },
            new WoWSetting { Key = "autorun", Title = "Auto-run on the backtick key",
                Detail = "WoW calls that key TILDE. Unticking clears it rather than guessing your old bind.",
                CVars = D(), Bindings = D("TILDE", "TOGGLEAUTORUN"), Group = "Movement keys" },

            new WoWSetting { Key = "swingTimer", Title = "Show the swing timer",
                Detail = "New in 1.60: weapon swing timing without an addon.",
                CVars = D("showSwingTimer", "1"), Group = "Combat readouts" },
            new WoWSetting { Key = "threatNumeric", Title = "Numeric threat on target and focus",
                Detail = "A number rather than a bar, on both frames.",
                CVars = D("threatShowNumeric", "1"), Group = "Combat readouts" },

            new WoWSetting { Key = "damageMeter", Title = "Built-in damage meter",
                Detail = "New in 1.60, no addon needed. Resets when you enter a new instance.",
                CVars = D("damageMeterEnabled", "1", "damageMeterResetOnNewInstance", "1"),
                Group = "Combat readouts" },
            new WoWSetting { Key = "cooldownViewer", Title = "Cooldown viewer",
                Detail = "New in 1.60: your cooldowns as a tracked row rather than squinting at action bars.",
                CVars = D("cooldownViewerEnabled", "1"), Group = "Combat readouts" },
            new WoWSetting { Key = "combatWarnings", Title = "Combat warnings and boss timeline",
                Detail = "The client's own words: \"combat warning UI functionality such as the boss timeline or warnings displays\".",
                CVars = D("combatWarningsEnabled", "1"), Group = "Combat readouts" },
            new WoWSetting { Key = "assistedCombat", Title = "Highlight the next suggested spell",
                Detail = "Single-Button Assistant's highlight. Confirmed present in this client.",
                CVars = D("assistedCombatHighlight", "1"), Group = "Combat readouts" },

            new WoWSetting { Key = "pings", Title = "Ping system, on the middle mouse button",
                Detail = "BUTTON3 opens the ping wheel. TOGGLEPINGLISTENER is the real command, read from the client's own binding list.",
                CVars = D("enablePings", "1", "showPingsInChat", "1", "showPingsOnRaidFrames", "1"),
                Bindings = D("BUTTON3", "TOGGLEPINGLISTENER"), Group = "Pings" },

            new WoWSetting { Key = "combinedBags", Title = "One combined bag frame",
                Detail = "All bags in a single window instead of one frame each.",
                CVars = D("combinedBags", "1"), Group = "Interface" },

            new WoWSetting { Key = "bgFPS", Title = "Cap the background client at 30 fps",
                Detail = "The healer only needs to draw frames; the GPU goes to the character you are playing.",
                CVars = D("maxFPSBk", "30"), Group = "Two clients at once" },
            new WoWSetting { Key = "bgSound", Title = "Sound only from the focused client",
                Detail = "No doubled combat audio from two clients.",
                CVars = D("Sound_EnableSoundWhenGameIsInBG", "0"), Group = "Two clients at once" },
            new WoWSetting { Key = "errorSpeech", Title = "Mute spoken errors",
                Detail = "No \"Not enough mana\" voice lines, twice over.",
                CVars = D("Sound_EnableErrorSpeech", "0"), Group = "Two clients at once" },
            new WoWSetting { Key = "viewDistance", Title = "Maximum view distance",
                Detail = "Your own pre-reset value. Resetting the graphics options drags this down with the quality preset.",
                CVars = D("graphicsViewDistance", "10"), Group = "Two clients at once" },
            // Both of these came from the Mac client and are kept so the two builds agree on
            // what each key means. Shadow Quality Low is 0, Secondary Lighting Fair is 1.
            new WoWSetting { Key = "shadowLow", Title = "Shadow Quality low (Mac)",
                Detail = "On the Mac client, above Low foliage renders black and a green tint covers everything. Harmless here; off by default.",
                CVars = D("graphicsShadowQuality", "0"), Group = "Graphics", DefaultOn = false },
            // Never raise this one, and never offer a higher value: on the Mac beta, above
            // Fair it drops the compositor at world-load.
            new WoWSetting { Key = "secondaryLightingFair", Title = "Secondary Lighting no higher than Fair (Mac)",
                Detail = "A Mac-only crash workaround. Harmless here; off by default.",
                CVars = D("giQuality", "1"), Group = "Graphics", DefaultOn = false },

            new WoWSetting { Key = "zoom", Title = "Maximum camera zoom-out",
                Detail = "More of the pull in view while tanking.",
                CVars = D("cameraDistanceMaxZoomFactor", "2.6"), Group = "Camera" },
            // Verified on the live client: this is the one that stops the camera re-centring,
            // and the client's own note says it can override the other camera settings.
            new WoWSetting { Key = "cameraFree", Title = "Camera stays where you put it",
                Detail = "Stops the camera swinging back to centre behind you. Ticked means CameraKeepCharacterCentered 0.",
                CVars = D("CameraKeepCharacterCentered", "0"), Group = "Camera" },
        };

        /// Role-specific settings, appended after construction so the role sets stay readable.
        static Wow()
        {
            // MARK: Tank and DPS

            Settings.Add(new WoWSetting { Key = "m.autoInteract", Title = "Walk to the interact target automatically",
                Detail = "Loot without closing the gap yourself. Moves your character, so it is a real change.",
                CVars = D("autoInteract", "1"), Roles = Melee, Group = "Tank and DPS" });
            Settings.Add(new WoWSetting { Key = "m.interactSound", Title = "Sound cue when something becomes interactable",
                Detail = "Hear that the interact key will do something without looking for the icon.",
                CVars = D("softTargettingInteractKeySound", "1"), Roles = Melee, Group = "Tank and DPS" });
            Settings.Add(new WoWSetting { Key = "m.keepAttacking", Title = "Keep auto-attacking when you switch targets",
                Detail = "Swapping targets no longer stops your swing.",
                CVars = D("stopAutoAttackOnTargetChange", "0"), Roles = Melee, Group = "Tank and DPS" });
            Settings.Add(new WoWSetting { Key = "m.targetAttacker", Title = "Target whoever attacks you",
                Detail = "Off by default: it can pull your target off the mob you meant to hold.",
                CVars = D("TargetEnemyAttacker", "1"), Roles = Melee, Group = "Tank and DPS", DefaultOn = false });
            Settings.Add(new WoWSetting { Key = "m.targetLock", Title = "Lock targets the game picks for you",
                Detail = "A target the game auto-set will not be quietly swapped out from under you.",
                CVars = D("TargetAutoLock", "1"), Roles = Melee, Group = "Tank and DPS", DefaultOn = false });

            // MARK: Healer

            Settings.Add(new WoWSetting { Key = "h.mouseoverCast", Title = "Mouseover casting",
                Detail = "New in 1.60, built in. Cast on the frame under the pointer -- the job Clique does today.",
                CVars = D("enableMouseoverCast", "1"), Roles = Healer, Group = "Healer" });
            Settings.Add(new WoWSetting { Key = "h.assistAttack", Title = "Attack after an assist",
                Detail = "Assisting the tank starts your attack too.",
                CVars = D("assistAttack", "1"), Roles = Healer, Group = "Healer" });
            Settings.Add(new WoWSetting { Key = "h.noAutoSelfCast", Title = "Never silently heal yourself instead",
                Detail = "A heal with no valid target errors rather than landing on you. One key, one outcome.",
                CVars = D("autoSelfCast", "0"), Roles = Healer, Group = "Healer" });
            Settings.Add(new WoWSetting { Key = "h.targetAutoFriend", Title = "Auto-target from helpful spells",
                Detail = "A single-target heal picks its friendly target for you.",
                CVars = D("TargetAutoFriend", "1"), Roles = Healer, Group = "Healer", DefaultOn = false });
            Settings.Add(new WoWSetting { Key = "h.interactSound", Title = "Sound cue when something becomes interactable",
                Detail = "The tank's cue, offered here too so you can compare. Silent while this client is in the background.",
                CVars = D("softTargettingInteractKeySound", "1"), Roles = Healer, Group = "Healer", DefaultOn = false });

            Settings.Add(new WoWSetting { Key = "h.raidShown", Title = "Show the raid-style party frames",
                Detail = "The frames Glass mirrors onto the other monitor.",
                CVars = D("raidOptionIsShown", "1"), Roles = Healer, Group = "Party frames" });
            Settings.Add(new WoWSetting { Key = "h.raidIncomingHeals", Title = "Incoming heals on the frames",
                Detail = "Read through a mirror, this is the difference between overhealing and not.",
                CVars = D("raidFramesDisplayIncomingHeals", "1"), Roles = Healer, Group = "Party frames" });
            Settings.Add(new WoWSetting { Key = "h.raidDispellable", Title = "Only debuffs you can dispel",
                Detail = "Filters the frames down to what you can actually act on.",
                CVars = D("raidFramesDisplayDebuffs", "1", "raidFramesDisplayOnlyDispellableDebuffs", "1"),
                Roles = Healer, Group = "Party frames" });
            Settings.Add(new WoWSetting { Key = "h.raidAggro", Title = "Aggro highlight on the frames",
                Detail = "Who pulled, visible from the other screen.",
                CVars = D("raidFramesDisplayAggroHighlight", "1"), Roles = Healer, Group = "Party frames" });
            Settings.Add(new WoWSetting { Key = "h.raidClassColor", Title = "Class colour the frames",
                Detail = "Tells the party apart at mirror scale.",
                CVars = D("raidFramesDisplayClassColor", "1"), Roles = Healer, Group = "Party frames" });
            Settings.Add(new WoWSetting { Key = "h.raidPower", Title = "Mana bars on the frames",
                CVars = D("raidFramesDisplayPowerBars", "1"), Roles = Healer, Group = "Party frames" });
            // Frame order decides which pixel is which person. Glass mirrors a fixed
            // rectangle, so a reorder silently retargets every click you have learned.
            Settings.Add(new WoWSetting { Key = "h.raidStable", Title = "Keep frame positions stable",
                Detail = "Groups stay together and pets stay out, so a name never moves to another slot.",
                CVars = D("raidOptionKeepGroupsTogether", "1", "raidOptionDisplayPets", "0"),
                Roles = Healer, Group = "Party frames" });

            Settings.Add(new WoWSetting { Key = "h.nameplateClassColor", Title = "Class colour on friendly nameplates",
                CVars = D("nameplateShowFriendlyClassColor", "1"), Roles = Healer, Group = "Nameplates" });
            Settings.Add(new WoWSetting { Key = "h.nameplateDebuffs", Title = "Debuffs on friendly nameplates",
                CVars = D("nameplateShowDebuffsOnFriendly", "1"), Roles = Healer, Group = "Nameplates" });
            Settings.Add(new WoWSetting { Key = "h.nameplatePersonalAuras", Title = "All of your own auras on nameplates",
                Detail = "Including the ones normally hidden.",
                CVars = D("nameplateShowAllPersonalAuras", "1"), Roles = Healer, Group = "Nameplates" });
            Settings.Add(new WoWSetting { Key = "h.nameplateSelf", Title = "Personal resource display",
                CVars = D("nameplateShowSelf", "1"), Roles = Healer, Group = "Nameplates" });
            Settings.Add(new WoWSetting { Key = "h.nameplateFriendlyNpcs", Title = "Nameplates on friendly NPCs",
                Detail = "Off by default: it crowds the screen you are mirroring.",
                CVars = D("nameplateShowFriendlyNpcs", "1"), Roles = Healer, Group = "Nameplates", DefaultOn = false });
            Settings.Add(new WoWSetting { Key = "h.nameplateMinions", Title = "Nameplates on friendly pets, guardians and totems",
                Detail = "Off by default, same reason.",
                CVars = D("nameplateShowFriendlyPlayerPets", "1", "nameplateShowFriendlyPlayerGuardians", "1",
                          "nameplateShowFriendlyPlayerTotems", "1"),
                Roles = Healer, Group = "Nameplates", DefaultOn = false });
        }

        public static WoWSetting Find(string key) => Settings.FirstOrDefault(s => s.Key == key);

        public static List<WoWSetting> Shared => Settings.Where(s => s.IsShared).ToList();
        public static List<WoWSetting> For(WoWRole role) =>
            Settings.Where(s => !s.IsShared && s.Roles.Contains(role)).ToList();

        // MARK: - Console commands

        /// The chat commands that apply these settings right now, without waiting for a
        /// reload. Same source of truth as the addon, so the two cannot disagree.
        public static List<string> ConsoleCommands(IEnumerable<WoWSetting> settings)
        {
            var lines = new List<string>();
            bool rebind = false;
            foreach (var s in settings)
            {
                bool on = Saved.WowSetting(s);
                foreach (var kv in s.CVars.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    // Unticked means Blizzard's default, which the console cannot express
                    // directly.
                    lines.Add(on
                        ? "/console " + kv.Key + " " + kv.Value
                        : "/run SetCVar(\"" + kv.Key + "\",GetCVarDefault(\"" + kv.Key + "\"))");
                }
                foreach (var kv in s.Bindings.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    string want = on ? kv.Value
                                     : (s.OffBindings.TryGetValue(kv.Key, out var off) ? off : "");
                    // Bindings have no console command; SetBinding with no action clears the key.
                    lines.Add(want.Length == 0
                        ? "/run SetBinding(\"" + kv.Key + "\")"
                        : "/run SetBinding(\"" + kv.Key + "\",\"" + want + "\")");
                    rebind = true;
                }
                if (s.SelfCastNone)
                {
                    lines.Add("/run SetModifiedClick(\"SELFCAST\",\"" + (on ? "NONE" : "ALT") + "\")");
                    rebind = true;
                }
            }
            if (rebind) lines.Add("/run SaveBindings(GetCurrentBindingSet())");
            return lines;
        }

        /// Just the commands for one setting, in the order they must run.
        public static List<string> CommandsFor(WoWSetting s) => ConsoleCommands(new[] { s });

        /// WoW's chat box strips newlines, so a multi-line paste arrives as one unparseable
        /// line. A macro body does accept newlines, and caps at 255 characters -- so commands
        /// are grouped into macro-sized chunks you can paste and click once each.
        public static List<string> MacroChunks(List<string> lines, int limit = 255)
        {
            var chunks = new List<string>();
            var current = "";
            foreach (var line in lines)
            {
                var candidate = current.Length == 0 ? line : current + "\n" + line;
                if (candidate.Length > limit && current.Length > 0) { chunks.Add(current); current = line; }
                else current = candidate;
            }
            if (current.Length > 0) chunks.Add(current);
            return chunks;
        }

        // MARK: - Companion addon

        /// Named GlassSetup because a well-known chat addon already owns the folder name "Glass".
        public const string AddonName = "GlassSetup";

        /// WoW install flavours (_classic_era_, _classic_beta_, ...): wherever a client is
        /// running, plus the usual install locations on every fixed drive.
        public static List<string> FlavorDirs()
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!p.ProcessName.StartsWith("Wow", StringComparison.OrdinalIgnoreCase)) continue;
                    var exe = Wnd.ProcessPath((uint)p.Id);
                    if (string.IsNullOrEmpty(exe)) continue;
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir)) dirs.Add(dir);
                }
                catch { }                              // a process we may not read; skip it
                finally { try { p.Dispose(); } catch { } }
            }

            var roots = new List<string>(RegistryRoots());
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                roots.Add(Path.Combine(drive.Name, "Program Files (x86)", "World of Warcraft"));
                roots.Add(Path.Combine(drive.Name, "Program Files", "World of Warcraft"));
                roots.Add(Path.Combine(drive.Name, "World of Warcraft"));
                roots.Add(Path.Combine(drive.Name, "Games", "World of Warcraft"));
            }
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var d in Directory.GetDirectories(root))
                    {
                        var name = Path.GetFileName(d);
                        if (name.StartsWith("_") && name.EndsWith("_")) dirs.Add(d);
                    }
                }
                catch { }
            }
            return dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// Where Battle.net says World of Warcraft lives, for an install on a drive or folder
        /// nobody would guess. InstallPath names one flavour folder; the root is its parent.
        static IEnumerable<string> RegistryRoots()
        {
            var found = new List<string>();
            void Read(string key, string value, bool parentOfFlavor)
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                    {
                        var path = (k?.GetValue(value) as string)?.TrimEnd('\\', '/');
                        if (string.IsNullOrEmpty(path)) return;
                        var name = Path.GetFileName(path);
                        if (parentOfFlavor && name.StartsWith("_") && name.EndsWith("_")) path = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(path)) found.Add(path);
                    }
                }
                catch { }
            }
            Read(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft", "InstallPath", true);
            Read(@"SOFTWARE\Blizzard Entertainment\World of Warcraft", "InstallPath", true);
            Read(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft", "InstallLocation", false);
            return found;
        }

        public static string AddonDir(string flavor) =>
            Path.Combine(flavor, "Interface", "AddOns", AddonName);

        public static bool AddonInstalled(string flavor) =>
            File.Exists(Path.Combine(AddonDir(flavor), "Core.lua"));

        // The addon's two fixed files are embedded byte for byte as the macOS build writes them
        // (addon/, extracted from the Mac build's glass.swift), rather than re-typed as C# strings.
        // Each client reads the interface number for its own flavour from the .toc's first line.
        static string AddonToc => Resource("GlassSetup.toc");
        static string AddonCore => Resource("Core.lua");

        static string Resource(string name)
        {
            using (var s = typeof(Wow).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) throw new InvalidOperationException("missing embedded resource " + name);
                using (var r = new StreamReader(s, new UTF8Encoding(false)))
                    return r.ReadToEnd();
            }
        }

        static string Quote(string s) => "\"" + s + "\"";
        static string Table(IEnumerable<KeyValuePair<string, string>> pairs) =>
            string.Join(", ", pairs.Select(p => "[" + Quote(p.Key) + "] = " + Quote(p.Value)));

        /// The ticked settings, in the shape Core.lua reads: shared first, then one block per
        /// role with what that role owns, so a role's settings can be undone elsewhere.
        public static string AddonConfigLua()
        {
            var watch = new SortedSet<string>(StringComparer.Ordinal);
            var watchBinds = new SortedSet<string>(StringComparer.Ordinal);
            var watchKeys = new SortedSet<string>(StringComparer.Ordinal);
            var selfCast = "ALT";

            Func<List<WoWSetting>, string> block = settings =>
            {
                var set = new List<KeyValuePair<string, string>>();
                var reset = new SortedSet<string>(StringComparer.Ordinal);
                var bind = new List<KeyValuePair<string, string>>();
                var ownedBind = new List<KeyValuePair<string, string>>();
                var owned = new SortedSet<string>(StringComparer.Ordinal);

                foreach (var s in settings)
                {
                    bool on = Saved.WowSetting(s);
                    foreach (var kv in s.CVars.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        watch.Add(kv.Key); owned.Add(kv.Key);
                        if (on) set.Add(kv); else reset.Add(kv.Key);
                    }
                    foreach (var kv in s.Bindings.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        watchKeys.Add(kv.Key); watchBinds.Add(kv.Value);
                        var off = s.OffBindings.TryGetValue(kv.Key, out var o) ? o : "";
                        bind.Add(new KeyValuePair<string, string>(kv.Key, on ? kv.Value : off));
                        ownedBind.Add(new KeyValuePair<string, string>(kv.Key, off));
                    }
                    if (s.SelfCastNone) selfCast = on ? "NONE" : "ALT";
                }

                return "{ set = { " + Table(set) + " }, reset = { "
                     + string.Join(", ", reset.Select(Quote)) + " },         bind = { " + Table(bind)
                     + " }, ownedBind = { " + Table(ownedBind) + " },         owned = { "
                     + string.Join(", ", owned.Select(Quote)) + " } }";
            };

            var shared = block(Shared);
            var blocks = string.Join("\n", WoWRoles.All.Select(r =>
                "        [" + Quote(WoWRoles.Raw(r)) + "] = " + block(For(r)) + ","));
            var roles = Table(Saved.WowRoles.OrderBy(k => k.Key, StringComparer.Ordinal));

            // Byte for byte what the macOS build writes: \n line breaks on every platform (Lua
            // does not mind, and the two builds' files can then be compared directly).
            return string.Join("\n", new[]
            {
                "-- Written by the Glass app. Edits here are overwritten; change settings in Glass.",
                "GlassConfig = {",
                "    shared = " + shared + ",",
                "    blocks = {",
                blocks,
                "    },",
                "    roles = { " + roles + " },",
                "    watch = { " + string.Join(", ", watch.Select(Quote)) + " },",
                "    watchBinds = { " + string.Join(", ", watchBinds.Select(Quote)) + " },",
                "    watchKeys = { " + string.Join(", ", watchKeys.Select(Quote)) + " },",
                "    selfCast = " + Quote(selfCast) + ",",
                "}",
                "",
            });
        }

        /// Install or update the addon in every flavour. Returns where it went, and failures.
        public static (List<string> installed, List<string> failed) InstallAddon()
        {
            var installed = new List<string>();
            var failed = new List<string>();
            foreach (var flavor in FlavorDirs())
            {
                var dir = AddonDir(flavor);
                try
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, AddonName + ".toc"), AddonToc, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(dir, "Core.lua"), AddonCore, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(dir, "Config.lua"), AddonConfigLua(), new UTF8Encoding(false));
                    installed.Add(flavor);
                }
                catch (Exception e) { failed.Add(flavor + ": " + e.Message); }
            }
            return (installed, failed);
        }

        static readonly object syncGate = new object();

        /// Rewrite just the choices wherever the addon is already installed. Runs off the UI
        /// thread; the lock keeps two quick ticks from writing the same file at once, and each
        /// write reads the settings afresh, so the last one to run is always current.
        public static void SyncAddonConfig()
        {
            lock (syncGate)
            {
                foreach (var flavor in FlavorDirs())
                {
                    if (!AddonInstalled(flavor)) continue;
                    try
                    {
                        File.WriteAllText(Path.Combine(AddonDir(flavor), "Config.lua"),
                                          AddonConfigLua(), new UTF8Encoding(false));
                    }
                    catch (Exception e) { Log.Write("addon sync failed for " + flavor + ": " + e.Message); }
                }
            }
        }
    }
}
