-- Standalone Lua 5.3/5.4 regression test; never load as a game mod.
local messages, hooks, delayed, writes = {}, {}, {}, {}
local second, gameTime, realTime, pitch = 100, 10, 11, 5
local async, writeFails
local flightPath = "/Game/ProjectWingman/Blueprints/PlayerActor/Player/FlyingPawn.FlyingPawn_C"
print = function(message) messages[#messages + 1] = message end
os.time = function() return second end
RegisterHook = function(path, callback, post)
    if path:sub(1, 8) == "/Script/" then
        assert(type(callback) == "function" and type(post) == "function")
        hooks[path] = post
    else
        assert(post == nil, "Blueprint hook must use callback #2")
        hooks[path] = callback
    end
    return 1, 2
end
ExecuteWithDelay = function(delay, callback)
    assert(delay == 100)
    delayed[#delayed + 1] = callback
end
ExecuteInGameThread = function(callback) callback() end
LoopAsync = function(period, callback) assert(period == 16); async = callback end
io.open = function(path, mode)
    assert(path == [[\\.\pipe\PWTelemetry]] and mode == "w")
    return {
        setvbuf = function(_, mode) assert(mode == "no") end,
        write = function(_, line)
            if writeFails then return nil end
            writes[#writes + 1] = line
            return true
        end,
        close = function() end,
    }
end
local gameplay = {
    IsValid = function() return true end,
    GetTimeSeconds = function() return gameTime end,
    GetRealTimeSeconds = function() return realTime end,
    GetWorldOriginLocation = function() return {X = 100000, Y = -200000, Z = 30000} end,
    IsGamePaused = function() return false end,
}
StaticFindObject = function(path)
    assert(path == "/Script/Engine.Default__GameplayStatics")
    return gameplay
end
local function wrap(value) return {get = function() return value end} end
local function object(name, class, isLocal)
    return {
        IsValid = function() return true end,
        GetFullName = function() return name end,
        GetClass = function() return {GetFullName = function() return class end} end,
        IsLocalController = function() return isLocal end,
        IsLocallyControlled = function() return isLocal end,
        K2_GetActorLocation = function() return {X = 100, Y = 200, Z = 300} end,
        K2_GetActorRotation = function() return {Pitch = pitch, Yaw = 10, Roll = 15} end,
        g_Health = 100, g_IsDestroyed = false, ["p_Current forward speed"] = 10000,
    }
end
local function flush()
    local work = delayed
    delayed = {}
    for _, callback in ipairs(work) do callback() end
end
local function sampleCount()
    local n = 0
    for _, line in ipairs(messages) do
        if line:find("sample session=", 1, true) then n = n + 1 end
    end
    return n
end
local function advance()
    second, gameTime, realTime = second + 1, gameTime + 1, realTime + 1
end

dofile("mod/PWProbe/Scripts/main.lua")
async()
assert(writes[#writes] == "none\n")
local restart = assert(hooks["/Script/Engine.PlayerController:ClientRestart"])
local controller = object("Controller Local", "Class PlayerController", true)
local remote = object("Controller Remote", "Class PlayerController", false)
local pawn = object("Pawn Player", "DynamicClass " .. flightPath, true)
restart(wrap(controller), wrap(pawn))
flush()
local tick = assert(hooks[flightPath .. ":ReceiveTick"])
tick(wrap(object("Pawn Other", "DynamicClass " .. flightPath, true)))
assert(sampleCount() == 0)
tick(wrap(pawn)); async()
assert(sampleCount() == 1)
assert(writes[#writes]:find("x=1001.000000;y=-1998.000000;z=303.000000;", 1, true))
assert(writes[#writes]:find("spd=100.000000;hp=100.0000;dead=0;", 1, true))
assert(messages[#messages]:find("afterburner_raw=unknown", 1, true))
local count = #writes
async(); tick(wrap(pawn)); async()
assert(#writes == count, "do not retransmit stale/duplicate samples")
gameTime, realTime = gameTime + .02, realTime + .02
tick(wrap(pawn)); async()
assert(#writes == count + 1 and sampleCount() == 1, "high rate capture, low rate logs")
advance(); pawn.g_Health = 80
tick(wrap(pawn)); async()
assert(sampleCount() == 2)
assert(writes[#writes]:find("hp=80.0000;", 1, true))
restart(wrap(remote), wrap(pawn))
advance(); tick(wrap(pawn)); async()
assert(sampleCount() == 3, "remote restart must not end local session")
pitch = 0 / 0
advance(); tick(wrap(pawn)); async()
assert(writes[#writes] == "none\n")
assert(sampleCount() == 3)
pitch = 5
advance(); pawn.g_IsDestroyed = true; tick(wrap(pawn)); async()
assert(writes[#writes] == "none\n")
pawn.g_IsDestroyed = false
advance(); tick(wrap(pawn)); async()
writeFails = true
advance(); tick(wrap(pawn)); async()
assert(messages[#messages]:find("pipe_disconnected:", 1, true))
writeFails = false
async()
assert(writes[#writes]:find("v=1;", 1, true), "reconnect retains newest frame")
restart(wrap(controller), wrap(nil)); async()
assert(writes[#writes] == "none\n")
local samples = sampleCount()
advance(); tick(wrap(pawn)); async()
assert(sampleCount() == samples, "no samples after leaving flight")
restart(wrap(controller), wrap({IsValid = function() return false end}))
assert(messages[#messages]:find("pawn=none class=none", 1, true))
restart(wrap(nil), wrap(nil))
assert(messages[#messages]:find("local=unknown", 1, true))
local before = #messages
for _ = 1, 10 do restart(nil, nil) end
assert(#messages == before + 5, "bounded error logging")
hooks, delayed = {}, {}
dofile("mod/PWProbe/Scripts/main.lua")
restart = hooks["/Script/Engine.PlayerController:ClientRestart"]
restart(wrap(controller), wrap(pawn)); restart(wrap(controller), wrap(nil)); flush()
assert(hooks[flightPath .. ":ReceiveTick"] == nil, "stale deferred registration")
RegisterHook = function() error("unavailable") end
dofile("mod/PWProbe/Scripts/main.lua")
assert(messages[#messages]:find("hook_failed:", 1, true))
io.write("PASS: lifecycle, deferred gate, SI/origin conversion, high-rate capture, duplicate suppression, death/error invalidation, async reconnect\n")
