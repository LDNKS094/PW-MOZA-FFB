#pragma once
#include "ProcessIdentity.hpp"
#include <cstdio>
#include <filesystem>
#include <mutex>

inline void WriteError(HMODULE module, const char* message, DWORD code = 0) {
    static std::mutex gate;
    std::lock_guard lock(gate);
    const auto file = std::filesystem::path(ModulePath(module)).parent_path()
        / (L"vibration-errors-" + std::to_wstring(GetCurrentProcessId()) + L".log");
    FILE* stream = nullptr;
    if (_wfopen_s(&stream, file.c_str(), L"a") == 0 && stream) {
        std::fprintf(stream, "%llu %s", GetTickCount64(), message);
        if (code) std::fprintf(stream, " (Win32=%lu)", code);
        std::fputc('\n', stream);
        std::fclose(stream);
    }
}
