#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include "native_resolution.h"
#include "hd_intro.h"
#include "hd_assets.h"
#include "movie_guard.h"
#include "resolution_profiles.h"

// The game imports only DirectInput8Create. This proxy preserves that API and
// records the display initialization calls used by the original executable.
static HANDLE logFile = INVALID_HANDLE_VALUE;
static char gameDirectory[MAX_PATH];
static char patchConfigPath[MAX_PATH];
static bool forceWindowed;
static bool virtualFullscreen;
static bool nativeResolution;
static bool borderless;
static DWORD renderWidth;
static DWORD renderHeight;
static DWORD activeRenderEnum;
static HWND gameWindow;
static void *gameDevice;
static UINT logicalWidth;
static UINT logicalHeight;
static LONG inputTraceCount;
static LONG fullscreenCorrectionActive;
static LONG fullscreenCorrectionCount;
using seal_resolution::kResolutionProfiles;
using seal_resolution::kResolutionOrder;
static int currentProfile = -1;
static int pendingProfile = -1;
static char customResolutionLabel[32];
static INIT_ONCE inputOnce = INIT_ONCE_STATIC_INIT;
static HMODULE systemInput;
using InputCreate = HRESULT (WINAPI *)(HINSTANCE, DWORD, REFIID, LPVOID *, LPUNKNOWN);
static InputCreate realInputCreate;

static void Log(const char *format, ...)
{
    if (logFile == INVALID_HANDLE_VALUE) return;
    char message[2048];
    SYSTEMTIME now;
    GetLocalTime(&now);
    int offset = _snprintf_s(message, sizeof(message), _TRUNCATE,
        "%02u:%02u:%02u.%03u [%lu] ", now.wHour, now.wMinute, now.wSecond,
        now.wMilliseconds, GetCurrentThreadId());
    if (offset < 0) return;
    va_list arguments;
    va_start(arguments, format);
    _vsnprintf_s(message + offset, sizeof(message) - offset, _TRUNCATE, format, arguments);
    va_end(arguments);
    size_t length = strlen(message);
    if (length + 2 < sizeof(message)) {
        message[length++] = '\r';
        message[length++] = '\n';
    }
    DWORD written;
    WriteFile(logFile, message, static_cast<DWORD>(length), &written, nullptr);
    FlushFileBuffers(logFile);
}

using ChangeDisplay = LONG (WINAPI *)(DEVMODEA *, DWORD);
using EnumDisplay = BOOL (WINAPI *)(LPCSTR, DWORD, DEVMODEA *);
using DeviceCaps = int (WINAPI *)(HDC, int);
using D3DCreate = void *(WINAPI *)(UINT);
using GfxBuild = HRESULT (__cdecl *)(void *, HWND);
using GfxChangeDisplay = HRESULT (__cdecl *)(DWORD, int);
using VideoPlay = HRESULT (__cdecl *)(LPCSTR, void *, void *, RECT *, DWORD);
using RunCommand = int (__cdecl *)(LPCSTR);
using MapPoint = BOOL (WINAPI *)(HWND, LPPOINT);
using ChangeWindowStyle = LONG (WINAPI *)(HWND, int, LONG);
using PositionWindow = BOOL (WINAPI *)(HWND, HWND, int, int, int, int, UINT);
using ReadClientRect = BOOL (WINAPI *)(HWND, LPRECT);
using ReadAsyncKey = SHORT (WINAPI *)(int);
static ChangeDisplay realChangeDisplay;
static EnumDisplay realEnumDisplay;
static DeviceCaps realDeviceCaps;
static D3DCreate realD3DCreate;
static GfxBuild realGfxBuild;
static GfxChangeDisplay realGfxChangeDisplay;
static VideoPlay realVideoPlay;
static RunCommand realIntroCommand;
static MapPoint realScreenToClient;
static MapPoint realClientToScreen;
static ChangeWindowStyle realSetWindowLong;
static PositionWindow realSetWindowPos;
static ReadClientRect realGetClientRect;
static ReadAsyncKey realGetAsyncKeyState;
static HWND mouseLatchWindow;
static WNDPROC mouseLatchPrevious;
static volatile LONG mousePressEdges[2];
static LONG recoveredMousePresses;
static HHOOK modalFocusHook;
static LONG modalMinimizeBlocked;

// A native owned confirmation is still part of the game. Preserve the disabled
// rendering window while that dialog becomes active; unrelated deactivation
// and minimize requests must retain their normal behavior.
static bool IsOwnedGameDialog(HWND owner, HWND candidate)
{
    if (!owner || !candidate || candidate == owner || !IsWindow(candidate)) return false;
    DWORD candidateProcess = 0;
    DWORD candidateThread = GetWindowThreadProcessId(candidate, &candidateProcess);
    if (candidateProcess != GetCurrentProcessId() ||
        candidateThread != GetWindowThreadProcessId(owner, nullptr)) return false;
    char name[32] = {};
    if (!GetClassNameA(candidate, name, sizeof(name)) || strcmp(name, "#32770")) return false;
    HWND current = GetWindow(candidate, GW_OWNER);
    for (unsigned depth = 0; current && depth < 16; ++depth) {
        if (current == owner) return true;
        HWND next = GetWindow(current, GW_OWNER);
        if (next == current) break;
        current = next;
    }
    return false;
}

static void ClearMousePresses()
{
    InterlockedExchange(&mousePressEdges[0], 0);
    InterlockedExchange(&mousePressEdges[1], 0);
}

struct OwnedDialogSearch { HWND owner, found; };
static BOOL CALLBACK FindOwnedDialogWindow(HWND window, LPARAM parameter)
{
    auto search = reinterpret_cast<OwnedDialogSearch *>(parameter);
    if (!IsOwnedGameDialog(search->owner, window)) return TRUE;
    search->found = window;
    return FALSE;
}

static HWND FindOwnedGameDialog(HWND owner)
{
    OwnedDialogSearch search = { owner, nullptr };
    EnumThreadWindows(GetWindowThreadProcessId(owner, nullptr), FindOwnedDialogWindow,
        reinterpret_cast<LPARAM>(&search));
    return search.found;
}

static LRESULT CALLBACK ModalFocusCbtProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HCBT_MINMAX && reinterpret_cast<HWND>(wParam) == gameWindow && gameWindow) {
        int command = static_cast<int>(lParam);
        bool minimizing = command == SW_MINIMIZE || command == SW_SHOWMINIMIZED ||
            command == SW_SHOWMINNOACTIVE || command == SW_FORCEMINIMIZE;
        HWND foreground = GetForegroundWindow();
        HWND dialog = minimizing ? FindOwnedGameDialog(gameWindow) : nullptr;
#if defined(SEALHD_DIALOG_DIAGNOSTICS)
        Log("Dialog CBT minmax command=%d game=%p enabled=%d iconic=%d foreground=%p ownedDialog=%p",
            command, gameWindow, IsWindowEnabled(gameWindow), IsIconic(gameWindow), foreground, dialog);
        static LONG minimizeTraceCount;
        if (minimizing && InterlockedIncrement(&minimizeTraceCount) <= 12) {
            void *frames[16] = {};
            USHORT length = CaptureStackBackTrace(0, _countof(frames), frames, nullptr);
            for (USHORT i = 0; i < length; ++i) Log("Dialog CBT caller frame=%u address=%p", i, frames[i]);
        }
#endif
        if (borderless && virtualFullscreen && minimizing && !IsWindowEnabled(gameWindow) &&
            foreground == gameWindow && dialog) {
            ClearMousePresses();
            LONG count = InterlockedIncrement(&modalMinimizeBlocked);
            if (count <= 8) Log("Owned modal minimize prevented game=%p dialog=%p command=%d count=%ld",
                gameWindow, dialog, command, count);
            // WH_CBT permits a nonzero return to cancel only this minimize.
            // The native modal owner stays disabled and its decision is untouched.
            return 1;
        }
    }
    return CallNextHookEx(modalFocusHook, code, wParam, lParam);
}

