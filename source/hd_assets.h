#pragma once

// Seal of Evil's menu is a CPU-painted RGB565 surface.  This module keeps that
// surface (and its original widgets) but replaces only recognized background
// images with a chroma canvas while an output-resolution D3D11 compositor
// is ready.  If acquisition or any GPU setup fails, original images are used.
// Include once in the x86 proxy translation unit. No game binaries are changed.

#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <dxgi1_5.h>
#include <d3dcompiler.h>
#include <wincodec.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <vector>
#include <array>
#include <algorithm>
#include <memory>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "dxguid.lib")

#if !defined(_M_IX86)
#error SealHD menu integration is for the original x86 game only.
#endif

namespace seal_hd {

using LogFn = void (__cdecl *)(const char *, ...);
using CreateXBmpFn = int (__cdecl *)(void *, void *&);
using GetProcFn = FARPROC (WINAPI *)(HMODULE, LPCSTR);
using DeletePaperFn = void *(__thiscall *)(void *, unsigned int);

template <typename T> static void Release(T *&object)
{
    if (object) { object->Release(); object = nullptr; }
}

static LogFn logger;
static HMODULE executable;
static HMODULE graphics;
static HMODULE compilerModule;
static HMODULE bootstrapDxgiModule;
static INIT_ONCE factoryBootstrapOnce = INIT_ONCE_STATIC_INIT;
static GetProcFn systemGetProc;
static CreateXBmpFn createXBmp;
static wchar_t imagePath[MAX_PATH];
static wchar_t openingImagePath[MAX_PATH];
static SRWLOCK hookLock = SRWLOCK_INIT;
static SRWLOCK paperLock = SRWLOCK_INIT;
static SRWLOCK gpuLock = SRWLOCK_INIT;
static volatile LONG enabled;
static volatile LONG compositorReady;
static volatile LONG compositorScene;
static volatile LONG keyDrawGeneration;
static volatile LONG openingActive;
static volatile LONG openingClosed;
static bool openingEnabled;
static bool animateFire = true;
static DWORD installThread;

static bool Readable(const void *address, size_t size)
{
    uintptr_t position = reinterpret_cast<uintptr_t>(address);
    if (!position || size > UINTPTR_MAX - position) return false;
    const uintptr_t end = position + size;
    while (position < end) {
        MEMORY_BASIC_INFORMATION info = {};
        if (!VirtualQuery(reinterpret_cast<const void *>(position), &info, sizeof(info)) ||
            info.State != MEM_COMMIT || (info.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
        uintptr_t next = reinterpret_cast<uintptr_t>(info.BaseAddress) + info.RegionSize;
        if (next <= position) return false;
        position = next;
    }
    return true;
}

static DWORD Read32(const void *bytes)
{
    DWORD result;
    memcpy(&result, bytes, sizeof(result));
    return result;
}

static WORD Read16(const void *bytes)
{
    WORD result;
    memcpy(&result, bytes, sizeof(result));
    return result;
}

static DWORD ApplicationState()
{
    const BYTE *state = reinterpret_cast<const BYTE *>(executable) + 0x2e5f08;
    return enabled && executable && Readable(state, 4) ? Read32(state) : MAXDWORD;
}

enum SceneKind : LONG { NoScene, OpeningScene, MenuScene };

static SceneKind WantedScene()
{
    DWORD state = ApplicationState();
    if (state == 2) return MenuScene;
    if (openingEnabled && openingActive && !openingClosed && state <= 1) return OpeningScene;
    return NoScene;
}

static void EndOpeningTitle()
{
    InterlockedExchange(&openingClosed, 1);
    if (InterlockedExchange(&openingActive, 0)) {
        InterlockedExchange(&compositorReady, 0);
        InterlockedExchange(&compositorScene, NoScene);
        InterlockedExchange(&keyDrawGeneration, 0);
        if (logger) logger("HD opening title ended at Logo1 command boundary");
    }
}

struct HookRecord { void **slot; void *original; void *replacement; };
static HookRecord hooks[96] = {};
static size_t hookCount;

struct ChainTable {
    void *object = nullptr;
    IUnknown *identity = nullptr;
    void **originalTable = nullptr;
    unsigned methods = 0;
    unsigned publicMethods = 0;
    bool alive = true;
    // DXGI implementation methods follow its public COM slots. Preserve that
    // executable tail because native destruction dispatches through it too.
    std::array<void *, 512> original = {};
    std::array<void *, 513> replacement = {};
    void **Table() { return replacement.data() + 1; }
};
static std::vector<std::unique_ptr<ChainTable>> chainTables;
static std::vector<IDXGISwapChain *> pendingChains;
static volatile LONG chainRetirementEpoch;

static bool Executable(const void *address)
{
    MEMORY_BASIC_INFORMATION information = {};
    return address && VirtualQuery(address, &information, sizeof(information)) &&
        information.State == MEM_COMMIT && !(information.Protect & (PAGE_GUARD | PAGE_NOACCESS)) &&
        (information.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY));
}

static bool PatchSlot(void **slot, void *replacement)
{
    if (!Readable(slot, sizeof(void *))) return false;
    AcquireSRWLockExclusive(&hookLock);
    for (size_t i = 0; i < hookCount; ++i) {
        if (hooks[i].slot == slot) {
            bool same = hooks[i].replacement == replacement && *slot == replacement;
            ReleaseSRWLockExclusive(&hookLock);
            return same;
        }
    }
    if (hookCount == _countof(hooks)) { ReleaseSRWLockExclusive(&hookLock); return false; }
    DWORD protection;
    if (!VirtualProtect(slot, sizeof(void *), PAGE_READWRITE, &protection)) {
        ReleaseSRWLockExclusive(&hookLock); return false;
    }
    void *original = InterlockedExchangePointer(slot, replacement);
    hooks[hookCount++] = { slot, original, replacement };
    DWORD ignored;
    VirtualProtect(slot, sizeof(void *), protection, &ignored);
    ReleaseSRWLockExclusive(&hookLock);
    return true;
}

static void *Original(void *object, size_t index)
{
    if (!object || !Readable(object, sizeof(void *))) return nullptr;
    void **table = *reinterpret_cast<void ***>(object);
    void **slot = table + index;
    AcquireSRWLockShared(&hookLock);
    void *result = nullptr;
    for (const auto &chain : chainTables) {
        if (chain->alive && chain->object == object && chain->Table() == table && index < chain->methods) {
            result = chain->original[index];
            break;
        }
    }
    for (size_t i = 0; i < hookCount; ++i)
        if (!result && hooks[i].slot == slot) { result = hooks[i].original; break; }
    ReleaseSRWLockShared(&hookLock);
    return result;
}

static bool PatchImport(HMODULE module, const char *name, void *replacement, void **old = nullptr)
{
    BYTE *base = reinterpret_cast<BYTE *>(module);
    if (!base || !Readable(base, sizeof(IMAGE_DOS_HEADER))) return false;
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER *>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0 || dos->e_lfanew > 0x100000) return false;
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS32 *>(base + dos->e_lfanew);
    if (!Readable(nt, sizeof(*nt)) || nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) return false;
    DWORD imageSize = nt->OptionalHeader.SizeOfImage;
    auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!directory.VirtualAddress || directory.VirtualAddress >= imageSize) return false;
    auto descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR *>(base + directory.VirtualAddress);
    bool patched = false;
    for (; Readable(descriptor, sizeof(*descriptor)) && descriptor->Name; ++descriptor) {
        if (descriptor->FirstThunk >= imageSize || descriptor->OriginalFirstThunk >= imageSize) continue;
        // dgVoodoo's packed PE deliberately has no OriginalFirstThunk. Its live
        // import addresses are still usable after the DLL loader has run.
        if (!descriptor->OriginalFirstThunk) {
            if (descriptor->Name >= imageSize || !Readable(base + descriptor->Name, 1)) continue;
            auto libraryName = reinterpret_cast<const char *>(base + descriptor->Name);
            HMODULE library = GetModuleHandleA(libraryName);
            FARPROC expected = library ? systemGetProc(library, name) : nullptr;
            auto pointers = reinterpret_cast<IMAGE_THUNK_DATA32 *>(base + descriptor->FirstThunk);
            for (size_t i = 0; expected && Readable(pointers + i, sizeof(*pointers)) &&
                pointers[i].u1.Function; ++i) {
                auto slot = reinterpret_cast<void **>(&pointers[i].u1.Function);
                if (*slot != reinterpret_cast<void *>(expected)) continue;
                if (old && !*old) *old = *slot;
                patched = PatchSlot(slot, replacement) || patched;
            }
            continue;
        }
        auto names = reinterpret_cast<IMAGE_THUNK_DATA32 *>(base + descriptor->OriginalFirstThunk);
        auto pointers = reinterpret_cast<IMAGE_THUNK_DATA32 *>(base + descriptor->FirstThunk);
        for (size_t i = 0; Readable(names + i, sizeof(*names)) && names[i].u1.AddressOfData; ++i) {
            DWORD rva = names[i].u1.AddressOfData;
            if (IMAGE_SNAP_BY_ORDINAL32(rva) || rva >= imageSize) continue;
            auto entry = reinterpret_cast<IMAGE_IMPORT_BY_NAME *>(base + rva);
            if (!Readable(entry, 2 + strlen(name) + 1) || strcmp(reinterpret_cast<char *>(entry->Name), name)) continue;
            auto slot = reinterpret_cast<void **>(&pointers[i].u1.Function);
            if (old && !*old) *old = *slot;
            patched = PatchSlot(slot, replacement) || patched;
        }
    }
    return patched;
}

