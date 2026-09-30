
// Stand-in for the app's UserDefaults-backed Saved: defaults unless a scenario overrides them.
enum Saved {
    static var overrides: [String: Bool] = [:]
    static var wowRoles: [String: String] = [:]
    static func wowSetting(_ s: WoWSetting) -> Bool { overrides[s.key] ?? s.defaultOn }
}
enum WoWRole: String, CaseIterable {
    case tank, healer, dps
    var title: String {
        switch self {
        case .tank: return "Tank"
        case .healer: return "Healer"
        case .dps: return "DPS"
        }
    }
}

/// A WoW client setting, offered as console commands you copy and run yourself. Every CVar
/// name here was checked against the Classic Era client binary before being offered. Glass
/// does not touch the game: it only produces the text.
struct WoWSetting {
    let key: String
    let title: String
    var detail = ""
    let cvars: [String: String]          // name → value when enabled
    /// key → binding command, applied when ticked. WoW names the backtick key TILDE and
    /// the middle mouse button BUTTON3.
    var bindings: [String: String] = [:]
    /// What those keys go back to when unticked. A key missing here is cleared instead.
    var offBindings: [String: String] = [:]
    var roles = Set(WoWRole.allCases)    // every role, unless narrowed
    var group = ""                       // section heading within its tab
    var defaultOn = true                 // off by default when a setting can surprise you
    var selfCastNone = false             // the self-cast modifier is a binding, not a CVar

    /// Applies to everybody, so it lives under Everyone rather than a role tab.
    var isShared: Bool { roles.count == WoWRole.allCases.count }
    func applies(to role: WoWRole?) -> Bool {
        isShared || (role.map(roles.contains) ?? false)
    }
}

let melee: Set<WoWRole> = [.tank, .dps]

