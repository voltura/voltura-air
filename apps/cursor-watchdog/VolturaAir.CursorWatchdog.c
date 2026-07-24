#include <windows.h>
#include <errno.h>
#include <limits.h>
#include <shellapi.h>
#include <stdlib.h>
#include <wchar.h>

#define RESTORE_COMPLETED_EVENT_ARGUMENT L"--restore-completed-event"
#define NO_RESTORE_COMPLETED_EVENT L"-"
#define INITIAL_RESTORE_RETRY_MS 25
#define MAX_RESTORE_RETRY_MS 1000

typedef enum WatchdogResult
{
    WATCHDOG_SUCCESS = 0,
    WATCHDOG_INVALID_ARGUMENTS = 1,
    WATCHDOG_STARTUP_FAILED = 2,
    WATCHDOG_WAIT_FAILED = 3
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

static void RestoreWindowsCursorScheme(const wchar_t* restoreCompletedEventName)
{
    DWORD retryDelay = INITIAL_RESTORE_RETRY_MS;
    while (!SystemParametersInfoW(SPI_SETCURSORS, 0, NULL, 0))
    {
        Sleep(retryDelay);
        retryDelay = min(retryDelay * 2, MAX_RESTORE_RETRY_MS);
    }

    if (wcscmp(restoreCompletedEventName, NO_RESTORE_COMPLETED_EVENT) != 0)
    {
        (void)SignalNamedEvent(restoreCompletedEventName);
    }
}

static WatchdogResult RunMonitor(
    DWORD hostProcessId,
    const wchar_t* readyEventName,
    const wchar_t* restoreCompletedEventName)
{
    DWORD sessionId = 0;
    if (!ProcessIdToSessionId(hostProcessId, &sessionId))
    {
        return WATCHDOG_STARTUP_FAILED;
    }

    wchar_t mutexName[128];
    if (swprintf_s(
            mutexName,
            ARRAYSIZE(mutexName),
            L"Local\\VolturaAir.CursorRecovery.%lu",
            sessionId) < 0)
    {
        return WATCHDOG_STARTUP_FAILED;
    }

    HANDLE sessionMutex = CreateMutexW(NULL, FALSE, mutexName);
    if (sessionMutex == NULL)
    {
        return WATCHDOG_STARTUP_FAILED;
    }

    HANDLE hostProcess = OpenProcess(SYNCHRONIZE, FALSE, hostProcessId);
    if (hostProcess == NULL)
    {
        CloseHandle(sessionMutex);
        return WATCHDOG_STARTUP_FAILED;
    }

    const DWORD mutexResult = WaitForSingleObject(sessionMutex, INFINITE);
    if (mutexResult != WAIT_OBJECT_0 && mutexResult != WAIT_ABANDONED)
    {
        CloseHandle(hostProcess);
        CloseHandle(sessionMutex);
        return WATCHDOG_WAIT_FAILED;
    }

    if (WaitForSingleObject(hostProcess, 0) == WAIT_OBJECT_0)
    {
        CloseHandle(hostProcess);
        RestoreWindowsCursorScheme(restoreCompletedEventName);
        ReleaseMutex(sessionMutex);
        CloseHandle(sessionMutex);
        return WATCHDOG_SUCCESS;
    }

    if (!SignalNamedEvent(readyEventName))
    {
        CloseHandle(hostProcess);
        ReleaseMutex(sessionMutex);
        CloseHandle(sessionMutex);
        return WATCHDOG_STARTUP_FAILED;
    }

    const DWORD hostWaitResult = WaitForSingleObject(hostProcess, INFINITE);
    CloseHandle(hostProcess);
    if (hostWaitResult != WAIT_OBJECT_0)
    {
        ReleaseMutex(sessionMutex);
        CloseHandle(sessionMutex);
        return WATCHDOG_WAIT_FAILED;
    }

    RestoreWindowsCursorScheme(restoreCompletedEventName);
    ReleaseMutex(sessionMutex);
    CloseHandle(sessionMutex);
    return WATCHDOG_SUCCESS;
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
    if (argumentCount == 3)
    {
        const DWORD hostProcessId = ParseProcessId(arguments[1]);
        if (hostProcessId != 0 && arguments[2][0] != L'\0')
        {
            result = RunMonitor(hostProcessId, arguments[2], NO_RESTORE_COMPLETED_EVENT);
        }
    }
    else if (argumentCount == 5 && wcscmp(arguments[3], RESTORE_COMPLETED_EVENT_ARGUMENT) == 0)
    {
        const DWORD hostProcessId = ParseProcessId(arguments[1]);
        if (hostProcessId != 0 && arguments[2][0] != L'\0' && arguments[4][0] != L'\0')
        {
            result = RunMonitor(hostProcessId, arguments[2], arguments[4]);
        }
    }

    LocalFree(arguments);
    return result;
}
