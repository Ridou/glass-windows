-- GlassSetup: applies the settings chosen in the Glass app at every login, and records
-- what is actually set so Glass can show it. Written by Glass; update it from there.
local f = CreateFrame("Frame")
f:RegisterEvent("PLAYER_LOGIN")
f:SetScript("OnEvent", function()
    local cfg = GlassConfig or {}
    local changed = 0

    local rebind = false
    local gxChanged = false

    local function apply(block)
        if not block then return end
        for name, value in pairs(block.set or {}) do
            local current = GetCVar(name)
            if current ~= nil and current ~= value then
                SetCVar(name, value)
                changed = changed + 1
                if name:find("^Gx") or name == "graphicsQuality" then gxChanged = true end
            end
        end
        for _, name in ipairs(block.reset or {}) do
            local current, default = GetCVar(name), GetCVarDefault(name)
            if current ~= nil and default ~= nil and current ~= default then
                SetCVar(name, default)
                changed = changed + 1
            end
        end
        -- An empty command means clear the key. Bindings are saved once, at the end.
        for key, command in pairs(block.bind or {}) do
            local current = GetBindingAction(key)
            if command == "" then
                if current and current ~= "" then
                    SetBinding(key)
                    changed, rebind = changed + 1, true
                end
            elseif current ~= command then
                SetBinding(key, command)
                changed, rebind = changed + 1, true
            end
        end
    end

    local me = UnitName("player") .. "-" .. GetRealmName()
    local role = (cfg.roles or {})[me]

    apply(cfg.shared)
    -- Undo the other role first, then apply ours. Order matters: the two roles can offer
    -- the same CVar, and resetting afterwards would wipe what we just set.
    for name, block in pairs(cfg.blocks or {}) do
        if name ~= role then apply({ reset = block.owned, bind = block.ownedBind }) end
    end
    if role then apply((cfg.blocks or {})[role]) end

    if cfg.selfCast and GetModifiedClick("SELFCAST") ~= cfg.selfCast then
        SetModifiedClick("SELFCAST", cfg.selfCast)
        changed, rebind = changed + 1, true
    end
    if rebind then SaveBindings(GetCurrentBindingSet()) end
    -- Gx* and the quality preset are latched — they show nothing until the renderer restarts.
    if gxChanged and RestartGx then RestartGx() end

    -- Flat on purpose, so Glass can read it back without a Lua parser.
    GlassStatus = GlassStatus or {}
    local report = { time = time(), selfCast = GetModifiedClick("SELFCAST"), role = role or "none" }
    for _, name in ipairs(cfg.watch or {}) do
        report["cvar:" .. name] = GetCVar(name)
        report["default:" .. name] = GetCVarDefault(name)
    end

    -- Binding command names live in FrameXML, not the client binary, so Glass cannot check
    -- them offline. Report which ones this client actually knows, and what they are bound to.
    local commands = {}
    for i = 1, GetNumBindings() do
        local cmd = GetBinding(i)
        if cmd then commands[cmd] = true end
    end
    for _, cmd in ipairs(cfg.watchBinds or {}) do
        report["hasbind:" .. cmd] = commands[cmd] and "1" or "0"
    end
    for _, key in ipairs(cfg.watchKeys or {}) do
        report["key:" .. key] = GetBindingAction(key) or ""
    end
    -- One-off discovery so the ping binding can be named rather than guessed at.
    local pings = {}
    for cmd in pairs(commands) do
        if cmd:find("PING") then pings[#pings + 1] = cmd end
    end
    table.sort(pings)
    report["pingCommands"] = table.concat(pings, " ")
    GlassStatus[me] = report

    print(("|cff7fd4ffGlass|r: %d setting%s changed (%s)."):format(
        changed, changed == 1 and "" or "s", role or "no role set"))
end)