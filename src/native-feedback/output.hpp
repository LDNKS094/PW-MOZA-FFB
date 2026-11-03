#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>

namespace pwffb {
constexpr uint32_t LeaseMs = 150, RestartMs = 50;
constexpr float Gain = 1.5f;
using Values = std::array<float, 4>;
static_assert(sizeof(Values) == 16);
inline bool Valid(const Values& values) {
    return std::all_of(values.begin(), values.end(), [](float v) { return std::isfinite(v) && v >= 0 && v <= 1; });
}
inline bool PlatformAllows(bool first, bool second) { return !first || second; }
inline bool Fresh(uint64_t nowMs, uint64_t sourceMs) { return sourceMs && nowMs >= sourceMs && nowMs - sourceMs <= LeaseMs; }
struct Choice { uint32_t magnitude = 0, periodMs = 0, source = 0; };
inline Choice Choose(const Values& values) {
    if (!Valid(values)) return {};
    const float largeMotor = std::max(values[0], values[2]), smallMotor = std::max(values[1], values[3]);
    // Device-policy analogy to AC7's cloud (70 ms) and missile (60 ms) carriers.
    // PW mixed channels do not identify the underlying event.
    return {static_cast<uint32_t>(std::min(1.0f, Gain * std::max(largeMotor, smallMotor)) * 10000.0f), largeMotor > 0 && largeMotor >= smallMotor ? 70u : 60u};
}
inline Choice ImpactChoice(uint64_t nowMs, uint64_t atMs, uint32_t packed, float scale = 1) {
    const uint32_t kind = packed >> 16, strength = packed & 65535;
    const uint32_t duration = kind >= 2 ? 800 : 200;
    if (!atMs || kind < 1 || kind > 3 || nowMs < atMs || nowMs - atMs >= duration || !std::isfinite(scale) || scale <= 0) return {};
    const float envelope = duration == 800 ? std::min(1.0f, (duration - (nowMs - atMs)) / 600.0f)
        : 1 - (nowMs - atMs) / static_cast<float>(duration);
    float peak = Gain * strength / 65535.0f * scale;
    if (duration == 800) peak = std::min(1.0f, peak); // Clamp before the decay so gain does not extend the peak hold.
    return {static_cast<uint32_t>(std::min(1.0f, peak * envelope) * 10000.0f), kind == 1 ? 35u : 80u, kind == 1 ? 1u : kind + 1};
}
inline Choice GunChoice(uint64_t nowMs, uint64_t atMs, float scale = 1) {
    if (!atMs || nowMs < atMs || nowMs - atMs >= 80 || !std::isfinite(scale) || scale <= 0) return {};
    const float raw = .45f * (1 - (nowMs - atMs) / 80.0f) * scale;
    return {static_cast<uint32_t>(std::min(1.0f, Gain * raw) * 10000.0f), 28, 2};
}
struct Command {
    enum Kind { None, Stop, Parameters } kind = None;
    uint32_t magnitude = 0, periodMs = 0, phase = 0;
    bool restart = false;
    uint32_t source = 0;
};
// Phase is hundredths of a degree; timestamps are monotonic microseconds.
class Playback {
    bool playing_ = false;
    uint32_t magnitude_ = 0, period_ = 0, phase_ = 0;
    uint64_t startedUs_ = 0;
    static uint32_t advance(uint64_t elapsedUs, uint32_t periodMs) {
        const uint64_t cycle = uint64_t(periodMs) * 1000;
        return static_cast<uint32_t>((elapsedUs % cycle) * 36000 / cycle);
    }
public:
    bool playing() const { return playing_; }
    Command next(const Values& values, bool allowed, uint64_t nowUs, Choice extra = {}) {
        auto choice = Choose(values);
        if (Valid(values) && extra.magnitude <= 10000 && extra.periodMs >= 1 && extra.periodMs <= 1000 && extra.magnitude > choice.magnitude)
            choice = extra;
        if (!allowed || !choice.magnitude) {
            if (!playing_) return {};
            playing_ = false;
            return {Command::Stop};
        }
        const uint64_t elapsed = playing_ && nowUs >= startedUs_ ? nowUs - startedUs_ : 0;
        const bool restart = !playing_ || elapsed >= uint64_t(RestartMs) * 1000;
        const uint32_t phaseNow = playing_ ? (phase_ + advance(elapsed, period_)) % 36000 : 0;
        uint32_t nextBase = playing_ ? phase_ : 0;
        if (playing_ && period_ != choice.periodMs)
            nextBase = (phaseNow + 36000 - advance(elapsed, choice.periodMs)) % 36000;
        if (!restart && magnitude_ == choice.magnitude && period_ == choice.periodMs) return {};
        if (restart) { nextBase = phaseNow; startedUs_ = nowUs; }
        phase_ = nextBase; period_ = choice.periodMs; magnitude_ = choice.magnitude; playing_ = true;
        return {Command::Parameters, magnitude_, period_, phase_, restart, choice.source};
    }
};
}
