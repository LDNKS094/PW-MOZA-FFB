#include "Runtime/ErrorLog.hpp"
#include "Runtime/OwnerChannel.hpp"
#include "Device/DirectInputVibration.hpp"
#include "Game/CannonEvents.hpp"
#include <MinHook.h>
#include <intrin.h>
#include <atomic>
#include <chrono>
#include <mutex>
#include <stdexcept>

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
std::mutex snapshotMutex;
std::atomic<bool> stopRequested{false};
std::atomic<uint64_t> gunAtMs{0};
thread_local bool inFeedbackFrame = false, framePaused = true;
thread_local void* frameController = nullptr;
thread_local pwffb::GunSpawn gunSpawn;
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
        gunSpawn.begin(path, reinterpret_cast<uintptr_t>(context), reinterpret_cast<uintptr_t>(owner),
            reinterpret_cast<uintptr_t>(actor), localPawn, now);
    }
    return actor;
}
void* finishSpawnHook(void* actor, const void* transform) {
    const int path = pwffb::GunPathIndex(reinterpret_cast<uintptr_t>(_ReturnAddress()) - base, true);
    const uint64_t before = GetTickCount64();
    const uintptr_t pawn = path >= 0 ? localGunPawn(before) : 0;
    const int match = path >= 0 ? gunSpawn.finish(path, reinterpret_cast<uintptr_t>(actor), pawn, before) : -1;
    void* result = originalFinishSpawn(actor, transform);
    const uint64_t after = GetTickCount64();
    if (match >= 0 && result == actor && pawn == localGunPawn(after)) gunAtMs.store(after);
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
        // Only the current local controller's FlyingPawn may own a cannon event.
        uintptr_t pawn = allowed ? *reinterpret_cast<uintptr_t*>(static_cast<uint8_t*>(frameController) + 0x250) : 0;
        bool flying = pawn && *reinterpret_cast<uintptr_t*>(pawn) == base + 0x7dbccf0;
        const uint64_t now = GetTickCount64();
        std::lock_guard lock(snapshotMutex);
        if (latest.pawn != (flying ? pawn : 0)) gunAtMs.store(0);
        latest = {copy, now, allowed, eventScale, flying ? pawn : 0};
    } catch (...) { stopRequested.store(true); WriteError(selfModule, "Game feedback callback failed; vibration stopped"); }
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
Snapshot feedbackSnapshot() {
    std::lock_guard lock(snapshotMutex);
    return latest;
}
void outputLoop(HANDLE stopEvent, const OwnerChannel& owner) {
    pwffb::DirectInputVibration output;
    bool prepared = false;
    HANDLE signals[]{stopEvent, owner.process};
    for (;;) {
        const DWORD signal = WaitForMultipleObjects(2, signals, FALSE, 10);
        if (signal != WAIT_TIMEOUT || stopRequested.load()) {
            if (signal == WAIT_FAILED) WriteError(selfModule, "Bridge lifetime wait failed", GetLastError());
            break;
        }
        auto snapshot = feedbackSnapshot();
        uint64_t now = GetTickCount64();
        if (!owner.fresh(now)) { WriteError(selfModule, "Bridge heartbeat expired; vibration stopped"); break; }
        if (!prepared && snapshot.allowed && pwffb::Fresh(now, snapshot.updatedMs)) {
            if (!output.open()) { WriteError(selfModule, output.failure().c_str()); break; }
            prepared = true;
            // Acquisition may block: refresh once before the first output, never replay an old sample.
            snapshot = feedbackSnapshot(); now = GetTickCount64();
            if (!owner.fresh(now) || WaitForMultipleObjects(2, signals, FALSE, 0) != WAIT_TIMEOUT) break;
        }
        if (prepared) {
            const bool allowed = snapshot.allowed && pwffb::Fresh(now, snapshot.updatedMs) && !stopRequested.load() && foreground();
            auto extra = owner.impact(now, snapshot.eventScale);
            const auto gun = pwffb::GunChoice(now, gunAtMs.load(), snapshot.eventScale);
            if (gun.magnitude > extra.magnitude) extra = gun;
            if (!output.tick(snapshot.values, allowed, nowUs(), extra)) { WriteError(selfModule, output.failure().c_str()); break; }
        }
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
    stopRequested.store(true);
    // The device destructor releases only this module's effect, on its owning thread.
}
void serveConnections(HANDLE connectEvent, HANDLE stopEvent, DWORD pid) {
    for (;;) {
        if (WaitForSingleObject(connectEvent, INFINITE) != WAIT_OBJECT_0)
            throw std::runtime_error("Bridge connection wait failed");
        ResetEvent(stopEvent);
        OwnerChannel owner;
        if (!owner.open(pid) || !owner.fresh(GetTickCount64())) continue;
        {
            std::lock_guard lock(snapshotMutex);
            latest = {};
            gunAtMs.store(0);
        }
        stopRequested.store(false);
        HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, EventName(L"Ready", pid).c_str());
        if (ready) { SetEvent(ready); CloseHandle(ready); }
        try { outputLoop(stopEvent, owner); }
        catch (...) { stopRequested.store(true); WriteError(selfModule, "Vibration session failed; output stopped"); }
    }
}
DWORD workerBody() {
    const DWORD pid = GetCurrentProcessId();
    base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (!SupportedImageSize(base)) {
        WriteError(selfModule, "Game image version mismatch"); return 0;
    }
    const uint8_t signature[] = {0x48,0x89,0x6c,0x24,0x18,0x48,0x89,0x7c,0x24,0x20,0x41,0x56,0x48,0x83,0xec,0x20};
    const uint8_t mixerSignature[] = {0x48,0x8b,0xc4,0x48,0x89,0x48,0x08,0x55};
    if (std::memcmp(reinterpret_cast<void*>(base + PlatformRva), signature, sizeof(signature)) != 0
        || std::memcmp(reinterpret_cast<void*>(base + MixerRva), mixerSignature, sizeof(mixerSignature)) != 0
        || !gunSignaturesMatch()) {
        WriteError(selfModule, "Game function signature mismatch"); return 0;
    }
    HANDLE stopEvent = CreateEventW(nullptr, TRUE, FALSE, EventName(L"Stop", pid).c_str());
    if (!stopEvent) { WriteError(selfModule, "Vibration stop event creation failed", GetLastError()); return 0; }
    HANDLE connectEvent = CreateEventW(nullptr, FALSE, FALSE, EventName(L"Connect", pid).c_str());
    if (!connectEvent) { WriteError(selfModule, "Bridge connection event creation failed", GetLastError()); CloseHandle(stopEvent); return 0; }
    stopRequested.store(true);
    try {
        if (!install()) WriteError(selfModule, "Game feedback hook installation failed");
        else serveConnections(connectEvent, stopEvent, pid);
    } catch (...) { stopRequested.store(true); CloseHandle(connectEvent); CloseHandle(stopEvent); throw; }
    CloseHandle(connectEvent);
    CloseHandle(stopEvent);
    return 0;
}
DWORD WINAPI worker(void*) {
    try { return workerBody(); }
    catch (...) { stopRequested.store(true); WriteError(selfModule, "Vibration worker failed; output stopped"); return 1; }
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
