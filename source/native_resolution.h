#pragma once

#include <windows.h>
#include <stdint.h>

// These patches replace the game's 1024x768 slot. Select 显示模式=2 in
// XOR-0x78 encoded Ini/Config.ini to request state 6 and renderer enum 4/5.
// The initial 640x480 menu slot remains a separate engine state.
namespace seal_native_resolution {

enum class Module { Game, Graphics };
enum class Dimension { Width, Height };

struct Patch {
    Module module;
    DWORD rva;
    DWORD expected;
    Dimension dimension;
    const char* description;
};

static const Patch patches[] = {
    {Module::Game, 0x0087B6, 1024, Dimension::Width, "state 6 client width"},
    {Module::Game, 0x0087C0, 768, Dimension::Height, "state 6 client height"},
    {Module::Game, 0x06EB3E, 1024, Dimension::Width, "world viewport width comparison"},
    {Module::Game, 0x06EB47, 768, Dimension::Height, "world viewport initial height"},
    {Module::Game, 0x0794FE, 1024, Dimension::Width, "display cycle width comparison"},
    {Module::Game, 0x08329E, 1024, Dimension::Width, "screen cache width comparison"},
    {Module::Game, 0x0832A6, 768, Dimension::Height, "screen cache height comparison"},
    {Module::Game, 0x08791B, 1024, Dimension::Width, "world viewport reset comparison"},
    {Module::Game, 0x08792E, 768, Dimension::Height, "world viewport reset height"},
    {Module::Game, 0x087933, 1024, Dimension::Width, "world viewport reset width"},
    {Module::Graphics, 0x076C4D, 1024, Dimension::Width, "renderer enum 4/5 width"},
    {Module::Graphics, 0x076CAD, 768, Dimension::Height, "renderer enum 4/5 height"},
};

static const unsigned patch_count = sizeof(patches) / sizeof(patches[0]);

inline bool Is32BitImage(HMODULE module, DWORD* image_size) {
    if (!module) {
        SetLastError(ERROR_INVALID_HANDLE);
        return false;
    }
    const BYTE* base = reinterpret_cast<const BYTE*>(module);
    const IMAGE_DOS_HEADER* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 || dos->e_lfanew > 0x100000) {
        SetLastError(ERROR_BAD_EXE_FORMAT);
        return false;
    }
    const IMAGE_NT_HEADERS32* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
        SetLastError(ERROR_BAD_EXE_FORMAT);
        return false;
    }
    *image_size = nt->OptionalHeader.SizeOfImage;
    return true;
}

inline BYTE* Address(HMODULE game, HMODULE graphics, const Patch& patch) {
    HMODULE module = patch.module == Module::Game ? game : graphics;
    return reinterpret_cast<BYTE*>(module) + patch.rva;
}

// Validate both complete images and every expected DWORD before any memory write.
inline bool ValidateAll(HMODULE game, HMODULE graphics, DWORD* failed_rva = nullptr) {
    if (failed_rva) *failed_rva = 0;
    DWORD game_size = 0;
    DWORD graphics_size = 0;
    if (!Is32BitImage(game, &game_size) || !Is32BitImage(graphics, &graphics_size)) return false;
    for (unsigned i = 0; i < patch_count; ++i) {
        const Patch& patch = patches[i];
        const DWORD image_size = patch.module == Module::Game ? game_size : graphics_size;
        DWORD current = 0;
        if (patch.rva > image_size || image_size - patch.rva < sizeof(current)) {
            if (failed_rva) *failed_rva = patch.rva;
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return false;
        }
        CopyMemory(&current, Address(game, graphics, patch), sizeof(current));
        if (current != patch.expected) {
            if (failed_rva) *failed_rva = patch.rva;
            SetLastError(ERROR_REVISION_MISMATCH);
            return false;
        }
    }
    return true;
}

inline bool RestoreProtection(HMODULE game, HMODULE graphics, const DWORD* protections, unsigned count) {
    bool restored = true;
    while (count > 0) {
        --count;
        DWORD ignored = 0;
        if (!VirtualProtect(Address(game, graphics, patches[count]), sizeof(DWORD), protections[count], &ignored)) {
            restored = false;
        }
    }
    return restored;
}