struct SceneImage {
    DWORD id = 0;
    DWORD width = 0;
    DWORD height = 0;
    std::vector<BYTE> original;
    std::vector<BYTE> keyed;
};
static std::array<SceneImage, 21> images;

// Validate the complete run structure before changing any payload. Transparent
// skip runs are retained; opaque pixels use RGB565 0xF01E. The engine's later
// CImageSurface blit discards exact 0xF81F, so that magic color cannot carry our
// compositor key through the complete CPU surface pipeline.
static bool MakeKeyedImage(SceneImage &image)
{
    const auto &source = image.original;
    if (source.size() < 64 || memcmp(source.data(), "xbm\0", 4) ||
        source[28] != 16 || source[29] != 16 || source[30] != 1 ||
        source[31] || source[36]) return false;
    image.width = Read32(source.data() + 20);
    image.height = Read32(source.data() + 24);
    if (!image.width || image.width > 2048 || !image.height || image.height > 2048 ||
        source.size() < 64 + image.height * 4) return false;
    image.keyed = source;
    for (DWORD y = 0; y < image.height; ++y) {
        size_t position = 64 + Read32(source.data() + 64 + y * 4);
        size_t end = y + 1 < image.height ?
            64 + Read32(source.data() + 64 + (y + 1) * 4) : source.size();
        if (position > end || end > source.size() || position < 64 + image.height * 4) return false;
        DWORD xBytes = 0;
        while (position < end) {
            if (end - position < 2) return false;
            WORD skip = Read16(source.data() + position);
            position += 2;
            xBytes += skip;
            // A final skip to the right edge has no copy-count field.
            if (xBytes == image.width * 2 && position == end) break;
            if (xBytes > image.width * 2 || end - position < 2) return false;
            WORD count = Read16(source.data() + position);
            position += 2;
            xBytes += count;
            if ((skip & 1) || (count & 1) || xBytes > image.width * 2 || count > end - position) return false;
            for (size_t p = position; p < position + count; p += 2) {
                image.keyed[p] = 0x1e;
                image.keyed[p + 1] = 0xf0;
            }
            position += count;
        }
        if (position != end || xBytes != image.width * 2) return false;
    }
    return true;
}

static bool ReadExact(HANDLE file, DWORD offset, void *bytes, DWORD count)
{
    LARGE_INTEGER location = {};
    location.QuadPart = offset;
    DWORD read = 0;
    return SetFilePointerEx(file, location, nullptr, FILE_BEGIN) &&
        ReadFile(file, bytes, count, &read, nullptr) && read == count;
}

static bool LoadSceneImages(const wchar_t *directory)
{
    wchar_t path[MAX_PATH];
    if (swprintf_s(path, L"%sres\\ResNewUI.Lib", directory) < 0) return false;
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    BYTE header[256] = {};
    LARGE_INTEGER fileSize = {};
    bool result = GetFileSizeEx(file, &fileSize) && ReadExact(file, 0, header, sizeof(header)) &&
        !memcmp(header, "Lib 1.01", 8);
    const DWORD ids[21] = { 214, 1083, 1084, 1085, 1086, 1087, 1119, 1120, 1121, 1122,
        1089, 1090, 1091, 1092, 1093, 1243, 1244, 1245, 1246, 1247, 2627 };
    auto load = [&](size_t i) {
        DWORD entry[2] = {};
        bool valid = ids[i] < Read32(header + 16) && ReadExact(file, 256 + ids[i] * 8, entry, sizeof(entry)) &&
            entry[1] >= 64 && entry[1] <= 4 * 1024 * 1024 &&
            static_cast<ULONGLONG>(entry[0]) + entry[1] <= static_cast<ULONGLONG>(fileSize.QuadPart);
        if (!valid) return false;
        images[i].id = ids[i];
        images[i].original.resize(entry[1]);
        valid = ReadExact(file, entry[0], images[i].original.data(), entry[1]) && MakeKeyedImage(images[i]);
        if (!valid && logger) logger("HD menu XBM validation failed id=%lu offset=%lu size=%lu", ids[i], entry[0], entry[1]);
        return valid;
    };
    for (size_t i = 0; result && i < 20; ++i) result = load(i);
    if (result && openingEnabled && (!load(20) || images[20].width != 640 || images[20].height != 480)) {
        openingEnabled = false;
        images[20] = {};
        if (logger) logger("HD opening title resource guard failed; original title retained");
    }
    CloseHandle(file);
    return result;
}

struct Paper {
    void *object = nullptr;
    void *keyed = nullptr;
    void **originalTable = nullptr;
    void *table[18] = {};
    DWORD id = 0;
};
static Paper papers[128] = {};
static LONG matchedPapers;
static volatile LONG backgroundKeyDraws;
static volatile LONG openingKeyDraws;
static void PrepareForPaint();

static Paper *FindPaper(void *object)
{
    for (auto &paper : papers) if (paper.object == object) return &paper;
    return nullptr;
}

// EDX:EAX carries {replacement-this, target-function} to an x86 tail thunk.
// The thunk retains the exact caller stack, covering all six original Paint
// overloads without guessing their different stack argument counts.
static unsigned __int64 __cdecl Dispatch(void *object, DWORD slot)
{
    AcquireSRWLockShared(&paperLock);
    Paper *initial = FindPaper(object);
    DWORD imageId = initial ? initial->id : 0;
    ReleaseSRWLockShared(&paperLock);
    // Only this exact original title bitmap may open the startup interval.
    // World-loading screens and the subsequent movies never activate it.
    if (imageId == 2627 && openingEnabled && !openingClosed && ApplicationState() <= 1 &&
            !InterlockedExchange(&openingActive, 1) && logger)
        logger("HD opening title activated by first Paint id=2627 state=%lu", ApplicationState());
    // The game's complete scene is cached after its first Paint. Establish key
    // substitution before that Paint, rather than waiting for its first Present.
    SceneKind wanted = WantedScene();
    if (wanted && (!compositorReady || compositorScene != wanted)) PrepareForPaint();
    AcquireSRWLockShared(&paperLock);
    Paper *paper = FindPaper(object);
    void *target = nullptr;
    void *destination = object;
    if (paper) {
        target = paper->originalTable[slot / 4];
        bool sceneImage = paper->id == 2627 ? wanted == OpeningScene : wanted == MenuScene;
        if (compositorReady && compositorScene == wanted && sceneImage && paper->keyed) {
            destination = paper->keyed;
            target = (*reinterpret_cast<void ***>(destination))[slot / 4];
            InterlockedIncrement(&keyDrawGeneration);
            if (paper->id == 214 && InterlockedIncrement(&backgroundKeyDraws) == 1 && logger)
                logger("HD menu first full background keyed object=%p slot=%lu", object, slot / 4);
            if (paper->id == 2627 && InterlockedIncrement(&openingKeyDraws) == 1 && logger)
                logger("HD opening title first background keyed object=%p slot=%lu", object, slot / 4);
        }
    }
    ReleaseSRWLockShared(&paperLock);
    return (static_cast<unsigned __int64>(reinterpret_cast<uintptr_t>(destination)) << 32) |
        reinterpret_cast<uintptr_t>(target);
}

#define SEAL_HD_DRAW_THUNK(Name, Slot) \
    static __declspec(naked) void Name() { \
        __asm { pushfd } \
        __asm { pushad } \
        __asm { push Slot } \
        __asm { push ecx } \
        __asm { call Dispatch } \
        __asm { add esp, 8 } \
        __asm { mov dword ptr [esp + 24], edx } \
        __asm { mov dword ptr [esp + 28], eax } \
        __asm { popad } \
        __asm { popfd } \
        __asm { jmp eax } \
    }
SEAL_HD_DRAW_THUNK(Draw0c, 0x0c)
SEAL_HD_DRAW_THUNK(Draw10, 0x10)
SEAL_HD_DRAW_THUNK(Draw14, 0x14)
SEAL_HD_DRAW_THUNK(Draw18, 0x18)
SEAL_HD_DRAW_THUNK(Draw1c, 0x1c)
SEAL_HD_DRAW_THUNK(Draw20, 0x20)
#undef SEAL_HD_DRAW_THUNK

