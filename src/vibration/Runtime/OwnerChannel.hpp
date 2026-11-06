#pragma once
#include "ProcessIdentity.hpp"
#include "../Effects/VibrationEnvelope.hpp"

struct OwnerState {
    uint32_t pid, version;
    volatile uint64_t heartbeatMs, impactAtMs;
    volatile uint32_t impactPacked, sequence;
};
static_assert(sizeof(OwnerState) == 32 && offsetof(OwnerState, impactAtMs) == 16 && offsetof(OwnerState, impactPacked) == 24);
struct OwnerChannel {
    HANDLE mapping = nullptr, process = nullptr;
    const OwnerState* state = nullptr;
    mutable uint64_t impactAt = 0;
    mutable uint32_t impactPacked = 0;
    bool open(DWORD gamePid) {
        mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, EventName(L"Owner", gamePid).c_str());
        if (!mapping) return false;
        state = static_cast<const OwnerState*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, sizeof(OwnerState)));
        if (!state || state->version != 3 || !state->pid) return false;
        process = OpenProcess(SYNCHRONIZE, FALSE, state->pid);
        return process && WaitForSingleObject(process, 0) == WAIT_TIMEOUT;
    }
    bool fresh(uint64_t now) const {
        const uint64_t heartbeat = state->heartbeatMs;
        return heartbeat && now >= heartbeat && now - heartbeat <= 1000;
    }
    pwffb::Choice impact(uint64_t now, float scale) const {
        const uint32_t before = state->sequence;
        MemoryBarrier();
        const uint64_t at = state->impactAtMs;
        const uint32_t packed = state->impactPacked;
        MemoryBarrier();
        if (!(before & 1) && before == state->sequence) { impactAt = at; impactPacked = packed; }
        return pwffb::ImpactChoice(now, impactAt, impactPacked, scale);
    }
    ~OwnerChannel() {
        if (state) UnmapViewOfFile(state);
        if (mapping) CloseHandle(mapping);
        if (process) CloseHandle(process);
    }
};