static void InstallModalFocusHook(HWND window)
{
    if (modalFocusHook || !borderless || !virtualFullscreen || window != gameWindow ||
        GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId()) return;
    modalFocusHook = SetWindowsHookExA(WH_CBT, ModalFocusCbtProc, nullptr, GetCurrentThreadId());
    Log("Owned modal focus hook installed=%d thread=%lu error=%lu", modalFocusHook != nullptr,
        GetCurrentThreadId(), modalFocusHook ? ERROR_SUCCESS : GetLastError());
}

static LRESULT CALLBACK MouseLatchWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    WNDPROC previous = mouseLatchPrevious;
    if (window == mouseLatchWindow) {
#if defined(SEALHD_DIALOG_DIAGNOSTICS)
        static LONG activationTraceCount;
        if ((message == WM_ACTIVATE || message == WM_ACTIVATEAPP || message == WM_ENABLE ||
            message == WM_KILLFOCUS || message == WM_CANCELMODE || message == WM_SYSCOMMAND ||
            message == WM_SIZE && wParam == SIZE_MINIMIZED) && InterlockedIncrement(&activationTraceCount) <= 64)
            Log("Dialog activation message=%04x wParam=%p lParam=%p enabled=%d iconic=%d foreground=%p nextOwnedDialog=%d",
                message, reinterpret_cast<void *>(wParam), reinterpret_cast<void *>(lParam),
                IsWindowEnabled(window), IsIconic(window), GetForegroundWindow(),
                message == WM_ACTIVATE && IsOwnedGameDialog(window, reinterpret_cast<HWND>(lParam)));
#endif
        if (message == WM_LBUTTONDOWN || message == WM_LBUTTONDBLCLK ||
            message == WM_RBUTTONDOWN || message == WM_RBUTTONDBLCLK) {
            if (GetForegroundWindow() == window && IsWindowEnabled(window)) {
                unsigned button = message == WM_RBUTTONDOWN || message == WM_RBUTTONDBLCLK ? 1 : 0;
                // GetAsyncKeyState reports physical buttons, while window
                // messages follow the user's primary/secondary button mapping.
                if (GetSystemMetrics(SM_SWAPBUTTON)) button ^= 1;
                InterlockedExchange(&mousePressEdges[button], 1);
            }
        } else if (message == WM_KILLFOCUS || (message == WM_ACTIVATEAPP && !wParam)) {
            ClearMousePresses();
        } else if (message == WM_NCDESTROY) {
            ClearMousePresses();
            if (reinterpret_cast<WNDPROC>(GetWindowLongPtrA(window, GWLP_WNDPROC)) == MouseLatchWindowProc)
                SetWindowLongPtrA(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(previous));
            mouseLatchWindow = nullptr;
            mouseLatchPrevious = nullptr;
        }
    }
    return previous ? CallWindowProcA(previous, window, message, wParam, lParam) :
        DefWindowProcA(window, message, wParam, lParam);
}

static void RemoveMouseLatch()
{
    if (modalFocusHook) { UnhookWindowsHookEx(modalFocusHook); modalFocusHook = nullptr; }
    ClearMousePresses();
    HWND window = mouseLatchWindow;
    if (window && IsWindow(window) &&
        reinterpret_cast<WNDPROC>(GetWindowLongPtrA(window, GWLP_WNDPROC)) == MouseLatchWindowProc) {
        if (SetWindowLongPtrA(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(mouseLatchPrevious))) {
            mouseLatchWindow = nullptr;
            mouseLatchPrevious = nullptr;
        }
    }
}

static bool InstallMouseLatch(HWND window)
{
    if (!realGetAsyncKeyState || !window || window != gameWindow || !IsWindow(window)) return false;
    InstallModalFocusHook(window);
    if (window == mouseLatchWindow) return true;
    // Device creation and subclass installation run on the game's window
    // thread. Do not change another thread's window procedure from a helper.
    if (GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId()) return false;
    if (mouseLatchWindow) {
        RemoveMouseLatch();
        if (mouseLatchWindow) return false;
    }
    auto previous = reinterpret_cast<WNDPROC>(GetWindowLongPtrA(window, GWLP_WNDPROC));
    if (!previous || previous == MouseLatchWindowProc) return false;
    ClearMousePresses();
    mouseLatchWindow = window;
    mouseLatchPrevious = previous;
    SetLastError(ERROR_SUCCESS);
    LONG_PTR replaced = SetWindowLongPtrA(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(MouseLatchWindowProc));
    if (!replaced) {
        DWORD error = GetLastError();
        mouseLatchWindow = nullptr;
        mouseLatchPrevious = nullptr;
        Log("Mouse press latch installation failed hwnd=%p error=%lu", window, error);
        return false;
    }
    mouseLatchPrevious = reinterpret_cast<WNDPROC>(replaced);
    Log("Mouse press latch installed hwnd=%p previousProc=%p", window, mouseLatchPrevious);
    return true;
}

static SHORT WINAPI TraceGetAsyncKeyState(int key)
{
    SHORT state = realGetAsyncKeyState(key);
    DWORD error = GetLastError();
    if (key == VK_LBUTTON || key == VK_RBUTTON) {
        if (mouseLatchWindow == gameWindow && gameWindow &&
            GetForegroundWindow() == gameWindow && IsWindowEnabled(gameWindow)) {
            unsigned button = key == VK_RBUTTON ? 1 : 0;
            LONG edge = InterlockedExchange(&mousePressEdges[button], 0);
            // Consume the edge even when the real button is still held, so it
            // cannot become an extra synthetic press after the user releases.
            if (edge && !(static_cast<unsigned short>(state) & 0x8000)) {
                state = static_cast<SHORT>(static_cast<unsigned short>(state) | 0x8000);
                LONG count = InterlockedIncrement(&recoveredMousePresses);
                if (count <= 8) Log("Mouse short press recovered key=%d count=%ld", key, count);
            }
        } else ClearMousePresses();
    }
    SetLastError(error);
    return state;
}

static BOOL WINAPI TraceGetClientRect(HWND window, LPRECT rectangle)
{
    BOOL result = realGetClientRect(window, rectangle);
    if (result && rectangle && virtualFullscreen && window == gameWindow && logicalWidth && logicalHeight) {
        rectangle->left = 0;
        rectangle->top = 0;
        rectangle->right = logicalWidth;
        rectangle->bottom = logicalHeight;
    }
    return result;
}

static LONG BorderlessStyle(LONG style)
{
    style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MAXIMIZEBOX | WS_MINIMIZEBOX);
    return style | WS_POPUP;
}

static bool GetMonitorBounds(HWND window, RECT *bounds)
{
    MONITORINFO monitor = { sizeof(monitor) };
    HMONITOR output = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
    if (!GetMonitorInfoA(output, &monitor)) return false;
    *bounds = monitor.rcMonitor;
    return true;
}

static LONG WINAPI TraceSetWindowLong(HWND window, int index, LONG value)
{
    if (borderless && window == gameWindow && index == GWL_STYLE)
        value = BorderlessStyle(value);
    return realSetWindowLong(window, index, value);
}

static BOOL WINAPI TraceSetWindowPos(HWND window, HWND after, int x, int y, int width, int height, UINT flags)
{
    RECT monitor;
    if (borderless && virtualFullscreen && window == gameWindow && GetMonitorBounds(window, &monitor)) {
        x = monitor.left;
        y = monitor.top;
        width = monitor.right - monitor.left;
        height = monitor.bottom - monitor.top;
        flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
        flags |= SWP_FRAMECHANGED;
    }
    return realSetWindowPos(window, after, x, y, width, height, flags);
}

