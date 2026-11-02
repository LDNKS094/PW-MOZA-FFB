-- Read-only diagnostic probe; no retained UObject references or device output.
-- ClientRestart route suggested by PWCoop; this flight class was confirmed locally in P1.
local flightPath = "/Game/ProjectWingman/Blueprints/PlayerActor/Player/FlyingPawn.FlyingPawn_C"
local activeName, lastSampleSecond, lastCaptureReal
local session, sequence, tickRegistered = 0, 0, false
local latest = "none\n"
local errors = {}

local function log(message)
    print("[PWProbe] " .. message .. "\n")
end

local function reportError(tag, message)
    errors[tag] = (errors[tag] or 0) + 1
    if errors[tag] <= 5 then log(tag .. ": " .. tostring(message)) end
end

local function describe(object)
    if not object or not object:IsValid() then return "none", "none" end
    return object:GetFullName(), object:GetClass():GetFullName()
end

local function finite(value)
    return type(value) == "number" and value == value and math.abs(value) < math.huge
end

local function number(value)
    assert(finite(value), "non-finite or missing numeric value")
    return string.format("%.4f", value)
end

local function optional(pawn, name)
    local ok, value = pcall(function() return pawn[name] end)
    if ok and finite(value) then return number(value) end
    if ok and type(value) == "boolean" then return tostring(value) end
    return "unknown"
end

local function sample(context)
    if not activeName then return end
    local pawn = context:get()
    if not pawn or not pawn:IsValid() or pawn:GetFullName() ~= activeName then return end
    if not pawn:IsLocallyControlled() then latest = "none\n"; return end
    local gameplay = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    assert(gameplay and gameplay:IsValid(), "GameplayStatics unavailable")
    local gameTime, realTime = gameplay:GetTimeSeconds(pawn), gameplay:GetRealTimeSeconds(pawn)
    assert(finite(gameTime) and finite(realTime), "invalid clocks")
    if gameplay:IsGamePaused(pawn) then latest = "none\n"; lastCaptureReal = nil; return end
    if lastCaptureReal and realTime >= lastCaptureReal and realTime - lastCaptureReal < 1 / 60 then return end
    lastCaptureReal = realTime
    local health, destroyed = pawn.g_Health, pawn.g_IsDestroyed
    assert(finite(health) and type(destroyed) == "boolean", "unavailable health/destroyed state")
    if health <= 0 or destroyed then latest = "none\n"; return end
    local position, rotation = pawn:K2_GetActorLocation(), pawn:K2_GetActorRotation()
    local origin = gameplay:GetWorldOriginLocation(pawn)
    local forward = pawn["p_Current forward speed"]
    assert(finite(position.X) and finite(position.Y) and finite(position.Z)
        and finite(origin.X) and finite(origin.Y) and finite(origin.Z), "invalid position/origin")
    assert(finite(rotation.Pitch) and finite(rotation.Yaw) and finite(rotation.Roll)
        and finite(forward) and forward >= 0, "invalid rotation/speed")
    sequence = sequence + 1
    -- SI wire values; sum in double precision before scaling. Never use GetVelocity (always zero here).
    latest = string.format("v=1;s=%d;q=%d;t=%.6f;rt=%.6f;x=%.6f;y=%.6f;z=%.6f;p=%.4f;yaw=%.4f;r=%.4f;spd=%.6f;hp=%.4f;dead=0;\n",
        session, sequence, gameTime, realTime, (position.X + origin.X) / 100,
        (position.Y + origin.Y) / 100, (position.Z + origin.Z) / 100,
        rotation.Pitch, rotation.Yaw, rotation.Roll, forward / 100, health)
    local second = os.time()
    if second == lastSampleSecond then return end
    lastSampleSecond = second
    log("sample session=" .. session
        .. " seq=" .. sequence .. " game_t=" .. number(gameTime)
        .. " real_t=" .. number(realTime)
        .. " paused=" .. tostring(gameplay:IsGamePaused(pawn))
        .. " x=" .. number(position.X) .. " y=" .. number(position.Y) .. " z=" .. number(position.Z)
        .. " ox=" .. number(origin.X) .. " oy=" .. number(origin.Y) .. " oz=" .. number(origin.Z)
        .. " pitch=" .. number(rotation.Pitch) .. " yaw=" .. number(rotation.Yaw) .. " roll=" .. number(rotation.Roll)
        .. " forward_raw=" .. optional(pawn, "p_Current forward speed")
        .. " health_raw=" .. optional(pawn, "g_Health")
        .. " destroyed_raw=" .. optional(pawn, "g_IsDestroyed")
        .. " afterburner_raw=" .. optional(pawn, "a_AfterburnerFloat"))