// Call during proxy initialization, before the game's main thread uses these
// instructions. All protections are acquired before the first value is changed.
inline bool ApplyNativeResolutionPatches(HMODULE game, HMODULE graphics, DWORD width, DWORD height,
                                         DWORD* failed_rva = nullptr) {
    if (width == 0 || height == 0) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return false;
    }
    if (!ValidateAll(game, graphics, failed_rva)) return false;

    DWORD protections[patch_count] = {};
    unsigned protected_count = 0;
    for (; protected_count < patch_count; ++protected_count) {
        const Patch& patch = patches[protected_count];
        if (!VirtualProtect(Address(game, graphics, patch), sizeof(DWORD), PAGE_EXECUTE_READWRITE,
                            &protections[protected_count])) {
            const DWORD error = GetLastError();
            if (failed_rva) *failed_rva = patch.rva;
            RestoreProtection(game, graphics, protections, protected_count);
            SetLastError(error);
            return false;
        }
    }
    if (!ValidateAll(game, graphics, failed_rva)) {
        const DWORD error = GetLastError();
        RestoreProtection(game, graphics, protections, protected_count);
        SetLastError(error);
        return false;
    }
    for (unsigned i = 0; i < patch_count; ++i) {
        const Patch& patch = patches[i];
        const DWORD value = patch.dimension == Dimension::Width ? width : height;
        CopyMemory(Address(game, graphics, patch), &value, sizeof(value));
    }
    if (!FlushInstructionCache(GetCurrentProcess(), nullptr, 0)) {
        const DWORD error = GetLastError();
        for (unsigned i = 0; i < patch_count; ++i) {
            CopyMemory(Address(game, graphics, patches[i]), &patches[i].expected, sizeof(DWORD));
        }
        FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
        RestoreProtection(game, graphics, protections, protected_count);
        SetLastError(error);
        return false;
    }
    // The geometry is committed after the successful instruction-cache flush.
    // Keep the reported state consistent if restoring page protection fails.
    RestoreProtection(game, graphics, protections, protected_count);
    return true;
}

inline bool ValidateCurrentResolution(HMODULE game, HMODULE graphics, DWORD width, DWORD height,
                                      DWORD* failed_rva = nullptr) {
    if (failed_rva) *failed_rva = 0;
    DWORD game_size = 0;
    DWORD graphics_size = 0;
    if (!Is32BitImage(game, &game_size) || !Is32BitImage(graphics, &graphics_size)) return false;
    for (unsigned i = 0; i < patch_count; ++i) {
        const Patch& patch = patches[i];
        const DWORD image_size = patch.module == Module::Game ? game_size : graphics_size;
        DWORD current = 0;
        const DWORD expected = patch.dimension == Dimension::Width ? width : height;
        if (patch.rva > image_size || image_size - patch.rva < sizeof(current)) {
            if (failed_rva) *failed_rva = patch.rva;
            SetLastError(ERROR_BAD_EXE_FORMAT);
            return false;
        }
        CopyMemory(&current, Address(game, graphics, patch), sizeof(current));
        if (current != expected) {
            if (failed_rva) *failed_rva = patch.rva;
            SetLastError(ERROR_REVISION_MISMATCH);
            return false;
        }
    }
    return true;
}

// The options handler runs on the game's main thread. Validate the currently
// selected dimensions before replacing them and rebuilding the world viewport.
inline bool ChangeNativeResolution(HMODULE game, HMODULE graphics, DWORD old_width, DWORD old_height,
                                   DWORD new_width, DWORD new_height, DWORD* failed_rva = nullptr) {
    if (!new_width || !new_height ||
        !ValidateCurrentResolution(game, graphics, old_width, old_height, failed_rva)) return false;
    DWORD protections[patch_count] = {};
    unsigned protected_count = 0;
    for (; protected_count < patch_count; ++protected_count) {
        const Patch& patch = patches[protected_count];
        if (!VirtualProtect(Address(game, graphics, patch), sizeof(DWORD), PAGE_EXECUTE_READWRITE,
                            &protections[protected_count])) {
            const DWORD error = GetLastError();
            if (failed_rva) *failed_rva = patch.rva;
            RestoreProtection(game, graphics, protections, protected_count);
            SetLastError(error);
            return false;
        }
    }
    if (!ValidateCurrentResolution(game, graphics, old_width, old_height, failed_rva)) {
        const DWORD error = GetLastError();
        RestoreProtection(game, graphics, protections, protected_count);
        SetLastError(error);
        return false;
    }
    for (unsigned i = 0; i < patch_count; ++i) {
        const Patch& patch = patches[i];
        const DWORD value = patch.dimension == Dimension::Width ? new_width : new_height;
        CopyMemory(Address(game, graphics, patch), &value, sizeof(value));
    }
    if (!FlushInstructionCache(GetCurrentProcess(), nullptr, 0)) {
        const DWORD error = GetLastError();
        for (unsigned i = 0; i < patch_count; ++i) {
            const Patch& patch = patches[i];
            const DWORD value = patch.dimension == Dimension::Width ? old_width : old_height;
            CopyMemory(Address(game, graphics, patch), &value, sizeof(value));
        }
        FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
        RestoreProtection(game, graphics, protections, protected_count);
        SetLastError(error);
        return false;
    }
    // Once all instructions have been committed, report the selected geometry
    // consistently even if Windows cannot restore a page's previous protection.
    RestoreProtection(game, graphics, protections, protected_count);
    return true;
}

} // namespace seal_native_resolution