let wowSettings: [WoWSetting] = [
    // MARK: Both characters

    WoWSetting(key: "autoLoot", title: "Auto loot",
               detail: "Looting takes everything at once, no loot window.",
               cvars: ["autoLootDefault": "1"], group: "Looting and interacting"),
    WoWSetting(key: "interact", title: "Interact with what's in front of you",
               detail: "Bind Interact With Target; one key talks, loots or gathers. An icon shows the target.",
               cvars: ["SoftTargetInteract": "3", "SoftTargetIconInteract": "1"],
               group: "Looting and interacting"),
    // The client's own help text: "2 = Can be anywhere in targeting area". Range is left
    // alone — it is capped by each object's interact range anyway.
    WoWSetting(key: "interactArc", title: "Interact reaches all around you",
               detail: "Not just what you face: quest givers and loot beside or behind you count too.",
               cvars: ["SoftTargetInteractArc": "2"], group: "Looting and interacting"),
    WoWSetting(key: "autoLootRate", title: "Loot as fast as the client allows",
               detail: "Tick rate for auto loot, in milliseconds. 0 asks for every frame; the client may clamp it, and the report will say so.",
               cvars: ["autoLootRate": "0"], group: "Looting and interacting"),
    WoWSetting(key: "interactQuestItems", title: "Interact key uses quest items",
               detail: "Quest items count as something the interact key can act on.",
               cvars: ["interactQuestItems": "1"], group: "Looting and interacting"),
    WoWSetting(key: "questItemLeftClick", title: "Left click uses quest items on creatures",
               detail: "Click a relevant creature to use the quest item on it.",
               cvars: ["canUseQuestItemWithLeftClick": "1"], group: "Looting and interacting"),

    WoWSetting(key: "selfCast", title: "No self-cast modifier",
               detail: "Otherwise Alt casts on yourself, so Clique alt-binds never reach the frame.",
               cvars: [:], group: "Casting and targeting", selfCastNone: true),
    WoWSetting(key: "keyDown", title: "Cast on key press, not release",
               detail: "Saves the time you hold the key down.",
               cvars: ["ActionButtonUseKeyDown": "1"], group: "Casting and targeting"),
    WoWSetting(key: "sticky", title: "Keep target when clicking the ground",
               detail: "A stray click on terrain no longer drops your target.",
               cvars: ["deselectOnClick": "0"], group: "Casting and targeting"),
    WoWSetting(key: "secureAbility", title: "Ignore an accidental double click on an aura",
               detail: "A doubled click cannot toggle an aura off. You forward clicks, so this can happen.",
               cvars: ["secureAbilityToggle": "1"], group: "Casting and targeting"),

    WoWSetting(key: "strafe", title: "Q and E strafe instead of turning",
               detail: "Turning with the keyboard is slower than the mouse; strafing is what you want bound.",
               cvars: [:], bindings: ["Q": "STRAFELEFT", "E": "STRAFERIGHT"],
               offBindings: ["Q": "TURNLEFT", "E": "TURNRIGHT"], group: "Movement keys"),
    WoWSetting(key: "autorun", title: "Auto-run on the backtick key",
               detail: "WoW calls that key TILDE. Unticking clears it rather than guessing your old bind.",
               cvars: [:], bindings: ["TILDE": "TOGGLEAUTORUN"], group: "Movement keys"),

    WoWSetting(key: "swingTimer", title: "Show the swing timer",
               detail: "New in 1.60: weapon swing timing without an addon.",
               cvars: ["showSwingTimer": "1"], group: "Combat readouts"),
    WoWSetting(key: "threatNumeric", title: "Numeric threat on target and focus",
               detail: "A number rather than a bar, on both frames.",
               cvars: ["threatShowNumeric": "1"], group: "Combat readouts"),

    WoWSetting(key: "damageMeter", title: "Built-in damage meter",
               detail: "New in 1.60, no addon needed. Resets when you enter a new instance.",
               cvars: ["damageMeterEnabled": "1", "damageMeterResetOnNewInstance": "1"],
               group: "Combat readouts"),
    WoWSetting(key: "cooldownViewer", title: "Cooldown viewer",
               detail: "New in 1.60: your cooldowns as a tracked row rather than squinting at action bars.",
               cvars: ["cooldownViewerEnabled": "1"], group: "Combat readouts"),
    WoWSetting(key: "combatWarnings", title: "Combat warnings and boss timeline",
               detail: "The client's own words: \"combat warning UI functionality such as the boss timeline or warnings displays\".",
               cvars: ["combatWarningsEnabled": "1"], group: "Combat readouts"),
    WoWSetting(key: "assistedCombat", title: "Highlight the next suggested spell",
               detail: "Single-Button Assistant's highlight. Confirmed present in this client.",
               cvars: ["assistedCombatHighlight": "1"], group: "Combat readouts"),

    WoWSetting(key: "pings", title: "Ping system, on the middle mouse button",
               detail: "BUTTON3 opens the ping wheel. TOGGLEPINGLISTENER is the real command, read from the client's own binding list.",
               cvars: ["enablePings": "1", "showPingsInChat": "1", "showPingsOnRaidFrames": "1"],
               bindings: ["BUTTON3": "TOGGLEPINGLISTENER"], group: "Pings"),

    WoWSetting(key: "combinedBags", title: "One combined bag frame",
               detail: "All bags in a single window instead of one frame each.",
               cvars: ["combinedBags": "1"], group: "Interface"),

    WoWSetting(key: "bgFPS", title: "Cap the background client at 30 fps",
               detail: "The Priest only needs to draw frames; the GPU goes to the Warrior.",
               cvars: ["maxFPSBk": "30"], group: "Two clients at once"),
    WoWSetting(key: "bgSound", title: "Sound only from the focused client",
               detail: "No doubled combat audio from two clients.",
               cvars: ["Sound_EnableSoundWhenGameIsInBG": "0"], group: "Two clients at once"),
    WoWSetting(key: "errorSpeech", title: "Mute spoken errors",
               detail: "No \"Not enough mana\" voice lines, twice over.",
               cvars: ["Sound_EnableErrorSpeech": "0"], group: "Two clients at once"),
    WoWSetting(key: "viewDistance", title: "Maximum view distance",
               detail: "Your own pre-reset value. Resetting the graphics options drags this down with the quality preset.",
               cvars: ["graphicsViewDistance": "10"], group: "Two clients at once"),
    // Both of these are the user's own findings on an M4 Max / macOS 26.6.2, and both values
    // are what the client itself wrote to Config.wtf after setting them in Options — so the
    // scales are measured, not guessed at. Shadow Quality Low is 0, Secondary Lighting Fair
    // is 1. Earlier workarounds here (GxCompatOptionalGpuFeatures 0, a whole low-quality
    // recipe) treated the symptom by making an M4 Max pretend to be a minimum-spec GPU;
    // these two replace the lot, and everything else can run at whatever you like.
    WoWSetting(key: "shadowLow", title: "Shadow Quality low (Mac)",
               detail: "Above Low, foliage renders black and a green tint covers everything.",
               cvars: ["graphicsShadowQuality": "0"], group: "Graphics"),
    // Applying this one in the world is the documented crash: chrisdecember/wow-mac-boost hung
    // a Mac twice with /console giQuality, and the beta drops WindowServer at world-load when
    // it is above Fair. The addon only writes a CVar whose value differs, and lowering it is
    // the safe direction — but never raise it, and never offer a higher value here.
    WoWSetting(key: "secondaryLightingFair", title: "Secondary Lighting no higher than Fair (Mac)",
               detail: "Above Fair the beta crashes WindowServer at world-load, or when you apply graphics settings mid-game.",
               cvars: ["giQuality": "1"], group: "Graphics"),

    WoWSetting(key: "zoom", title: "Maximum camera zoom-out",
               detail: "More of the pull in view while tanking.",
               cvars: ["cameraDistanceMaxZoomFactor": "2.6"], group: "Camera"),
    // Verified on the live client: this is the one that stops the camera re-centring, and
    // the client's own note says it can override the other camera settings.
    WoWSetting(key: "cameraFree", title: "Camera stays where you put it",
               detail: "Stops the camera swinging back to centre behind you. Ticked means CameraKeepCharacterCentered 0.",
               cvars: ["CameraKeepCharacterCentered": "0"], group: "Camera"),

    // MARK: Tank and DPS

    WoWSetting(key: "m.autoInteract", title: "Walk to the interact target automatically",
               detail: "Loot without closing the gap yourself. Moves your character, so it is a real change.",
               cvars: ["autoInteract": "1"], roles: melee, group: "Tank and DPS"),
    WoWSetting(key: "m.interactSound", title: "Sound cue when something becomes interactable",
               detail: "Hear that the interact key will do something without looking for the icon.",
               cvars: ["softTargettingInteractKeySound": "1"], roles: melee, group: "Tank and DPS"),
    WoWSetting(key: "m.keepAttacking", title: "Keep auto-attacking when you switch targets",
               detail: "Swapping targets no longer stops your swing.",
               cvars: ["stopAutoAttackOnTargetChange": "0"], roles: melee, group: "Tank and DPS"),
    WoWSetting(key: "m.targetAttacker", title: "Target whoever attacks you",
               detail: "Off by default: it can pull your target off the mob you meant to hold.",
               cvars: ["TargetEnemyAttacker": "1"], roles: melee, group: "Tank and DPS", defaultOn: false),
    WoWSetting(key: "m.targetLock", title: "Lock targets the game picks for you",
               detail: "A target the game auto-set will not be quietly swapped out from under you.",
               cvars: ["TargetAutoLock": "1"], roles: melee, group: "Tank and DPS", defaultOn: false),

    // MARK: Healer

    WoWSetting(key: "h.mouseoverCast", title: "Mouseover casting",
               detail: "New in 1.60, built in. Cast on the frame under the pointer — the job Clique does today.",
               cvars: ["enableMouseoverCast": "1"], roles: [.healer], group: "Healer"),
    WoWSetting(key: "h.assistAttack", title: "Attack after an assist",
               detail: "Assisting the Warrior starts your attack too.",
               cvars: ["assistAttack": "1"], roles: [.healer], group: "Healer"),
    WoWSetting(key: "h.noAutoSelfCast", title: "Never silently heal yourself instead",
               detail: "A heal with no valid target errors rather than landing on you. One key, one outcome.",
               cvars: ["autoSelfCast": "0"], roles: [.healer], group: "Healer"),
    WoWSetting(key: "h.targetAutoFriend", title: "Auto-target from helpful spells",
               detail: "A single-target heal picks its friendly target for you.",
               cvars: ["TargetAutoFriend": "1"], roles: [.healer], group: "Healer", defaultOn: false),
    WoWSetting(key: "h.interactSound", title: "Sound cue when something becomes interactable",
               detail: "The Warrior's cue, offered here too so you can compare. Silent while she is in the background.",
               cvars: ["softTargettingInteractKeySound": "1"], roles: [.healer], group: "Healer",
               defaultOn: false),

    WoWSetting(key: "h.raidShown", title: "Show the raid-style party frames",
               detail: "The frames Glass mirrors onto the Warrior's monitor.",
               cvars: ["raidOptionIsShown": "1"], roles: [.healer], group: "Party frames"),
    WoWSetting(key: "h.raidIncomingHeals", title: "Incoming heals on the frames",
               detail: "Read through a mirror, this is the difference between overhealing and not.",
               cvars: ["raidFramesDisplayIncomingHeals": "1"], roles: [.healer],
               group: "Party frames"),
    WoWSetting(key: "h.raidDispellable", title: "Only debuffs you can dispel",
               detail: "Filters the frames down to what you can actually act on.",
               cvars: ["raidFramesDisplayDebuffs": "1", "raidFramesDisplayOnlyDispellableDebuffs": "1"],
               roles: [.healer], group: "Party frames"),
    WoWSetting(key: "h.raidAggro", title: "Aggro highlight on the frames",
               detail: "Who pulled, visible from the Warrior's screen.",
               cvars: ["raidFramesDisplayAggroHighlight": "1"], roles: [.healer],
               group: "Party frames"),
    WoWSetting(key: "h.raidClassColor", title: "Class colour the frames",
               detail: "Tells the party apart at mirror scale.",
               cvars: ["raidFramesDisplayClassColor": "1"], roles: [.healer],
               group: "Party frames"),
    WoWSetting(key: "h.raidPower", title: "Mana bars on the frames",
               cvars: ["raidFramesDisplayPowerBars": "1"], roles: [.healer],
               group: "Party frames"),
    // Frame order decides which pixel is which person. Glass mirrors a fixed rectangle, so
    // a reorder silently retargets every click you have learned.
    WoWSetting(key: "h.raidStable", title: "Keep frame positions stable",
               detail: "Groups stay together and pets stay out, so a name never moves to another slot.",
               cvars: ["raidOptionKeepGroupsTogether": "1", "raidOptionDisplayPets": "0"],
               roles: [.healer], group: "Party frames"),

    WoWSetting(key: "h.nameplateClassColor", title: "Class colour on friendly nameplates",
               cvars: ["nameplateShowFriendlyClassColor": "1"], roles: [.healer],
               group: "Nameplates"),
    WoWSetting(key: "h.nameplateDebuffs", title: "Debuffs on friendly nameplates",
               cvars: ["nameplateShowDebuffsOnFriendly": "1"], roles: [.healer],
               group: "Nameplates"),
    WoWSetting(key: "h.nameplatePersonalAuras", title: "All of your own auras on nameplates",
               detail: "Including the ones normally hidden.",
               cvars: ["nameplateShowAllPersonalAuras": "1"], roles: [.healer],
               group: "Nameplates"),
    WoWSetting(key: "h.nameplateSelf", title: "Personal resource display",
               cvars: ["nameplateShowSelf": "1"], roles: [.healer], group: "Nameplates"),
    WoWSetting(key: "h.nameplateFriendlyNpcs", title: "Nameplates on friendly NPCs",
               detail: "Off by default: it crowds the screen you are mirroring.",
               cvars: ["nameplateShowFriendlyNpcs": "1"], roles: [.healer],
               group: "Nameplates", defaultOn: false),
    WoWSetting(key: "h.nameplateMinions", title: "Nameplates on friendly pets, guardians and totems",
               detail: "Off by default, same reason.",
               cvars: ["nameplateShowFriendlyPlayerPets": "1",
                       "nameplateShowFriendlyPlayerGuardians": "1",
                       "nameplateShowFriendlyPlayerTotems": "1"],
               roles: [.healer], group: "Nameplates", defaultOn: false),
]