static void * __fastcall DeletePaper(void *object, void *, unsigned int flags)
{
    DeletePaperFn original = nullptr;
    void *keyed = nullptr;
    AcquireSRWLockExclusive(&paperLock);
    Paper *paper = FindPaper(object);
    if (paper) {
        original = reinterpret_cast<DeletePaperFn>(paper->originalTable[0]);
        *reinterpret_cast<void ***>(object) = paper->originalTable;
        keyed = paper->keyed;
        paper->object = nullptr;
        paper->keyed = nullptr;
    }
    ReleaseSRWLockExclusive(&paperLock);
    if (keyed) reinterpret_cast<DeletePaperFn>((*reinterpret_cast<void ***>(keyed))[0])(keyed, 1);
    return original ? original(object, flags) : object;
}

static int __cdecl CreateXBmp(void *bytes, void *&output)
{
    int result = createXBmp(bytes, output);
    if (!result || !output || !enabled || !Readable(bytes, 64)) return result;
    DWORD width = Read32(static_cast<BYTE *>(bytes) + 20);
    DWORD height = Read32(static_cast<BYTE *>(bytes) + 24);
    const SceneImage *match = nullptr;
    for (const auto &image : images) {
        if (image.width == width && image.height == height && Readable(bytes, image.original.size()) &&
            !memcmp(bytes, image.original.data(), image.original.size())) { match = &image; break; }
    }
    if (!match) return result;
    void **table = *reinterpret_cast<void ***>(output);
    BYTE *gfx = reinterpret_cast<BYTE *>(graphics);
    if (table != reinterpret_cast<void **>(gfx + 0xe38bc) ||
        !Readable(table, 18 * sizeof(void *)) || table[0] != gfx + 0x54720 ||
        table[3] != gfx + 0x69310 || table[4] != gfx + 0x681a0) {
        if (logger) logger("HD menu rejected unexpected RTBMP vtable id=%lu object=%p table=%p", match->id, output, table);
        return result;
    }
    void *keyed = nullptr;
    if (!createXBmp(const_cast<BYTE *>(match->keyed.data()), keyed) || !keyed) return result;
    AcquireSRWLockExclusive(&paperLock);
    Paper *record = nullptr;
    for (auto &paper : papers) if (!paper.object) { record = &paper; break; }
    if (record) {
        record->object = output;
        record->keyed = keyed;
        record->originalTable = table;
        record->id = match->id;
        memcpy(record->table, table, sizeof(record->table));
        record->table[0] = reinterpret_cast<void *>(DeletePaper);
        record->table[3] = reinterpret_cast<void *>(Draw0c);
        record->table[4] = reinterpret_cast<void *>(Draw10);
        record->table[5] = reinterpret_cast<void *>(Draw14);
        record->table[6] = reinterpret_cast<void *>(Draw18);
        record->table[7] = reinterpret_cast<void *>(Draw1c);
        record->table[8] = reinterpret_cast<void *>(Draw20);
        *reinterpret_cast<void ***>(output) = record->table;
    }
    ReleaseSRWLockExclusive(&paperLock);
    if (!record) reinterpret_cast<DeletePaperFn>((*reinterpret_cast<void ***>(keyed))[0])(keyed, 1);
    else if (logger) logger("HD menu recognized scene id=%lu dimensions=%lux%lu object=%p keyed=%p count=%ld",
        match->id, width, height, output, keyed, InterlockedIncrement(&matchedPapers));
    return result;
}

static const char shaders[] = R"HLSL(
Texture2D<float4> gameFrame : register(t0);
Texture2D<float4> scene : register(t1);
SamplerState imageSampler : register(s0);
cbuffer Frame : register(b0) {
    float outputAspect;
    float sceneAspect;
    float seconds;
    float fireAnimation;
};
struct Vertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
Vertex VSMain(uint id : SV_VertexID) {
    Vertex v;
    float2 p = float2((id << 1) & 2, id & 2);
    v.uv = p;
    v.position = float4(p.x * 2 - 1, 1 - p.y * 2, 0, 1);
    return v;
}
float rectangle(float2 p, float2 low, float2 high, float fade) {
    float2 a = smoothstep(low, low + fade, p);
    float2 b = 1 - smoothstep(high - fade, high, p);
    return a.x * a.y * b.x * b.y;
}
float4 PSMain(Vertex input) : SV_Target {
    // COVER fills the physical monitor. On a 16:10 display this crops only the
    // outer horizontal portions of a 16:9 image, preserving original UI mapping.
    float2 bgUV = input.uv;
    if (outputAspect < sceneAspect) bgUV.x = .5 + (bgUV.x - .5) * outputAspect / sceneAspect;
    else bgUV.y = .5 + (bgUV.y - .5) * sceneAspect / outputAspect;
    float flame = rectangle(bgUV, float2(.548, .772), float2(.68, .835), .009) * fireAnimation;
    float flicker = sin(seconds * 11.1) * .45 + sin(seconds * 17.7 + 1.2) * .3 + sin(seconds * 6.3) * .25;
    float2 warpedUV = bgUV;
    // Warp only the flames below the cauldron. The rim/body and village geometry
    // retain their exact generated silhouette. No low-resolution art is overlaid.
    warpedUV.x += flame * sin(bgUV.y * 540 - seconds * 13) * .00065;
    warpedUV.y += flame * sin(bgUV.x * 450 + seconds * 9) * .0003;
    float3 background = scene.SampleLevel(imageSampler, warpedUV, 0).rgb;
    float emissive = saturate((background.r - background.g) * 3) * smoothstep(.4, .95, background.r);
    background *= 1 + flame * emissive * flicker * .075;
    float glow = rectangle(bgUV, float2(.53, .815), float2(.70, .91), .035) * fireAnimation;
    background += glow * flicker * float3(.009, .0035, .0005);

    // Original widgets occupy the existing centered 4:3 viewport, so all input
    // coordinate hooks continue to use their already validated mapping.
    float uiAspect = 4.0 / 3.0;
    float2 uiUV = input.uv;
    bool inUI = outputAspect >= uiAspect ?
        abs(input.uv.x - .5) <= .5 * uiAspect / outputAspect :
        abs(input.uv.y - .5) <= .5 * outputAspect / uiAspect;
    float3 ui = gameFrame.SampleLevel(imageSampler, uiUV, 0).rgb;
    // The CPU UI may darken our key behind its translucent panels, or mix it
    // with antialiased text and bilinear widget edges. Recover that key's
    // contribution continuously, preserving the foreground's premultiplied
    // color and opacity instead of leaving dark-purple blocks and fringes.
    const float3 keyColor = float3(30.0 / 31.0, 0, 30.0 / 31.0);
    float keyFraction = saturate((min(ui.r, ui.b) - ui.g) / keyColor.r);
    float3 foreground = max(0, ui - keyFraction * keyColor);
    float3 composed = foreground + keyFraction * saturate(background);
    return float4(saturate(inUI ? composed : background), 1);
}
)HLSL";

struct Gpu {
    IDXGISwapChain *owner = nullptr; // weak, never prolong the game's chain
    ID3D11Device *device = nullptr;
    ID3D11DeviceContext1 *context = nullptr;
    ID3DDeviceContextState *state = nullptr;
    ID3D11Texture2D *frame = nullptr;
    ID3D11ShaderResourceView *frameView = nullptr;
    ID3D11ShaderResourceView *sceneView = nullptr;
    ID3D11VertexShader *vertex = nullptr;
    ID3D11PixelShader *pixel = nullptr;
    ID3D11SamplerState *sampler = nullptr;
    ID3D11RasterizerState *rasterizer = nullptr;
    ID3D11Buffer *constants = nullptr;
    D3D11_TEXTURE2D_DESC frameDescription = {};
    float sceneAspect = 16.0f / 9.0f;
    ULONGLONG start = 0;
    LONG composed = 0;
    bool failed = false;
    LONG retirementEpoch = 0;
    SceneKind sceneKind = NoScene;
};
static Gpu gpu;
static volatile LONG acquiredChains;
static volatile LONG presentCalls;

static void DestroyGpu(bool clearFailure)
{
    InterlockedExchange(&compositorReady, 0);
    InterlockedExchange(&compositorScene, NoScene);
    Release(gpu.state);
    Release(gpu.constants);
    Release(gpu.rasterizer);
    Release(gpu.sampler);
    Release(gpu.pixel);
    Release(gpu.vertex);
    Release(gpu.sceneView);
    Release(gpu.frameView);
    Release(gpu.frame);
    Release(gpu.context);
    Release(gpu.device);
    gpu.frameDescription = {};
    gpu.owner = nullptr;
    if (clearFailure) gpu.failed = false;
}

using CompileFn = HRESULT (WINAPI *)(LPCVOID, SIZE_T, LPCSTR, const D3D_SHADER_MACRO *,
    ID3DInclude *, LPCSTR, LPCSTR, UINT, UINT, ID3DBlob **, ID3DBlob **);