end

local function onTick(context)
    local ok, message = pcall(sample, context)
    if not ok then latest = "none\n"; lastCaptureReal = nil; reportError("sample_failed", message) end
end

local function registerFlightTick(expectedSession)
    if session ~= expectedSession or not activeName or tickRegistered then return end
    -- /Game Blueprint hooks use callback #2 as post-hook, unlike /Script native hooks.
    local ok, result = pcall(RegisterHook, flightPath .. ":ReceiveTick", onTick)
    tickRegistered = ok
    if ok then log("flight_tick registered; capture up to 60 Hz, logs about 1 Hz; diagnostic pipe only")
    else reportError("tick_hook_failed", result) end
end

local function snapshot(context, newPawn)
    local controller, pawn = context:get(), newPawn:get()
    local controllerName = describe(controller)
    local pawnName, pawnClass = describe(pawn)
    local ok, isLocal = pcall(function() return controller:IsLocalController() end)
    log("client_restart controller=" .. controllerName
        .. " local=" .. (ok and tostring(isLocal) or "unknown")
        .. " pawn=" .. pawnName .. " class=" .. pawnClass)
    if ok and isLocal == false then return end
    session = session + 1
    activeName, lastSampleSecond, lastCaptureReal = nil, nil, nil
    latest = "none\n"
    if ok and isLocal == true and pawnClass == "DynamicClass " .. flightPath then
        activeName = pawnName
        local expectedSession = session
        -- Defer registration out of the current ProcessEvent; capture strings/numbers only.
        ExecuteWithDelay(100, function()
            ExecuteInGameThread(function() registerFlightTick(expectedSession) end)
        end)
    end
end

local function onRestart(context, newPawn)
    local ok, message = pcall(snapshot, context, newPawn)
    if not ok then
        activeName = nil
        latest = "none\n"
        reportError("snapshot_failed", message)
    end
end

local ok, result = pcall(RegisterHook, "/Script/Engine.PlayerController:ClientRestart",
    function() end, onRestart)
if ok then log("loaded P3 diagnostic; ClientRestart post-hook registered; no Cockpit/device output")
else log("hook_failed: " .. tostring(result)) end

-- Only plain strings cross into the async worker. A single latest slot drops old frames instead of queuing them.
assert(type(LoopAsync) == "function", "UE4SS async timer required")
local pipe, lastSent, attempts = nil, nil, 0
LoopAsync(16, function()
    if latest == lastSent then return false end
    if not pipe then
        attempts = attempts + 1
        if attempts ~= 1 and attempts % 60 ~= 0 then return false end
        pipe = io.open("\\\\.\\pipe\\PWTelemetry", "w")
        if not pipe then return false end
        pipe:setvbuf("no")
        log("diagnostic bridge connected")
    end
    local frame = latest
    local sent, message = pcall(function() assert(pipe:write(frame), "pipe write failed") end)
    if sent then lastSent = frame
    else
        pcall(function() pipe:close() end)
        pipe, attempts = nil, 0
        reportError("pipe_disconnected", message)
    end
    return false
end)
