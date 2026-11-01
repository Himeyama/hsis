#define _CRT_RAND_S
#include <windows.h>
#include <dwmapi.h>
#include <windowsx.h>
#include <shellapi.h>
#include <filesystem>
#include <string>
#include <vector>
#include <stdexcept>
#include <cstdlib>
#include <cstdint>
#include <cstring>

namespace fs = std::filesystem;
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE input = nullptr) : value(input) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct Footer {
    char magic[8];
    int64_t nativeLength, runtimeOffset, runtimeLength, payloadOffset, payloadLength, mode, depth;
};
static_assert(sizeof(Footer) == 64);
static HWND windowHandle = nullptr, statusHandle = nullptr, headingHandle = nullptr, spinnerHandle = nullptr, closeHandle = nullptr;
static bool cancelled = false;
static HANDLE cancelEvent = nullptr;
static HFONT fontHandle = nullptr, headingFont = nullptr, spinnerFont = nullptr;
static bool closeHover = false;
static WNDPROC originalCloseProcedure = nullptr;
static size_t spinnerFrame = 0;
static const wchar_t* spinnerFrames[] = { L"⠋", L"⠙", L"⠹", L"⠸", L"⠼", L"⠴", L"⠦", L"⠧", L"⠇", L"⠏" };

static bool IsNotoFont(HFONT font) {
    HDC dc = GetDC(windowHandle);
    HGDIOBJ previous = SelectObject(dc, font);
    wchar_t face[128]{};
    GetTextFaceW(dc, 128, face);
    SelectObject(dc, previous); ReleaseDC(windowHandle, dc);
    return std::wstring(face).rfind(L"Noto Sans JP", 0) == 0;
}

static void LoadFonts() {
    for (int identifier : { 101, 102 }) {
        HRSRC resource = FindResourceW(nullptr, MAKEINTRESOURCEW(identifier), RT_RCDATA);
        HGLOBAL memory = resource ? LoadResource(nullptr, resource) : nullptr;
        DWORD count = 0;
        if (!memory || !AddFontMemResourceEx(LockResource(memory), SizeofResource(nullptr, resource), nullptr, &count) || count == 0)
            throw std::runtime_error("font");
    }
}

static LRESULT CALLBACK CloseProcedure(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_MOUSEMOVE && !closeHover) {
        closeHover = true;
        TRACKMOUSEEVENT tracking{ sizeof(tracking), TME_LEAVE, window, 0 };
        TrackMouseEvent(&tracking); InvalidateRect(window, nullptr, FALSE);
    }
    if (message == WM_MOUSELEAVE) { closeHover = false; InvalidateRect(window, nullptr, FALSE); }
    return CallWindowProcW(originalCloseProcedure, window, message, w, l);
}

static void Layout(UINT dpi) {
    if (!statusHandle) return;
    auto scale = [dpi](int pixels) { return MulDiv(pixels, dpi, 96); };
    RECT client{}; GetClientRect(windowHandle, &client);
    int width = client.right - scale(56);
    MoveWindow(spinnerHandle, scale(28), scale(56), scale(34), scale(46), TRUE);
    MoveWindow(headingHandle, scale(70), scale(56), width - scale(42), scale(46), TRUE);
    MoveWindow(statusHandle, scale(28), scale(118), width, scale(82), TRUE);
    MoveWindow(closeHandle, client.right - scale(46), 0, scale(46), scale(32), TRUE);
    LOGFONTW font{}; wcscpy_s(font.lfFaceName, L"Noto Sans JP");
    font.lfHeight = -scale(20);
    HFONT body = CreateFontIndirectW(&font);
    font.lfHeight = -scale(26); font.lfWeight = FW_SEMIBOLD;
    HFONT heading = CreateFontIndirectW(&font);
    wcscpy_s(font.lfFaceName, L"Segoe UI Symbol"); font.lfWeight = FW_NORMAL;
    HFONT spinner = CreateFontIndirectW(&font);
    SendMessageW(statusHandle, WM_SETFONT, reinterpret_cast<WPARAM>(body), TRUE);
    SendMessageW(headingHandle, WM_SETFONT, reinterpret_cast<WPARAM>(heading), TRUE);
    SendMessageW(spinnerHandle, WM_SETFONT, reinterpret_cast<WPARAM>(spinner), TRUE);
    if (fontHandle) DeleteObject(fontHandle);
    if (headingFont) DeleteObject(headingFont);
    if (spinnerFont) DeleteObject(spinnerFont);
    fontHandle = body; headingFont = heading; spinnerFont = spinner;
    // Expose the verified in-process GDI result; font handles cannot be selected in another process.
    if (IsNotoFont(body) && IsNotoFont(heading)) SetPropW(windowHandle, L"Hsis.NotoSansJP", reinterpret_cast<HANDLE>(1));
    else RemovePropW(windowHandle, L"Hsis.NotoSansJP");
}

