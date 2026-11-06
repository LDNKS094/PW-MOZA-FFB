#pragma once
#include <windows.h>
#include <string>

inline std::wstring EventName(const wchar_t* kind, DWORD pid) {
    return std::wstring(L"Local\\PWVibration-") + kind + L"-" + std::to_wstring(pid);
}
inline std::wstring ModulePath(HMODULE module = nullptr) {
    std::wstring path(32768, L'\0');
    const DWORD size = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
    if (!size || size == path.size()) return {};
    path.resize(size);
    return path;
}
inline bool SupportedImageSize(uintptr_t base) {
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    // Bound native RVA reads before checking the hook signatures.
    return nt->OptionalHeader.SizeOfImage == 0x9b4e000;
}
