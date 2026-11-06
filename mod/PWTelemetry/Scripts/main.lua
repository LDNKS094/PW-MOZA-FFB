-- Only local-pawn values and plain strings cross into the pipe worker.
local flightPath = "/Game/ProjectWingman/Blueprints/PlayerActor/Player/FlyingPawn.FlyingPawn_C"
local activeName, lastCaptureReal, deathSent, pendingEvent
local session, sequence, tickRegistered = 0, 0, false
local latest = "none\n"
local errors = {}
local errorPath = (debug.getinfo(1, "S").source:match("^@(.+[\\/])") or "") .. "telemetry-errors.log"

local function reportError(tag, message)
    if errors[tag] then return end
    errors[tag] = true
    local text = os.date("!%Y-%m-%dT%H:%M:%SZ") .. " " .. tag .. ": " .. tostring(message) .. "\n"
    local file = io.open(errorPath, "a")
    if file then file:write(text); file:close() else print("[PWTelemetry error] " .. text) end
end
local function sample(context)
    if not activeName then return end
    local pawn = context:get()
    if not pawn or not pawn:IsValid() or pawn:GetFullName() ~= activeName then return end
    if not pawn:IsLocallyControlled() then latest = "none\n"; return end
    local gameplay = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    assert(gameplay and gameplay:IsValid(), "GameplayStatics unavailable")
    local gameTime, realTime = gameplay:GetTimeSeconds(pawn), gameplay:GetRealTimeSeconds(pawn)
    if gameplay:IsGamePaused(pawn) then latest = "none\n"; lastCaptureReal = nil; return end
    if lastCaptureReal and realTime >= lastCaptureReal and realTime - lastCaptureReal < 1 / 60 then return end
    lastCaptureReal = realTime
    local health, destroyed = pawn.g_Health, pawn.g_IsDestroyed
    assert(type(destroyed) == "boolean", "unavailable destroyed state")
    if health <= 0 or destroyed then
        if not deathSent then
            sequence = sequence + 1
            pendingEvent = string.format("v=1;event=destroyed;s=%d;q=%d;t=%.6f;rt=%.6f;\n",
                session, sequence, gameTime, realTime)
            deathSent = true
        end
        latest = "none\n"
        return
    end
    local position, rotation = pawn:K2_GetActorLocation(), pawn:K2_GetActorRotation()
    local origin = gameplay:GetWorldOriginLocation(pawn)
    local forward = pawn["p_Current forward speed"]
    sequence = sequence + 1
    latest = string.format("v=1;s=%d;q=%d;t=%.6f;rt=%.6f;x=%.6f;y=%.6f;z=%.6f;p=%.4f;yaw=%.4f;r=%.4f;spd=%.6f;hp=%.4f;dead=0;\n",
        session, sequence, gameTime, realTime, (position.X + origin.X) / 100,
        (position.Y + origin.Y) / 100, (position.Z + origin.Z) / 100,
        rotation.Pitch, rotation.Yaw, rotation.Roll, forward / 100, health)
end
local function onTick(context)
    local ok, message = pcall(sample, context)
    if not ok then latest = "none\n"; lastCaptureReal = nil; reportError("sample_failed", message) end
end
local function registerFlightTick(expectedSession)
    if session ~= expectedSession or not activeName or tickRegistered then return end
    -- Blueprint hooks use callback #2; /Script native hooks use the post callback.
    local ok, result = pcall(RegisterHook, flightPath .. ":ReceiveTick", onTick)
    tickRegistered = ok
    if not ok then reportError("tick_hook_failed", result) end
end
local function restart(context, newPawn)
    local controller, pawn = context:get(), newPawn:get()
    if not controller:IsLocalController() then return end
    session = session + 1
    activeName, lastCaptureReal, deathSent, pendingEvent = nil, nil, false, nil
    latest = "none\n"
    if pawn and pawn:IsValid() and pawn:GetClass():GetFullName() == "DynamicClass " .. flightPath then
        activeName = pawn:GetFullName()
        local expectedSession = session
        ExecuteWithDelay(100, function()
            ExecuteInGameThread(function() registerFlightTick(expectedSession) end)
        end)
    end
end
local function onRestart(context, newPawn)
    local ok, message = pcall(restart, context, newPawn)
    if not ok then activeName = nil; latest = "none\n"; reportError("restart_failed", message) end
end
local ok, result = pcall(RegisterHook, "/Script/Engine.PlayerController:ClientRestart", function() end, onRestart)
if not ok then reportError("restart_hook_failed", result) end

local pipe, lastSent, attempts = nil, nil, 0
LoopAsync(16, function()
    if latest == lastSent and not pendingEvent then return false end
    if not pipe then
        attempts = attempts + 1
        if attempts ~= 1 and attempts % 60 ~= 0 then return false end
        pipe = io.open("\\\\.\\pipe\\PWTelemetry", "w")
        if not pipe then return false end
        pipe:setvbuf("no")
    end
    local frame = pendingEvent or latest
    local sent = pcall(function() assert(pipe:write(frame), "pipe write failed") end)
    if sent then
        if pendingEvent == frame then pendingEvent = nil else lastSent = frame end
    else
        pcall(function() pipe:close() end)
        pipe, attempts = nil, 0
    end
    return false
end)