static void PrepareVirtualWindow(HWND window)
{
    if (!borderless || !virtualFullscreen || window != gameWindow) return;
    LONG style = GetWindowLongA(window, GWL_STYLE);
    SetWindowLongA(window, GWL_STYLE, BorderlessStyle(style));
    RECT monitor;
    if (GetMonitorBounds(window, &monitor)) {
        SetWindowPos(window, HWND_TOP, monitor.left, monitor.top,
            monitor.right - monitor.left, monitor.bottom - monitor.top,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }
}

static void EnsureFullscreen()
{
    if (!borderless || !virtualFullscreen || !gameWindow || IsIconic(gameWindow)) return;
    RECT monitor, outer, client;
    if (!GetMonitorBounds(gameWindow, &monitor) || !GetWindowRect(gameWindow, &outer)) return;
    BOOL haveClient = realGetClientRect ? realGetClientRect(gameWindow, &client) : GetClientRect(gameWindow, &client);
    if (!haveClient) return;
    LONG_PTR style = GetWindowLongPtrW(gameWindow, GWL_STYLE);
    LONG_PTR extendedStyle = GetWindowLongPtrW(gameWindow, GWL_EXSTYLE);
    LONG_PTR desiredStyle = (style & ~static_cast<LONG_PTR>(0x00cf0000)) | WS_POPUP;
    LONG_PTR desiredExtendedStyle = extendedStyle & ~static_cast<LONG_PTR>(0x301);
    LONG width = monitor.right - monitor.left;
    LONG height = monitor.bottom - monitor.top;
    bool needsCorrection = style != desiredStyle || extendedStyle != desiredExtendedStyle ||
        !EqualRect(&outer, &monitor) || client.right - client.left != width || client.bottom - client.top != height;
    if (!needsCorrection || InterlockedCompareExchange(&fullscreenCorrectionActive, 1, 0) != 0) return;
    SetWindowLongPtrW(gameWindow, GWL_STYLE, desiredStyle);
    SetWindowLongPtrW(gameWindow, GWL_EXSTYLE, desiredExtendedStyle);
    BOOL positioned = SetWindowPos(gameWindow, HWND_TOP, monitor.left, monitor.top, width, height,
        SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    LONG count = InterlockedIncrement(&fullscreenCorrectionCount);
    if (count <= 10)
        Log("Fullscreen correction=%ld positioned=%d oldStyle=%08lx oldExStyle=%08lx "
            "oldOuter=%ld,%ld,%ld,%ld oldClient=%ldx%ld target=%ld,%ld,%ld,%ld",
            count, positioned, static_cast<DWORD>(style), static_cast<DWORD>(extendedStyle),
            outer.left, outer.top, outer.right, outer.bottom,
            client.right - client.left, client.bottom - client.top,
            monitor.left, monitor.top, monitor.right, monitor.bottom);
    InterlockedExchange(&fullscreenCorrectionActive, 0);
}

struct InputViewport {
    LONG physicalWidth, physicalHeight;
    double scale, offsetX, offsetY;
};

static bool GetInputViewport(HWND window, InputViewport *viewport)
{
    if (!virtualFullscreen || window != gameWindow || !logicalWidth || !logicalHeight) return false;
    RECT client;
    // The saved system function returns the physical client size. The game-only
    // IAT hook below supplies logical dimensions to its dialog layout code.
    BOOL result = realGetClientRect ? realGetClientRect(window, &client) : GetClientRect(window, &client);
    if (!result) return false;
    viewport->physicalWidth = client.right - client.left;
    viewport->physicalHeight = client.bottom - client.top;
    if (viewport->physicalWidth <= 0 || viewport->physicalHeight <= 0) return false;
    double scaleX = static_cast<double>(viewport->physicalWidth) / logicalWidth;
    double scaleY = static_cast<double>(viewport->physicalHeight) / logicalHeight;
    viewport->scale = scaleX < scaleY ? scaleX : scaleY;
    viewport->offsetX = (viewport->physicalWidth - logicalWidth * viewport->scale) / 2;
    viewport->offsetY = (viewport->physicalHeight - logicalHeight * viewport->scale) / 2;
    return true;
}

static void LogInputMapping(const char *operation, POINT before, POINT after, const InputViewport &viewport)
{
    if (InterlockedIncrement(&inputTraceCount) <= 8)
        Log("%s input=%ld,%ld result=%ld,%ld logical=%ux%u physical=%ldx%ld scale=%.6f offset=%.3f,%.3f",
            operation, before.x, before.y, after.x, after.y, logicalWidth, logicalHeight,
            viewport.physicalWidth, viewport.physicalHeight, viewport.scale, viewport.offsetX, viewport.offsetY);
}

static BOOL WINAPI TraceScreenToClient(HWND window, LPPOINT point)
{
    BOOL result = realScreenToClient(window, point);
    DWORD error = GetLastError();
    InputViewport viewport;
    if (result && point && GetInputViewport(window, &viewport)) {
        POINT before = *point;
        point->x = static_cast<LONG>((point->x - viewport.offsetX) / viewport.scale);
        point->y = static_cast<LONG>((point->y - viewport.offsetY) / viewport.scale);
        LogInputMapping("ScreenToClient", before, *point, viewport);
    }
    SetLastError(error);
    return result;
}

static BOOL WINAPI TraceClientToScreen(HWND window, LPPOINT point)
{
    InputViewport viewport;
    if (point && GetInputViewport(window, &viewport)) {
        POINT before = *point;
        point->x = static_cast<LONG>(point->x * viewport.scale + viewport.offsetX);
        point->y = static_cast<LONG>(point->y * viewport.scale + viewport.offsetY);
        LogInputMapping("ClientToScreen", before, *point, viewport);
    }
    return realClientToScreen(window, point);
}

static LONG WINAPI TraceChangeDisplay(DEVMODEA *mode, DWORD flags)
{
    if (mode) Log("ChangeDisplaySettingsA request %lux%lu %lu bpp %lu Hz fields=%08lx flags=%08lx",
        mode->dmPelsWidth, mode->dmPelsHeight, mode->dmBitsPerPel,
        mode->dmDisplayFrequency, mode->dmFields, flags);
    else Log("ChangeDisplaySettingsA restore flags=%08lx", flags);
    LONG result = realChangeDisplay(mode, flags);
    Log("ChangeDisplaySettingsA result=%ld", result);
    return result;
}

static BOOL WINAPI TraceEnumDisplay(LPCSTR device, DWORD index, DEVMODEA *mode)
{
    BOOL result = realEnumDisplay(device, index, mode);
    if (index == ENUM_CURRENT_SETTINGS || index == ENUM_REGISTRY_SETTINGS || !result) {
        if (result && mode) Log("EnumDisplaySettingsA device=%s index=%lu -> %lux%lu %lu bpp %lu Hz",
            device ? device : "default", index, mode->dmPelsWidth, mode->dmPelsHeight,
            mode->dmBitsPerPel, mode->dmDisplayFrequency);
        else Log("EnumDisplaySettingsA device=%s index=%lu -> %d error=%lu",
            device ? device : "default", index, result, GetLastError());
    }
    return result;
}

static int WINAPI TraceDeviceCaps(HDC dc, int index)
{
    int result = realDeviceCaps(dc, index);
    if (index == BITSPIXEL || index == PLANES || index == HORZRES ||
        index == VERTRES || index == DESKTOPHORZRES || index == DESKTOPVERTRES)
        Log("GetDeviceCaps index=%d -> %d", index, result);
    return result;
}

static HRESULT TryHDIntro(const char *origin, bool *handled)
{
    *handled = false;
    wchar_t directory[MAX_PATH];
    DWORD length = GetModuleFileNameW(nullptr, directory, MAX_PATH);
    wchar_t *name = length && length < MAX_PATH ? wcsrchr(directory, L'\\') : nullptr;
    if (!name || static_cast<size_t>(name + 1 - directory) + _countof(L"SealHD-assets") > MAX_PATH)
        return S_FALSE;
    wcscpy_s(name + 1, MAX_PATH - static_cast<size_t>(name + 1 - directory), L"SealHD-assets");
    const wchar_t *failureReason = nullptr;
    Log("HD intro attempting origin=%s directory=%ls hwnd=%p", origin, directory, gameWindow);
    HRESULT result = SealHDIntro::TryPlayIntro(gameWindow, directory, handled, &failureReason, Log);
    Log("HD intro result origin=%s handled=%d result=%08lx reason=%ls", origin, *handled,
        result, failureReason ? failureReason : L"completed");
    return result;
}

static HRESULT __cdecl TraceVideoPlay(LPCSTR filename, void *directDraw, void *surface,
    RECT *rectangle, DWORD videoType)
{
    Log("Mfx_VIDEO_Play filename=%s videoType=%lu hwnd=%p", filename ? filename : "(null)", videoType, gameWindow);
    const char *basename = filename;
    if (filename) {
        const char *slash = strrchr(filename, '\\');
        const char *forwardSlash = strrchr(filename, '/');
        if (forwardSlash && (!slash || forwardSlash > slash)) slash = forwardSlash;
        if (slash) basename = slash + 1;
    }
    if (basename && (_stricmp(basename, "Logo1.dat") == 0 || _stricmp(basename, "Logo1") == 0)) {
        bool handled = false;
        HRESULT result = TryHDIntro(filename, &handled);
        if (handled) return result;
    }
    bool handled = false;
    HRESULT guarded = seal_movie_guard::TryHandle(filename, Log, &handled);
    if (handled) return guarded;
    return realVideoPlay(filename, directDraw, surface, rectangle, videoType);
}

static int __cdecl TraceIntroCommand(LPCSTR command)
{
    Log("HD intro startup command=%s", command ? command : "(null)");
    if (command && !strcmp(command, "Logo1")) {
        seal_hd::EndOpeningTitle();
        bool handled = false;
        TryHDIntro("CMD_Run(Logo1)", &handled);
        if (handled) return 1;
    }
    return realIntroCommand(command);
}

static bool InstallIntroCommand()
{
    BYTE *base = reinterpret_cast<BYTE *>(GetModuleHandleW(nullptr));
    const BYTE expected[] = { 0x68, 0x14, 0xe0, 0x6c, 0x00, 0xc7, 0x46, 0x14, 0x09, 0x00, 0x00, 0x00,
        0xe8, 0x6f, 0xe2, 0x03, 0x00, 0x83, 0xc4, 0x04 };
    const BYTE targetPrefix[] = { 0xa1, 0x5c, 0x02, 0x6e, 0x00, 0x81, 0xec, 0x78, 0x03, 0x00, 0x00 };
    if (!seal_hd::Readable(base + 0x74c0, sizeof(expected)) ||
        memcmp(base + 0x74c0, expected, sizeof(expected)) ||
        !seal_hd::Readable(base + 0x45740, sizeof(targetPrefix)) ||
        memcmp(base + 0x45740, targetPrefix, sizeof(targetPrefix)) ||
        !seal_hd::Readable(base + 0x2ce014, 6) || memcmp(base + 0x2ce014, "Logo1", 6)) {
        Log("HD intro command hook rejected unsupported startup instructions");
        return false;
    }
    BYTE *call = base + 0x74cc;
    DWORD protection;
    if (!VirtualProtect(call, 5, PAGE_EXECUTE_READWRITE, &protection)) return false;
    realIntroCommand = reinterpret_cast<RunCommand>(base + 0x45740);
    LONG relative = static_cast<LONG>(reinterpret_cast<BYTE *>(TraceIntroCommand) - (call + 5));
    memcpy(call + 1, &relative, sizeof(relative));
    BOOL installed = FlushInstructionCache(GetCurrentProcess(), call, 5);
    if (!installed) {
        memcpy(call, expected + 12, 5);
        FlushInstructionCache(GetCurrentProcess(), call, 5);
        realIntroCommand = nullptr;
    }
    DWORD ignored;
    VirtualProtect(call, 5, protection, &ignored);
    Log("HD intro command hook installed=%d RVA=000074CC", installed);
    return installed != FALSE;
}

struct PresentParameters {
    UINT width, height;
    DWORD format;
    UINT backBufferCount;
    DWORD multiSampleType, swapEffect;
    HWND window;
    BOOL windowed, autoDepthStencil;
    DWORD depthStencilFormat, flags;
    UINT refreshRate, presentationInterval;
};
static_assert(sizeof(PresentParameters) == 52, "D3D8 x86 presentation parameters");
using CreateDevice = HRESULT (WINAPI *)(void *, UINT, DWORD, HWND, DWORD, PresentParameters *, void **);
using ResetDevice = HRESULT (WINAPI *)(void *, PresentParameters *);
using PresentDevice = HRESULT (WINAPI *)(void *, const RECT *, const RECT *, HWND, const RGNDATA *);

struct VtableRecord {
    void **table;
    CreateDevice create;
    ResetDevice reset;
    PresentDevice present;
    LONG presentCount;
};
static VtableRecord records[32];
static SRWLOCK recordLock = SRWLOCK_INIT;

static VtableRecord *FindRecord(void *object)
{
    if (!object) return nullptr;
    void **table = *reinterpret_cast<void ***>(object);
    for (auto &record : records) if (record.table == table) return &record;
    return nullptr;
}

static bool WritePointer(void **slot, void *replacement)
{
    DWORD protection;
    if (!VirtualProtect(slot, sizeof(void *), PAGE_READWRITE, &protection)) return false;
    InterlockedExchangePointer(slot, replacement);
    DWORD ignored;
    VirtualProtect(slot, sizeof(void *), protection, &ignored);
    return true;
}

static bool PatchWindowedPolicy()
{
    BYTE *base = reinterpret_cast<BYTE *>(GetModuleHandleW(nullptr));
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER *>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS *>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->OptionalHeader.ImageBase != 0x400000 ||
        reinterpret_cast<ULONG_PTR>(base) != 0x400000 ||
        nt->OptionalHeader.SizeOfImage <= 0xe776) {
        Log("Windowed policy patch rejected unsupported executable layout");
        return false;
    }
    const BYTE expectedGetter[] = { 0xa1, 0xfc, 0x65, 0x70, 0x00, 0xc3 };
    const BYTE patchedGetter[] = { 0xb8, 0x01, 0x00, 0x00, 0x00, 0xc3 };
    const BYTE expectedCheck[] = { 0xe8, 0x4b, 0xfe, 0xff, 0xff };
    const BYTE patchedCheck[] = { 0xb8, 0x10, 0x00, 0x00, 0x00 };
    const BYTE expectedDisplayChange[] = { 0x0f, 0x84, 0x1d, 0x03, 0x00, 0x00 };
    const BYTE patchedDisplayChange[] = { 0xe9, 0x1e, 0x03, 0x00, 0x00, 0x90 };
    struct PolicyPatch {
        DWORD rva;
        const BYTE *expected;
        const BYTE *replacement;
        size_t length;
        DWORD protection;
    };
    PolicyPatch patches[] = {
        { 0xe770, expectedGetter, patchedGetter, sizeof(expectedGetter), 0 },
        { 0x8850, expectedCheck, patchedCheck, sizeof(expectedCheck), 0 },
        { 0x3d80, expectedDisplayChange, patchedDisplayChange, sizeof(expectedDisplayChange), 0 },
    };
    const unsigned count = virtualFullscreen ? 3 : 2;
    for (unsigned i = 0; i < count; ++i) {
        const auto &patch = patches[i];
        if (memcmp(base + patch.rva, patch.expected, patch.length) != 0) {
            Log("Windowed policy patch rejected byte mismatch at RVA=%08lx", patch.rva);
            return false;
        }
    }
    unsigned writable = 0;
    for (; writable < count; ++writable) {
        auto &patch = patches[writable];
        if (!VirtualProtect(base + patch.rva, patch.length, PAGE_EXECUTE_READWRITE, &patch.protection)) break;
    }
    if (writable != count) {
        DWORD error = GetLastError();
        while (writable) {
            const auto &patch = patches[--writable];
            DWORD ignored;
            VirtualProtect(base + patch.rva, patch.length, patch.protection, &ignored);
        }
        Log("Windowed policy protection failed error=%lu", error);
        return false;
    }
    for (unsigned i = 0; i < count; ++i) {
        const auto &patch = patches[i];
        memcpy(base + patch.rva, patch.replacement, patch.length);
    }
    BOOL committed = FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
    if (!committed) {
        for (unsigned i = 0; i < count; ++i) {
            const auto &patch = patches[i];
            memcpy(base + patch.rva, patch.expected, patch.length);
        }
        FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
    }
    for (unsigned i = count; i; --i) {
        const auto &patch = patches[i - 1];
        DWORD ignored;
        if (!VirtualProtect(base + patch.rva, patch.length, patch.protection, &ignored))
            Log("Windowed policy protection restore failed RVA=%08lx error=%lu", patch.rva, GetLastError());
    }
    if (!committed) {
        Log("Windowed policy instruction-cache update failed; original instructions restored");
        return false;
    }
    Log("Applied windowed getter and legacy depth policy patches displayChange=%d", virtualFullscreen);
    return true;
}

static void LogParameters(const char *operation, const PresentParameters *parameters)
{
    if (!parameters) { Log("%s parameters=null", operation); return; }
    Log("%s width=%u height=%u format=%lu count=%u windowed=%d hwnd=%p multisample=%lu "
        "swap=%lu depth=%lu flags=%08lx refresh=%u interval=%08x", operation,
        parameters->width, parameters->height, parameters->format, parameters->backBufferCount,
        parameters->windowed, parameters->window, parameters->multiSampleType,
        parameters->swapEffect, parameters->depthStencilFormat, parameters->flags,
        parameters->refreshRate, parameters->presentationInterval);
}

static HRESULT WINAPI TraceReset(void *object, PresentParameters *parameters)
{
    VtableRecord *record = FindRecord(object);
    if (object == gameDevice && parameters) {
        if (nativeResolution && (activeRenderEnum == 4 || activeRenderEnum == 5)) {
            parameters->width = renderWidth;
            parameters->height = renderHeight;
        }
        PrepareVirtualWindow(gameWindow);
    }
    LogParameters("IDirect3DDevice8::Reset", parameters);
    HRESULT result = record->reset(object, parameters);
    Log("IDirect3DDevice8::Reset -> %08lx", result);
    if (SUCCEEDED(result) && object == gameDevice && parameters && parameters->width && parameters->height) {
        logicalWidth = parameters->width;
        logicalHeight = parameters->height;
        Log("Tracked game backbuffer after Reset=%ux%u hwnd=%p", logicalWidth, logicalHeight, gameWindow);
        EnsureFullscreen();
    }
    return result;
}

static HRESULT __cdecl TraceGfxChangeDisplay(DWORD resolutionEnum, int windowed)
{
    Log("Gfx3d_ChangeDisplayMode requested enum=%lu windowed=%d", resolutionEnum, windowed);
    if (virtualFullscreen) windowed = FALSE;
    else if (forceWindowed) windowed = TRUE;
    activeRenderEnum = resolutionEnum;
    Log("Gfx3d_ChangeDisplayMode applied enum=%lu windowed=%d", resolutionEnum, windowed);
    HRESULT result = realGfxChangeDisplay(resolutionEnum, windowed);
    Log("Gfx3d_ChangeDisplayMode -> %08lx", result);
    return result;
}

static HRESULT WINAPI TracePresent(void *object, const RECT *source, const RECT *destination,
    HWND overrideWindow, const RGNDATA *dirtyRegion)
{
    VtableRecord *record = FindRecord(object);
    HRESULT result = record->present(object, source, destination, overrideWindow, dirtyRegion);
    if (object == gameDevice) EnsureFullscreen();
    if (object == gameDevice && SUCCEEDED(result)) seal_hd::ArmPendingChains();
    LONG count = InterlockedIncrement(&record->presentCount);
    if (count == 1 || count == 60 || count == 600)
        Log("IDirect3DDevice8::Present count=%ld -> %08lx", count, result);
    return result;
}

// GPU resources alone do not prove that this patch can composite a frame.
// A compatibility shim may replace CreateDevice before our device hook runs.
static bool HasLivePresentationPath()
{
    if (!gameDevice || !seal_hd::Readable(gameDevice, sizeof(void *))) return false;
    void **table = *reinterpret_cast<void ***>(gameDevice);
    return table && seal_hd::Readable(table, 16 * sizeof(void *)) &&
        table[15] == reinterpret_cast<void *>(TracePresent);
}

static HRESULT WINAPI TraceCreateDevice(void *object, UINT adapter, DWORD deviceType,
    HWND window, DWORD behavior, PresentParameters *parameters, void **device)
{
    VtableRecord *record = FindRecord(object);
    Log("IDirect3D8::CreateDevice adapter=%u type=%lu behavior=%08lx hwnd=%p",
        adapter, deviceType, behavior, window);
    HWND deviceWindow = parameters && parameters->window ? parameters->window : window;
    if (!gameWindow) gameWindow = deviceWindow;
    PrepareVirtualWindow(deviceWindow);
    if (nativeResolution && parameters &&
        (parameters->format == 21 || parameters->format == 22 ||
         parameters->format == 23 || parameters->format == 24 || parameters->format == 25)) {
        bool nativeScene = activeRenderEnum == 4 || activeRenderEnum == 5;
        if (nativeScene) {
            parameters->width = renderWidth;
            parameters->height = renderHeight;
        }
        if (forceWindowed && !virtualFullscreen) parameters->windowed = TRUE;
        HWND targetWindow = parameters->window ? parameters->window : window;
        if (borderless && !virtualFullscreen && targetWindow && parameters->windowed) {
            LONG_PTR style = GetWindowLongPtrA(targetWindow, GWL_STYLE);
            style &= (WS_CLIPCHILDREN | WS_CLIPSIBLINGS);
            style |= (WS_POPUP | WS_VISIBLE);
            SetWindowLongPtrA(targetWindow, GWL_STYLE, style);
            LONG_PTR extendedStyle = GetWindowLongPtrA(targetWindow, GWL_EXSTYLE);
            extendedStyle &= ~(WS_EX_CLIENTEDGE | WS_EX_WINDOWEDGE | WS_EX_DLGMODALFRAME);
            SetWindowLongPtrA(targetWindow, GWL_EXSTYLE, extendedStyle);
            MONITORINFO monitor = { sizeof(monitor) };
            POINT origin = {};
            HMONITOR primary = MonitorFromPoint(origin, MONITOR_DEFAULTTOPRIMARY);
            int x = 0;
            int y = 0;
            if (GetMonitorInfoA(primary, &monitor)) {
                x = monitor.rcMonitor.left +
                    (monitor.rcMonitor.right - monitor.rcMonitor.left - static_cast<int>(renderWidth)) / 2;
                y = monitor.rcMonitor.top +
                    (monitor.rcMonitor.bottom - monitor.rcMonitor.top - static_cast<int>(renderHeight)) / 2;
            }
            BOOL positioned = SetWindowPos(targetWindow, HWND_TOP, x, y,
                static_cast<int>(renderWidth), static_cast<int>(renderHeight),
                SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            RECT client = {};
            GetClientRect(targetWindow, &client);
            Log("Native borderless target=%lux%lu position=%d,%d SetWindowPos=%d client=%ldx%ld",
                renderWidth, renderHeight, x, y, positioned, client.right, client.bottom);
        }
        Log("Renderer enum=%lu nativeScene=%d backbuffer request=%ux%u",
            activeRenderEnum, nativeScene, parameters->width, parameters->height);
    }
    LogParameters("IDirect3D8::CreateDevice", parameters);
    HRESULT result = record->create(object, adapter, deviceType, window, behavior, parameters, device);
    Log("IDirect3D8::CreateDevice -> %08lx device=%p", result, device ? *device : nullptr);
    if (SUCCEEDED(result) && device && *device) {
        if (deviceWindow == gameWindow && parameters && parameters->width && parameters->height) {
            gameDevice = *device;
            logicalWidth = parameters->width;
            logicalHeight = parameters->height;
            Log("Tracked game backbuffer after CreateDevice=%ux%u hwnd=%p", logicalWidth, logicalHeight, gameWindow);
            InstallMouseLatch(gameWindow);
        }
        void **table = *reinterpret_cast<void ***>(*device);
        AcquireSRWLockExclusive(&recordLock);
        if (!FindRecord(*device)) {
            for (auto &entry : records) if (!entry.table) {
                entry.reset = reinterpret_cast<ResetDevice>(table[14]);
                entry.present = reinterpret_cast<PresentDevice>(table[15]);
                entry.table = table;
                WritePointer(table + 14, reinterpret_cast<void *>(TraceReset));
                WritePointer(table + 15, reinterpret_cast<void *>(TracePresent));
                break;
            }
        }
        ReleaseSRWLockExclusive(&recordLock);
    }
    return result;
}

static void *WINAPI TraceD3DCreate(UINT version)
{
    seal_hd::EnsureRuntimeHooks();
    void *object = realD3DCreate(version);
    Log("Direct3DCreate8 version=%u -> %p", version, object);
    if (!object) return nullptr;
    void **table = *reinterpret_cast<void ***>(object);
    AcquireSRWLockExclusive(&recordLock);
    if (!FindRecord(object)) {
        for (auto &entry : records) if (!entry.table) {
            entry.create = reinterpret_cast<CreateDevice>(table[15]);
            entry.table = table;
            WritePointer(table + 15, reinterpret_cast<void *>(TraceCreateDevice));
            break;
        }
    }
    ReleaseSRWLockExclusive(&recordLock);
    return object;
}

static HRESULT __cdecl TraceGfxBuild(void *configuration, HWND window)
{
    Log("Gfx3d_Build config=%p hwnd=%p", configuration, window);
    if (window) gameWindow = window;
    if (configuration) {
        DWORD *words = static_cast<DWORD *>(configuration);
        if (virtualFullscreen) {
            words[0x218 / 4] = FALSE;
            Log("VirtualFullscreen enabled original engine fullscreen renderer");
        } else if (forceWindowed) {
            words[0x218 / 4] = 1;
            Log("ForceWindowed applied");
        }
        activeRenderEnum = words[0];
        Log("Gfx3d configuration enum=%lu windowed=%lu preferred16BitFormat=%lu",
            activeRenderEnum, words[0x218 / 4], words[0x25c / 4]);
    }
    HRESULT result = realGfxBuild(configuration, window);
    Log("Gfx3d_Build -> %08lx", result);
    return result;
}

template<typename Function>
static void PatchImport(HMODULE module, const char *name, Function replacement, Function &original)
{
    if (!module) return;
    BYTE *base = reinterpret_cast<BYTE *>(module);
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER *>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return;
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS *>(base + dos->e_lfanew);
    DWORD rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    if (!rva) return;
    auto descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR *>(base + rva);
    for (; descriptor->Name; ++descriptor) {
        if (!descriptor->OriginalFirstThunk) continue;
        auto names = reinterpret_cast<IMAGE_THUNK_DATA *>(base + descriptor->OriginalFirstThunk);
        auto addresses = reinterpret_cast<IMAGE_THUNK_DATA *>(base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++addresses) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto imported = reinterpret_cast<IMAGE_IMPORT_BY_NAME *>(base + names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<const char *>(imported->Name), name) != 0) continue;
            auto slot = reinterpret_cast<void **>(&addresses->u1.Function);
            if (*slot == reinterpret_cast<void *>(replacement)) continue;
            if (!original) original = reinterpret_cast<Function>(*slot);
            if (WritePointer(slot, reinterpret_cast<void *>(replacement)))
                Log("Hook module=%p import=%s", module, name);
        }
    }
}

