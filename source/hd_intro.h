#pragma once

#include <windows.h>
#include <objidl.h>
#include <gdiplus.h>
#include <array>
#include <memory>
#include <string>

#pragma comment(lib, "gdiplus.lib")

namespace SealHDIntro {

using LogFn = void (__cdecl *)(const char *, ...);

class GdiPlusSession {
public:
    GdiPlusSession() {
        Gdiplus::GdiplusStartupInput input;
        ready = Gdiplus::GdiplusStartup(&token, &input, nullptr) == Gdiplus::Ok;
    }

    ~GdiPlusSession() {
        if (ready) {
            Gdiplus::GdiplusShutdown(token);
        }
    }

    bool IsReady() const { return ready; }

private:
    ULONG_PTR token = 0;
    bool ready = false;
};

class NativePixelScope {
public:
    NativePixelScope() {
        HMODULE user32 = GetModuleHandleW(L"user32.dll");
        setContext = reinterpret_cast<SetContextFunction>(
            GetProcAddress(user32, "SetThreadDpiAwarenessContext"));
        if (setContext) {
            previousContext = setContext(reinterpret_cast<HANDLE>(static_cast<INT_PTR>(-4)));
        }
    }

    ~NativePixelScope() {
        if (setContext && previousContext) {
            setContext(previousContext);
        }
    }

private:
    using SetContextFunction = HANDLE(WINAPI*)(HANDLE);
    SetContextFunction setContext = nullptr;
    HANDLE previousContext = nullptr;
};

struct IntroState {
    std::array<std::unique_ptr<Gdiplus::Image>, 3> images;
    std::array<bool, 3> paintLogged = {};
    ULONGLONG startTime = 0;
    unsigned int currentCard = 0;
    bool finished = false;
    bool shown = false;
    HWND owner = nullptr;
    RECT monitor = {};
    LogFn log = nullptr;
};

static void FinishIntro(HWND window, IntroState* state) {
    state->finished = true;
    KillTimer(window, 1);
    DestroyWindow(window);
}

static void LogWindows(const char *event, HWND window, const IntroState& state) {
    if (!state.log) return;
    RECT owner = {}, popup = {}, client = {};
    GetWindowRect(state.owner, &owner);
    GetWindowRect(window, &popup);
    GetClientRect(window, &client);
    state.log("HD intro %s owner=%p ownerRect=%ld,%ld,%ld,%ld iconic=%d enabled=%d popup=%p popupRect=%ld,%ld,%ld,%ld client=%ldx%ld visible=%d foreground=%p active=%p",
        event, state.owner, owner.left, owner.top, owner.right, owner.bottom,
        IsIconic(state.owner), IsWindowEnabled(state.owner), window,
        popup.left, popup.top, popup.right, popup.bottom,
        client.right - client.left, client.bottom - client.top, IsWindowVisible(window),
        GetForegroundWindow(), GetActiveWindow());
}

static bool OwnerIsForeground(const IntroState& state) {
    HWND foreground = GetForegroundWindow();
    return state.owner && IsWindow(state.owner) && !IsIconic(state.owner) &&
        (foreground == state.owner || IsChild(state.owner, foreground));
}

static void UpdateVisibility(HWND window, IntroState& state) {
    const bool show = OwnerIsForeground(state);
    if (!show) {
        if (state.shown) {
            ShowWindow(window, SW_HIDE);
            state.shown = false;
            LogWindows("hidden while owner inactive", window, state);
        }
        return;
    }
    RECT actual = {};
    const bool placed = GetWindowRect(window, &actual) && EqualRect(&actual, &state.monitor);
    if (!state.shown || !IsWindowVisible(window) || !placed) {
        SetWindowPos(window, HWND_TOPMOST, state.monitor.left, state.monitor.top,
            state.monitor.right - state.monitor.left, state.monitor.bottom - state.monitor.top,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        state.shown = true;
        InvalidateRect(window, nullptr, FALSE);
        LogWindows("shown without activation", window, state);
    }
}

static void PaintIntro(HWND window, HDC target, IntroState& state) {
    RECT client = {};
    GetClientRect(window, &client);
    const int width = client.right - client.left;
    const int height = client.bottom - client.top;
    if (width <= 0 || height <= 0) {
        return;
    }

    HDC buffer = CreateCompatibleDC(target);
    HBITMAP bitmap = buffer ? CreateCompatibleBitmap(target, width, height) : nullptr;
    HGDIOBJ previousBitmap = bitmap ? SelectObject(buffer, bitmap) : nullptr;
    HDC drawingTarget = bitmap ? buffer : target;
    Gdiplus::Status graphicsStatus = Gdiplus::GenericError, drawStatus = Gdiplus::GenericError;
    {
        Gdiplus::Graphics graphics(drawingTarget);
        graphicsStatus = graphics.GetLastStatus();
        graphics.Clear(Gdiplus::Color(255, 0, 0, 0));
        graphics.SetPageUnit(Gdiplus::UnitPixel);
        graphics.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
        graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);

        Gdiplus::Image* image = state.images[state.currentCard].get();
        const float imageWidth = static_cast<float>(image->GetWidth());
        const float imageHeight = static_cast<float>(image->GetHeight());
        const float horizontalScale = width / imageWidth;
        const float verticalScale = height / imageHeight;
        const float scale = horizontalScale < verticalScale ? horizontalScale : verticalScale;
        const float outputWidth = imageWidth * scale;
        const float outputHeight = imageHeight * scale;
        const Gdiplus::RectF destination(
            (width - outputWidth) / 2.0f,
            (height - outputHeight) / 2.0f,
            outputWidth, outputHeight);
        drawStatus = graphics.DrawImage(image, destination, 0.0f, 0.0f,
            imageWidth, imageHeight, Gdiplus::UnitPixel);
    }
    BOOL blitted = TRUE;
    if (bitmap) {
        blitted = BitBlt(target, 0, 0, width, height, buffer, 0, 0, SRCCOPY);
        SelectObject(buffer, previousBitmap);
        DeleteObject(bitmap);
    }
    if (buffer) {
        DeleteDC(buffer);
    }
    if (!state.paintLogged[state.currentCard]) {
        state.paintLogged[state.currentCard] = true;
        if (state.log) state.log("HD intro first Paint card=%u image=%ux%u output=%dx%d graphics=%d DrawImage=%d buffered=%d BitBlt=%d elapsed=%llu",
            state.currentCard, state.images[state.currentCard]->GetWidth(), state.images[state.currentCard]->GetHeight(),
            width, height, graphicsStatus, drawStatus, bitmap != nullptr, blitted,
            GetTickCount64() - state.startTime);
        LogWindows("first Paint geometry", window, state);
    }
}

static LRESULT CALLBACK IntroWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    IntroState* state = reinterpret_cast<IntroState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const CREATESTRUCTW* creation = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        state = static_cast<IntroState*>(creation->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
    }
    if (!state) {
        return DefWindowProcW(window, message, wParam, lParam);
    }