/// The ticked settings, in the shape Core.lua reads: shared first, then one block per role
/// with what that role owns, so a role's settings can be undone on the other characters.
func addonConfigLua() -> String {
    func quote(_ s: String) -> String { "\"\(s)\"" }
    func table(_ pairs: [(String, String)]) -> String {
        pairs.map { "[\(quote($0.0))] = \(quote($0.1))" }.joined(separator: ", ")
    }

    var watch = Set<String>(), watchBinds = Set<String>(), watchKeys = Set<String>()
    var selfCast = "ALT"

    /// One `set`/`reset`/`bind` block from a slice of the settings list.
    func block(_ settings: [WoWSetting]) -> String {
        var set: [(String, String)] = [], reset: [String] = []
        var bind: [(String, String)] = [], ownedBind: [(String, String)] = []
        var owned = Set<String>()
        for s in settings {
            let on = Saved.wowSetting(s)
            for (name, value) in s.cvars.sorted(by: { $0.key < $1.key }) {
                watch.insert(name); owned.insert(name)
                if on { set.append((name, value)) } else { reset.append(name) }
            }
            for (key, command) in s.bindings.sorted(by: { $0.key < $1.key }) {
                watchKeys.insert(key); watchBinds.insert(command)
                bind.append((key, on ? command : (s.offBindings[key] ?? "")))
                ownedBind.append((key, s.offBindings[key] ?? ""))
            }
            if s.selfCastNone { selfCast = on ? "NONE" : "ALT" }
        }
        return """
        { set = { \(table(set)) }, reset = { \(reset.sorted().map(quote).joined(separator: ", ")) },         bind = { \(table(bind)) }, ownedBind = { \(table(ownedBind)) },         owned = { \(owned.sorted().map(quote).joined(separator: ", ")) } }
        """
    }

    let shared = block(wowSettings.filter(\.isShared))
    let blocks = WoWRole.allCases.map { role in
        "        [\(quote(role.rawValue))] = \(block(wowSettings.filter { !$0.isShared && $0.roles.contains(role) })),"
    }.joined(separator: "\n")
    let roles = table(Saved.wowRoles.sorted { $0.key < $1.key }.map { ($0.key, $0.value) })

    return """
    -- Written by the Glass app. Edits here are overwritten; change settings in Glass.
    GlassConfig = {
        shared = \(shared),
        blocks = {
    \(blocks)
        },
        roles = { \(roles) },
        watch = { \(watch.sorted().map(quote).joined(separator: ", ")) },
        watchBinds = { \(watchBinds.sorted().map(quote).joined(separator: ", ")) },
        watchKeys = { \(watchKeys.sorted().map(quote).joined(separator: ", ")) },
        selfCast = \(quote(selfCast)),
    }

    """
}