static int ResolveResolutionProfile(int storedProfile, DWORD *width, DWORD *height)
{
    // Profile IDs are stable across releases: the original IDs 0..4 keep their
    // dimensions. A valid ID is authoritative; custom configurations opt out
    // with ProfileIndex=-1 and retain their explicit Width and Height.
    if (storedProfile >= 0 && storedProfile < static_cast<int>(_countof(kResolutionProfiles))) {
        *width = kResolutionProfiles[storedProfile].width;
        *height = kResolutionProfiles[storedProfile].height;
        return storedProfile;
    }
    for (int id = 0; id < static_cast<int>(_countof(kResolutionProfiles)); ++id)
        if (kResolutionProfiles[id].width == *width && kResolutionProfiles[id].height == *height)
            return id;
    return -1;
}

static int NextResolutionProfile(int profile, int direction)
{
    const int count = static_cast<int>(_countof(kResolutionOrder));
    // Selection order is separate from persistent IDs so inserting a common
    // lower mode never turns an existing 1080p/1440p/4K setting into another mode.
    for (int index = 0; index < count; ++index)
        if (kResolutionOrder[index] == profile)
            return kResolutionOrder[(index + count + (direction < 0 ? -1 : 1)) % count];
    return kResolutionOrder[direction < 0 ? count - 1 : 0];
}