static HRESULT Compile(const char *entry, const char *profile, ID3DBlob **output)
{
    if (!compilerModule) compilerModule = LoadLibraryExW(L"d3dcompiler_47.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto compile = compilerModule ? reinterpret_cast<CompileFn>(systemGetProc(compilerModule, "D3DCompile")) : nullptr;
    if (!compile) return HRESULT_FROM_WIN32(GetLastError());
    ID3DBlob *errors = nullptr;
    HRESULT result = compile(shaders, sizeof(shaders) - 1, "SealHD-menu", nullptr, nullptr,
        entry, profile, D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, output, &errors);
    if (FAILED(result) && logger) logger("HD menu shader %s failed hr=%08lx text=%.1200s", entry, result,
        errors ? static_cast<const char *>(errors->GetBufferPointer()) : "");
    Release(errors);
    return result;
}

static HRESULT LoadSceneTexture(ID3D11Device *device, ID3D11ShaderResourceView **output, float &aspect,
    const wchar_t *path)
{
    HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    bool uninitialize = SUCCEEDED(apartment);
    IWICImagingFactory *factory = nullptr;
    IWICBitmapDecoder *decoder = nullptr;
    IWICBitmapFrameDecode *frame = nullptr;
    IWICFormatConverter *converter = nullptr;
    ID3D11Texture2D *texture = nullptr;
    HRESULT result = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        __uuidof(IWICImagingFactory), reinterpret_cast<void **>(&factory));
    if (SUCCEEDED(result)) result = factory->CreateDecoderFromFilename(path, nullptr,
        GENERIC_READ, WICDecodeMetadataCacheOnLoad, &decoder);
    if (SUCCEEDED(result)) result = decoder->GetFrame(0, &frame);
    UINT width = 0, height = 0;
    if (SUCCEEDED(result)) result = frame->GetSize(&width, &height);
    if (SUCCEEDED(result) && (width < 1024 || height < 600 || width > 8192 || height > 8192)) result = E_INVALIDARG;
    if (SUCCEEDED(result)) result = factory->CreateFormatConverter(&converter);
    if (SUCCEEDED(result)) result = converter->Initialize(frame, GUID_WICPixelFormat32bppRGBA,
        WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom);
    std::vector<BYTE> pixels;
    if (SUCCEEDED(result)) {
        pixels.resize(static_cast<size_t>(width) * height * 4);
        result = converter->CopyPixels(nullptr, width * 4, static_cast<UINT>(pixels.size()), pixels.data());
    }
    if (SUCCEEDED(result)) {
        D3D11_TEXTURE2D_DESC description = {};
        description.Width = width; description.Height = height;
        description.MipLevels = description.ArraySize = description.SampleDesc.Count = 1;
        description.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        description.Usage = D3D11_USAGE_IMMUTABLE;
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data = { pixels.data(), width * 4, 0 };
        result = device->CreateTexture2D(&description, &data, &texture);
        if (SUCCEEDED(result)) result = device->CreateShaderResourceView(texture, nullptr, output);
        aspect = static_cast<float>(width) / height;
    }
    if (logger) logger("HD menu PNG load hr=%08lx texture=%ux%u path=%ls", result, width, height, path);
    Release(texture); Release(converter); Release(frame); Release(decoder); Release(factory);
    if (uninitialize) CoUninitialize();
    return result;
}

static HRESULT InitializeGpu(IDXGISwapChain *chain, SceneKind sceneKind)
{
    gpu.owner = chain;
    gpu.sceneKind = sceneKind;
    gpu.retirementEpoch = InterlockedCompareExchange(&chainRetirementEpoch, 0, 0);
    HRESULT result = chain->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void **>(&gpu.device));
    ID3D11DeviceContext *context = nullptr;
    ID3D11Device1 *device1 = nullptr;
    ID3DBlob *vertex = nullptr, *pixel = nullptr;
    if (SUCCEEDED(result)) {
        gpu.device->GetImmediateContext(&context);
        result = context->QueryInterface(__uuidof(ID3D11DeviceContext1), reinterpret_cast<void **>(&gpu.context));
    }
    if (SUCCEEDED(result)) result = gpu.device->QueryInterface(__uuidof(ID3D11Device1), reinterpret_cast<void **>(&device1));
    if (SUCCEEDED(result)) {
        D3D_FEATURE_LEVEL requested = gpu.device->GetFeatureLevel(), selected;
        UINT stateFlags = (gpu.device->GetCreationFlags() & D3D11_CREATE_DEVICE_SINGLETHREADED) ?
            D3D11_1_CREATE_DEVICE_CONTEXT_STATE_SINGLETHREADED : 0;
        result = device1->CreateDeviceContextState(stateFlags, &requested, 1, D3D11_SDK_VERSION,
            __uuidof(ID3D11Device), &selected, &gpu.state);
    }
    if (SUCCEEDED(result)) result = Compile("VSMain", "vs_4_0", &vertex);
    if (SUCCEEDED(result)) result = Compile("PSMain", "ps_4_0", &pixel);
    if (SUCCEEDED(result)) result = gpu.device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &gpu.vertex);
    if (SUCCEEDED(result)) result = gpu.device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &gpu.pixel);
    if (SUCCEEDED(result)) result = LoadSceneTexture(gpu.device, &gpu.sceneView, gpu.sceneAspect,
        sceneKind == OpeningScene ? openingImagePath : imagePath);
    if (SUCCEEDED(result)) {
        D3D11_SAMPLER_DESC sampler = {};
        sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.MaxLOD = D3D11_FLOAT32_MAX;
        result = gpu.device->CreateSamplerState(&sampler, &gpu.sampler);
    }
    if (SUCCEEDED(result)) {
        D3D11_RASTERIZER_DESC rasterizer = {};
        rasterizer.FillMode = D3D11_FILL_SOLID;
        rasterizer.CullMode = D3D11_CULL_NONE;
        rasterizer.DepthClipEnable = TRUE;
        result = gpu.device->CreateRasterizerState(&rasterizer, &gpu.rasterizer);
    }
    if (SUCCEEDED(result)) {
        D3D11_BUFFER_DESC constants = {};
        constants.ByteWidth = 16;
        constants.Usage = D3D11_USAGE_DEFAULT;
        constants.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        result = gpu.device->CreateBuffer(&constants, nullptr, &gpu.constants);
    }
    Release(vertex); Release(pixel); Release(device1); Release(context);
    if (FAILED(result)) {
        if (logger) logger("HD menu compositor setup failed hr=%08lx; original scene remains active", result);
        DestroyGpu(false);
        gpu.failed = true;
        return result;
    }
    gpu.start = GetTickCount64();
    gpu.composed = 0;
    InterlockedExchange(&compositorScene, sceneKind);
    InterlockedExchange(&compositorReady, 1);
    if (logger) logger("HD menu compositor ready chain=%p D3D11.1 state isolation=1 feature=%x scene=%ld",
        chain, gpu.device->GetFeatureLevel(), sceneKind);
    return S_OK;
}

static void PrepareMenuChain(IDXGISwapChain *chain)
{
    SceneKind wanted = WantedScene();
    if (!enabled || !wanted) return;
    AcquireSRWLockExclusive(&gpuLock);
    if ((gpu.sceneKind && gpu.sceneKind != wanted) || (gpu.owner && (gpu.owner != chain ||
            gpu.retirementEpoch != InterlockedCompareExchange(&chainRetirementEpoch, 0, 0)))) DestroyGpu(true);
    if (enabled && WantedScene() == wanted && !gpu.device && !gpu.failed) {
        HRESULT result = InitializeGpu(chain, wanted);
        if (logger) logger("HD menu prepared before first Paint chain=%p hr=%08lx scene=%ld", chain, result, wanted);
    }
    ReleaseSRWLockExclusive(&gpuLock);
}

static void PrepareForPaint()
{
    if (!enabled || !WantedScene()) return;
    // The factory queue already owns a reference until the surrounding D3D8
    // Present completes. Borrow it only for this call, so an older startup
    // chain is never pinned across the engine's display-mode replacement.
    AcquireSRWLockShared(&hookLock);
    IDXGISwapChain *chain = pendingChains.empty() ? nullptr : pendingChains.back();
    if (chain) chain->AddRef();
    ReleaseSRWLockShared(&hookLock);
    if (!chain) return;
    PrepareMenuChain(chain);
    chain->Release();
}

static DXGI_FORMAT ViewFormat(DXGI_FORMAT format)
{
    if (format == DXGI_FORMAT_R8G8B8A8_TYPELESS) return DXGI_FORMAT_R8G8B8A8_UNORM;
    if (format == DXGI_FORMAT_B8G8R8A8_TYPELESS) return DXGI_FORMAT_B8G8R8A8_UNORM;
    if (format == DXGI_FORMAT_B8G8R8X8_TYPELESS) return DXGI_FORMAT_B8G8R8X8_UNORM;
    if (format == DXGI_FORMAT_R10G10B10A2_TYPELESS) return DXGI_FORMAT_R10G10B10A2_UNORM;
    return format;
}

