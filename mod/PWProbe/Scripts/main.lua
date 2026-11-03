-- Local flight telemetry and destruction events; no retained UObject references or device output.
local flightPath = "/Game/ProjectWingman/Blueprints/PlayerActor/Player/FlyingPawn.FlyingPawn_C"
local activeName, lastCaptureReal, lastState, deathSent, pendingEvent
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

local function scalar(value)
    if finite(value) then return string.format("%.4f", value) end
    if type(value) == "boolean" then return tostring(value) end
    return "unknown"
end

local function setState(reason, gameTime, realTime, health, destroyed)
    if reason ~= "active" then latest = "none\n" end
    if reason == lastState then return end
    lastState = reason
    log("state reason=" .. reason .. " session=" .. session .. " last_seq=" .. sequence
        .. " game_t=" .. scalar(gameTime) .. " real_t=" .. scalar(realTime)
        .. " health_raw=" .. scalar(health) .. " destroyed_raw=" .. scalar(destroyed))
end

local function sample(context)
    if not activeName then return end
    local pawn = context:get()
    if not pawn or not pawn:IsValid() or pawn:GetFullName() ~= activeName then return end
    if not pawn:IsLocallyControlled() then setState("not_local"); return end
    local gameplay = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    assert(gameplay and gameplay:IsValid(), "GameplayStatics unavailable")
    local gameTime, realTime = gameplay:GetTimeSeconds(pawn), gameplay:GetRealTimeSeconds(pawn)
    assert(finite(gameTime) and finite(realTime), "invalid clocks")
    if gameplay:IsGamePaused(pawn) then setState("paused", gameTime, realTime); lastCaptureReal = nil; return end
    if lastCaptureReal and realTime >= lastCaptureReal and realTime - lastCaptureReal < 1 / 60 then return end
    lastCaptureReal = realTime
    local health, destroyed = pawn.g_Health, pawn.g_IsDestroyed
    assert(finite(health) and type(destroyed) == "boolean", "unavailable health/destroyed state")
    if health <= 0 or destroyed then
        if not deathSent then
            sequence = sequence + 1
            pendingEvent = string.format("v=1;event=destroyed;s=%d;q=%d;t=%.6f;rt=%.6f;\n",
                session, sequence, gameTime, realTime)
            deathSent = true
        end
        setState(destroyed and "destroyed" or "health_depleted", gameTime, realTime, health, destroyed)
        return
    end
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
    setState("active", gameTime, realTime, health, destroyed)
end

local function onTick(context)
    local ok, message = pcall(sample, context)
    if not ok then setState("sample_failed"); lastCaptureReal = nil; reportError("sample_failed", message) end
end

local function registerFlightTick(expectedSession)
    if session ~= expectedSession or not activeName or tickRegistered then return end
    -- /Game Blueprint hooks use callback #2 as post-hook, unlike /Script native hooks.
    local ok, result = pcall(RegisterHook, flightPath .. ":ReceiveTick", onTick)
    tickRegistered = ok
    if ok then log("flight_tick registered; local telemetry up to 60 Hz")
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
    deathSent, pendingEvent = false, nil
    activeName, lastCaptureReal, lastState = nil, nil, nil
    if ok and isLocal == true and pawnClass == "DynamicClass " .. flightPath then
        setState("awaiting_tick")
        activeName = pawnName
        local expectedSession = session
        -- Defer registration out of the current ProcessEvent; capture strings/numbers only.
        ExecuteWithDelay(100, function()
            ExecuteInGameThread(function() registerFlightTick(expectedSession) end)
        end)
    else
        setState(ok and "not_flight" or "controller_unknown")
    end
end

local function onRestart(context, newPawn)
    local ok, message = pcall(snapshot, context, newPawn)
    if not ok then
        activeName = nil
        setState("snapshot_failed")
        reportError("snapshot_failed", message)
    end
end

local ok, result = pcall(RegisterHook, "/Script/Engine.PlayerController:ClientRestart",
    function() end, onRestart)
if ok then log("loaded; ClientRestart post-hook registered")
else log("hook_failed: " .. tostring(result)) end

-- Only plain strings cross into the async worker. A single latest slot drops old frames instead of queuing them.
assert(type(LoopAsync) == "function", "UE4SS async timer required")
local pipe, lastSent, attempts = nil, nil, 0
LoopAsync(16, function()
    if latest == lastSent and not pendingEvent then return false end
    if not pipe then
        attempts = attempts + 1
        if attempts ~= 1 and attempts % 60 ~= 0 then return false end
        pipe = io.open("\\\\.\\pipe\\PWTelemetry", "w")
        if not pipe then return false end
        pipe:setvbuf("no")
        log("bridge connected")
    end
    local frame = pendingEvent or latest
    local sent, message = pcall(function() assert(pipe:write(frame), "pipe write failed") end)
    if sent then
        if pendingEvent == frame then pendingEvent = nil else lastSent = frame end
    else
        pcall(function() pipe:close() end)
        pipe, attempts = nil, 0
        reportError("pipe_disconnected", message)
    end
    return false
end)