static void Pump() {
    MSG message;
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message); DispatchMessageW(&message);
    }
}
static bool IsCancelled() { return cancelled || (cancelEvent && WaitForSingleObject(cancelEvent, 0) == WAIT_OBJECT_0); }
static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_NCCALCSIZE && w) return 0;
    if (message == WM_NCHITTEST) {
        POINT point{ GET_X_LPARAM(l), GET_Y_LPARAM(l) }; ScreenToClient(window, &point);
        RECT client{}; GetClientRect(window, &client);
        UINT dpi = GetDpiForWindow(window);
        if (point.y >= 0 && point.y < MulDiv(32, dpi, 96) && point.x < client.right - MulDiv(46, dpi, 96)) return HTCAPTION;
        return HTCLIENT;
    }
    if (message == WM_CTLCOLORSTATIC) {
        HDC dc = reinterpret_cast<HDC>(w); SetTextColor(dc, RGB(0, 0, 0)); SetBkColor(dc, RGB(255, 255, 255));
        return reinterpret_cast<LRESULT>(GetStockObject(WHITE_BRUSH));
    }
    if (message == WM_TIMER && w == 1 && spinnerHandle) {
        spinnerFrame = (spinnerFrame + 1) % (sizeof(spinnerFrames) / sizeof(spinnerFrames[0]));
        SetWindowTextW(spinnerHandle, spinnerFrames[spinnerFrame]);
        return 0;
    }
    if (message == WM_DRAWITEM && w == 100) {
        DRAWITEMSTRUCT* item = reinterpret_cast<DRAWITEMSTRUCT*>(l);
        bool pressed = (item->itemState & ODS_SELECTED) != 0;
        HBRUSH brush = CreateSolidBrush(closeHover ? (pressed ? RGB(165, 38, 12) : RGB(196, 43, 28)) : RGB(255, 255, 255));
        FillRect(item->hDC, &item->rcItem, brush); DeleteObject(brush);
        UINT dpi = GetDpiForWindow(window);
        int radius = MulDiv(5, dpi, 96), centerX = (item->rcItem.right + item->rcItem.left) / 2, centerY = (item->rcItem.bottom + item->rcItem.top) / 2;
        HPEN pen = CreatePen(PS_SOLID, MulDiv(1, dpi, 96), closeHover ? RGB(255, 255, 255) : RGB(0, 0, 0));
        HGDIOBJ previous = SelectObject(item->hDC, pen);
        MoveToEx(item->hDC, centerX - radius, centerY - radius, nullptr); LineTo(item->hDC, centerX + radius, centerY + radius);
        MoveToEx(item->hDC, centerX + radius, centerY - radius, nullptr); LineTo(item->hDC, centerX - radius, centerY + radius);
        SelectObject(item->hDC, previous); DeleteObject(pen);
        if (item->itemState & ODS_FOCUS) { RECT focus = item->rcItem; InflateRect(&focus, -3, -3); DrawFocusRect(item->hDC, &focus); }
        return TRUE;
    }
    if (message == WM_DPICHANGED) {
        RECT* suggested = reinterpret_cast<RECT*>(l);
        SetWindowPos(window, nullptr, suggested->left, suggested->top, suggested->right - suggested->left,
            suggested->bottom - suggested->top, SWP_NOZORDER | SWP_NOACTIVATE);
        Layout(HIWORD(w));
        return 0;
    }
    if (message == WM_CLOSE || (message == WM_COMMAND && LOWORD(w) == 100)) {
        cancelled = true;
        if (cancelEvent) SetEvent(cancelEvent);
        return 0;
    }
    return DefWindowProcW(window, message, w, l);
}
static void ShowPreparing(bool quiet, bool uninstall) {
    if (quiet) return;
    LoadFonts();
    WNDCLASSW type{};
    type.hInstance = GetModuleHandleW(nullptr); type.lpfnWndProc = WindowProcedure;
    type.lpszClassName = L"Hsis.Preparing"; type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    type.hIcon = LoadIconW(type.hInstance, MAKEINTRESOURCEW(1));
    type.hbrBackground = reinterpret_cast<HBRUSH>(GetStockObject(WHITE_BRUSH));
    RegisterClassW(&type);
    UINT dpi = GetDpiForSystem();
    int width = MulDiv(650, dpi, 96), height = MulDiv(240, dpi, 96);
    windowHandle = CreateWindowExW(WS_EX_DLGMODALFRAME, type.lpszClassName,
        uninstall ? L"アンインストールを準備しています" : L"セットアップを準備しています",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU, (GetSystemMetrics(SM_CXSCREEN) - width) / 2,
        (GetSystemMetrics(SM_CYSCREEN) - height) / 2, width, height, nullptr, nullptr, type.hInstance, nullptr);
    if (!windowHandle) throw std::runtime_error("window");
    DWM_WINDOW_CORNER_PREFERENCE corners = DWMWCP_ROUND;
    DwmSetWindowAttribute(windowHandle, DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(corners));
    SetWindowPos(windowHandle, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
    closeHandle = CreateWindowW(L"BUTTON", L"閉じる", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
        0, 0, 0, 0, windowHandle, reinterpret_cast<HMENU>(100), type.hInstance, nullptr);
    originalCloseProcedure = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(closeHandle, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(CloseProcedure)));
    spinnerHandle = CreateWindowW(L"STATIC", spinnerFrames[0], WS_CHILD | WS_VISIBLE,
        0, 0, 0, 0, windowHandle, nullptr, type.hInstance, nullptr);
    headingHandle = CreateWindowW(L"STATIC", uninstall ? L"アンインストールを準備しています…" : L"セットアップを準備しています…",
        WS_CHILD | WS_VISIBLE, 0, 0, 0, 0, windowHandle, nullptr, type.hInstance, nullptr);
    statusHandle = CreateWindowW(L"STATIC", L"必要なファイルを展開しています。\n画面が開くまで、そのままお待ちください。",
        WS_CHILD | WS_VISIBLE, 0, 0, 0, 0, windowHandle, nullptr, type.hInstance, nullptr);
    Layout(GetDpiForWindow(windowHandle));
    SetTimer(windowHandle, 1, 90, nullptr);
    ShowWindow(windowHandle, SW_SHOW); UpdateWindow(windowHandle); Pump();
}
static std::wstring Quote(const std::wstring& value) {
    std::wstring output = L"\"";
    size_t slashes = 0;
    for (wchar_t character : value) {
        if (character == L'\\') { ++slashes; continue; }
        output.append(slashes * (character == L'"' ? 2 : 1), L'\\');
        slashes = 0;
        if (character == L'"') output += L'\\';
        output += character;
    }
    output.append(slashes * 2, L'\\'); output += L'"';
    return output;
}
static HANDLE Start(const std::wstring& executable, const std::vector<std::wstring>& arguments, const fs::path& directory) {
    std::wstring command = Quote(executable);
    for (const std::wstring& argument : arguments) command += L" " + Quote(argument);
    STARTUPINFOW info{}; info.cb = sizeof(info);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
        nullptr, directory.c_str(), &info, &process)) throw std::runtime_error("process");
    CloseHandle(process.hThread);
    return process.hProcess;
}
static void Wait(HANDLE process, bool owned = true) {
    while (WaitForSingleObject(process, 25) == WAIT_TIMEOUT) {
        Pump();
        if (IsCancelled()) {
            if (owned) { TerminateProcess(process, ERROR_CANCELLED); WaitForSingleObject(process, INFINITE); }
            throw std::runtime_error("cancelled");
        }
    }
}
static Footer ReadFooter(HANDLE file) {
    LARGE_INTEGER size{}; if (!GetFileSizeEx(file, &size) || size.QuadPart < sizeof(Footer)) throw std::runtime_error("footer");
    LARGE_INTEGER position{}; position.QuadPart = size.QuadPart - sizeof(Footer);
    if (!SetFilePointerEx(file, position, nullptr, FILE_BEGIN)) throw std::runtime_error("seek");
    Footer footer{}; DWORD read = 0;
    if (!ReadFile(file, &footer, sizeof(footer), &read, nullptr) || read != sizeof(footer) || memcmp(footer.magic, "HSISPKG2", 8)) throw std::runtime_error("format");
    int64_t end = size.QuadPart - sizeof(Footer);
    if (footer.nativeLength <= 0 || footer.runtimeOffset != footer.nativeLength || footer.runtimeLength <= 0
        || footer.runtimeOffset > end - footer.runtimeLength || footer.payloadOffset != footer.runtimeOffset + footer.runtimeLength
        || footer.payloadLength < 0 || footer.payloadOffset > end - footer.payloadLength || footer.payloadOffset + footer.payloadLength != end
        || (footer.mode != 0 && footer.mode != 1) || footer.depth < 0 || footer.depth > 100) throw std::runtime_error("range");
    return footer;
}
static void CopySegment(HANDLE source, int64_t offset, int64_t length, const fs::path& destination) {
    Handle output(CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (output.value == INVALID_HANDLE_VALUE) throw std::runtime_error("output");
    LARGE_INTEGER position{}; position.QuadPart = offset;
    if (!SetFilePointerEx(source, position, nullptr, FILE_BEGIN)) throw std::runtime_error("seek");
    std::vector<char> buffer(1024 * 1024);
    while (length > 0) {
        Pump(); if (IsCancelled()) throw std::runtime_error("cancelled");
        DWORD count = static_cast<DWORD>(length < buffer.size() ? length : buffer.size()), read = 0, written = 0;
        if (!ReadFile(source, buffer.data(), count, &read, nullptr) || read != count
            || !WriteFile(output.value, buffer.data(), read, &written, nullptr) || written != read) throw std::runtime_error("copy");
        length -= read;
    }
}
static fs::path TempRoot() {
    std::vector<wchar_t> buffer(32768);
    DWORD length = GetTempPathW(static_cast<DWORD>(buffer.size()), buffer.data());
    if (!length || length >= buffer.size()) throw std::runtime_error("temp");
    return fs::path(buffer.data());
}
static std::wstring Token() {
    std::wstring token;
    for (int index = 0; index < 4; index++) {
        unsigned int random = 0; if (rand_s(&random)) throw std::runtime_error("random");
        token += std::to_wstring(random) + L"-";
    }
    return token;
}
static bool IsSession(const fs::path& root, const fs::path& executable) {
    return root.parent_path() == TempRoot().lexically_normal().parent_path() // GetTempPath ends in a separator.
        && root.filename().wstring().rfind(L"Hsis-", 0) == 0
        && executable.filename() == L"Hsis.Bootstrap.exe"
        && fs::exists(root / L".hsis-session")
        && !(GetFileAttributesW(root.c_str()) & FILE_ATTRIBUTE_REPARSE_POINT);
}
static void CleanSession(const fs::path& root, const fs::path& executable) {
    if (!IsSession(root, executable)) return;
    std::error_code error;
    for (const fs::directory_entry& item : fs::directory_iterator(root, error))
        if (item.path() != executable) fs::remove_all(item.path(), error);
    // The running helper itself is small and is released when the process exits.
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    int count = 0;
    LPWSTR* parsed = CommandLineToArgvW(GetCommandLineW(), &count);
    std::vector<std::wstring> arguments;
    for (int index = 0; index < count; index++) arguments.emplace_back(parsed[index]);
    LocalFree(parsed);
    auto value = [&](const std::wstring& key) -> std::wstring {
        for (size_t index = 1; index + 1 < arguments.size(); index++) if (arguments[index] == key) return arguments[index + 1];
        return L"";
    };
    bool quiet = false;
    for (const std::wstring& argument : arguments) if (!_wcsicmp(argument.c_str(), L"/S")) quiet = true;
    fs::path own = fs::absolute(arguments[0]), root;
    bool runner = !value(L"--runner").empty();
    try {
        if (!runner) {
            Handle file(CreateFileW(own.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
            Footer footer = ReadFooter(file.value);
            std::wstring token = Token();
            root = TempRoot() / (L"Hsis-" + token);
            if (!CreateDirectoryW(root.c_str(), nullptr)) throw std::runtime_error("temp directory");
            Handle marker(CreateFileW((root / L".hsis-session").c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
            if (marker.value == INVALID_HANDLE_VALUE) throw std::runtime_error("session");
            std::wstring readyName = L"Local\\Hsis.Runner." + token, cancelName = L"Local\\Hsis.Cancel." + token;
            Handle ready(CreateEventW(nullptr, TRUE, FALSE, readyName.c_str()));
            Handle cancel(CreateEventW(nullptr, TRUE, FALSE, cancelName.c_str())); cancelEvent = cancel.value;
            ShowPreparing(quiet, footer.mode == 1);
            fs::path helper = root / L"Hsis.Bootstrap.exe";
            CopySegment(file.value, 0, footer.nativeLength, helper);
            std::vector<std::wstring> command{ L"--runner", own.wstring(), L"--parent", std::to_wstring(GetCurrentProcessId()), L"--ready", readyName, L"--cancel", cancelName };
            if (quiet) command.emplace_back(L"/S");
            Handle child(Start(helper.wstring(), command, root));
            HANDLE waits[]{ ready.value, child.value };
            DWORD outcome = WAIT_TIMEOUT;
            for (int tick = 0; tick < 1200 && outcome == WAIT_TIMEOUT; tick++) {
                outcome = WaitForMultipleObjects(2, waits, FALSE, 25); Pump();
            }
            if (outcome != WAIT_OBJECT_0) throw std::runtime_error("runner");
            return IsCancelled() ? ERROR_CANCELLED : 0;
        }
        root = own.parent_path();
        if (!IsSession(root, own)) throw std::runtime_error("session path");
        fs::path package = value(L"--runner");
        Handle cancel(OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, FALSE, value(L"--cancel").c_str())); cancelEvent = cancel.value;
        Handle file(CreateFileW(package.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
        Footer footer = ReadFooter(file.value);
        ShowPreparing(quiet, footer.mode == 1);
        Handle ready(OpenEventW(EVENT_MODIFY_STATE, FALSE, value(L"--ready").c_str()));
        if (!ready.value || !SetEvent(ready.value)) throw std::runtime_error("ready event");
        Handle parent(OpenProcess(SYNCHRONIZE, FALSE, static_cast<DWORD>(std::stoul(value(L"--parent")))));
        if (parent.value) Wait(parent.value, false);
        fs::path runtime = root / L"runtime";
        fs::create_directory(runtime);
        fs::path archive = root / L"Runtime.7z";
        CopySegment(file.value, footer.runtimeOffset, footer.runtimeLength, archive);
        // Release the installed uninstaller before the managed removal process starts.
        CloseHandle(file.value); file.value = nullptr;
        std::vector<wchar_t> system(32768); GetSystemDirectoryW(system.data(), static_cast<UINT>(system.size()));
        Handle extract(Start((fs::path(system.data()) / L"tar.exe").wstring(), { L"-x", L"-f", archive.wstring(), L"-C", runtime.wstring() }, root));
        Wait(extract.value);
        DWORD code = 1; GetExitCodeProcess(extract.value, &code);
        if (code) throw std::runtime_error("extract");
        fs::remove(archive);
        if (statusHandle) SetWindowTextW(statusHandle, L"準備ができました。画面を開いています。\nそのままお待ちください。");
        std::wstring managedName = L"Local\\Hsis.Window." + Token();
        Handle managedReady(CreateEventW(nullptr, TRUE, FALSE, managedName.c_str()));
        std::vector<std::wstring> command{ L"--package", package.wstring(), L"--startup-event", managedName };
        if (footer.mode == 1) {
            fs::path installRoot = package.parent_path();
            for (int64_t index = 0; index < footer.depth; index++) installRoot = installRoot.parent_path();
            command.emplace_back(L"--uninstall"); command.push_back(installRoot.wstring());
        }
        if (quiet) command.emplace_back(L"--quiet");
        Handle managed(Start((runtime / L"Hsis.Setup.exe").wstring(), command, runtime));
        if (!quiet) {
            HANDLE waits[]{ managedReady.value, managed.value };
            DWORD outcome = WAIT_TIMEOUT;
            for (int tick = 0; tick < 2400 && outcome == WAIT_TIMEOUT; tick++) {
                outcome = WaitForMultipleObjects(2, waits, FALSE, 25); Pump();
                if (IsCancelled()) { TerminateProcess(managed.value, ERROR_CANCELLED); WaitForSingleObject(managed.value, INFINITE); throw std::runtime_error("cancelled"); }
            }
            if (outcome == WAIT_OBJECT_0 + 1) {
                GetExitCodeProcess(managed.value, &code);
                if (code == 0 || code == ERROR_CANCELLED) { CleanSession(root, own); return static_cast<int>(code); }
                throw std::runtime_error("window startup");
            }
            if (outcome != WAIT_OBJECT_0) { TerminateProcess(managed.value, 1); WaitForSingleObject(managed.value, INFINITE); throw std::runtime_error("window startup"); }
            if (windowHandle) ShowWindow(windowHandle, SW_HIDE);
        }
        Wait(managed.value); GetExitCodeProcess(managed.value, &code);
        CleanSession(root, own);
        return static_cast<int>(code);
    } catch (const std::exception&) {
        if (!quiet && !IsCancelled()) MessageBoxW(windowHandle,
            L"準備を完了できませんでした。\n空き容量と Windows の更新状況を確認して、もう一度実行してください。",
            L"セットアップ", MB_OK | MB_ICONERROR);
        if (runner) CleanSession(root, own);
        return IsCancelled() ? ERROR_CANCELLED : 1;
    }
}
