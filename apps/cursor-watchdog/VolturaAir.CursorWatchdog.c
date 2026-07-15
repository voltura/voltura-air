#include <windows.h>
#include <shellapi.h>
#include <wchar.h>

#define SPI_SETCURSORS 0x0057

static DWORD ParseHostProcessId(int argumentCount, wchar_t** arguments)
{
    if (argumentCount != 2)
    {
        return 0;
    }

    wchar_t* end = NULL;
    const unsigned long parsed = wcstoul(arguments[1], &end, 10);
    if (arguments[1][0] == L'\0' || end == NULL || *end != L'\0' || parsed == 0 || parsed > MAXDWORD)
    {
        return 0;
    }

    return (DWORD)parsed;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previousInstance, PWSTR commandLine, int showCommand)
{
    (void)instance;
    (void)previousInstance;
    (void)commandLine;
    (void)showCommand;

    int argumentCount = 0;
    wchar_t** arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    if (arguments == NULL)
    {
        return 1;
    }

    const DWORD hostProcessId = ParseHostProcessId(argumentCount, arguments);
    LocalFree(arguments);
    if (hostProcessId == 0)
    {
        return 1;
    }

    HANDLE singleInstance = CreateMutexW(NULL, FALSE, L"Local\\VolturaAir.CursorWatchdog");
    if (singleInstance == NULL)
    {
        return 2;
    }

    const DWORD mutexResult = WaitForSingleObject(singleInstance, 0);
    if (mutexResult != WAIT_OBJECT_0 && mutexResult != WAIT_ABANDONED)
    {
        CloseHandle(singleInstance);
        return 3;
    }

    HANDLE hostProcess = OpenProcess(SYNCHRONIZE, FALSE, hostProcessId);
    if (hostProcess != NULL)
    {
        (void)WaitForSingleObject(hostProcess, INFINITE);
        CloseHandle(hostProcess);
    }

    const BOOL restored = SystemParametersInfoW(SPI_SETCURSORS, 0, NULL, 0);
    ReleaseMutex(singleInstance);
    CloseHandle(singleInstance);
    return restored ? 0 : 2;
}
