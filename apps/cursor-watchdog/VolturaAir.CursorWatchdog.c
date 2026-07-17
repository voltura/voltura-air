#include <windows.h>
#include <limits.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

#define SPI_SETCURSORS 0x0057
#define MONITOR_READY_TIMEOUT_MS 5000
#define COMPLETION_EVENT_ARGUMENT L"--completion-event"
#define MONITOR_ARGUMENT L"--monitor"
#define NO_COMPLETION_EVENT L"-"

static DWORD ParseProcessId(const wchar_t* value)
{
    wchar_t* end = NULL;
    const unsigned long parsed = wcstoul(value, &end, 10);
    if (value[0] == L'\0' || end == NULL || *end != L'\0' || parsed == 0 || parsed > MAXDWORD)
    {
        return 0;
    }

    return (DWORD)parsed;
}

static BOOL SignalNamedEvent(const wchar_t* eventName)
{
    HANDLE eventHandle = OpenEventW(EVENT_MODIFY_STATE, FALSE, eventName);
    if (eventHandle == NULL)
    {
        return FALSE;
    }

    const BOOL signaled = SetEvent(eventHandle);
    CloseHandle(eventHandle);
    return signaled;
}

static int RunMonitor(DWORD hostProcessId, const wchar_t* readyEventName, const wchar_t* completionEventName)
{
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
    if (hostProcess == NULL || !SignalNamedEvent(readyEventName))
    {
        if (hostProcess != NULL)
        {
            CloseHandle(hostProcess);
        }

        ReleaseMutex(singleInstance);
        CloseHandle(singleInstance);
        return 4;
    }

    (void)WaitForSingleObject(hostProcess, INFINITE);
    CloseHandle(hostProcess);

    const BOOL restored = SystemParametersInfoW(SPI_SETCURSORS, 0, NULL, 0);
    if (restored && wcscmp(completionEventName, NO_COMPLETION_EVENT) != 0)
    {
        (void)SignalNamedEvent(completionEventName);
    }

    ReleaseMutex(singleInstance);
    CloseHandle(singleInstance);
    return restored ? 0 : 2;
}

static int LaunchMonitor(DWORD hostProcessId, const wchar_t* completionEventName)
{
    wchar_t executablePath[MAX_PATH];
    const DWORD pathLength = GetModuleFileNameW(NULL, executablePath, ARRAYSIZE(executablePath));
    if (pathLength == 0 || pathLength >= ARRAYSIZE(executablePath))
    {
        return -4;
    }

    wchar_t readyEventName[128];
    if (swprintf_s(
            readyEventName,
            ARRAYSIZE(readyEventName),
            L"Local\\VolturaAir.CursorWatchdog.Ready.%lu.%lu",
            hostProcessId,
            GetCurrentProcessId()) < 0)
    {
        return -4;
    }

    HANDLE readyEvent = CreateEventW(NULL, TRUE, FALSE, readyEventName);
    if (readyEvent == NULL)
    {
        return -4;
    }

    wchar_t commandLine[MAX_PATH + 512];
    if (swprintf_s(
            commandLine,
            ARRAYSIZE(commandLine),
            L"\"%ls\" %ls %lu \"%ls\" \"%ls\"",
            executablePath,
            MONITOR_ARGUMENT,
            hostProcessId,
            readyEventName,
            completionEventName) < 0)
    {
        CloseHandle(readyEvent);
        return -4;
    }

    STARTUPINFOW startupInfo = {0};
    startupInfo.cb = sizeof(startupInfo);
    PROCESS_INFORMATION processInformation = {0};
    const BOOL started = CreateProcessW(
        executablePath,
        commandLine,
        NULL,
        NULL,
        FALSE,
        CREATE_NO_WINDOW,
        NULL,
        NULL,
        &startupInfo,
        &processInformation);
    if (!started)
    {
        CloseHandle(readyEvent);
        return -4;
    }

    CloseHandle(processInformation.hThread);
    const DWORD readyResult = WaitForSingleObject(readyEvent, MONITOR_READY_TIMEOUT_MS);
    CloseHandle(readyEvent);
    if (readyResult != WAIT_OBJECT_0)
    {
        (void)TerminateProcess(processInformation.hProcess, 4);
        (void)WaitForSingleObject(processInformation.hProcess, MONITOR_READY_TIMEOUT_MS);
        CloseHandle(processInformation.hProcess);
        return -4;
    }

    const DWORD monitorProcessId = processInformation.dwProcessId;
    CloseHandle(processInformation.hProcess);
    return monitorProcessId <= INT_MAX ? (int)monitorProcessId : -4;
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

    int result = -1;
    if (argumentCount == 2)
    {
        const DWORD hostProcessId = ParseProcessId(arguments[1]);
        if (hostProcessId != 0)
        {
            result = LaunchMonitor(hostProcessId, NO_COMPLETION_EVENT);
        }
    }
    else if (argumentCount == 4 && wcscmp(arguments[2], COMPLETION_EVENT_ARGUMENT) == 0)
    {
        const DWORD hostProcessId = ParseProcessId(arguments[1]);
        if (hostProcessId != 0 && arguments[3][0] != L'\0')
        {
            result = LaunchMonitor(hostProcessId, arguments[3]);
        }
    }
    else if (argumentCount == 5 && wcscmp(arguments[1], MONITOR_ARGUMENT) == 0)
    {
        const DWORD hostProcessId = ParseProcessId(arguments[2]);
        if (hostProcessId != 0 && arguments[3][0] != L'\0' && arguments[4][0] != L'\0')
        {
            result = RunMonitor(hostProcessId, arguments[3], arguments[4]);
        }
    }

    LocalFree(arguments);
    return result;
}