/// Just the commands for one setting, in the order they must run.
func wowCommands(for s: WoWSetting) -> [String] { wowConsoleCommands([s]) }

/// WoW's chat box strips newlines, so a multi-line paste arrives as one unparseable line.
/// A macro body does accept newlines, and caps at 255 characters — so commands are grouped
/// into macro-sized chunks you can paste and click once each.
func wowMacroChunks(_ lines: [String], limit: Int = 255) -> [String] {
    var chunks: [String] = []
    var current = ""
    for line in lines {
        let candidate = current.isEmpty ? line : current + "\n" + line
        if candidate.count > limit && !current.isEmpty {
            chunks.append(current)
            current = line
        } else {
            current = candidate
        }
    }
    if !current.isEmpty { chunks.append(current) }
    return chunks
}

/// The chat commands that apply these settings right now, without waiting for a reload.
/// Same source of truth as the addon, so the two cannot disagree.
func wowConsoleCommands(_ settings: [WoWSetting]) -> [String] {
    var lines: [String] = []
    var rebind = false
    for s in settings {
        let on = Saved.wowSetting(s)
        for (name, value) in s.cvars.sorted(by: { $0.key < $1.key }) {
            // Unticked means Blizzard's default, which the console cannot express directly.
            lines.append(on ? "/console \(name) \(value)"
                            : "/run SetCVar(\"\(name)\",GetCVarDefault(\"\(name)\"))")
        }
        for (key, command) in s.bindings.sorted(by: { $0.key < $1.key }) {
            let want = on ? command : (s.offBindings[key] ?? "")
            // Bindings have no console command; SetBinding with no action clears the key.
            lines.append(want.isEmpty
                ? "/run SetBinding(\"\(key)\")"
                : "/run SetBinding(\"\(key)\",\"\(want)\")")
            rebind = true
        }
        if s.selfCastNone {
            lines.append("/run SetModifiedClick(\"SELFCAST\",\"\(on ? "NONE" : "ALT")\")")
            rebind = true
        }
    }
    if rebind { lines.append("/run SaveBindings(GetCurrentBindingSet())") }
    return lines
}