static HRESULT MakeFrameCopy(ID3D11Texture2D *backbuffer)
{
    D3D11_TEXTURE2D_DESC source = {};
    backbuffer->GetDesc(&source);
    if (gpu.frame && source.Width == gpu.frameDescription.Width &&
        source.Height == gpu.frameDescription.Height && source.Format == gpu.frameDescription.Format &&
        source.SampleDesc.Count == gpu.frameDescription.SampleDesc.Count) return S_OK;
    Release(gpu.frameView); Release(gpu.frame);
    gpu.frameDescription = source;
    D3D11_TEXTURE2D_DESC copy = source;
    copy.MipLevels = copy.ArraySize = copy.SampleDesc.Count = 1;
    copy.SampleDesc.Quality = 0;
    copy.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    copy.CPUAccessFlags = copy.MiscFlags = 0;
    copy.Usage = D3D11_USAGE_DEFAULT;
    HRESULT result = gpu.device->CreateTexture2D(&copy, nullptr, &gpu.frame);
    if (SUCCEEDED(result)) {
        D3D11_SHADER_RESOURCE_VIEW_DESC view = {};
        view.Format = ViewFormat(copy.Format);
        view.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        view.Texture2D.MipLevels = 1;
        result = gpu.device->CreateShaderResourceView(gpu.frame, &view, &gpu.frameView);
    }
    if (logger) logger("HD menu output surface hr=%08lx size=%ux%u format=%u samples=%u", result,
        source.Width, source.Height, source.Format, source.SampleDesc.Count);
    return result;
}

static void Composite(IDXGISwapChain *chain)
{
    // Initialization touches WIC, shader compilation and D3D11 context-state
    // objects. Keep those operations inside the menu's rendering lifetime.
    SceneKind wanted = WantedScene();
    if (!enabled || !wanted) return;
    AcquireSRWLockExclusive(&gpuLock);
    if (WantedScene() != wanted) { ReleaseSRWLockExclusive(&gpuLock); return; }
    if ((gpu.sceneKind && gpu.sceneKind != wanted) || (gpu.owner && (gpu.owner != chain ||
            gpu.retirementEpoch != InterlockedCompareExchange(&chainRetirementEpoch, 0, 0)))) DestroyGpu(true);
    if (!gpu.device && !gpu.failed) InitializeGpu(chain, wanted);
    if (!gpu.device || WantedScene() != wanted || !keyDrawGeneration) { ReleaseSRWLockExclusive(&gpuLock); return; }
    ID3D11Texture2D *backbuffer = nullptr;
    ID3D11RenderTargetView *target = nullptr;
    HRESULT result = chain->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void **>(&backbuffer));
    if (SUCCEEDED(result)) result = MakeFrameCopy(backbuffer);
    if (SUCCEEDED(result)) result = gpu.device->CreateRenderTargetView(backbuffer, nullptr, &target);
    if (SUCCEEDED(result)) {
        ID3DDeviceContextState *previous = nullptr;
        gpu.context->SwapDeviceContextState(gpu.state, &previous);
        if (gpu.frameDescription.SampleDesc.Count > 1)
            gpu.context->ResolveSubresource(gpu.frame, 0, backbuffer, 0, ViewFormat(gpu.frameDescription.Format));
        else gpu.context->CopyResource(gpu.frame, backbuffer);
        struct Constants { float aspect, sceneAspect, seconds, animation; } constants = {
            static_cast<float>(gpu.frameDescription.Width) / gpu.frameDescription.Height,
            gpu.sceneAspect, static_cast<float>(GetTickCount64() - gpu.start) * .001f,
            wanted == MenuScene && animateFire ? 1.f : 0.f };
        gpu.context->UpdateSubresource(gpu.constants, 0, nullptr, &constants, 0, 0);
        gpu.context->IASetInputLayout(nullptr);
        gpu.context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        gpu.context->VSSetShader(gpu.vertex, nullptr, 0);
        gpu.context->GSSetShader(nullptr, nullptr, 0);
        gpu.context->HSSetShader(nullptr, nullptr, 0);
        gpu.context->DSSetShader(nullptr, nullptr, 0);
        gpu.context->PSSetShader(gpu.pixel, nullptr, 0);
        gpu.context->PSSetConstantBuffers(0, 1, &gpu.constants);
        ID3D11ShaderResourceView *views[] = { gpu.frameView, gpu.sceneView };
        gpu.context->PSSetShaderResources(0, 2, views);
        gpu.context->PSSetSamplers(0, 1, &gpu.sampler);
        gpu.context->RSSetState(gpu.rasterizer);
        D3D11_VIEWPORT viewport = { 0, 0, static_cast<float>(gpu.frameDescription.Width),
            static_cast<float>(gpu.frameDescription.Height), 0, 1 };
        gpu.context->RSSetViewports(1, &viewport);
        gpu.context->OMSetRenderTargets(1, &target, nullptr);
        gpu.context->Draw(3, 0);
        ID3D11ShaderResourceView *empty[2] = {};
        gpu.context->PSSetShaderResources(0, 2, empty);
        gpu.context->OMSetRenderTargets(0, nullptr, nullptr);
        ID3DDeviceContextState *ours = nullptr;
        gpu.context->SwapDeviceContextState(previous, &ours);
        Release(ours); Release(previous);
        if (++gpu.composed == 1 || gpu.composed % 600 == 0)
            if (logger) logger("HD menu composite count=%ld output=%ux%u keyDraws=%ld state=%lu scene=%ld",
                gpu.composed, gpu.frameDescription.Width, gpu.frameDescription.Height, keyDrawGeneration,
                ApplicationState(), wanted);
    } else {
        if (logger) logger("HD menu frame preparation failed hr=%08lx; key redirection disabled", result);
        // The current keyed frame may be visible once; the very next game draw
        // uses its original bitmap again. Do not keep displaying a broken key.
        DestroyGpu(false);
        gpu.failed = true;
    }
    Release(target); Release(backbuffer);
    ReleaseSRWLockExclusive(&gpuLock);
}

using PresentFn = HRESULT (STDMETHODCALLTYPE *)(IDXGISwapChain *, UINT, UINT);
using Present1Fn = HRESULT (STDMETHODCALLTYPE *)(IDXGISwapChain1 *, UINT, UINT, const DXGI_PRESENT_PARAMETERS *);
using ResizeFn = HRESULT (STDMETHODCALLTYPE *)(IDXGISwapChain *, UINT, UINT, UINT, DXGI_FORMAT, UINT);
using Resize1Fn = HRESULT (STDMETHODCALLTYPE *)(void *, UINT, UINT, UINT, DXGI_FORMAT, UINT, const UINT *, IUnknown *const *);
using ReleaseFn = ULONG (STDMETHODCALLTYPE *)(IUnknown *);

static HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain *chain, UINT interval, UINT flags)
{
    auto original = reinterpret_cast<PresentFn>(Original(chain, 8));
    if (!original) return E_UNEXPECTED;
    if (!(flags & DXGI_PRESENT_TEST)) Composite(chain);
    LONG count = InterlockedIncrement(&presentCalls);
    if (count == 1 && logger) logger("HD menu DXGI Present acquired chain=%p flags=%x", chain, flags);
    HRESULT result = original(chain, interval, flags);
    if (result == DXGI_ERROR_DEVICE_REMOVED || result == DXGI_ERROR_DEVICE_RESET) {
        AcquireSRWLockExclusive(&gpuLock); DestroyGpu(true); ReleaseSRWLockExclusive(&gpuLock);
    }
    return result;
}

static HRESULT STDMETHODCALLTYPE Present1(IDXGISwapChain1 *chain, UINT interval, UINT flags,
    const DXGI_PRESENT_PARAMETERS *parameters)
{
    auto original = reinterpret_cast<Present1Fn>(Original(chain, 22));
    if (!original) return E_UNEXPECTED;
    if (!(flags & DXGI_PRESENT_TEST)) Composite(chain);
    return original(chain, interval, flags, parameters);
}

static HRESULT STDMETHODCALLTYPE Resize(IDXGISwapChain *chain, UINT count, UINT width, UINT height,
    DXGI_FORMAT format, UINT flags)
{
    auto original = reinterpret_cast<ResizeFn>(Original(chain, 13));
    if (!original) return E_UNEXPECTED;
    AcquireSRWLockExclusive(&gpuLock);
    DestroyGpu(true);
    InterlockedExchange(&keyDrawGeneration, 0);
    ReleaseSRWLockExclusive(&gpuLock);
    HRESULT result = original(chain, count, width, height, format, flags);
    if (SUCCEEDED(result)) PrepareMenuChain(chain);
    if (logger) logger("HD menu DXGI ResizeBuffers %ux%u count=%u hr=%08lx", width, height, count, result);
    return result;
}

static HRESULT STDMETHODCALLTYPE Resize1(void *chain, UINT count, UINT width, UINT height,
    DXGI_FORMAT format, UINT flags, const UINT *masks, IUnknown *const *queues)
{
    auto original = reinterpret_cast<Resize1Fn>(Original(chain, 39));
    if (!original) return E_UNEXPECTED;
    AcquireSRWLockExclusive(&gpuLock);
    DestroyGpu(true);
    InterlockedExchange(&keyDrawGeneration, 0);
    ReleaseSRWLockExclusive(&gpuLock);
    HRESULT result = original(chain, count, width, height, format, flags, masks, queues);
    if (SUCCEEDED(result)) PrepareMenuChain(static_cast<IDXGISwapChain *>(chain));
    return result;
}

