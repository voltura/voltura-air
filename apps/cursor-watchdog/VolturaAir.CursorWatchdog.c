#include <windows.h>
#include <errno.h>
#include <limits.h>
#include <shellapi.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>

#define MONITOR_READY_TIMEOUT_MS 5000
#define MONITOR_ARGUMENT L"--monitor"
#define RESTORE_COMPLETED_EVENT_ARGUMENT L"--restore-completed-event"
#define NO_RESTORE_COMPLETED_EVENT L"-"

typedef enum WatchdogResult
{
    WATCHDOG_SUCCESS = 0,
    WATCHDOG_INVALID_ARGUMENTS = 1,
    WATCHDOG_RESTORE_FAILED = 2,
    WATCHDOG_ALREADY_RUNNING = 3,
    WATCHDOG_STARTUP_FAILED = 4,
    WATCHDOG_WAIT_FAILED = 5,
    WATCHDOG_LAUNCH_FAILED = -WATCHDOG_STARTUP_FAILED
} WatchdogResult;

static DWORD ParseProcessId(const wchar_t* value)
{
    if (value == NULL || value[0] == L'\0')
    {
        return 0;
    }

    for (const wchar_t* character = value; *character != L'\0'; ++character)
    {
        if (*character < L'0' || *character > L'9')
        {
            return 0;
        }
    }

    errno = 0;
    wchar_t* end = NULL;
    const unsigned long long parsed = wcstoull(value, &end, 10);
    if (errno == ERANGE || end == NULL || end == value || *end != L'\0' || parsed == 0 || parsed > MAXDWORD)
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

static WatchdogResult RunMonitor(
    DWORD hostProcessId,
    const wchar_t* readyEventName,
    const wchar_t* restoreCompletedEventName)
{
    wchar_t mutexName[128];
    if (swprintf_s(
            mutexName,
            ARRAYSIZE(mutexName),
            L"Local\\VolturaAir.CursorWatchdog.%lu",
            hostProcessId) < 0)
    {
        return WATCHDOG_STARTUP_FAILED;
    }

    /* One monitor may own a given host PID; a previous host may finish independently. */
    HANDLE singleInstance = CreateMutexW(NULL, FALSE, mutexName);
    if (singleInstance == NULL)
    {
        return WATCHDOG_STARTUP_FAILED;
    }

    const DWORD mutexResult = WaitForSingleObject(singleInstance, 0);
    if (mutexResult != WAIT_OBJECT_0 && mutexResult != WAIT_ABANDONED)
    {
        CloseHandle(singleInstance);
        return mutexResult == WAIT_TIMEOUT ? WATCHDOG_ALREADY_RUNNING : WATCHDOG_STARTUP_FAILED;
    }

    HANDLE hostProcess = OpenProcess(SYNCHRONIZE, FALSE, hostProcessId);
    if (hostProcess == NULL)
    {
        ReleaseMutex(singleInstance);
        CloseHandle(singleInstance);
        return WATCHDOG_STARTUP_FAILED;
    }

    if (!SignalNamedEvent(readyEventName))
    {
        CloseHandle(hostProcess);
        ReleaseMutex(singleInstance);
        CloseHandle(singleInstance);
        return WATCHDOG_STARTUP_FAILED;
    }

    const DWORD hostWaitResult = WaitForSingleObject(hostProcess, INFINITE);
    CloseHandle(hostProcess);
    if (hostWaitResult != WAIT_OBJECT_0)
    {
        ReleaseMutex(singleInstance);
        CloseHandle(singleInstance);
        return WATCHDOG_WAIT_FAILED;
    }

    /*
     * Restore after every confirmed host exit. A second restore is harmless,
     * while host-provided clean-exit state would add recovery failure paths.
     */
    const BOOL restored = SystemParametersInfoW(SPI_SETCURSORS, 0, NULL, 0);
    if (restored && wcscmp(restoreCompletedEventName, NO_RESTORE_COMPLETED_EVENT) != 0)
    {
        (void)SignalNamedEvent(restoreCompletedEventName);
    }

    ReleaseMutex(singleInstance);
    CloseHandle(singleInstance);
    return restored ? WATCHDOG_SUCCESS : WATCHDOG_RESTORE_FAILED;
}

/*
 * Returns a positive monitor PID after readiness, or WATCHDOG_LAUNCH_FAILED.
 * The caller must receive a positive PID before replacing any system cursor.
 */
static int LaunchMonitor(DWORD hostProcessId, const wchar_t* restoreCompletedEventName)
{
    wchar_t executablePath[MAX_PATH];
    const DWORD pathLength = GetModuleFileNameW(NULL, executablePath, ARRAYSIZE(executablePath));
    if (pathLength == 0 || pathLength >= ARRAYSIZE(executablePath))
    {
        return WATCHDOG_LAUNCH_FAILED;
    }

    wchar_t readyEventName[128];
    if (swprintf_s(
            readyEventName,
            ARRAYSIZE(readyEventName),
            L"Local\\VolturaAir.CursorWatchdog.Ready.%lu.%lu",
            hostProcessId,
            GetCurrentProcessId()) < 0)
    {
        return WATCHDOG_LAUNCH_FAILED;
    }

    HANDLE readyEvent = CreateEventW(NULL, TRUE, FALSE, readyEventName);
    if (readyEvent == NULL)
    {
        return WATCHDOG_LAUNCH_FAILED;
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
            restoreCompletedEventName) < 0)
    {
        CloseHandle(readyEvent);
        return WATCHDOG_LAUNCH_FAILED;
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
        return WATCHDOG_LAUNCH_FAILED;
    }

    CloseHandle(processInformation.hThread);
    const HANDLE startupWaitHandles[] =
    {
        readyEvent,
        processInformation.hProcess
    };
    const DWORD startupResult = WaitForMultipleObjects(
        ARRAYSIZE(startupWaitHandles),
        startupWaitHandles,
        FALSE,
        MONITOR_READY_TIMEOUT_MS);
    CloseHandle(readyEvent);

    if (startupResult == WAIT_OBJECT_0)
    {
        const DWORD monitorProcessId = processInformation.dwProcessId;
        if (monitorProcessId <= INT_MAX)
        {
            CloseHandle(processInformation.hProcess);
            return (int)monitorProcessId;
        }
    }
    else if (startupResult == WAIT_OBJECT_0 + 1)
    {
        CloseHandle(processInformation.hProcess);
        return WATCHDOG_LAUNCH_FAILED;
    }

    if (TerminateProcess(processInformation.hProcess, WATCHDOG_STARTUP_FAILED))
    {
        const DWORD terminationResult = WaitForSingleObject(
            processInformation.hProcess,
            MONITOR_READY_TIMEOUT_MS);
        if (terminationResult == WAIT_FAILED)
        {
            CloseHandle(processInformation.hProcess);
            return WATCHDOG_LAUNCH_FAILED;
        }
    }

    CloseHandle(processInformation.hProcess);
    return WATCHDOG_LAUNCH_FAILED;
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
        return WATCHDOG_STARTUP_FAILED;
    }

    int result = WATCHDOG_INVALID_ARGUMENTS;
    if (argumentCount == 2)
    {
        const DWORD hostProcessId = ParseProcessId(arguments[1]);
        if (hostProcessId != 0)
        {
            result = LaunchMonitor(hostProcessId, NO_RESTORE_COMPLETED_EVENT);
        }
    }
    else if (argumentCount == 4 && wcscmp(arguments[2], RESTORE_COMPLETED_EVENT_ARGUMENT) == 0)
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