    switch (message) {
    case WM_MOUSEACTIVATE:
        return MA_NOACTIVATE;
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT: {
        PAINTSTRUCT paint = {};
        HDC target = BeginPaint(window, &paint);
        PaintIntro(window, target, *state);
        EndPaint(window, &paint);
        return 0;
    }
    case WM_TIMER: {
        if (wParam != 1) {
            break;
        }
        const ULONGLONG elapsed = GetTickCount64() - state->startTime;
        if (elapsed >= 19167) {
            FinishIntro(window, state);
            return 0;
        }
        UpdateVisibility(window, *state);
        const unsigned int nextCard = elapsed < 4200 ? 0 : (elapsed < 11200 ? 1 : 2);
        if (nextCard != state->currentCard) {
            state->currentCard = nextCard;
            InvalidateRect(window, nullptr, FALSE);
        }
        return 0;
    }
    case WM_KEYDOWN:
    case WM_SYSKEYDOWN:
        if (wParam == VK_ESCAPE || wParam == VK_SPACE) {
            FinishIntro(window, state);
            return 0;
        }
        break;
    case WM_LBUTTONDOWN:
    case WM_RBUTTONDOWN:
    case WM_MBUTTONDOWN:
    case WM_CLOSE:
        FinishIntro(window, state);
        return 0;
    case WM_DESTROY:
        KillTimer(window, 1);
        state->finished = true;
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

// Call only from the intercepted Logo1.dat video call, after DLL initialization.
// If handled remains false, the caller should invoke the original video function.
static HRESULT TryPlayIntro(HWND owner, const wchar_t* assetDirectory, bool* handled,
    const wchar_t** failureReason = nullptr, LogFn log = nullptr) {
    if (failureReason) *failureReason = nullptr;
    if (!handled) {
        if (failureReason) *failureReason = L"handled output parameter missing";
        return E_POINTER;
    }
    *handled = false;
    if (!assetDirectory || !assetDirectory[0]) {
        if (failureReason) *failureReason = L"asset directory missing";
        return S_FALSE;
    }

    GdiPlusSession session;
    if (!session.IsReady()) {
        if (failureReason) *failureReason = L"GDI+ initialization failed";
        return S_FALSE;
    }
    IntroState state;
    state.log = log;
    std::wstring directory(assetDirectory);
    if (directory.back() != L'\\' && directory.back() != L'/') {
        directory += L'\\';
    }
    const wchar_t* filenames[] = {
        L"intro-zoom.png", L"intro-object.png", L"intro-strategy.png"
    };
    for (size_t index = 0; index < state.images.size(); ++index) {
        const std::wstring path = directory + filenames[index];
        state.images[index].reset(Gdiplus::Image::FromFile(path.c_str(), FALSE));
        if (!state.images[index] || state.images[index]->GetLastStatus() != Gdiplus::Ok ||
            state.images[index]->GetWidth() == 0 || state.images[index]->GetHeight() == 0) {
            if (failureReason) *failureReason = filenames[index];
            return S_FALSE;
        }
    }

    NativePixelScope nativePixels;
    HWND validOwner = IsWindow(owner) ? owner : nullptr;
    if (!validOwner) {
        if (failureReason) *failureReason = L"game owner window unavailable";
        return S_FALSE;
    }
    state.owner = validOwner;
    MONITORINFO monitor = {};
    monitor.cbSize = sizeof(monitor);
    if (!GetMonitorInfoW(MonitorFromWindow(validOwner, MONITOR_DEFAULTTOPRIMARY), &monitor)) {
        if (failureReason) *failureReason = L"monitor lookup failed";
        return S_FALSE;
    }
    state.monitor = monitor.rcMonitor;
    HINSTANCE module = GetModuleHandleW(nullptr);
    const wchar_t* className = L"SealHD.IntroCards.1";
    WNDCLASSEXW windowClass = {};
    windowClass.cbSize = sizeof(windowClass);
    windowClass.hInstance = module;
    windowClass.lpfnWndProc = IntroWindowProc;
    windowClass.lpszClassName = className;
    windowClass.hCursor = LoadCursorW(nullptr, MAKEINTRESOURCEW(32512));
    const bool registeredClass = RegisterClassExW(&windowClass) != 0;
    if (!registeredClass && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        if (failureReason) *failureReason = L"window class registration failed";
        return S_FALSE;
    }
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST, className, L"Seal of Evil",
        WS_POPUP, monitor.rcMonitor.left, monitor.rcMonitor.top,
        monitor.rcMonitor.right - monitor.rcMonitor.left,
        monitor.rcMonitor.bottom - monitor.rcMonitor.top,
        validOwner, nullptr, module, &state);
    if (!window || !SetTimer(window, 1, 33, nullptr)) {
        if (failureReason) *failureReason = L"intro window or timer creation failed";
        if (window) {
            DestroyWindow(window);
        }
        if (registeredClass) {
            UnregisterClassW(className, module);
        }
        return S_FALSE;
    }

    state.startTime = GetTickCount64();
    LogWindows("created noactivate popup", window, state);
    if (OwnerIsForeground(state)) {
        ShowWindow(window, SW_SHOWNOACTIVATE);
        state.shown = true;
        UpdateVisibility(window, state);
        UpdateWindow(window);
    }
    *handled = true;

    bool receivedQuit = false;
    int quitCode = 0;
    while (!state.finished) {
        MSG message = {};
        const BOOL result = GetMessageW(&message, nullptr, 0, 0);
        if (result <= 0) {
            receivedQuit = result == 0;
            quitCode = static_cast<int>(message.wParam);
            break;
        }
        if ((message.hwnd == validOwner || IsChild(validOwner, message.hwnd)) &&
            (message.message == WM_KEYDOWN || message.message == WM_SYSKEYDOWN) &&
            (message.wParam == VK_ESCAPE || message.wParam == VK_SPACE)) {
            if (state.log) state.log("HD intro skipped through owner key=%lu", static_cast<DWORD>(message.wParam));
            FinishIntro(window, &state);
            continue;
        }
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    if (IsWindow(window)) {
        FinishIntro(window, &state);
    }
    if (registeredClass) {
        UnregisterClassW(className, module);
    }
    if (receivedQuit) {
        PostQuitMessage(quitCode);
    }
    return S_OK;
}

} // namespace SealHDIntro