static void LogFunctionAddress(const char *name, void *address)
{
    if (!logger) return;
    HMODULE module = nullptr;
    wchar_t path[MAX_PATH] = {};
    if (address && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, reinterpret_cast<LPCWSTR>(address), &module))
        GetModuleFileNameW(module, path, _countof(path));
    char prefix[16 * 3 + 1] = {};
    if (Readable(address, 16)) {
        const BYTE *bytes = static_cast<const BYTE *>(address);
        for (unsigned i = 0; i < 16; ++i)
            _snprintf_s(prefix + i * 3, sizeof(prefix) - i * 3, _TRUNCATE, "%02X ", bytes[i]);
    }
    logger("HD menu %s address=%p module=%ls prefix=%s", name, address,
        path[0] ? path : L"unknown", prefix);
}

static ULONG STDMETHODCALLTYPE ChainRelease(IUnknown *object)
{
    auto original = reinterpret_cast<ReleaseFn>(Original(object, 2));
    if (!original) {
        if (logger) logger("HD menu instance Release original unavailable object=%p", object);
        return 0;
    }
    IUnknown *identity = nullptr;
    AcquireSRWLockShared(&hookLock);
    for (const auto &entry : chainTables)
        if (entry->alive && entry->object == object) { identity = entry->identity; break; }
    ReleaseSRWLockShared(&hookLock);
    ULONG remaining = original(object);
    if (!remaining && identity) {
        AcquireSRWLockExclusive(&hookLock);
        for (auto &entry : chainTables) if (entry->identity == identity) {
            entry->alive = false;
            entry->object = nullptr;
        }
        ReleaseSRWLockExclusive(&hookLock);
        InterlockedExchange(&compositorReady, 0);
        InterlockedIncrement(&chainRetirementEpoch);
        if (logger) logger("HD menu swapchain retired identity=%p", identity);
    }
    return remaining;
}

static bool CaptureChainInstance(IDXGISwapChain *chain)
{
    struct Candidate { IUnknown *object; unsigned methods; bool release; };
    Candidate candidates[5] = {};
    unsigned count = 0;
    auto add = [&](IUnknown *object, unsigned methods, bool release) {
        if (!object) return;
        for (unsigned i = 0; i < count; ++i) if (candidates[i].object == object) {
            if (methods > candidates[i].methods) candidates[i].methods = methods;
            if (release) object->Release();
            return;
        }
        candidates[count++] = { object, methods, release };
    };
    add(chain, 18, false);
    IDXGISwapChain4 *chain4 = nullptr;
    if (SUCCEEDED(chain->QueryInterface(__uuidof(IDXGISwapChain4), reinterpret_cast<void **>(&chain4))))
        add(chain4, 41, true);
    IDXGISwapChain3 *chain3 = nullptr;
    if (SUCCEEDED(chain->QueryInterface(__uuidof(IDXGISwapChain3), reinterpret_cast<void **>(&chain3))))
        add(chain3, 40, true);
    IDXGISwapChain2 *chain2 = nullptr;
    if (SUCCEEDED(chain->QueryInterface(__uuidof(IDXGISwapChain2), reinterpret_cast<void **>(&chain2))))
        add(chain2, 36, true);
    IDXGISwapChain1 *chain1 = nullptr;
    if (SUCCEEDED(chain->QueryInterface(__uuidof(IDXGISwapChain1), reinterpret_cast<void **>(&chain1))))
        add(chain1, 29, true);
    IUnknown *identity = nullptr;
    chain->QueryInterface(__uuidof(IUnknown), reinterpret_cast<void **>(&identity));
    std::array<std::unique_ptr<ChainTable>, 5> prepared;
    DWORD protections[5] = {};
    bool valid = identity != nullptr;
    unsigned writable = 0;
    AcquireSRWLockExclusive(&hookLock);
    try {
        if (chainTables.size() + count > 1024) valid = false;
        for (unsigned i = 0; valid && i < count; ++i) {
            bool captured = false;
            for (const auto &entry : chainTables)
                if (entry->alive && entry->object == candidates[i].object &&
                    *reinterpret_cast<void ***>(entry->object) == entry->Table()) { captured = true; break; }
            if (captured) continue;
            void **table = *reinterpret_cast<void ***>(candidates[i].object);
            if (!Readable(table, candidates[i].methods * sizeof(void *))) { valid = false; break; }
            prepared[i] = std::make_unique<ChainTable>();
            auto &entry = *prepared[i];
            entry.object = candidates[i].object;
            entry.identity = identity;
            entry.originalTable = table;
            entry.methods = candidates[i].methods;
            entry.publicMethods = candidates[i].methods;
            while (entry.methods < entry.original.size() &&
                Readable(table + entry.methods, sizeof(void *)) && Executable(table[entry.methods])) ++entry.methods;
            memcpy(entry.original.data(), table, entry.methods * sizeof(void *));
            if (Readable(table - 1, sizeof(void *))) entry.replacement[0] = table[-1];
            memcpy(entry.Table(), table, entry.methods * sizeof(void *));
            entry.Table()[2] = reinterpret_cast<void *>(ChainRelease);
            entry.Table()[8] = reinterpret_cast<void *>(Present);
            entry.Table()[13] = reinterpret_cast<void *>(Resize);
            if (entry.publicMethods >= 29) entry.Table()[22] = reinterpret_cast<void *>(Present1);
            if (entry.publicMethods >= 40) entry.Table()[39] = reinterpret_cast<void *>(Resize1);
        }
        if (valid) chainTables.reserve(chainTables.size() + count);
    } catch (const std::bad_alloc &) { valid = false; }
    for (; valid && writable < count; ++writable) {
        if (prepared[writable] && !VirtualProtect(candidates[writable].object, sizeof(void *),
                PAGE_READWRITE, &protections[writable])) valid = false;
    }
    for (unsigned i = 0; valid && i < count; ++i)
        if (prepared[i] && *reinterpret_cast<void ***>(prepared[i]->object) != prepared[i]->originalTable)
            valid = false;
    if (valid) {
        for (unsigned i = 0; i < count; ++i) if (prepared[i]) {
            auto &entry = *prepared[i];
            InterlockedExchangePointer(reinterpret_cast<void **>(entry.object), entry.Table());
            chainTables.push_back(std::move(prepared[i]));
        }
    }
    for (unsigned i = writable; i; --i) if (protections[i - 1]) {
        DWORD ignored;
        VirtualProtect(candidates[i - 1].object, sizeof(void *), protections[i - 1], &ignored);
    }
    ReleaseSRWLockExclusive(&hookLock);
    for (unsigned i = 0; i < count; ++i) if (candidates[i].release) candidates[i].object->Release();
    Release(identity);
    if (valid) {
        LogFunctionAddress("instance original Present", Original(chain, 8));
        if (logger) logger("HD menu captured DXGI swapchain=%p privateTable=%p interfaces=%u capture=%ld",
            chain, *reinterpret_cast<void ***>(chain), count, InterlockedIncrement(&acquiredChains));
    } else {
        InterlockedExchange(&compositorReady, 0);
        if (logger) logger("HD menu instance capture failed chain=%p", chain);
    }
    return valid;
}

static void HookChain(IDXGISwapChain *chain)
{
    if (!enabled || !chain || !Readable(chain, sizeof(void *))) return;
    DXGI_SWAP_CHAIN_DESC description = {};
    HRESULT described = chain->GetDesc(&description);
    DWORD ownerProcess = 0;
    if (description.OutputWindow) GetWindowThreadProcessId(description.OutputWindow, &ownerProcess);
    if (FAILED(described) || description.BufferDesc.Width <= 1 || description.BufferDesc.Height <= 1 ||
        !IsWindow(description.OutputWindow) || ownerProcess != GetCurrentProcessId()) {
        if (logger) logger("HD menu skipped helper swapchain=%p output=%ux%u window=%p ownerProcess=%lu hr=%08lx",
            chain, description.BufferDesc.Width, description.BufferDesc.Height,
            description.OutputWindow, ownerProcess, described);
        return;
    }
    AcquireSRWLockExclusive(&hookLock);
    bool queued = false;
    for (auto pending : pendingChains) if (pending == chain) { queued = true; break; }
    if (!queued) {
        try {
            pendingChains.push_back(chain);
            chain->AddRef();
        } catch (const std::bad_alloc &) {
            queued = true;
            InterlockedExchange(&enabled, 0);
            InterlockedExchange(&compositorReady, 0);
        }
    }
    ReleaseSRWLockExclusive(&hookLock);
    if (logger && !queued) logger("HD menu pending real swapchain=%p output=%ux%u window=%p",
        chain, description.BufferDesc.Width, description.BufferDesc.Height, description.OutputWindow);
}