static const char *ResolutionLabel(int profile)
{
    if (profile >= 0 && profile < static_cast<int>(_countof(kResolutionProfiles)))
        return kResolutionProfiles[profile].label;
    _snprintf_s(customResolutionLabel, sizeof(customResolutionLabel), _TRUNCATE,
        "%lu*%lu", renderWidth, renderHeight);
    return customResolutionLabel;
}

static void SetResolutionLabel(void *widget, int profile)
{
    if (!widget) return;
    using SetText = void (__thiscall *)(void *, const char *);
    void **table = *reinterpret_cast<void ***>(widget);
    auto setText = reinterpret_cast<SetText>(table[5]);
    setText(widget, ResolutionLabel(profile));
}

static void __fastcall ShowResolutionLabel(void *widget, void *dialog)
{
    pendingProfile = currentProfile;
    // Every profile uses the original 1024 slot. The independent profile index
    // controls its actual dimensions and is committed only by the OK handler.
    *reinterpret_cast<DWORD *>(static_cast<BYTE *>(dialog) + 0x344) = 1;
    SetResolutionLabel(widget, pendingProfile);
    Log("Options opened active profile=%d dimensions=%lux%lu", currentProfile, renderWidth, renderHeight);
}

__declspec(naked) static void ShowResolutionLabelThunk()
{
    __asm {
        mov edx, esi
        jmp ShowResolutionLabel
    }
}

