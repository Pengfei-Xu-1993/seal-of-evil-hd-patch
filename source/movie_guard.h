#pragma once

// Startup safety only: preserve the original movies and skip an IV50 movie when
// an app-local, isolated graph-construction probe cannot establish video support.
// This does not decode, replace, or claim to validate playback of the movie.
#include <windows.h>
#include <stdint.h>
#include <string>
#include <vector>
#include <cstring>

namespace seal_movie_guard {

typedef void (*LogFn)(const char *, ...);

namespace detail {

class Handle {
public:
    explicit Handle(HANDLE value = INVALID_HANDLE_VALUE) : value_(value) {}
    ~Handle() { if (value_ && value_ != INVALID_HANDLE_VALUE) CloseHandle(value_); }
    HANDLE get() const { return value_; }
    bool valid() const { return value_ && value_ != INVALID_HANDLE_VALUE; }
    Handle(const Handle &) = delete;
    Handle &operator=(const Handle &) = delete;
private:
    HANDLE value_;
};

class ExclusiveLock {
public:
    explicit ExclusiveLock(SRWLOCK *lock) : lock_(lock) { AcquireSRWLockExclusive(lock_); }
    ~ExclusiveLock() { ReleaseSRWLockExclusive(lock_); }
    ExclusiveLock(const ExclusiveLock &) = delete;
    ExclusiveLock &operator=(const ExclusiveLock &) = delete;
private:
    SRWLOCK *lock_;
};

inline DWORD Read32(const BYTE *bytes) {
    return static_cast<DWORD>(bytes[0]) | (static_cast<DWORD>(bytes[1]) << 8) |
        (static_cast<DWORD>(bytes[2]) << 16) | (static_cast<DWORD>(bytes[3]) << 24);
}

inline bool Tag(const BYTE *bytes, const char *value) { return memcmp(bytes, value, 4) == 0; }

inline bool IV50Tag(const BYTE *bytes) {
    // Original movies use lower-case "iv50" in strh and upper-case "IV50" in strf.
    return (bytes[0] == 'i' || bytes[0] == 'I') && (bytes[1] == 'v' || bytes[1] == 'V') &&
        bytes[2] == '5' && bytes[3] == '0';
}

inline bool StreamIsIV50(const std::vector<BYTE> &bytes, size_t begin, size_t end) {
    bool videoIV50 = false, formatIV50 = false;
    for (size_t offset = begin; offset <= end && end - offset >= 8;) {
        const DWORD size = Read32(&bytes[offset + 4]);
        const uint64_t next = static_cast<uint64_t>(offset) + 8 + size + (size & 1);
        if (next > end) return false;
        if (Tag(&bytes[offset], "strh") && size >= 8) {
            videoIV50 = Tag(&bytes[offset + 8], "vids") && IV50Tag(&bytes[offset + 12]);
        } else if (Tag(&bytes[offset], "strf") && size >= 40) {
            // BITMAPINFOHEADER.biCompression must agree with strh.fccHandler.
            const DWORD bitmapSize = Read32(&bytes[offset + 8]);
            formatIV50 = bitmapSize >= 40 && bitmapSize <= size && IV50Tag(&bytes[offset + 24]);
        }
        offset = static_cast<size_t>(next);
    }
    return videoIV50 && formatIV50;
}

inline bool HeaderListHasIV50(const std::vector<BYTE> &bytes, size_t begin, size_t end) {
    for (size_t offset = begin; offset <= end && end - offset >= 8;) {
        const DWORD size = Read32(&bytes[offset + 4]);
        const uint64_t next = static_cast<uint64_t>(offset) + 8 + size + (size & 1);
        if (next > end) return false;
        if (Tag(&bytes[offset], "LIST") && size >= 4 && Tag(&bytes[offset + 8], "strl") &&
            StreamIsIV50(bytes, offset + 12, offset + 8 + size)) return true;
        offset = static_cast<size_t>(next);
    }
    return false;
}

inline bool IsIV50Avi(HANDLE file, uint64_t fileSize) {
    const size_t maxHeader = 1024 * 1024;
    const DWORD amount = static_cast<DWORD>(fileSize < maxHeader ? fileSize : maxHeader);
    if (amount < 12) return false;
    std::vector<BYTE> bytes(amount);
    DWORD read = 0;
    LARGE_INTEGER zero = {};
    if (!SetFilePointerEx(file, zero, nullptr, FILE_BEGIN) ||
        !ReadFile(file, bytes.data(), amount, &read, nullptr) || read != amount ||
        !Tag(bytes.data(), "RIFF") || !Tag(&bytes[8], "AVI ")) return false;
    const uint64_t riffEnd = static_cast<uint64_t>(Read32(&bytes[4])) + 8;
    if (riffEnd < 12 || riffEnd > fileSize) return false;
    for (size_t offset = 12; static_cast<uint64_t>(offset) + 8 <= riffEnd &&
        offset <= bytes.size() && bytes.size() - offset >= 8;) {
        const DWORD size = Read32(&bytes[offset + 4]);
        const uint64_t next = static_cast<uint64_t>(offset) + 8 + size + (size & 1);
        if (next > riffEnd) return false;
        if (Tag(&bytes[offset], "LIST") && size >= 4 &&
            static_cast<uint64_t>(offset) + 12 <= bytes.size() && Tag(&bytes[offset + 8], "hdrl")) {
            const uint64_t headerEnd = static_cast<uint64_t>(offset) + 8 + size;
            if (headerEnd > bytes.size()) return false;
            return HeaderListHasIV50(bytes, offset + 12, static_cast<size_t>(headerEnd));
        }
        if (next > bytes.size()) return false;
        offset = static_cast<size_t>(next);
    }
    return false;
}

inline bool ExeDirectory(std::wstring &directory) {
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (!length || length >= buffer.size()) return false;
    directory.assign(buffer.data(), length);
    const size_t slash = directory.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return false;
    directory.resize(slash);
    return true;
}

inline bool FinalPath(HANDLE file, std::wstring &path) {
    const DWORD required = GetFinalPathNameByHandleW(file, nullptr, 0, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
    if (!required || required >= 32768) return false;
    std::vector<wchar_t> buffer(required + 1);
    const DWORD length = GetFinalPathNameByHandleW(file, buffer.data(),
        static_cast<DWORD>(buffer.size()), FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
    if (!length || length >= buffer.size()) return false;
    path.assign(buffer.data(), length);
    // DirectShow and the original game accept regular DOS paths, including spaces.
    if (path.compare(0, 8, L"\\\\?\\UNC\\") == 0) path = L"\\\\" + path.substr(8);
    else if (path.compare(0, 4, L"\\\\?\\") == 0) path.erase(0, 4);
    return true;
}

enum ProbeReason { CompleteGraph, MissingHelper, StartFailed, TimedOut, WaitFailed, RejectedGraph, ProbeError };

struct ProbeResult {
    ProbeReason reason;
    DWORD value;
    bool skip;
};

inline ProbeResult Probe(const std::wstring &directory, const std::wstring &movie) {
    const std::wstring helper = directory + L"\\SealHD-bin\\SealVideoProbe.exe";
    const DWORD attributes = GetFileAttributesW(helper.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) return { MissingHelper, GetLastError(), true };
    if (attributes & FILE_ATTRIBUTE_DIRECTORY) return { MissingHelper, ERROR_DIRECTORY, true };
    std::wstring command = L"\"" + helper + L"\" \"" + movie + L"\"";
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process = {};
    if (!CreateProcessW(helper.c_str(), &command[0], nullptr, nullptr, FALSE,
        CREATE_NO_WINDOW | CREATE_DEFAULT_ERROR_MODE, nullptr, directory.c_str(), &startup, &process))
        return { StartFailed, GetLastError(), true };
    Handle processHandle(process.hProcess), threadHandle(process.hThread);
    const DWORD wait = WaitForSingleObject(processHandle.get(), 3000);
    if (wait != WAIT_OBJECT_0) {
        const DWORD error = wait == WAIT_FAILED ? GetLastError() : wait;
        // This handle identifies only the probe just created by this call.
        TerminateProcess(processHandle.get(), 3);
        WaitForSingleObject(processHandle.get(), 250);
        return { wait == WAIT_TIMEOUT ? TimedOut : WaitFailed, error, true };
    }
    DWORD exitCode = 3;
    if (!GetExitCodeProcess(processHandle.get(), &exitCode)) return { WaitFailed, GetLastError(), true };
    return { exitCode == 0 ? CompleteGraph : exitCode == 2 ? RejectedGraph : ProbeError,
        exitCode, exitCode != 0 };
}

struct CacheEntry {
    bool used = false;
    std::wstring path;
    uint64_t size = 0;
    FILETIME modified = {};
    ProbeResult result = { RejectedGraph, 3, true };
};

inline void Report(LogFn logger, const std::wstring &path, const ProbeResult &result, bool cached) {
    if (!logger) return;
    const char *reason = "unsupported-or-partial-video-graph";
    switch (result.reason) {
    case CompleteGraph: reason = "complete-video-graph-detected"; break;
    case MissingHelper: reason = "app-local-probe-missing"; break;
    case StartFailed: reason = "app-local-probe-launch-failed"; break;
    case TimedOut: reason = "app-local-probe-timeout-3000ms"; break;
    case WaitFailed: reason = "app-local-probe-process-error"; break;
    case ProbeError: reason = "app-local-probe-error"; break;
    default: break;
    }
    if (result.skip)
        logger("Movie guard SKIP original IV50 movie=%ls reason=%s code=%lu cached=%d; original file preserved; movie was not played",
            path.c_str(), reason, result.value, cached ? 1 : 0);
    else
        logger("Movie guard PASS original IV50 movie=%ls reason=%s cached=%d; original Mfx playback follows; playback not verified by probe",
            path.c_str(), reason, cached ? 1 : 0);
}

} // namespace detail

// handled=true / S_OK means a known original IV50 movie was deliberately skipped.
// handled=false leaves every other call, and supported IV50 movies, to Mfx.
inline HRESULT TryHandle(const char *filename, LogFn logger, bool *handled) {
    if (!handled) return E_POINTER;
    *handled = false;
    bool confirmedIV50 = false;
    try {
    if (!filename || !*filename) return S_FALSE;
    const int length = MultiByteToWideChar(CP_ACP, 0, filename, -1, nullptr, 0);
    if (length <= 1 || length > 32768) return S_FALSE;
    std::vector<wchar_t> input(length);
    if (!MultiByteToWideChar(CP_ACP, 0, filename, -1, input.data(), length)) return S_FALSE;
    const std::wstring requested(input.data());
    const size_t slash = requested.find_last_of(L"\\/");
    const std::wstring basename = requested.substr(slash == std::wstring::npos ? 0 : slash + 1);
    const bool start = _wcsicmp(basename.c_str(), L"Start.dat") == 0;
    if (!start && _wcsicmp(basename.c_str(), L"End.dat") != 0) return S_FALSE;
    std::wstring directory;
    if (!detail::ExeDirectory(directory)) return S_FALSE;
    const std::wstring expected = directory + (start ? L"\\Movie\\Start.dat" : L"\\Movie\\End.dat");
    detail::Handle actual(CreateFileW(requested.c_str(), GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
    detail::Handle original(CreateFileW(expected.c_str(), GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!actual.valid() || !original.valid()) return S_FALSE;
    std::wstring actualPath, expectedPath;
    if (!detail::FinalPath(actual.get(), actualPath) || !detail::FinalPath(original.get(), expectedPath) ||
        _wcsicmp(actualPath.c_str(), expectedPath.c_str()) != 0) return S_FALSE;
    BY_HANDLE_FILE_INFORMATION information = {};
    if (!GetFileInformationByHandle(actual.get(), &information) ||
        (information.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return S_FALSE;
    const uint64_t size = (static_cast<uint64_t>(information.nFileSizeHigh) << 32) | information.nFileSizeLow;
    if (!detail::IsIV50Avi(actual.get(), size)) return S_FALSE;
    confirmedIV50 = true;

    // At most the two whitelisted originals are cached. A new path/size/mtime is probed again.
    static SRWLOCK lock = SRWLOCK_INIT;
    static detail::CacheEntry cache[2];
    bool cached;
    detail::ProbeResult result;
    {
        detail::ExclusiveLock held(&lock);
        detail::CacheEntry &entry = cache[start ? 0 : 1];
        cached = entry.used && _wcsicmp(entry.path.c_str(), actualPath.c_str()) == 0 &&
            entry.size == size && CompareFileTime(&entry.modified, &information.ftLastWriteTime) == 0;
        if (!cached) {
            entry.result = detail::Probe(directory, actualPath);
            entry.path = actualPath;
            entry.size = size;
            entry.modified = information.ftLastWriteTime;
            entry.used = true;
        }
        result = entry.result;
    }
    detail::Report(logger, actualPath, result, cached);
    *handled = result.skip;
    return result.skip ? S_OK : S_FALSE;
    } catch (...) {
        if (logger) logger("Movie guard internal error confirmed-original-IV50=%d; original file preserved; %s",
            confirmedIV50 ? 1 : 0, confirmedIV50 ? "movie skipped and was not played" : "original player unchanged");
        *handled = confirmedIV50;
        return confirmedIV50 ? S_OK : S_FALSE;
    }
}

} // namespace seal_movie_guard