// Arm only after the surrounding D3D8 Present has completed. Steam's native
// overlay initialization therefore observes the original shared method table.
static void ArmPendingChains()
{
    std::vector<IDXGISwapChain *> pending;
    AcquireSRWLockExclusive(&hookLock);
    pending.swap(pendingChains);
    ReleaseSRWLockExclusive(&hookLock);
    for (auto chain : pending) {
        if (enabled) CaptureChainInstance(chain);
        chain->Release();
    }
}

using FactoryCreateFn = HRESULT (STDMETHODCALLTYPE *)(IDXGIFactory *, IUnknown *, DXGI_SWAP_CHAIN_DESC *, IDXGISwapChain **);
using FactoryHwndFn = HRESULT (STDMETHODCALLTYPE *)(IDXGIFactory2 *, IUnknown *, HWND,
    const DXGI_SWAP_CHAIN_DESC1 *, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC *, IDXGIOutput *, IDXGISwapChain1 **);
using FactoryCoreFn = HRESULT (STDMETHODCALLTYPE *)(IDXGIFactory2 *, IUnknown *, IUnknown *,
    const DXGI_SWAP_CHAIN_DESC1 *, IDXGIOutput *, IDXGISwapChain1 **);
using FactoryCompositionFn = HRESULT (STDMETHODCALLTYPE *)(IDXGIFactory2 *, IUnknown *,
    const DXGI_SWAP_CHAIN_DESC1 *, IDXGIOutput *, IDXGISwapChain1 **);

static HRESULT STDMETHODCALLTYPE FactoryCreate(IDXGIFactory *factory, IUnknown *device,
    DXGI_SWAP_CHAIN_DESC *description, IDXGISwapChain **chain)
{
    auto original = reinterpret_cast<FactoryCreateFn>(Original(factory, 10));
    if (!original) return E_UNEXPECTED;
    HRESULT result = original(factory, device, description, chain);
    if (SUCCEEDED(result) && chain) HookChain(*chain);
    if (logger) logger("HD menu factory CreateSwapChain hr=%08lx output=%ux%u", result,
        description ? description->BufferDesc.Width : 0, description ? description->BufferDesc.Height : 0);
    return result;
}

static HRESULT STDMETHODCALLTYPE FactoryHwnd(IDXGIFactory2 *factory, IUnknown *device, HWND window,
    const DXGI_SWAP_CHAIN_DESC1 *description, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC *fullscreen,
    IDXGIOutput *restrictOutput, IDXGISwapChain1 **chain)
{
    auto original = reinterpret_cast<FactoryHwndFn>(Original(factory, 15));
    if (!original) return E_UNEXPECTED;
    HRESULT result = original(factory, device, window, description, fullscreen, restrictOutput, chain);
    if (SUCCEEDED(result) && chain) HookChain(*chain);
    if (logger) logger("HD menu factory CreateSwapChainForHwnd hr=%08lx window=%p", result, window);
    return result;
}

static HRESULT STDMETHODCALLTYPE FactoryCore(IDXGIFactory2 *factory, IUnknown *device, IUnknown *window,
    const DXGI_SWAP_CHAIN_DESC1 *description, IDXGIOutput *restrictOutput, IDXGISwapChain1 **chain)
{
    auto original = reinterpret_cast<FactoryCoreFn>(Original(factory, 16));
    if (!original) return E_UNEXPECTED;
    HRESULT result = original(factory, device, window, description, restrictOutput, chain);
    if (SUCCEEDED(result) && chain) HookChain(*chain);
    return result;
}

static HRESULT STDMETHODCALLTYPE FactoryComposition(IDXGIFactory2 *factory, IUnknown *device,
    const DXGI_SWAP_CHAIN_DESC1 *description, IDXGIOutput *restrictOutput, IDXGISwapChain1 **chain)
{
    auto original = reinterpret_cast<FactoryCompositionFn>(Original(factory, 24));
    if (!original) return E_UNEXPECTED;
    HRESULT result = original(factory, device, description, restrictOutput, chain);
    if (SUCCEEDED(result) && chain) HookChain(*chain);
    return result;
}

static void HookFactory(IUnknown *object)
{
    if (!object || !enabled) return;
    IDXGIFactory *factory = nullptr;
    if (SUCCEEDED(object->QueryInterface(__uuidof(IDXGIFactory), reinterpret_cast<void **>(&factory)))) {
        PatchSlot(*reinterpret_cast<void ***>(factory) + 10, reinterpret_cast<void *>(FactoryCreate));
        factory->Release();
    }
    IDXGIFactory2 *factory2 = nullptr;
    if (SUCCEEDED(object->QueryInterface(__uuidof(IDXGIFactory2), reinterpret_cast<void **>(&factory2)))) {
        void **table = *reinterpret_cast<void ***>(factory2);
        PatchSlot(table + 15, reinterpret_cast<void *>(FactoryHwnd));
        PatchSlot(table + 16, reinterpret_cast<void *>(FactoryCore));
        PatchSlot(table + 24, reinterpret_cast<void *>(FactoryComposition));
        factory2->Release();
    }
}

static void ObserveDevice(ID3D11Device *device)
{
    if (!device) return;
    IDXGIDevice *dxgi = nullptr;
    IDXGIAdapter *adapter = nullptr;
    IDXGIFactory *factory = nullptr;
    if (SUCCEEDED(device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void **>(&dxgi))) &&
        SUCCEEDED(dxgi->GetAdapter(&adapter)) &&
        SUCCEEDED(adapter->GetParent(__uuidof(IDXGIFactory), reinterpret_cast<void **>(&factory)))) HookFactory(factory);
    if (logger) logger("HD menu captured D3D11 device=%p factory=%p feature=%x", device, factory, device->GetFeatureLevel());
    Release(factory); Release(adapter); Release(dxgi);
}

using D3DDeviceFn = decltype(&D3D11CreateDevice);
using D3DDeviceChainFn = decltype(&D3D11CreateDeviceAndSwapChain);
using CreateFactoryFn = HRESULT (WINAPI *)(REFIID, void **);
using CreateFactory2Fn = HRESULT (WINAPI *)(UINT, REFIID, void **);
static D3DDeviceFn d3dDevice;
static D3DDeviceChainFn d3dDeviceChain;
static CreateFactoryFn createFactory;
static CreateFactoryFn createFactory1;
static CreateFactory2Fn createFactory2;

static BOOL CALLBACK BootstrapFactoryHooks(PINIT_ONCE, PVOID, PVOID *)
{
    if (!enabled || !systemGetProc) return FALSE;
    if (!bootstrapDxgiModule) {
        wchar_t path[MAX_PATH];
        UINT length = GetSystemDirectoryW(path, _countof(path));
        if (!length || length + _countof(L"\\dxgi.dll") > _countof(path)) return FALSE;
        wcscat_s(path, L"\\dxgi.dll");
        bootstrapDxgiModule = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!bootstrapDxgiModule) {
            if (logger) logger("HD menu factory bootstrap DXGI load failed error=%lu", GetLastError());
            return FALSE;
        }
    }
    auto create = reinterpret_cast<CreateFactoryFn>(systemGetProc(bootstrapDxgiModule, "CreateDXGIFactory1"));
    if (!create) {
        if (logger) logger("HD menu factory bootstrap export unavailable error=%lu", GetLastError());
        return FALSE;
    }
    IDXGIFactory1 *factory = nullptr;
    HRESULT result = create(__uuidof(IDXGIFactory1), reinterpret_cast<void **>(&factory));
    bool ready = false;
    size_t installed = 0;
    if (SUCCEEDED(result) && factory) {
        HookFactory(factory);
        ready = (*reinterpret_cast<void ***>(factory))[10] == reinterpret_cast<void *>(FactoryCreate);
        AcquireSRWLockShared(&hookLock);
        installed = hookCount;
        ReleaseSRWLockShared(&hookLock);
    }
    if (logger) logger("HD menu factory bootstrap hr=%08lx factory=%p sharedHooks=%d hookCount=%zu",
        result, factory, ready, installed);
    Release(factory);
    return ready ? TRUE : FALSE;
}

// Called by the outer D3D8 proxy before forwarding its first runtime creation.
// This covers renderers that cached DXGI exports before the IAT hook existed.
static bool EnsureRuntimeHooks()
{
    if (!enabled) return false;
    return InitOnceExecuteOnce(&factoryBootstrapOnce, BootstrapFactoryHooks, nullptr, nullptr) != FALSE;
}

static HRESULT WINAPI D3DDevice(IDXGIAdapter *adapter, D3D_DRIVER_TYPE type, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL *levels, UINT count, UINT sdk, ID3D11Device **device,
    D3D_FEATURE_LEVEL *selected, ID3D11DeviceContext **context)
{
    HRESULT result = d3dDevice(adapter, type, software, flags, levels, count, sdk, device, selected, context);
    if (SUCCEEDED(result) && device) ObserveDevice(*device);
    return result;
}

