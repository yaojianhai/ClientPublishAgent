#include <windows.h>
#include <shlwapi.h>
#include <gdiplus.h>
#include <nsis/pluginapi.h>

#include <string>
#include <vector>

using namespace Gdiplus;

HINSTANCE g_hInstance;
HWND g_hwndParent;
unsigned int g_stringsize;
stack_t** g_stacktop;
LPTSTR g_variables;

namespace
{
const wchar_t kWindowClass[] = L"WarrenqUnicodeSlideshow";
ULONG_PTR g_gdiplusToken;
HWND g_slideWindow;
HANDLE g_workerThread;
HANDLE g_stopEvent;
UINT g_currentSlide;
UINT g_interval = 2500;
std::vector<Image*> g_images;

void DrawCurrentSlide(HDC hdc, int width, int height)
{
    Graphics graphics(hdc);
    graphics.DrawImage(g_images[g_currentSlide], 0, 0, width, height);
}

void ClearSlideshow()
{
    if (g_stopEvent != nullptr)
    {
        SetEvent(g_stopEvent);
    }

    if (g_workerThread != nullptr)
    {
        WaitForSingleObject(g_workerThread, INFINITE);
        CloseHandle(g_workerThread);
        g_workerThread = nullptr;
    }

    if (g_stopEvent != nullptr)
    {
        CloseHandle(g_stopEvent);
        g_stopEvent = nullptr;
    }

    if (g_slideWindow != nullptr)
    {
        DestroyWindow(g_slideWindow);
        g_slideWindow = nullptr;
    }

    for (Image* image : g_images)
    {
        delete image;
    }
    g_images.clear();

    if (g_gdiplusToken != 0)
    {
        GdiplusShutdown(g_gdiplusToken);
        g_gdiplusToken = 0;
    }
}

DWORD WINAPI SlideshowThread(void*)
{
    while (WaitForSingleObject(g_stopEvent, g_interval) == WAIT_TIMEOUT)
    {
        if (g_images.size() > 1 && IsWindow(g_slideWindow))
        {
            g_currentSlide = (g_currentSlide + 1) % static_cast<UINT>(g_images.size());
            RECT client{};
            GetClientRect(g_slideWindow, &client);
            HDC hdc = GetDC(g_slideWindow);
            if (hdc != nullptr)
            {
                DrawCurrentSlide(hdc, client.right - client.left, client.bottom - client.top);
                ReleaseDC(g_slideWindow, hdc);
            }
        }
    }

    return 0;
}

LRESULT CALLBACK SlideWindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_PAINT:
    {
        PAINTSTRUCT paint{};
        HDC hdc = BeginPaint(hwnd, &paint);
        RECT client{};
        GetClientRect(hwnd, &client);
        DrawCurrentSlide(hdc, client.right - client.left, client.bottom - client.top);
        EndPaint(hwnd, &paint);
        return 0;
    }
    }

    return DefWindowProcW(hwnd, message, wParam, lParam);
}

bool EnsureWindowClass()
{
    WNDCLASSW windowClass{};
    windowClass.hInstance = g_hInstance;
    windowClass.lpszClassName = kWindowClass;
    windowClass.lpfnWndProc = SlideWindowProc;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);

    return RegisterClassW(&windowClass) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
}

void LoadSlides(const wchar_t* configurationFile)
{
    wchar_t entries[32768]{};
    if (GetPrivateProfileSectionW(L"2052", entries, ARRAYSIZE(entries), configurationFile) == 0)
    {
        return;
    }

    wchar_t directory[MAX_PATH]{};
    lstrcpynW(directory, configurationFile, ARRAYSIZE(directory));
    PathRemoveFileSpecW(directory);

    for (const wchar_t* entry = entries; *entry != L'\0'; entry += lstrlenW(entry) + 1)
    {
        const wchar_t* value = entry[0] == L'=' ? entry + 1 : entry;
        const wchar_t* separator = wcschr(value, L',');
        if (separator == nullptr)
        {
            continue;
        }

        std::wstring filename(value, separator - value);
        wchar_t imagePath[MAX_PATH]{};
        wsprintfW(imagePath, L"%s\\%s", directory, filename.c_str());

        Image* image = Image::FromFile(imagePath, FALSE);
        if (image != nullptr && image->GetLastStatus() == Ok)
        {
            g_images.push_back(image);
        }
        else
        {
            delete image;
        }

        const wchar_t* duration = wcschr(separator + 1, L',');
        if (duration != nullptr)
        {
            UINT parsedInterval = static_cast<UINT>(wcstoul(duration + 1, nullptr, 10));
            if (parsedInterval > 0)
            {
                g_interval = parsedInterval + 500;
            }
        }
    }
}
}

extern "C" void __declspec(dllexport) show(
    HWND hwndParent,
    int string_size,
    LPTSTR variables,
    stack_t** stacktop,
    extra_parameters* extra,
    ...)
{
    EXDLL_INIT();
    g_hwndParent = hwndParent;
    ClearSlideshow();

    std::vector<wchar_t> argument(string_size);
    std::wstring configurationFile;
    HWND slideParent = hwndParent;

    while (popstring(argument.data()) == 0)
    {
        if (wcsncmp(argument.data(), L"/auto=", 6) == 0)
        {
            configurationFile = argument.data() + 6;
        }
        else if (wcsncmp(argument.data(), L"/hwnd=", 6) == 0)
        {
            slideParent = reinterpret_cast<HWND>(
                static_cast<ULONG_PTR>(wcstoull(argument.data() + 6, nullptr, 0)));
        }
    }

    if (configurationFile.empty())
    {
        return;
    }

    GdiplusStartupInput startupInput{};
    if (GdiplusStartup(&g_gdiplusToken, &startupInput, nullptr) != Ok)
    {
        g_gdiplusToken = 0;
        return;
    }

    LoadSlides(configurationFile.c_str());
    if (g_images.empty() || !EnsureWindowClass())
    {
        ClearSlideshow();
        return;
    }

    g_currentSlide = 0;
    g_slideWindow = CreateWindowExW(
        0,
        kWindowClass,
        L"",
        WS_CHILD | WS_VISIBLE,
        0,
        0,
        588,
        326,
        slideParent,
        nullptr,
        g_hInstance,
        nullptr);

    if (g_slideWindow == nullptr)
    {
        ClearSlideshow();
        return;
    }

    SetWindowPos(g_slideWindow, HWND_TOP, 0, 0, 588, 326, SWP_SHOWWINDOW);
    InvalidateRect(g_slideWindow, nullptr, FALSE);
    UpdateWindow(g_slideWindow);
    g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (g_stopEvent != nullptr)
    {
        g_workerThread = CreateThread(nullptr, 0, SlideshowThread, nullptr, 0, nullptr);
    }
}

extern "C" void __declspec(dllexport) stop(
    HWND hwndParent,
    int string_size,
    LPTSTR variables,
    stack_t** stacktop,
    extra_parameters* extra,
    ...)
{
    EXDLL_INIT();
    ClearSlideshow();
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hInstance = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}
