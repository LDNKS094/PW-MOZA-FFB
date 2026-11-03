#include "common.hpp"
#include "direct_output.hpp"
#include "gun_events.hpp"
#include <MinHook.h>
#include <intrin.h>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <filesystem>
#include <memory>
#include <mutex>

namespace {
using PlatformFn = void (*)(void*, int, const float*);
using MixerFn = void (*)(void*, float, bool);
using BeginSpawnFn = void* (*)(void*, void*, const void*, uint8_t, void*);
using FinishSpawnFn = void* (*)(void*, const void*);
constexpr uintptr_t PlatformRva = 0x56246a0, MixerRva = 0x6f7e0f0;
constexpr uintptr_t BeginSpawnRva = 0x6d09b70, FinishSpawnRva = 0x6d12950;
constexpr uintptr_t GateFirstRva = 0x941afc4, GateSecondRva = 0x941afc5;
HMODULE selfModule = nullptr;
uintptr_t base = 0;
PlatformFn originalPlatform = nullptr;
MixerFn originalMixer = nullptr;
BeginSpawnFn originalBeginSpawn = nullptr;
FinishSpawnFn originalFinishSpawn = nullptr;
struct Snapshot { pwffb::Values values{}; uint64_t updatedMs = 0; bool allowed = false; float eventScale = 0; uintptr_t pawn = 0; } latest;
std::mutex snapshotMutex, logMutex;
std::atomic<bool> stopRequested{false};
std::atomic<uint64_t> frames{0}, nonzeroFrames{0};
std::atomic<uint64_t> gunAtMs{0}, gunShots{0};
std::atomic<uint64_t> gunBeginCalls{0}, gunLocalBegins{0}, gunFinishCalls{0};
std::array<std::atomic<uint64_t>, 3> gunTypeShots{};
std::atomic<uint64_t> gunOutputUpdates{0};
thread_local bool inFeedbackFrame = false, framePaused = true;
thread_local void* frameController = nullptr;
thread_local pwffb::GunSpawn gunSpawn;
uintptr_t describedGunPawn = 0;
FILE* logFile = nullptr;

void log(const char* message) {
    std::lock_guard lock(logMutex);
    if (logFile) { std::fprintf(logFile, "%llu %s\n", GetTickCount64(), message); std::fflush(logFile); }
}
bool foreground() {
    DWORD pid = 0; GetWindowThreadProcessId(GetForegroundWindow(), &pid);
    return pid == GetCurrentProcessId();
}
uintptr_t localGunPawn(uint64_t now) {
    if (stopRequested.load()) return 0;
    std::lock_guard lock(snapshotMutex);
    return latest.allowed && pwffb::Fresh(now, latest.updatedMs) ? latest.pawn : 0;
}
void* beginSpawnHook(void* context, void* actorClass, const void* transform, uint8_t collision, void* owner) {
    const int path = pwffb::GunPathIndex(reinterpret_cast<uintptr_t>(_ReturnAddress()) - base, false);
    void* actor = originalBeginSpawn(context, actorClass, transform, collision, owner);
    if (path >= 0) {
        const uint64_t now = GetTickCount64();
        const uintptr_t localPawn = localGunPawn(now);
        gunBeginCalls.fetch_add(1);
        gunSpawn.begin(path, reinterpret_cast<uintptr_t>(context), reinterpret_cast<uintptr_t>(owner),
            reinterpret_cast<uintptr_t>(actor), localPawn, now);
        if (localPawn && context == reinterpret_cast<void*>(localPawn) && owner == context && actor)
            gunLocalBegins.fetch_add(1);
    }
    return actor;
}
void* finishSpawnHook(void* actor, const void* transform) {
    const int path = pwffb::GunPathIndex(reinterpret_cast<uintptr_t>(_ReturnAddress()) - base, true);
    const uintptr_t pawn = path >= 0 ? localGunPawn(GetTickCount64()) : 0;
    const int match = path >= 0 ? gunSpawn.finish(path, reinterpret_cast<uintptr_t>(actor), pawn, GetTickCount64()) : -1;
    void* result = originalFinishSpawn(actor, transform);
    if (path >= 0) gunFinishCalls.fetch_add(1);
    if (match >= 0 && result == actor && pawn == localGunPawn(GetTickCount64())) {
        gunAtMs.store(GetTickCount64()); gunShots.fetch_add(1);
        if (gunTypeShots[match].fetch_add(1) == 0) {
            char text[160];
            std::snprintf(text, sizeof(text), "gun_fire_confirmed type=%s; local cannon spawn finished", pwffb::GunPaths[match].name);
            log(text);
        }
    }
    return result;
}
void mixerHook(void* controller, float delta, bool paused) {
    const bool previousFrame = inFeedbackFrame, previousPaused = framePaused;
    void* previousController = frameController;
    frameController = controller;
    inFeedbackFrame = true; framePaused = paused;
    originalMixer(controller, delta, paused);
    inFeedbackFrame = previousFrame; framePaused = previousPaused;
    frameController = previousController;
}
void platformHook(void* application, int controller, const float* values) {
    pwffb::Values copy{};
    if (controller == 0 && values) std::memcpy(copy.data(), values, sizeof(copy));
    originalPlatform(application, controller, values);
    if (controller != 0) return;
    try {
        const bool valid = values && pwffb::Valid(copy);
        const bool feedbackEnabled = inFeedbackFrame && frameController && (static_cast<uint8_t*>(frameController)[0x448] & 0x20) != 0;
        const float eventScale = feedbackEnabled ? *reinterpret_cast<const float*>(static_cast<uint8_t*>(frameController) + 0x44c) : 0;
        const bool allowed = valid && feedbackEnabled && std::isfinite(eventScale) && eventScale > 0 && !framePaused && !stopRequested.load() && foreground()
            && pwffb::PlatformAllows(*reinterpret_cast<const uint8_t*>(base + GateFirstRva) != 0,
                                    *reinterpret_cast<const uint8_t*>(base + GateSecondRva) != 0);
        frames.fetch_add(1, std::memory_order_relaxed);
        if (valid && std::any_of(copy.begin(), copy.end(), [](float v) { return v != 0; })) nonzeroFrames.fetch_add(1, std::memory_order_relaxed);
        // Only the current local controller's FlyingPawn may own a cannon event.
        uintptr_t pawn = allowed && frameController ? *reinterpret_cast<uintptr_t*>(static_cast<uint8_t*>(frameController) + 0x250) : 0;
        bool flying = pawn && *reinterpret_cast<uintptr_t*>(pawn) == base + 0x7dbccf0;
        const uint64_t now = GetTickCount64();
        if (flying && pawn != describedGunPawn) {
            describedGunPawn = pawn;
            log("gun_source_ready; local FlyingPawn; three cannon spawn/finish paths; no ammo polling");
        }
        std::lock_guard lock(snapshotMutex);
        if (latest.pawn != (flying ? pawn : 0)) gunAtMs.store(0);
        latest = {copy, now, allowed, eventScale, flying ? pawn : 0};
    } catch (...) { stopRequested.store(true); }
}
bool gunSignaturesMatch() {
    const uint8_t begin[] = {0x48,0x89,0x5c,0x24,0x08,0x48,0x89,0x6c,0x24,0x10,0x48,0x89,0x74,0x24,0x18,0x48};
    const uint8_t finish[] = {0x40,0x53,0x48,0x83,0xec,0x20,0x48,0x8b,0xd9,0x48,0x85,0xc9,0x74,0x0b,0x45,0x33};
    if (std::memcmp(reinterpret_cast<void*>(base + BeginSpawnRva), begin, sizeof(begin)) != 0
        || std::memcmp(reinterpret_cast<void*>(base + FinishSpawnRva), finish, sizeof(finish)) != 0) return false;
    for (const auto& path : pwffb::GunPaths) {
        for (const auto& site : {std::pair{path.beginReturn, BeginSpawnRva}, std::pair{path.finishReturn, FinishSpawnRva}}) {
            const auto* call = reinterpret_cast<const uint8_t*>(base + site.first - 5);
            int32_t displacement = 0;
            std::memcpy(&displacement, call + 1, sizeof(displacement));
            if (*call != 0xe8 || static_cast<int64_t>(site.first) + displacement != static_cast<int64_t>(site.second)) return false;
        }
    }
    return true;
}
bool install() {
    if (MH_Initialize() != MH_OK) return false;
    void* platform = reinterpret_cast<void*>(base + PlatformRva);
    void* mixer = reinterpret_cast<void*>(base + MixerRva);
    void* begin = reinterpret_cast<void*>(base + BeginSpawnRva);
    void* finish = reinterpret_cast<void*>(base + FinishSpawnRva);
    if (MH_CreateHook(mixer, reinterpret_cast<void*>(&mixerHook), reinterpret_cast<void**>(&originalMixer)) != MH_OK
        || MH_CreateHook(platform, reinterpret_cast<void*>(&platformHook), reinterpret_cast<void**>(&originalPlatform)) != MH_OK
        || MH_CreateHook(begin, reinterpret_cast<void*>(&beginSpawnHook), reinterpret_cast<void**>(&originalBeginSpawn)) != MH_OK
        || MH_CreateHook(finish, reinterpret_cast<void*>(&finishSpawnHook), reinterpret_cast<void**>(&originalFinishSpawn)) != MH_OK
        || MH_QueueEnableHook(mixer) != MH_OK || MH_QueueEnableHook(platform) != MH_OK
        || MH_QueueEnableHook(begin) != MH_OK || MH_QueueEnableHook(finish) != MH_OK || MH_ApplyQueued() != MH_OK) {
        stopRequested.store(true); return false;
    }
    return true;
}
uint64_t nowUs() {
    return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
}
struct OwnerState {
    uint32_t pid, version;
    volatile uint64_t heartbeatMs, damageAtMs;
    volatile uint32_t damageStrengthQ16;
    volatile uint32_t sequence;
};
static_assert(sizeof(OwnerState) == 32 && offsetof(OwnerState, damageAtMs) == 16 && offsetof(OwnerState, damageStrengthQ16) == 24);
struct Owner {
    HANDLE mapping = nullptr, process = nullptr;
    const OwnerState* state = nullptr;
    mutable uint64_t damageAt = 0;
    mutable uint32_t damageStrength = 0;
    bool open(DWORD gamePid) {
        mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, EventName(L"Owner", gamePid).c_str());
        if (!mapping) return false;
        state = static_cast<const OwnerState*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, sizeof(OwnerState)));
        if (!state || state->version != 3 || !state->pid || state->pid == gamePid) return false;
        process = OpenProcess(SYNCHRONIZE, FALSE, state->pid);
        return process && WaitForSingleObject(process, 0) == WAIT_TIMEOUT;
    }
    bool fresh(uint64_t now) const {
        const uint64_t heartbeat = state->heartbeatMs;
        return heartbeat && now >= heartbeat && now - heartbeat <= 1000;
    }
    pwffb::Choice damage(uint64_t now, float scale) const {
        const uint32_t before = state->sequence;
        MemoryBarrier();
        const uint64_t at = state->damageAtMs;
        const uint32_t strength = state->damageStrengthQ16;
        MemoryBarrier();
        const uint32_t after = state->sequence;
        if (!(before & 1) && before == after) { damageAt = at; damageStrength = strength; }
        return pwffb::ImpactChoice(now, damageAt, damageStrength, scale);
    }
    ~Owner() {
        if (state) UnmapViewOfFile(state);
        if (mapping) CloseHandle(mapping);
        if (process) CloseHandle(process);
    }
};
void outputLoop(HANDLE stopEvent, const Owner& owner) {
    auto output = std::make_unique<pwffb::DirectOutput>();
    bool prepared = false, faultReported = false;
    uint64_t lastStatus = 0;
    HANDLE signals[]{stopEvent, owner.process};
    while (WaitForMultipleObjects(2, signals, FALSE, 10) == WAIT_TIMEOUT && !stopRequested.load()) {
        Snapshot snapshot;
        { std::lock_guard lock(snapshotMutex); snapshot = latest; }
        const uint64_t now = GetTickCount64();
        if (!owner.fresh(now)) { log("owner_heartbeat_stale; stopping output"); break; }
        const bool allowed = snapshot.allowed && pwffb::Fresh(now, snapshot.updatedMs);
        if (!prepared && allowed) {
            prepared = true;
            if (output->open()) {
                char text[180];
                std::snprintf(text, sizeof(text), "device=MOZA_AB6 backend=DirectInput axes=%lu gain=1.5 large_period_ms=70 small_period_ms=60 lease_ms=150", output->axisCount());
                log(text);
            }
        }
        if (prepared && !output->failed()) {
            // Device acquisition may block; do not start an old sample after a stop or focus change.
            { std::lock_guard lock(snapshotMutex); snapshot = latest; }
            const bool playAllowed = snapshot.allowed && pwffb::Fresh(GetTickCount64(), snapshot.updatedMs)
                && owner.fresh(GetTickCount64()) && WaitForSingleObject(owner.process, 0) == WAIT_TIMEOUT
                && !stopRequested.load() && WaitForSingleObject(stopEvent, 0) == WAIT_TIMEOUT && foreground();
            const bool wasRunning = output->running();
            pwffb::Command command;
            auto extra = owner.damage(GetTickCount64(), snapshot.eventScale);
            const auto gun = pwffb::GunChoice(GetTickCount64(), gunAtMs.load(), snapshot.eventScale);
            if (gun.magnitude > extra.magnitude) extra = gun;
            output->tick(snapshot.values, playAllowed, nowUs(), command, extra);
            if (command.kind == pwffb::Command::Parameters && command.source == 2) gunOutputUpdates.fetch_add(1);
            if (wasRunning != output->running()) {
                char text[220];
                std::snprintf(text, sizeof(text), "%s source=%s input=[%.6f,%.6f,%.6f,%.6f] period_ms=%u magnitude=%u",
                    output->running() ? "effect_start" : "effect_stop", command.source == 4 ? "plane_destroyed" : command.source == 3 ? "heavy_damage" : command.source == 2 ? "gun_spawn_finished" : command.source == 1 ? "health_loss" : "native",
                    snapshot.values[0], snapshot.values[1], snapshot.values[2], snapshot.values[3], command.periodMs, command.magnitude);
                log(text);
            }
        }
        if (output->failed() && !faultReported) {
            faultReported = true;
            log(("output_fault_latched " + output->failure() + "; owned effect closed; restart game after diagnosis").c_str());
        }
        if (now - lastStatus >= 2000) {
            char text[400];
            std::snprintf(text, sizeof(text), "frames=%llu nonzero=%llu gun_begin=%llu gun_local_begin=%llu gun_finish=%llu gun_shots=%llu gun_updates=%llu gun_gp=%llu gun_gau=%llu gun_base=%llu age_ms=%llu allowed=%d running=%d stopped=%d",
                frames.load(), nonzeroFrames.load(), gunBeginCalls.load(), gunLocalBegins.load(), gunFinishCalls.load(), gunShots.load(), gunOutputUpdates.load(),
                gunTypeShots[0].load(), gunTypeShots[1].load(), gunTypeShots[2].load(), snapshot.updatedMs ? now - snapshot.updatedMs : 0,
                static_cast<int>(allowed), output->running() ? 1 : 0, static_cast<int>(stopRequested.load()));
            log(text); lastStatus = now;
        }
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
    stopRequested.store(true);
    output.reset(); // Device operations and release remain on their owning worker thread.
    log("stopped; owned effect and DirectInput references released");
}
DWORD workerBody() {
    const DWORD pid = GetCurrentProcessId();
    const auto directory = std::filesystem::path(ModulePath(selfModule)).parent_path();
    _wfopen_s(&logFile, (directory / (L"PWNativeFeedback-" + std::to_wstring(pid) + L".log")).c_str(), L"w");
    const auto image = ModulePath();
    if (_wcsicmp(std::filesystem::path(image).filename().c_str(), GameName) != 0 || FileHash(image) != ExpectedHash) {
        log("image_identity_mismatch; no hooks or device output"); return 0;
    }
    base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const uint8_t signature[] = {0x48,0x89,0x6c,0x24,0x18,0x48,0x89,0x7c,0x24,0x20,0x41,0x56,0x48,0x83,0xec,0x20};
    const uint8_t mixerSignature[] = {0x48,0x8b,0xc4,0x48,0x89,0x48,0x08,0x55};
    if (std::memcmp(reinterpret_cast<void*>(base + PlatformRva), signature, sizeof(signature)) != 0
        || std::memcmp(reinterpret_cast<void*>(base + MixerRva), mixerSignature, sizeof(mixerSignature)) != 0
        || !gunSignaturesMatch()) {
        log("native_signature_mismatch; no hooks or output"); return 0;
    }
    HANDLE stopEvent = CreateEventW(nullptr, TRUE, FALSE, EventName(L"Stop", pid).c_str());
    if (!stopEvent) { log("stop_event_failed; no hooks or output"); return 0; }
    Owner owner;
    if (!owner.open(pid) || !owner.fresh(GetTickCount64())) {
        log("controller_owner_missing_or_stale; no hooks or output"); CloseHandle(stopEvent); return 0;
    }
    log("initializing; HARDWARE_OUTPUT_ENABLED backend=DirectInput gain=1.5 finite_lease=150ms phase_aligned_renewal");
    if (!install()) { log("hook_install_failed; no adapter output"); CloseHandle(stopEvent); return 0; }
    if (WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) {
        stopRequested.store(true); CloseHandle(stopEvent); return 0;
    }
    log("ready; verified_platform_hook; controller=0");
    HANDLE readyEvent = OpenEventW(EVENT_MODIFY_STATE, FALSE, EventName(L"Ready", pid).c_str());
    if (readyEvent) { SetEvent(readyEvent); CloseHandle(readyEvent); }
    try { outputLoop(stopEvent, owner); }
    catch (...) { stopRequested.store(true); CloseHandle(stopEvent); throw; }
    CloseHandle(stopEvent);
    return 0;
}
DWORD WINAPI worker(void*) {
    try { return workerBody(); }
    catch (...) { stopRequested.store(true); log("worker_exception; hardware effect request expires within 150ms"); return 1; }
}
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        selfModule = instance;
        const HANDLE thread = CreateThread(nullptr, 0, worker, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread); else return FALSE;
    }
    return TRUE;
}