static void __fastcall DefaultResolutionLabel(void *widget, void *dialog)
{
    pendingProfile = 0;
    *reinterpret_cast<DWORD *>(static_cast<BYTE *>(dialog) + 0x344) = 1;
    SetResolutionLabel(widget, pendingProfile);
    Log("Options default resolution pending profile=0");
}

__declspec(naked) static void DefaultResolutionLabelThunk()
{
    __asm {
        mov edx, esi
        jmp DefaultResolutionLabel
    }
}

static void StepResolution(void *dialog, int direction)
{
    pendingProfile = NextResolutionProfile(pendingProfile, direction);
    BYTE *object = static_cast<BYTE *>(dialog);
    *reinterpret_cast<DWORD *>(object + 0x344) = 1;
    void *widget = *reinterpret_cast<void **>(object + 0x2d0);
    SetResolutionLabel(widget, pendingProfile);
    using RefreshWidget = void (__thiscall *)(void *, int, int);
    auto refresh = reinterpret_cast<RefreshWidget>(reinterpret_cast<BYTE *>(GetModuleHandleW(nullptr)) + 0x216cc0);
    refresh(widget, 0, 1);
    Log("Options resolution selected pending profile=%d dimensions=%s direction=%d",
        pendingProfile, ResolutionLabel(pendingProfile), direction);
}

static void __fastcall CycleResolution(void *dialog, void *)
{
    StepResolution(dialog, 1);
}

static void __fastcall PreviousResolution(void *dialog, void *)
{
    StepResolution(dialog, -1);
}

