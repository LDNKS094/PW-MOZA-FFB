#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <array>
#include <string>
#include <vector>

inline constexpr char ExpectedHash[] = "092e84225624a4de9c05d2404ff55269bd4a2aa2ff0548f2a183c6bf36abc85a";
inline constexpr wchar_t GameName[] = L"ProjectWingman-Win64-Shipping.exe";
inline std::wstring EventName(const wchar_t* kind, DWORD pid) {
    return std::wstring(L"Local\\PWNativeFeedback-") + kind + L"-" + std::to_wstring(pid);
}
inline std::wstring ModulePath(HMODULE module = nullptr) {
    std::wstring path(32768, L'\0');
    const DWORD size = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
    if (!size || size == path.size()) return {};
    path.resize(size);
    return path;
}
inline std::string FileHash(const std::wstring& path) {
    const HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    std::array<unsigned char, 32> digest{};
    std::array<unsigned char, 65536> data{};
    bool ok = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0;
    if (ok) ok = BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) >= 0;
    while (ok) {
        DWORD size = 0;
        if (!ReadFile(file, data.data(), static_cast<DWORD>(data.size()), &size, nullptr)) { ok = false; break; }
        if (!size) break;
        ok = BCryptHashData(hash, data.data(), size, 0) >= 0;
    }
    if (ok) ok = BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0;
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    CloseHandle(file);
    if (!ok) return {};
    constexpr char digits[] = "0123456789abcdef";
    std::string result;
    for (auto byte : digest) { result += digits[byte >> 4]; result += digits[byte & 15]; }
    return result;
}