static HRESULT WINAPI D3DDeviceChain(IDXGIAdapter *adapter, D3D_DRIVER_TYPE type, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL *levels, UINT count, UINT sdk, const DXGI_SWAP_CHAIN_DESC *description,
    IDXGISwapChain **chain, ID3D11Device **device, D3D_FEATURE_LEVEL *selected, ID3D11DeviceContext **context)
{
    HRESULT result = d3dDeviceChain(adapter, type, software, flags, levels, count, sdk,
        description, chain, device, selected, context);
    if (SUCCEEDED(result)) {
        if (device) ObserveDevice(*device);
        if (chain) HookChain(*chain);
    }
    return result;
}

static HRESULT WINAPI CreateFactory(REFIID iid, void **output)
{
    HRESULT result = createFactory(iid, output);
    if (SUCCEEDED(result) && output) HookFactory(static_cast<IUnknown *>(*output));
    return result;
}
static HRESULT WINAPI CreateFactory1(REFIID iid, void **output)
{
    HRESULT result = createFactory1(iid, output);
    if (SUCCEEDED(result) && output) HookFactory(static_cast<IUnknown *>(*output));
    return result;
}
static HRESULT WINAPI CreateFactory2(UINT flags, REFIID iid, void **output)
{
    HRESULT result = createFactory2(flags, iid, output);
    if (SUCCEEDED(result) && output) HookFactory(static_cast<IUnknown *>(*output));
    return result;
}

static FARPROC WINAPI GetProcedure(HMODULE module, LPCSTR name)
{
    FARPROC result = systemGetProc(module, name);
    if (!result || !enabled || reinterpret_cast<uintptr_t>(name) <= 0xffff) return result;
#define SEAL_HD_PROCEDURE(Export, Stored, Replacement) \
    if (!strcmp(name, Export)) { \
        Stored = reinterpret_cast<decltype(Stored)>(result); \
        if (logger) logger("HD menu acquired export %s module=%p address=%p", name, module, result); \
        return reinterpret_cast<FARPROC>(Replacement); \
    }
    SEAL_HD_PROCEDURE("D3D11CreateDevice", d3dDevice, D3DDevice)
    SEAL_HD_PROCEDURE("D3D11CreateDeviceAndSwapChain", d3dDeviceChain, D3DDeviceChain)
    SEAL_HD_PROCEDURE("CreateDXGIFactory", createFactory, CreateFactory)
    SEAL_HD_PROCEDURE("CreateDXGIFactory1", createFactory1, CreateFactory1)
    SEAL_HD_PROCEDURE("CreateDXGIFactory2", createFactory2, CreateFactory2)
#undef SEAL_HD_PROCEDURE
    return result;
}

static bool Install(HMODULE game, HMODULE gfx, HMODULE d3d8, LogFn log)
{
    if (enabled) return true;
    logger = log;
    executable = game;
    graphics = gfx;
    installThread = GetCurrentThreadId();
    systemGetProc = ::GetProcAddress;
    wchar_t directory[MAX_PATH] = {};
    if (!game || !gfx || !d3d8 || !GetModuleFileNameW(game, directory, _countof(directory))) return false;
    wchar_t *separator = wcsrchr(directory, L'\\');
    if (!separator) return false;
    separator[1] = 0;
    wchar_t settings[MAX_PATH];
    if (swprintf_s(settings, L"%sSealHD.ini", directory) < 0 ||
        swprintf_s(imagePath, L"%sSealHD-assets\\mainmenu.png", directory) < 0 ||
        swprintf_s(openingImagePath, L"%sSealHD-assets\\opening-title.png", directory) < 0) return false;
    if (!GetPrivateProfileIntW(L"Assets", L"MainMenu", 1, settings)) {
        if (logger) logger("HD menu disabled by [Assets] MainMenu=0");
        return false;
    }
    animateFire = GetPrivateProfileIntW(L"Assets", L"FireAnimation", 1, settings) != 0;
    DWORD attributes = GetFileAttributesW(imagePath);
    if (attributes == INVALID_FILE_ATTRIBUTES || attributes & FILE_ATTRIBUTE_DIRECTORY) {
        if (logger) logger("HD menu PNG absent; original menu retained path=%ls", imagePath);
        return false;
    }
    DWORD openingAttributes = GetFileAttributesW(openingImagePath);
    openingEnabled = openingAttributes != INVALID_FILE_ATTRIBUTES && !(openingAttributes & FILE_ATTRIBUTE_DIRECTORY);
    if (!openingEnabled && logger) logger("HD opening title PNG absent; original title retained path=%ls", openingImagePath);
    auto exported = systemGetProc(gfx, "?CreateXBmp@@YAHPAXAAPAVVPaper@@@Z");
    const BYTE expected[2] = { 0x6a, 0xff };
    if (exported != reinterpret_cast<FARPROC>(reinterpret_cast<BYTE *>(gfx) + 0x6d570) ||
        !Readable(exported, sizeof(expected)) || memcmp(exported, expected, sizeof(expected)) ||
        !LoadSceneImages(directory)) {
        if (logger) logger("HD menu version/resource guards failed; original menu retained");
        return false;
    }
    createXBmp = reinterpret_cast<CreateXBmpFn>(exported);
    InterlockedExchange(&enabled, 1);
    bool bitmapHook = PatchImport(game, "?CreateXBmp@@YAHPAXAAPAVVPaper@@@Z", reinterpret_cast<void *>(CreateXBmp));
    bool gpuHook = PatchImport(d3d8, "GetProcAddress", reinterpret_cast<void *>(GetProcedure));
    HMODULE ddraw = GetModuleHandleW(L"ddraw.dll");
    bool drawHook = ddraw && PatchImport(ddraw, "GetProcAddress", reinterpret_cast<void *>(GetProcedure));
    if (logger) logger("HD menu installed bitmapHook=%d d3d8ProcedureHook=%d ddrawProcedureHook=%d sceneRecords=%d fire=%d openingTitle=%d",
        bitmapHook, gpuHook, drawHook, openingEnabled ? 21 : 20, animateFire, openingEnabled);
    if (!bitmapHook || !(gpuHook || drawHook)) {
        InterlockedExchange(&enabled, 0);
        return false;
    }
    return true;
}

static void Shutdown(bool processTerminating = false)
{
    InterlockedExchange(&enabled, 0);
    InterlockedExchange(&compositorReady, 0);
    // Windows releases all COM/GPU resources on process termination. Calling
    // driver/COM shutdown while DllMain holds the loader lock can deadlock.
    if (processTerminating) return;
    AcquireSRWLockExclusive(&gpuLock);
    DestroyGpu(true);
    ReleaseSRWLockExclusive(&gpuLock);
    std::vector<IDXGISwapChain *> pending;
    AcquireSRWLockExclusive(&hookLock);
    pending.swap(pendingChains);
    ReleaseSRWLockExclusive(&hookLock);
    for (auto chain : pending) chain->Release();
    AcquireSRWLockExclusive(&paperLock);
    for (auto &paper : papers) if (paper.object) {
        if (Readable(paper.object, sizeof(void *)) && *reinterpret_cast<void ***>(paper.object) == paper.table)
            *reinterpret_cast<void ***>(paper.object) = paper.originalTable;
        if (paper.keyed) reinterpret_cast<DeletePaperFn>((*reinterpret_cast<void ***>(paper.keyed))[0])(paper.keyed, 1);
        paper.object = paper.keyed = nullptr;
    }
    ReleaseSRWLockExclusive(&paperLock);
    AcquireSRWLockExclusive(&hookLock);
    for (auto &chain : chainTables) if (chain->alive && Readable(chain->object, sizeof(void *)) &&
            *reinterpret_cast<void ***>(chain->object) == chain->Table()) {
        DWORD protection;
        if (VirtualProtect(chain->object, sizeof(void *), PAGE_READWRITE, &protection)) {
            InterlockedExchangePointer(reinterpret_cast<void **>(chain->object), chain->originalTable);
            DWORD ignored;
            VirtualProtect(chain->object, sizeof(void *), protection, &ignored);
        }
    }
    for (size_t i = hookCount; i; --i) {
        auto &hook = hooks[i - 1];
        DWORD protection;
        if (Readable(hook.slot, sizeof(void *)) && *hook.slot == hook.replacement &&
            VirtualProtect(hook.slot, sizeof(void *), PAGE_READWRITE, &protection)) {
            InterlockedExchangePointer(hook.slot, hook.original);
            DWORD ignored;
            VirtualProtect(hook.slot, sizeof(void *), protection, &ignored);
        }
    }
    hookCount = 0;
    ReleaseSRWLockExclusive(&hookLock);
    for (auto &image : images) { image.original.clear(); image.keyed.clear(); }
    if (compilerModule) { FreeLibrary(compilerModule); compilerModule = nullptr; }
    if (bootstrapDxgiModule) { FreeLibrary(bootstrapDxgiModule); bootstrapDxgiModule = nullptr; }
    if (logger) logger("HD menu resources and hooks released");
}

} // namespace seal_hd