static bool PersistResolutionProfile(int profile)
{
    char value[32];
    _snprintf_s(value, sizeof(value), _TRUNCATE, "%d", profile);
    // ProfileIndex is the authoritative persisted selection. Width and Height
    // also mirror the active dimensions for a readable configuration file.
    BOOL profileSaved = WritePrivateProfileStringA("Display", "ProfileIndex", value, patchConfigPath);
    _snprintf_s(value, sizeof(value), _TRUNCATE, "%lu", renderWidth);
    BOOL widthSaved = WritePrivateProfileStringA("Display", "Width", value, patchConfigPath);
    _snprintf_s(value, sizeof(value), _TRUNCATE, "%lu", renderHeight);
    BOOL heightSaved = WritePrivateProfileStringA("Display", "Height", value, patchConfigPath);
    Log("Options persistence profile=%d selectionSaved=%d dimensionsSaved=%d",
        profile, profileSaved, widthSaved && heightSaved);
    return profileSaved != FALSE;
}

static int __cdecl ApplyResolutionProfile(int type, int mode, int force)
{
    using RequestDisplayMode = int (__cdecl *)(int, int, int);
    HMODULE game = GetModuleHandleW(nullptr);
    HMODULE graphics = GetModuleHandleW(L"Gfx3d.dll");
    auto requestDisplayMode = reinterpret_cast<RequestDisplayMode>(reinterpret_cast<BYTE *>(game) + 0x19a1d0);
    if (type != 1 || !nativeResolution) return requestDisplayMode(type, mode, force);
    DWORD oldWidth = renderWidth;
    DWORD oldHeight = renderHeight;
    DWORD newWidth = oldWidth;
    DWORD newHeight = oldHeight;
    if (pendingProfile >= 0 && pendingProfile < static_cast<int>(_countof(kResolutionProfiles))) {
        newWidth = kResolutionProfiles[pendingProfile].width;
        newHeight = kResolutionProfiles[pendingProfile].height;
    }
    bool changed = newWidth != oldWidth || newHeight != oldHeight;
    DWORD failedRva = 0;
    if (changed && !seal_native_resolution::ChangeNativeResolution(game, graphics,
            oldWidth, oldHeight, newWidth, newHeight, &failedRva)) {
        Log("Options native resolution change rejected failedRva=%08lx error=%lu", failedRva, GetLastError());
        pendingProfile = currentProfile;
        MessageBoxW(gameWindow, L"无法应用所选分辨率，已保留之前的设置。", L"Seal of Evil HD", MB_OK | MB_ICONERROR);
        return FALSE;
    }
    renderWidth = newWidth;
    renderHeight = newHeight;
    Log("Options applying profile=%d dimensions=%lux%lu requestedMode=%d forcedRebuild=1",
        pendingProfile, renderWidth, renderHeight, mode);
    // Main-menu mode 5 keeps its original 800x600 logical UI. An active world
    // requests mode 6 and rebuilds its viewport and native backbuffer here.
    int result = requestDisplayMode(type, mode, TRUE);
    if (!result) {
        bool restored = !changed || seal_native_resolution::ChangeNativeResolution(game, graphics,
            newWidth, newHeight, oldWidth, oldHeight, &failedRva);
        if (restored) {
            renderWidth = oldWidth;
            renderHeight = oldHeight;
            restored = requestDisplayMode(type, mode, TRUE) != FALSE;
        }
        pendingProfile = currentProfile;
        Log("Options resolution rebuild failed rollback=%d failedRva=%08lx", restored, failedRva);
        MessageBoxW(gameWindow,
            restored ? L"分辨率切换失败，已恢复之前的设置。" : L"分辨率切换失败，请重新启动游戏。",
            L"Seal of Evil HD", MB_OK | MB_ICONERROR);
        return FALSE;
    }
    currentProfile = pendingProfile;
    if (!PersistResolutionProfile(currentProfile))
        MessageBoxW(gameWindow, L"分辨率已应用，但无法保存设置。", L"Seal of Evil HD", MB_OK | MB_ICONWARNING);
    Log("Options resolution applied profile=%d dimensions=%lux%lu", currentProfile, renderWidth, renderHeight);
    return result;
}

static bool InstallResolutionOptions()
{
    if (!nativeResolution) return false;
    BYTE *base = reinterpret_cast<BYTE *>(GetModuleHandleW(nullptr));
    // Both arrow events originally share one two-choice toggle. Preserve the
    // right event's forward hook and give the independent left event its own
    // no-argument member callback (ECX is the options dialog).
    const DWORD expectedArrows[2][6] = {
        { 0x101, 0x200, 0x1bd, 0x1bd, 1, 0x004e32a0 },
        { 0x101, 0x200, 0x1be, 0x1be, 1, 0x004e32a0 },
    };
    if (!seal_hd::Readable(base + 0x289190, sizeof(expectedArrows)) ||
        memcmp(base + 0x289190, expectedArrows, sizeof(expectedArrows))) {
        Log("Options profile hooks rejected resolution arrow event records");
        return false;
    }
    struct UiPatch {
        DWORD rva;
        BYTE expected[6];
        unsigned length;
        void *target;
        BYTE opcode;
        DWORD protection;
    };
    UiPatch patches[] = {
        { 0xe2a75, { 0x8b, 0x11, 0x50, 0xff, 0x52, 0x14 }, 6,
            reinterpret_cast<void *>(ShowResolutionLabelThunk), 0xe8, 0 },
        { 0xe32a0, { 0x56, 0x8b, 0xf1, 0x33, 0xc0, 0x00 }, 5,
            reinterpret_cast<void *>(CycleResolution), 0xe9, 0 },
        { 0xe2e2c, { 0xe8, 0x9f, 0x73, 0x0b, 0x00, 0x00 }, 5,
            reinterpret_cast<void *>(ApplyResolutionProfile), 0xe8, 0 },
        { 0xe3073, { 0x8b, 0x01, 0x52, 0xff, 0x50, 0x14 }, 6,
            reinterpret_cast<void *>(DefaultResolutionLabelThunk), 0xe8, 0 },
        { 0x2891a4, { 0xa0, 0x32, 0x4e, 0x00, 0x00, 0x00 }, sizeof(void *),
            reinterpret_cast<void *>(PreviousResolution), 0, 0 },
    };
    for (const auto &patch : patches) {
        if (memcmp(base + patch.rva, patch.expected, patch.length) != 0) {
            Log("Options profile hooks rejected original bytes at RVA=%08lx", patch.rva);
            return false;
        }
    }
    unsigned protectedCount = 0;
    for (; protectedCount < _countof(patches); ++protectedCount) {
        auto &patch = patches[protectedCount];
        DWORD writable = patch.opcode ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE;
        if (!VirtualProtect(base + patch.rva, patch.length, writable, &patch.protection)) {
            while (protectedCount > 0) {
                auto &previous = patches[--protectedCount];
                DWORD ignored;
                VirtualProtect(base + previous.rva, previous.length, previous.protection, &ignored);
            }
            Log("Options profile hooks rejected memory protection");
            return false;
        }
    }
    for (const auto &patch : patches) {
        BYTE *address = base + patch.rva;
        if (!patch.opcode) {
            InterlockedExchangePointer(reinterpret_cast<void **>(address), patch.target);
            continue;
        }
        DWORD relative = static_cast<DWORD>(reinterpret_cast<ULONG_PTR>(patch.target) -
            reinterpret_cast<ULONG_PTR>(address + 5));
        address[0] = patch.opcode;
        memcpy(address + 1, &relative, sizeof(relative));
        if (patch.length == 6) address[5] = 0x90;
    }
    BOOL flushed = FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
    if (!flushed) {
        for (const auto &patch : patches) {
            if (patch.opcode) memcpy(base + patch.rva, patch.expected, patch.length);
            else {
                void *original;
                memcpy(&original, patch.expected, sizeof(original));
                InterlockedExchangePointer(reinterpret_cast<void **>(base + patch.rva), original);
            }
        }
        FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
    }
    for (unsigned i = static_cast<unsigned>(_countof(patches)); i > 0; --i) {
        auto &patch = patches[i - 1];
        DWORD ignored;
        VirtualProtect(base + patch.rva, patch.length, patch.protection, &ignored);
    }
    Log("Options five-resolution profile hooks installed=%d", flushed);
    return flushed != FALSE;
}

