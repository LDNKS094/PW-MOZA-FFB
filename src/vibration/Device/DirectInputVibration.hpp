#pragma once
#define DIRECTINPUT_VERSION 0x0800
#include <dinput.h>
#include "../Effects/VibrationEnvelope.hpp"
#include <cstdio>
#include <string>

namespace pwffb {
class DirectInputVibration {
    IDirectInput8W* input_ = nullptr;
    IDirectInputDevice8W* joystick_ = nullptr;
    IDirectInputEffect* effect_ = nullptr;
    HWND window_ = nullptr;
    GUID selected_{};
    int matches_ = 0;
    DWORD axes_[2]{};
    DWORD axisCount_ = 0;
    Playback playback_;
    std::string failure_;
    static BOOL CALLBACK device(const DIDEVICEINSTANCEW* info, void* context) {
        auto& out = *static_cast<DirectInputVibration*>(context);
        if (_wcsicmp(info->tszProductName, L"MOZA AB6 FFB Base") == 0) { out.selected_ = info->guidInstance; ++out.matches_; }
        return DIENUM_CONTINUE;
    }
    static BOOL CALLBACK axis(const DIDEVICEOBJECTINSTANCEW* info, void* context) {
        auto& out = *static_cast<DirectInputVibration*>(context);
        if (((info->dwFlags & DIDOI_FFACTUATOR) || (info->dwType & DIDFT_FFACTUATOR)) && out.axisCount_ < 2)
            out.axes_[out.axisCount_++] = info->dwType;
        return DIENUM_CONTINUE;
    }
    DIEFFECT parameters(const Command& command, DIPERIODIC& periodic, LONG (&direction)[2]) {
        periodic = {command.magnitude, 0, command.phase, command.periodMs * 1000};
        direction[0] = axisCount_ == 1 ? 1 : 7000; direction[1] = axisCount_ == 1 ? 0 : 7000;
        DIEFFECT value{};
        value.dwSize = sizeof(value); value.dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTIDS;
        value.dwDuration = LeaseMs * 1000; value.dwGain = DI_FFNOMINALMAX;
        value.dwTriggerButton = DIEB_NOTRIGGER; value.cAxes = axisCount_;
        value.rgdwAxes = axes_; value.rglDirection = direction;
        value.cbTypeSpecificParams = sizeof(periodic); value.lpvTypeSpecificParams = &periodic;
        return value;
    }
    bool fail(const char* stage, HRESULT result) {
        char message[160];
        std::snprintf(message, sizeof(message), "%s hr=0x%08lX", stage, static_cast<unsigned long>(result));
        failure_ = message; close(); return false;
    }
public:
    const std::string& failure() const { return failure_; }
    bool open() {
        auto result = DirectInput8Create(GetModuleHandleW(nullptr), DIRECTINPUT_VERSION, IID_IDirectInput8W, reinterpret_cast<void**>(&input_), nullptr);
        if (FAILED(result)) return fail("DirectInput8Create", result);
        result = input_->EnumDevices(DI8DEVCLASS_GAMECTRL, device, this, DIEDFL_ATTACHEDONLY | DIEDFL_FORCEFEEDBACK);
        if (FAILED(result)) return fail("EnumDevices", result);
        if (matches_ != 1) return fail(matches_ ? "ambiguous_AB6" : "AB6_not_found", E_FAIL);
        result = input_->CreateDevice(selected_, &joystick_, nullptr);
        if (FAILED(result)) return fail("CreateDevice", result);
        result = joystick_->SetDataFormat(&c_dfDIJoystick2);
        if (FAILED(result)) return fail("SetDataFormat", result);
        result = joystick_->EnumObjects(axis, this, DIDFT_AXIS);
        if (FAILED(result) || !axisCount_) return fail("force_axes", FAILED(result) ? result : E_FAIL);
        window_ = CreateWindowExW(0, L"STATIC", L"PW Vibration", WS_POPUP, 0, 0, 0, 0, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!window_) return fail("window", HRESULT_FROM_WIN32(GetLastError()));
        result = joystick_->SetCooperativeLevel(window_, DISCL_EXCLUSIVE | DISCL_BACKGROUND);
        if (FAILED(result)) return fail("SetCooperativeLevel", result);
        result = joystick_->Acquire();
        if (FAILED(result)) return fail("Acquire", result);
        DIPERIODIC periodic{}; LONG direction[2]{};
        auto params = parameters({Command::Parameters, 0, 60, 0, false}, periodic, direction);
        result = joystick_->CreateEffect(GUID_Sine, &params, &effect_, nullptr);
        if (FAILED(result)) return fail("CreateEffect", result);
        return true;
    }
    bool tick(const Values& values, bool allowed, uint64_t nowUs, Choice extra = {}) {
        const auto command = playback_.next(values, allowed, nowUs, extra);
        if (command.kind == Command::None) return true;
        if (command.kind == Command::Stop) {
            const auto result = effect_->Stop();
            if (FAILED(result)) return fail("Stop", result);
            return true;
        }
        DIPERIODIC periodic{}; LONG direction[2]{};
        auto params = parameters(command, periodic, direction);
        const DWORD flags = DIEP_TYPESPECIFICPARAMS | (command.restart ? DIEP_START : DIEP_NORESTART);
        const auto result = effect_->SetParameters(&params, flags);
        if (FAILED(result)) return fail(command.restart ? "phase_renewal" : "NoRestart_update", result);
        return true;
    }
    void close() {
        if (effect_) { effect_->Stop(); effect_->Release(); effect_ = nullptr; }
        if (joystick_) { joystick_->Unacquire(); joystick_->Release(); joystick_ = nullptr; }
        if (input_) { input_->Release(); input_ = nullptr; }
        if (window_) { DestroyWindow(window_); window_ = nullptr; }
    }
    ~DirectInputVibration() { close(); }
};
}