func dump(_ name: String) {
    print("== scenario \(name) ==")
    let tabs: [(String, [WoWSetting])] = [("Everyone", wowSettings.filter(\.isShared))]
        + WoWRole.allCases.map { r in (r.title, wowSettings.filter { !$0.isShared && $0.roles.contains(r) }) }
    for (t, list) in tabs {
        print("-- tab \(t)")
        print(wowConsoleCommands(list).joined(separator: "\n"))
        print("-- macros \(t)")
        print(wowMacroChunks(wowConsoleCommands(list)).joined(separator: "\n\n---\n\n"))
    }
    print("-- per setting")
    for s in wowSettings { print("\(s.key) [\(s.group)] shared=\(s.isShared): \(wowCommands(for: s).joined(separator: " | "))") }
    print("-- config")
    print(addonConfigLua(), terminator: "")
    print("-- end")
}

dump("defaults")
Saved.wowRoles = ["Jaysonheal-Doomhowl": "healer", "Ridou-Doomhowl": "tank", "Alt-Doomhowl": "dps"]
for s in wowSettings { Saved.overrides[s.key] = !s.defaultOn }
dump("inverted")
Saved.overrides = [:]
for (i, s) in wowSettings.enumerated() where i % 3 == 0 { Saved.overrides[s.key] = false }
dump("every third off")