static void InstallHooks()
{
    HMODULE modules[] = { GetModuleHandleW(nullptr), GetModuleHandleW(L"Gfx3d.dll"),
        GetModuleHandleW(L"Mfx.dll"), GetModuleHandleW(L"OmUtil.dll") };
    for (HMODULE module : modules) {
        PatchImport(module, "ChangeDisplaySettingsA", TraceChangeDisplay, realChangeDisplay);
        PatchImport(module, "EnumDisplaySettingsA", TraceEnumDisplay, realEnumDisplay);
        PatchImport(module, "GetDeviceCaps", TraceDeviceCaps, realDeviceCaps);
        PatchImport(module, "Direct3DCreate8", TraceD3DCreate, realD3DCreate);
        PatchImport(module, "?Gfx3d_Build@@YAJPAU__GFX3D_CONFIG@@PAUHWND__@@@Z", TraceGfxBuild, realGfxBuild);
        PatchImport(module, "?Gfx3d_ChangeDisplayMode@@YAJW4ENUM_GFX3D_RESOLUTION@@H@Z",
            TraceGfxChangeDisplay, realGfxChangeDisplay);
        PatchImport(module, "SetWindowLongA", TraceSetWindowLong, realSetWindowLong);
        PatchImport(module, "SetWindowPos", TraceSetWindowPos, realSetWindowPos);
    }
    HMODULE game = GetModuleHandleW(nullptr);
    PatchImport(game, "ScreenToClient", TraceScreenToClient, realScreenToClient);
    PatchImport(game, "ClientToScreen", TraceClientToScreen, realClientToScreen);
    PatchImport(game, "GetClientRect", TraceGetClientRect, realGetClientRect);
    PatchImport(game, "GetAsyncKeyState", TraceGetAsyncKeyState, realGetAsyncKeyState);
    HMODULE videoModules[] = { game, GetModuleHandleW(L"ERRelive.dll"), GetModuleHandleW(L"Gfx3d.dll"),
        GetModuleHandleW(L"Mfx.dll"), GetModuleHandleW(L"OmUtil.dll") };
    for (HMODULE module : videoModules)
        PatchImport(module, "?Mfx_VIDEO_Play@@YAJPBDPAUIDirectDraw@@PAUIDirectDrawSurface@@PAUtagRECT@@W4ENUM_MFX_VIDEOTYPE@@@Z",
            TraceVideoPlay, realVideoPlay);
}

static BOOL CALLBACK InitializeInput(PINIT_ONCE, PVOID, PVOID *)
{
    wchar_t path[MAX_PATH];
    UINT length = GetSystemDirectoryW(path, MAX_PATH);
    if (!length || length + 13 >= MAX_PATH) return FALSE;
    wcscat_s(path, L"\\dinput8.dll");
    systemInput = LoadLibraryW(path);
    if (!systemInput) { Log("System dinput8 load failed error=%lu", GetLastError()); return FALSE; }
    realInputCreate = reinterpret_cast<InputCreate>(GetProcAddress(systemInput, "DirectInput8Create"));
    return realInputCreate != nullptr;
}

extern "C" HRESULT WINAPI DirectInput8Create(HINSTANCE instance, DWORD version, REFIID iid,
    LPVOID *output, LPUNKNOWN outer)
{
    if (!InitOnceExecuteOnce(&inputOnce, InitializeInput, nullptr, nullptr)) return E_FAIL;
    HRESULT result = realInputCreate(instance, version, iid, output, outer);
    Log("DirectInput8Create version=%08lx -> %08lx", version, result);
    return result;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_ATTACH) {
        GetModuleFileNameA(nullptr, gameDirectory, MAX_PATH);
        char *name = strrchr(gameDirectory, '\\');
        if (name) name[1] = '\0';
        char path[MAX_PATH];
        strcpy_s(path, gameDirectory);
        strcat_s(path, "SealHD.log");
        logFile = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        Log("SealHD proxy 0.25-local x86 started (portable startup and presentation fallback; not published)");
        strcpy_s(path, gameDirectory);
        strcat_s(path, "SealHD.ini");
        strcpy_s(patchConfigPath, path);
        forceWindowed = GetPrivateProfileIntA("Compatibility", "ForceWindowed", 1, path) != 0;
        virtualFullscreen = GetPrivateProfileIntA("Compatibility", "VirtualFullscreen", 1, path) != 0;
        Log("Settings ForceWindowed=%d VirtualFullscreen=%d", forceWindowed, virtualFullscreen);
        renderWidth = GetPrivateProfileIntA("Display", "Width", 1920, path);
        renderHeight = GetPrivateProfileIntA("Display", "Height", 1080, path);
        int storedProfile = static_cast<int>(GetPrivateProfileIntA("Display", "ProfileIndex", -1, path));
        currentProfile = ResolveResolutionProfile(storedProfile, &renderWidth, &renderHeight);
        pendingProfile = currentProfile;
        Log("Resolution profile selection stored=%d active=%d dimensions=%lux%lu catalogCount=%u",
            storedProfile, currentProfile, renderWidth, renderHeight, seal_resolution::kResolutionCount);
        borderless = GetPrivateProfileIntA("Display", "Borderless", 1, path) != 0;
        bool validDimensions = renderWidth >= 640 && renderWidth <= 7680 &&
            renderHeight >= 480 && renderHeight <= 4320 &&
            static_cast<ULONGLONG>(renderWidth) * renderHeight <= 33177600;
        if (validDimensions) {
            DWORD failedRva = 0;
            HMODULE game = GetModuleHandleW(nullptr);
            HMODULE graphics = GetModuleHandleW(L"Gfx3d.dll");
            nativeResolution = seal_native_resolution::ApplyNativeResolutionPatches(
                game, graphics, renderWidth, renderHeight, &failedRva);
            Log("Native resolution patch=%d width=%lu height=%lu borderless=%d failedRva=%08lx error=%lu",
                nativeResolution, renderWidth, renderHeight, borderless, failedRva, GetLastError());
        } else {
            Log("Native resolution disabled invalid dimensions %lux%lu", renderWidth, renderHeight);
        }
        HMODULE user32 = GetModuleHandleW(L"user32.dll");
        using SetAwareness = BOOL (WINAPI *)(HANDLE);
        auto setAwareness = reinterpret_cast<SetAwareness>(GetProcAddress(user32, "SetProcessDpiAwarenessContext"));
        BOOL aware = FALSE;
        if (setAwareness) aware = setAwareness(reinterpret_cast<HANDLE>(-2));
        else {
            using SetAware = BOOL (WINAPI *)();
            auto setAware = reinterpret_cast<SetAware>(GetProcAddress(user32, "SetProcessDPIAware"));
            if (setAware) aware = setAware();
        }
        Log("System DPI awareness result=%d lastError=%lu", aware, GetLastError());
        if (forceWindowed && !PatchWindowedPolicy()) {
            forceWindowed = false;
            Log("Windowed policy remains original because patch validation failed");
        }
        InstallHooks();
        InstallIntroCommand();
        InstallResolutionOptions();
        seal_hd::SetPresentationPathProbe(HasLivePresentationPath);
        bool hdMenu = seal_hd::Install(GetModuleHandleW(nullptr), GetModuleHandleW(L"Gfx3d.dll"),
            GetModuleHandleW(L"d3d8.dll"), Log);
        Log("HD menu module install=%d", hdMenu);
    } else if (reason == DLL_PROCESS_DETACH) {
        if (!reserved) RemoveMouseLatch();
        seal_hd::Shutdown(reserved != nullptr);
        if (logFile != INVALID_HANDLE_VALUE) {
            Log("SealHD process detach");
            CloseHandle(logFile);
            logFile = INVALID_HANDLE_VALUE;
        }
    }
    return TRUE;
}
