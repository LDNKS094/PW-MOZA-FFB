#pragma once
#include "../Effects/VibrationEnvelope.hpp"

namespace pwffb {
struct GunPath { uintptr_t beginReturn, finishReturn; };
// FireGun's three class getters resolve to these cannon classes in the supported EXE.
inline constexpr std::array<GunPath, 3> GunPaths{{
    {0x26d5fd6, 0x26d6290}, // Cannon_GP_C
    {0x26d69d6, 0x26d6cc0}, // Cannon_Gau_C
    {0x26d71c7, 0x26d74a5}  // CANNON_BASE_C
}};
inline int GunPathIndex(uintptr_t caller, bool finish) {
    for (size_t i = 0; i < GunPaths.size(); ++i)
        if (caller == (finish ? GunPaths[i].finishReturn : GunPaths[i].beginReturn)) return static_cast<int>(i);
    return -1;
}
// One deferred spawn followed by its matching finish on the same game thread.
class GunSpawn {
    uintptr_t pawn_ = 0, actor_ = 0;
    uint64_t at_ = 0;
    int path_ = -1;
public:
    void begin(int path, uintptr_t context, uintptr_t owner, uintptr_t actor, uintptr_t localPawn, uint64_t nowMs) {
        const bool local = localPawn && context == localPawn && owner == localPawn && actor;
        pawn_ = local ? localPawn : 0; actor_ = local ? actor : 0;
        at_ = local ? nowMs : 0; path_ = local ? path : -1;
    }
    int finish(int path, uintptr_t actor, uintptr_t localPawn, uint64_t nowMs) {
        const bool match = path == path_ && actor == actor_
            && localPawn == pawn_ && Fresh(nowMs, at_);
        pawn_ = actor_ = at_ = 0; path_ = -1;
        return match ? path : -1;
    }
};
}
