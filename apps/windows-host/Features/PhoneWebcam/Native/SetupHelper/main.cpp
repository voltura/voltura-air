#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <sddl.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mfvirtualcamera.h>
#include <ks.h>
#include <ksmedia.h>
#include <wrl/client.h>

#include <filesystem>
#include <array>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
    class HiddenProcessStream
    {
    public:
        explicit HiddenProcessStream(const DWORD standardHandle) : m_standardHandle(standardHandle) {}

        template<typename T>
        HiddenProcessStream& operator<<(const T& value)
        {
            m_stream << value;
            return *this;
        }

        HiddenProcessStream& operator<<(std::wostream& (*manipulator)(std::wostream&))
        {
            manipulator(m_stream);
            Flush();
            return *this;
        }

    private:
        void Flush()
        {
            const std::wstring value = m_stream.str();
            m_stream.str(L"");
            m_stream.clear();
            if (value.empty()) return;
            const HANDLE handle = GetStdHandle(m_standardHandle);
            if (handle == nullptr || handle == INVALID_HANDLE_VALUE) return;
            const int byteCount = WideCharToMultiByte(
                CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
            if (byteCount <= 0) return;
            std::string encoded(static_cast<size_t>(byteCount), '\0');
            if (WideCharToMultiByte(
                    CP_UTF8, 0, value.data(), static_cast<int>(value.size()), encoded.data(), byteCount, nullptr, nullptr) <= 0)
                return;
            size_t offset = 0;
            while (offset < encoded.size())
            {
                DWORD written = 0;
                if (!WriteFile(
                        handle,
                        encoded.data() + offset,
                        static_cast<DWORD>(encoded.size() - offset),
                        &written,
                        nullptr) || written == 0)
                    return;
                offset += written;
            }
        }

        DWORD m_standardHandle;
        std::wostringstream m_stream;
    };

    HiddenProcessStream g_output(STD_OUTPUT_HANDLE);
    HiddenProcessStream g_error(STD_ERROR_HANDLE);
    bool g_allowFaultInjection = false;
    constexpr wchar_t SourceClsid[] = L"{50AAB70E-38BA-403E-A55B-58F2BCABE4FB}";
    constexpr wchar_t FriendlyName[] = L"Voltura Air Webcam";
    constexpr wchar_t InstalledDirectoryName[] = L"Voltura Air Webcam";
    constexpr wchar_t SourceDllName[] = L"VirtualCameraMediaSource.dll";
    constexpr wchar_t SetupHelperName[] = L"VolturaAir.WebcamSetup.exe";
    constexpr int MediaSourceResource = 101;
    constexpr UINT32 FrameWidth = 1920;
    constexpr UINT32 FrameHeight = 1080;
    constexpr DWORD ExpectedFrameBytes = FrameWidth * FrameHeight * 3 / 2;
    constexpr DWORD FirstVideoStream = static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    constexpr DWORD ElevatedOperationTimeoutMilliseconds = 10 * 60 * 1000;
    constexpr DWORD ElevatedWrapperTimeoutMilliseconds = ElevatedOperationTimeoutMilliseconds + 30 * 1000;
    constexpr DWORD FileReleaseRetryMilliseconds = 10 * 1000;
    constexpr DWORD FileReleaseRetryIntervalMilliseconds = 100;
    constexpr DWORD ServiceStopTimeoutMilliseconds = 30 * 1000;
    constexpr DWORD ServiceStopPollMilliseconds = 100;

    class ElevatedOperationTimeoutScope
    {
    public:
        ElevatedOperationTimeoutScope()
        {
            m_completed = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (m_completed == nullptr)
            {
                m_result = HRESULT_FROM_WIN32(GetLastError());
                return;
            }
            m_watchdog = CreateThread(nullptr, 0, Watch, this, 0, nullptr);
            if (m_watchdog == nullptr)
            {
                m_result = HRESULT_FROM_WIN32(GetLastError());
                CloseHandle(m_completed);
                m_completed = nullptr;
            }
        }

        ~ElevatedOperationTimeoutScope()
        {
            if (m_completed != nullptr) SetEvent(m_completed);
            if (m_watchdog != nullptr)
            {
                WaitForSingleObject(m_watchdog, INFINITE);
                CloseHandle(m_watchdog);
            }
            if (m_completed != nullptr) CloseHandle(m_completed);
        }

        HRESULT Result() const noexcept { return m_result; }

    private:
        static DWORD WINAPI Watch(LPVOID context)
        {
            auto* owner = static_cast<ElevatedOperationTimeoutScope*>(context);
            if (WaitForSingleObject(owner->m_completed, ElevatedOperationTimeoutMilliseconds) == WAIT_TIMEOUT)
                TerminateProcess(GetCurrentProcess(), ERROR_TIMEOUT);
            return 0;
        }

        HANDLE m_completed = nullptr;
        HANDLE m_watchdog = nullptr;
        HRESULT m_result = S_OK;
    };

    class MediaFoundationScope
    {
    public:
        MediaFoundationScope()
        {
            const HRESULT comResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
            m_uninitializeCom = SUCCEEDED(comResult);
            if (FAILED(comResult) && comResult != RPC_E_CHANGED_MODE)
            {
                m_result = comResult;
                return;
            }

            m_result = MFStartup(MF_VERSION, MFSTARTUP_FULL);
        }

        ~MediaFoundationScope()
        {
            if (SUCCEEDED(m_result))
            {
                MFShutdown();
            }
            if (m_uninitializeCom)
            {
                CoUninitialize();
            }
        }

        HRESULT Result() const noexcept { return m_result; }

    private:
        HRESULT m_result = E_FAIL;
        bool m_uninitializeCom = false;
    };

    std::wstring HResultText(const HRESULT result)
    {
        std::wostringstream stream;
        stream << L"0x" << std::uppercase << std::hex << std::setw(8) << std::setfill(L'0')
               << static_cast<unsigned long>(result);
        return stream.str();
    }

    bool ShouldInjectFault(const wchar_t* boundary)
    {
        if (!g_allowFaultInjection) return false;
        static bool injected = false;
        if (injected) return false;
        wchar_t value[128]{};
        const DWORD length = GetEnvironmentVariableW(L"VOLTURA_WEBCAM_FAULT", value, ARRAYSIZE(value));
        if (length == 0 || length >= ARRAYSIZE(value) || _wcsicmp(value, boundary) != 0) return false;
        injected = true;
        return true;
    }

    HRESULT ProgramFilesInstallDirectory(std::filesystem::path& path)
    {
        PWSTR rawPath = nullptr;
        const HRESULT result = SHGetKnownFolderPath(FOLDERID_ProgramFiles, KF_FLAG_DEFAULT, nullptr, &rawPath);
        if (FAILED(result))
        {
            return result;
        }
        path = std::filesystem::path(rawPath) / InstalledDirectoryName;
        CoTaskMemFree(rawPath);
        return S_OK;
    }

    std::filesystem::path CurrentExecutable()
    {
        std::wstring buffer(32768, L'\0');
        const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0 || length >= buffer.size())
        {
            return {};
        }
        buffer.resize(length);
        return std::filesystem::path(buffer);
    }

    HRESULT RejectReparsePointIfPresent(const std::filesystem::path& path)
    {
        const DWORD attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES)
        {
            const DWORD error = GetLastError();
            return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
                ? S_OK
                : HRESULT_FROM_WIN32(error);
        }
        return (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0
            ? S_OK
            : HRESULT_FROM_WIN32(ERROR_REPARSE_TAG_INVALID);
    }

    bool IsTransientFileReleaseError(const DWORD error)
    {
        return error == ERROR_ACCESS_DENIED || error == ERROR_SHARING_VIOLATION || error == ERROR_LOCK_VIOLATION;
    }

    HRESULT MoveReleasedFile(const std::filesystem::path& source, const std::filesystem::path& destination)
    {
        DWORD error = ERROR_GEN_FAILURE;
        for (DWORD elapsed = 0; elapsed <= FileReleaseRetryMilliseconds; elapsed += FileReleaseRetryIntervalMilliseconds)
        {
            if (MoveFileExW(source.c_str(), destination.c_str(), MOVEFILE_WRITE_THROUGH)) return S_OK;
            error = GetLastError();
            if (!IsTransientFileReleaseError(error) || elapsed == FileReleaseRetryMilliseconds) break;
            Sleep(FileReleaseRetryIntervalMilliseconds);
        }
        return HRESULT_FROM_WIN32(error);
    }

    HRESULT DeleteReleasedFile(const std::filesystem::path& path)
    {
        DWORD error = ERROR_GEN_FAILURE;
        for (DWORD elapsed = 0; elapsed <= FileReleaseRetryMilliseconds; elapsed += FileReleaseRetryIntervalMilliseconds)
        {
            if (DeleteFileW(path.c_str())) return S_OK;
            error = GetLastError();
            if (!IsTransientFileReleaseError(error) || elapsed == FileReleaseRetryMilliseconds) break;
            Sleep(FileReleaseRetryIntervalMilliseconds);
        }
        return HRESULT_FROM_WIN32(error);
    }

    HRESULT StopServiceForRemoval(SC_HANDLE manager, const wchar_t* serviceName)
    {
        SC_HANDLE service = OpenServiceW(manager, serviceName, SERVICE_QUERY_STATUS | SERVICE_STOP);
        if (service == nullptr)
        {
            const DWORD error = GetLastError();
            return error == ERROR_SERVICE_DOES_NOT_EXIST ? S_OK : HRESULT_FROM_WIN32(error);
        }

        SERVICE_STATUS_PROCESS status{};
        DWORD bytesNeeded = 0;
        auto queryStatus = [&]()
        {
            return QueryServiceStatusEx(
                service,
                SC_STATUS_PROCESS_INFO,
                reinterpret_cast<BYTE*>(&status),
                sizeof(status),
                &bytesNeeded) != FALSE;
        };

        HRESULT result = S_OK;
        if (!queryStatus())
        {
            result = HRESULT_FROM_WIN32(GetLastError());
        }
        else if (status.dwCurrentState != SERVICE_STOPPED)
        {
            if (status.dwCurrentState != SERVICE_STOP_PENDING)
            {
                SERVICE_STATUS controlStatus{};
                if (!ControlService(service, SERVICE_CONTROL_STOP, &controlStatus))
                {
                    const DWORD error = GetLastError();
                    if (error != ERROR_SERVICE_NOT_ACTIVE) result = HRESULT_FROM_WIN32(error);
                }
            }

            for (DWORD elapsed = 0; SUCCEEDED(result) && elapsed <= ServiceStopTimeoutMilliseconds;
                 elapsed += ServiceStopPollMilliseconds)
            {
                if (!queryStatus())
                {
                    result = HRESULT_FROM_WIN32(GetLastError());
                    break;
                }
                if (status.dwCurrentState == SERVICE_STOPPED) break;
                if (elapsed == ServiceStopTimeoutMilliseconds)
                {
                    result = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                    break;
                }
                Sleep(ServiceStopPollMilliseconds);
            }
        }

        CloseServiceHandle(service);
        return result;
    }

    HRESULT StopCameraServicesForRemoval()
    {
        SC_HANDLE manager = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (manager == nullptr) return HRESULT_FROM_WIN32(GetLastError());
        HRESULT result = StopServiceForRemoval(manager, L"FrameServerMonitor");
        if (SUCCEEDED(result)) result = StopServiceForRemoval(manager, L"FrameServer");
        CloseServiceHandle(manager);
        return result;
    }

    HRESULT FilesEqual(
        const std::filesystem::path& left,
        const std::filesystem::path& right,
        bool& equal)
    {
        equal = false;
        std::error_code error;
        if (!std::filesystem::is_regular_file(left, error) || error) return HRESULT_FROM_WIN32(error ? error.value() : ERROR_FILE_NOT_FOUND);
        if (!std::filesystem::is_regular_file(right, error) || error) return HRESULT_FROM_WIN32(error ? error.value() : ERROR_FILE_NOT_FOUND);
        if (std::filesystem::file_size(left, error) != std::filesystem::file_size(right, error) || error)
            return error ? HRESULT_FROM_WIN32(error.value()) : S_OK;
        std::ifstream leftStream(left, std::ios::binary);
        std::ifstream rightStream(right, std::ios::binary);
        if (!leftStream || !rightStream) return HRESULT_FROM_WIN32(ERROR_OPEN_FAILED);
        std::array<char, 64 * 1024> leftBuffer{};
        std::array<char, 64 * 1024> rightBuffer{};
        do
        {
            leftStream.read(leftBuffer.data(), leftBuffer.size());
            rightStream.read(rightBuffer.data(), rightBuffer.size());
            if (leftStream.gcount() != rightStream.gcount() ||
                !std::equal(leftBuffer.begin(), leftBuffer.begin() + leftStream.gcount(), rightBuffer.begin()))
                return S_OK;
        } while (leftStream.gcount() > 0);
        equal = true;
        return S_OK;
    }

    HRESULT LoadPackagedSource(std::vector<BYTE>& bytes)
    {
        HMODULE module = GetModuleHandleW(nullptr);
        HRSRC resource = FindResourceW(module, MAKEINTRESOURCEW(MediaSourceResource), RT_RCDATA);
        if (resource == nullptr) return HRESULT_FROM_WIN32(GetLastError());
        const DWORD size = SizeofResource(module, resource);
        if (size == 0) return HRESULT_FROM_WIN32(ERROR_RESOURCE_DATA_NOT_FOUND);
        HGLOBAL loaded = LoadResource(module, resource);
        if (loaded == nullptr) return HRESULT_FROM_WIN32(GetLastError());
        const BYTE* data = static_cast<const BYTE*>(LockResource(loaded));
        if (data == nullptr) return HRESULT_FROM_WIN32(ERROR_RESOURCE_DATA_NOT_FOUND);
        bytes.assign(data, data + size);
        return S_OK;
    }

    bool IsAdministrator()
    {
        SID_IDENTIFIER_AUTHORITY authority = SECURITY_NT_AUTHORITY;
        PSID administrators = nullptr;
        BOOL isMember = FALSE;
        if (!AllocateAndInitializeSid(
                &authority, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
                0, 0, 0, 0, 0, 0, &administrators))
        {
            return false;
        }
        const BOOL checked = CheckTokenMembership(nullptr, administrators, &isMember);
        FreeSid(administrators);
        return checked != FALSE && isMember != FALSE;
    }

    HRESULT CurrentUserSid(std::wstring& value)
    {
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return HRESULT_FROM_WIN32(GetLastError());
        DWORD tokenBytes = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &tokenBytes);
        std::vector<BYTE> tokenBuffer(tokenBytes);
        if (tokenBytes == 0 || !GetTokenInformation(token, TokenUser, tokenBuffer.data(), tokenBytes, &tokenBytes))
        {
            const HRESULT result = HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(token);
            return result;
        }
        CloseHandle(token);

        LPWSTR rawSid = nullptr;
        if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(tokenBuffer.data())->User.Sid, &rawSid))
            return HRESULT_FROM_WIN32(GetLastError());
        value = rawSid;
        LocalFree(rawSid);
        return S_OK;
    }

    HRESULT ValidateOwnerSid(const std::wstring& ownerSid)
    {
        PSID parsedSid = nullptr;
        if (!ConvertStringSidToSidW(ownerSid.c_str(), &parsedSid)) return HRESULT_FROM_WIN32(GetLastError());
        const bool validSid = IsValidSid(parsedSid) != FALSE;
        LocalFree(parsedSid);
        return validSid ? S_OK : E_INVALIDARG;
    }

    HRESULT RunElevatedAndWait(const std::wstring& arguments)
    {
        const std::filesystem::path executable = CurrentExecutable();
        if (executable.empty())
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        HANDLE executableLock = CreateFileW(
            executable.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, nullptr);
        if (executableLock == INVALID_HANDLE_VALUE)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        SHELLEXECUTEINFOW info{};
        info.cbSize = sizeof(info);
        info.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
        info.lpVerb = L"runas";
        info.lpFile = executable.c_str();
        info.lpParameters = arguments.c_str();
        info.nShow = SW_HIDE;
        if (!ShellExecuteExW(&info))
        {
            const HRESULT result = HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(executableLock);
            return result;
        }

        const DWORD waitResult = WaitForSingleObject(
            info.hProcess,
            ElevatedWrapperTimeoutMilliseconds);
        if (waitResult == WAIT_TIMEOUT)
        {
            // The elevated transaction is expected to finish or roll back in seconds.
            // Bound a wedged helper and request termination before releasing ownership.
            TerminateProcess(info.hProcess, ERROR_TIMEOUT);
            WaitForSingleObject(info.hProcess, 30 * 1000);
            CloseHandle(info.hProcess);
            CloseHandle(executableLock);
            return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
        }
        DWORD exitCode = ERROR_GEN_FAILURE;
        const BOOL gotExitCode = GetExitCodeProcess(info.hProcess, &exitCode);
        CloseHandle(info.hProcess);
        CloseHandle(executableLock);
        if (waitResult != WAIT_OBJECT_0)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        if (!gotExitCode)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        return exitCode == 0 ? S_OK : HRESULT_FROM_WIN32(exitCode);
    }

    HRESULT UnregisterComSource()
    {
        const std::wstring keyPath = std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + SourceClsid;
        LSTATUS result = RegDeleteTreeW(HKEY_LOCAL_MACHINE, keyPath.c_str());
        if (result == ERROR_SUCCESS && ShouldInjectFault(L"unregister-after-delete"))
            result = ERROR_WRITE_FAULT;
        return result == ERROR_FILE_NOT_FOUND ? S_OK : HRESULT_FROM_WIN32(result);
    }

    HRESULT ReadRegistryString(const std::wstring& keyPath, const wchar_t* valueName, std::wstring& value)
    {
        DWORD bytes = 0;
        LSTATUS result = RegGetValueW(
            HKEY_LOCAL_MACHINE, keyPath.c_str(), valueName, RRF_RT_REG_SZ, nullptr, nullptr, &bytes);
        if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);
        std::vector<wchar_t> buffer(bytes / sizeof(wchar_t));
        result = RegGetValueW(
            HKEY_LOCAL_MACHINE, keyPath.c_str(), valueName, RRF_RT_REG_SZ, nullptr, buffer.data(), &bytes);
        if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);
        value.assign(buffer.data());
        return S_OK;
    }

    HRESULT VerifyComSource(const std::filesystem::path& sourcePath, const std::wstring& ownerSid)
    {
        const std::wstring rootPath = std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + SourceClsid;
        const std::wstring inProcPath = rootPath + L"\\InProcServer32";
        std::wstring actualOwner;
        std::wstring actualSource;
        std::wstring actualThreading;
        HRESULT result = ReadRegistryString(rootPath, L"OwnerSid", actualOwner);
        if (SUCCEEDED(result)) result = ReadRegistryString(inProcPath, nullptr, actualSource);
        if (SUCCEEDED(result)) result = ReadRegistryString(inProcPath, L"ThreadingModel", actualThreading);
        if (FAILED(result)) return result;
        return actualOwner == ownerSid && actualSource == sourcePath.wstring() && actualThreading == L"Both"
            ? S_OK
            : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    }

    HRESULT RegistrationExists(bool& exists)
    {
        const std::wstring keyPath = std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + SourceClsid;
        HKEY key = nullptr;
        const LSTATUS result = RegOpenKeyExW(HKEY_LOCAL_MACHINE, keyPath.c_str(), 0, KEY_QUERY_VALUE, &key);
        if (result == ERROR_FILE_NOT_FOUND)
        {
            exists = false;
            return S_OK;
        }
        if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);
        RegCloseKey(key);
        exists = true;
        return S_OK;
    }

    HRESULT VerifyRegisteredOwner(const std::wstring& ownerSid)
    {
        bool registrationExists = false;
        HRESULT result = RegistrationExists(registrationExists);
        if (FAILED(result) || !registrationExists) return result;

        std::wstring storedOwnerSid;
        const std::wstring rootPath = std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + SourceClsid;
        result = ReadRegistryString(rootPath, L"OwnerSid", storedOwnerSid);
        if (FAILED(result)) return result;
        return storedOwnerSid == ownerSid ? S_OK : E_ACCESSDENIED;
    }

    HRESULT FindCamera(ComPtr<IMFActivate>& found, std::wstring& friendlyName);

    HRESULT FileMatchesPackagedSource(const std::filesystem::path& file, bool& equal)
    {
        equal = false;
        std::vector<BYTE> packaged;
        HRESULT result = LoadPackagedSource(packaged);
        if (FAILED(result)) return result;
        std::error_code error;
        const auto fileSize = std::filesystem::file_size(file, error);
        if (error) return HRESULT_FROM_WIN32(error.value());
        if (fileSize != packaged.size()) return S_OK;

        std::ifstream stream(file, std::ios::binary);
        if (!stream) return HRESULT_FROM_WIN32(ERROR_OPEN_FAILED);
        std::array<char, 64 * 1024> buffer{};
        size_t offset = 0;
        while (stream)
        {
            stream.read(buffer.data(), buffer.size());
            const std::streamsize read = stream.gcount();
            if (read > 0 && memcmp(
                    buffer.data(), packaged.data() + offset, static_cast<size_t>(read)) != 0)
            {
                return S_OK;
            }
            offset += static_cast<size_t>(read);
        }
        equal = stream.eof() && offset == packaged.size();
        return S_OK;
    }

    HRESULT WritePackagedSource(const std::filesystem::path& destination)
    {
        std::vector<BYTE> packaged;
        HRESULT result = LoadPackagedSource(packaged);
        if (FAILED(result)) return result;
        HANDLE file = CreateFileW(
            destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH, nullptr);
        if (file == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        DWORD written = 0;
        const bool succeeded = WriteFile(
                file, packaged.data(), static_cast<DWORD>(packaged.size()), &written, nullptr) != FALSE &&
            written == packaged.size() && FlushFileBuffers(file) != FALSE;
        const DWORD writeError = succeeded ? ERROR_SUCCESS : GetLastError();
        CloseHandle(file);
        if (!succeeded)
        {
            DeleteFileW(destination.c_str());
            return HRESULT_FROM_WIN32(writeError);
        }
        bool matches = false;
        result = FileMatchesPackagedSource(destination, matches);
        return SUCCEEDED(result) && !matches ? HRESULT_FROM_WIN32(ERROR_FILE_CORRUPT) : result;
    }

    HRESULT ReadInstallationState(bool& installed, bool& cleanupRequired, bool& updateRequired, std::wstring& name)
    {
        installed = false;
        cleanupRequired = false;
        updateRequired = false;
        ComPtr<IMFActivate> camera;
        HRESULT result = FindCamera(camera, name);
        if (result == HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
        {
            result = S_OK;
        }
        else if (SUCCEEDED(result))
        {
            installed = true;
            cleanupRequired = true;
        }
        if (FAILED(result)) return result;

        bool registrationExists = false;
        result = RegistrationExists(registrationExists);
        if (FAILED(result)) return result;
        cleanupRequired = cleanupRequired || registrationExists;

        std::filesystem::path installDirectory;
        result = ProgramFilesInstallDirectory(installDirectory);
        if (FAILED(result)) return result;
        std::error_code fileError;
        const bool directoryExists = std::filesystem::exists(installDirectory, fileError);
        if (fileError) return HRESULT_FROM_WIN32(fileError.value());
        result = RejectReparsePointIfPresent(installDirectory);
        if (FAILED(result)) return result;
        cleanupRequired = cleanupRequired || directoryExists;
        if (installed)
        {
            const std::filesystem::path installedDll = installDirectory / SourceDllName;
            const std::filesystem::path installedHelper = installDirectory / SetupHelperName;
            result = RejectReparsePointIfPresent(installedDll);
            if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(installedHelper);
            if (FAILED(result)) return result;
            if (!std::filesystem::is_regular_file(installedDll) ||
                !std::filesystem::is_regular_file(installedHelper))
            {
                updateRequired = true;
            }
            else
            {
                bool equal = false;
                result = FileMatchesPackagedSource(installedDll, equal);
                if (FAILED(result)) return result;
                updateRequired = !equal;
                if (!updateRequired)
                {
                    result = FilesEqual(CurrentExecutable(), installedHelper, equal);
                    if (FAILED(result)) return result;
                    updateRequired = !equal;
                }
            }
        }
        return S_OK;
    }

    HRESULT RegisterComSource(
        const std::filesystem::path& sourcePath,
        const std::wstring& ownerSid,
        bool* registrationCompleteOrAbsent = nullptr)
    {
        if (registrationCompleteOrAbsent != nullptr) *registrationCompleteOrAbsent = false;
        const HRESULT sidResult = ValidateOwnerSid(ownerSid);
        if (FAILED(sidResult)) return sidResult;

        const std::wstring rootPath = std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + SourceClsid;
        HKEY root = nullptr;
        HKEY inProc = nullptr;
        DWORD disposition = 0;
        LSTATUS result = RegCreateKeyExW(
            HKEY_LOCAL_MACHINE, rootPath.c_str(), 0, nullptr, REG_OPTION_NON_VOLATILE,
            KEY_SET_VALUE | KEY_CREATE_SUB_KEY, nullptr, &root, &disposition);
        if (result == ERROR_SUCCESS && disposition == REG_OPENED_EXISTING_KEY)
        {
            RegCloseKey(root);
            return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);
        }
        if (result == ERROR_SUCCESS)
        {
            result = RegSetValueExW(
                root, L"OwnerSid", 0, REG_SZ, reinterpret_cast<const BYTE*>(ownerSid.c_str()),
                static_cast<DWORD>((ownerSid.size() + 1) * sizeof(wchar_t)));
        }
        if (result == ERROR_SUCCESS)
        {
            result = RegCreateKeyExW(
                root, L"InProcServer32", 0, nullptr, REG_OPTION_NON_VOLATILE,
                KEY_SET_VALUE, nullptr, &inProc, nullptr);
        }
        const std::wstring pathValue = sourcePath.wstring();
        if (result == ERROR_SUCCESS)
        {
            result = RegSetValueExW(
                inProc, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(pathValue.c_str()),
                static_cast<DWORD>((pathValue.size() + 1) * sizeof(wchar_t)));
        }
        if (result == ERROR_SUCCESS)
        {
            constexpr wchar_t threadingModel[] = L"Both";
            result = RegSetValueExW(
                inProc, L"ThreadingModel", 0, REG_SZ,
                reinterpret_cast<const BYTE*>(threadingModel), sizeof(threadingModel));
        }
        if (inProc != nullptr) RegCloseKey(inProc);
        if (root != nullptr) RegCloseKey(root);
        if (result == ERROR_SUCCESS)
        {
            if (registrationCompleteOrAbsent != nullptr) *registrationCompleteOrAbsent = true;
            return S_OK;
        }

        const HRESULT cleanup = UnregisterComSource();
        if (FAILED(cleanup))
        {
            g_error << L"register-rollback-error=" << HResultText(cleanup) << std::endl;
            return cleanup;
        }
        if (registrationCompleteOrAbsent != nullptr) *registrationCompleteOrAbsent = true;
        return HRESULT_FROM_WIN32(result);
    }

    HRESULT RestoreComSource(const std::filesystem::path& sourcePath, const std::wstring& ownerSid)
    {
        HRESULT result = UnregisterComSource();
        if (SUCCEEDED(result)) result = RegisterComSource(sourcePath, ownerSid);
        if (SUCCEEDED(result)) result = VerifyComSource(sourcePath, ownerSid);
        return result;
    }

    HRESULT CreateCamera(ComPtr<IMFVirtualCamera>& camera, bool* safeToRollbackSystemFiles = nullptr)
    {
        if (safeToRollbackSystemFiles != nullptr) *safeToRollbackSystemFiles = true;
        HRESULT result = MFCreateVirtualCamera(
            MFVirtualCameraType_SoftwareCameraSource,
            MFVirtualCameraLifetime_System,
            MFVirtualCameraAccess_CurrentUser,
            FriendlyName,
            SourceClsid,
            nullptr,
            0,
            &camera);
        if (FAILED(result))
        {
            return result;
        }
        result = camera->Start(nullptr);
        if (SUCCEEDED(result)) return result;

        const HRESULT stopResult = camera->Stop();
        const HRESULT removeResult = camera->Remove();
        const HRESULT shutdownResult = camera->Shutdown();
        const bool shutdownComplete = SUCCEEDED(shutdownResult) || shutdownResult == MF_E_SHUTDOWN;
        if (FAILED(stopResult) && stopResult != MF_E_SHUTDOWN)
            g_error << L"camera-start-cleanup-stop-error=" << HResultText(stopResult) << std::endl;
        if (FAILED(removeResult))
            g_error << L"camera-start-cleanup-remove-error=" << HResultText(removeResult) << std::endl;
        if (!shutdownComplete)
            g_error << L"camera-start-cleanup-shutdown-error=" << HResultText(shutdownResult) << std::endl;
        if (safeToRollbackSystemFiles != nullptr)
            *safeToRollbackSystemFiles = SUCCEEDED(removeResult) && shutdownComplete;
        camera.Reset();
        return result;
    }

    HRESULT RemoveCamera()
    {
        ComPtr<IMFVirtualCamera> camera;
        HRESULT result = MFCreateVirtualCamera(
            MFVirtualCameraType_SoftwareCameraSource,
            MFVirtualCameraLifetime_System,
            MFVirtualCameraAccess_CurrentUser,
            FriendlyName,
            SourceClsid,
            nullptr,
            0,
            &camera);
        if (FAILED(result))
        {
            return result;
        }
        const HRESULT stopResult = camera->Stop();
        if (FAILED(stopResult) && stopResult != MF_E_SHUTDOWN)
        {
            const HRESULT shutdownResult = camera->Shutdown();
            if (FAILED(shutdownResult) && shutdownResult != MF_E_SHUTDOWN)
                g_error << L"camera-remove-stop-cleanup-error=" << HResultText(shutdownResult) << std::endl;
            return stopResult;
        }
        result = camera->Remove();
        HRESULT shutdownResult = camera->Shutdown();
        if (FAILED(result)) return result;
        if (ShouldInjectFault(L"remove-after-camera-remove")) shutdownResult = E_FAIL;
        if (FAILED(shutdownResult) && shutdownResult != MF_E_SHUTDOWN)
            g_error << L"camera-remove-shutdown-warning=" << HResultText(shutdownResult) << std::endl;
        return S_OK;
    }

    HRESULT FindCamera(ComPtr<IMFActivate>& found, std::wstring& friendlyName)
    {
        ComPtr<IMFAttributes> attributes;
        HRESULT result = MFCreateAttributes(&attributes, 2);
        if (SUCCEEDED(result)) result = attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
        if (SUCCEEDED(result)) result = attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_CATEGORY, KSCATEGORY_VIDEO_CAMERA);
        if (FAILED(result)) return result;

        IMFActivate** devices = nullptr;
        UINT32 count = 0;
        result = MFEnumDeviceSources(attributes.Get(), &devices, &count);
        if (FAILED(result)) return result;

        for (UINT32 index = 0; index < count; ++index)
        {
            wchar_t* value = nullptr;
            UINT32 valueLength = 0;
            const HRESULT nameResult = devices[index]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, &value, &valueLength);
            if (SUCCEEDED(nameResult) && value != nullptr)
            {
                const std::wstring name(value, valueLength);
                if (name.rfind(FriendlyName, 0) == 0)
                {
                    found = devices[index];
                    friendlyName = name;
                }
            }
            CoTaskMemFree(value);
            devices[index]->Release();
        }
        CoTaskMemFree(devices);
        return found ? S_OK : HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    }

    HRESULT ProbeCamera()
    {
        ComPtr<IMFActivate> activate;
        std::wstring name;
        HRESULT result = FindCamera(activate, name);
        if (FAILED(result)) return result;

        ComPtr<IMFMediaSource> source;
        result = activate->ActivateObject(IID_PPV_ARGS(&source));
        if (FAILED(result)) return result;

        ComPtr<IMFAttributes> readerAttributes;
        result = MFCreateAttributes(&readerAttributes, 1);
        if (SUCCEEDED(result)) result = readerAttributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, FALSE);
        if (FAILED(result)) return result;

        ComPtr<IMFSourceReader> reader;
        result = MFCreateSourceReaderFromMediaSource(source.Get(), readerAttributes.Get(), &reader);
        if (FAILED(result)) return result;

        ComPtr<IMFMediaType> requestedType;
        result = MFCreateMediaType(&requestedType);
        if (SUCCEEDED(result)) result = requestedType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        if (SUCCEEDED(result)) result = requestedType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
        if (SUCCEEDED(result)) result = MFSetAttributeSize(requestedType.Get(), MF_MT_FRAME_SIZE, FrameWidth, FrameHeight);
        if (SUCCEEDED(result)) result = MFSetAttributeRatio(requestedType.Get(), MF_MT_FRAME_RATE, 30, 1);
        if (SUCCEEDED(result)) result = reader->SetCurrentMediaType(FirstVideoStream, nullptr, requestedType.Get());
        if (FAILED(result)) return result;

        for (int attempt = 0; attempt < 30; ++attempt)
        {
            DWORD streamIndex = 0;
            DWORD flags = 0;
            LONGLONG timestamp = 0;
            ComPtr<IMFSample> sample;
            result = reader->ReadSample(FirstVideoStream, 0, &streamIndex, &flags, &timestamp, &sample);
            if (FAILED(result)) return result;
            if ((flags & MF_SOURCE_READERF_ERROR) != 0) return E_FAIL;
            if (!sample) continue;

            ComPtr<IMFMediaBuffer> buffer;
            result = sample->ConvertToContiguousBuffer(&buffer);
            if (FAILED(result)) return result;
            DWORD length = 0;
            result = buffer->GetCurrentLength(&length);
            if (FAILED(result)) return result;
            if (length != ExpectedFrameBytes) return MF_E_INVALIDMEDIATYPE;

            g_output << L"{\"probe\":\"ok\",\"name\":\"" << name
                       << L"\",\"width\":1920,\"height\":1080,\"format\":\"NV12\",\"bytes\":"
                       << length << L",\"timestamp\":" << timestamp << L"}" << std::endl;
            source->Shutdown();
            activate->ShutdownObject();
            return S_OK;
        }
        source->Shutdown();
        return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    }

    HRESULT InstallSystemFilesElevated(const std::wstring& ownerSid)
    {
        if (!IsAdministrator()) return E_ACCESSDENIED;
        const HRESULT sidResult = ValidateOwnerSid(ownerSid);
        if (FAILED(sidResult)) return sidResult;

        std::filesystem::path installDirectory;
        HRESULT result = ProgramFilesInstallDirectory(installDirectory);
        if (FAILED(result)) return result;
        const std::filesystem::path installedDll = installDirectory / SourceDllName;
        const std::filesystem::path installedHelper = installDirectory / SetupHelperName;
        const std::filesystem::path stagedDll = installDirectory / L"VirtualCameraMediaSource.removing";

        result = RejectReparsePointIfPresent(installDirectory);
        if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(installedDll);
        if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(installedHelper);
        if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(stagedDll);
        if (FAILED(result)) return result;

        bool registrationExists = false;
        result = RegistrationExists(registrationExists);
        if (FAILED(result)) return result;
        if (registrationExists || std::filesystem::exists(installedDll) ||
            std::filesystem::exists(installedHelper) || std::filesystem::exists(stagedDll))
            return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);

        std::error_code error;
        std::filesystem::create_directories(installDirectory, error);
        if (error) return HRESULT_FROM_WIN32(error.value());
        result = RejectReparsePointIfPresent(installDirectory);
        if (FAILED(result)) return result;
        if (!CopyFileW(CurrentExecutable().c_str(), installedHelper.c_str(), TRUE))
        {
            RemoveDirectoryW(installDirectory.c_str());
            return HRESULT_FROM_WIN32(GetLastError());
        }
        bool helperMatches = false;
        result = FilesEqual(CurrentExecutable(), installedHelper, helperMatches);
        if (FAILED(result) || !helperMatches)
        {
            DeleteFileW(installedHelper.c_str());
            RemoveDirectoryW(installDirectory.c_str());
            return FAILED(result) ? result : HRESULT_FROM_WIN32(ERROR_FILE_CORRUPT);
        }
        result = WritePackagedSource(installedDll);
        if (FAILED(result))
        {
            DeleteFileW(installedHelper.c_str());
            RemoveDirectoryW(installDirectory.c_str());
            return result;
        }
        g_output << L"state=files-copied" << std::endl;

        bool registrationCompleteOrAbsent = false;
        result = RegisterComSource(installedDll, ownerSid, &registrationCompleteOrAbsent);
        if (FAILED(result))
        {
            if (!registrationCompleteOrAbsent)
            {
                g_error << L"Installation retained the DLL because registry rollback was incomplete." << std::endl;
                return result;
            }
            HRESULT rollback = S_OK;
            if (!DeleteFileW(installedDll.c_str())) rollback = HRESULT_FROM_WIN32(GetLastError());
            if (!DeleteFileW(installedHelper.c_str()) && SUCCEEDED(rollback)) rollback = HRESULT_FROM_WIN32(GetLastError());
            if (!RemoveDirectoryW(installDirectory.c_str()) && GetLastError() != ERROR_PATH_NOT_FOUND && SUCCEEDED(rollback))
                rollback = HRESULT_FROM_WIN32(GetLastError());
            if (FAILED(rollback))
            {
                g_error << L"install-rollback-error=" << HResultText(rollback) << std::endl;
                return rollback;
            }
            return result;
        }
        g_output << L"state=com-registered" << std::endl;

        return S_OK;
    }

    HRESULT RemoveSystemFilesElevated(const std::wstring& ownerSid)
    {
        if (!IsAdministrator()) return E_ACCESSDENIED;
        const HRESULT sidResult = ValidateOwnerSid(ownerSid);
        if (FAILED(sidResult)) return sidResult;
        const HRESULT ownerResult = VerifyRegisteredOwner(ownerSid);
        if (FAILED(ownerResult)) return ownerResult;

        std::filesystem::path installDirectory;
        HRESULT result = ProgramFilesInstallDirectory(installDirectory);
        if (FAILED(result)) return result;
        const std::filesystem::path installedDll = installDirectory / SourceDllName;
        const std::filesystem::path installedHelper = installDirectory / SetupHelperName;
        const std::filesystem::path stagedDll = installDirectory / L"VirtualCameraMediaSource.removing";

        result = RejectReparsePointIfPresent(installDirectory);
        if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(installedDll);
        if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(installedHelper);
        if (SUCCEEDED(result)) result = RejectReparsePointIfPresent(stagedDll);
        if (FAILED(result)) return result;

        bool hasInstalledDll = std::filesystem::exists(installedDll);
        bool hasStagedDll = std::filesystem::exists(stagedDll);
        if (hasInstalledDll && hasStagedDll)
        {
            g_error << L"Removal found both installed and staged DLLs; no state was changed." << std::endl;
            return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);
        }
        if (!hasInstalledDll && hasStagedDll)
        {
            const HRESULT recoverResult = MoveReleasedFile(stagedDll, installedDll);
            if (FAILED(recoverResult)) return recoverResult;
            hasInstalledDll = true;
            hasStagedDll = false;
            g_output << L"state=staged-file-recovered" << std::endl;
        }
        if (hasInstalledDll)
        {
            result = StopCameraServicesForRemoval();
            if (FAILED(result)) return result;
            const HRESULT moveResult = MoveReleasedFile(installedDll, stagedDll);
            if (FAILED(moveResult))
            {
                g_error << L"Removal kept the complete recoverable installation because the DLL is in use. move="
                           << HResultText(moveResult) << std::endl;
                return moveResult;
            }
            if (ShouldInjectFault(L"remove-after-stage"))
            {
                g_error << L"fault-injected=remove-after-stage" << std::endl;
                return HRESULT_FROM_WIN32(ERROR_CANCELLED);
            }
        }

        result = UnregisterComSource();
        if (FAILED(result))
        {
            HRESULT rollback = S_OK;
            if (hasInstalledDll && !MoveFileExW(stagedDll.c_str(), installedDll.c_str(), MOVEFILE_WRITE_THROUGH))
                rollback = HRESULT_FROM_WIN32(GetLastError());
            if (SUCCEEDED(rollback) && hasInstalledDll)
                rollback = RestoreComSource(installedDll, ownerSid);
            g_error << L"unregister-error=" << HResultText(result) << std::endl;
            if (FAILED(rollback))
            {
                g_error << L"remove-rollback-error=" << HResultText(rollback) << std::endl;
                return rollback;
            }
            return result;
        }
        g_output << L"state=com-unregistered" << std::endl;

        const HRESULT deleteResult = hasInstalledDll ? DeleteReleasedFile(stagedDll) : S_OK;
        if (FAILED(deleteResult))
        {
            HRESULT rollback = S_OK;
            if (!MoveFileExW(stagedDll.c_str(), installedDll.c_str(), MOVEFILE_WRITE_THROUGH))
            {
                rollback = HRESULT_FROM_WIN32(GetLastError());
            }
            else
            {
                rollback = RegisterComSource(installedDll, ownerSid);
            }
            g_error << L"Removal kept the complete recoverable installation because the staged DLL could not be deleted. delete="
                       << HResultText(deleteResult) << L" rollback=" << HResultText(rollback) << std::endl;
            return FAILED(rollback) ? rollback : deleteResult;
        }
        bool helperRemovalDeferred = false;
        if (std::filesystem::exists(installedHelper))
        {
            const HRESULT helperDeleteResult = DeleteReleasedFile(installedHelper);
            if (FAILED(helperDeleteResult))
            {
                if (!MoveFileExW(installedHelper.c_str(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT))
                    return helperDeleteResult;
                helperRemovalDeferred = true;
                g_output << L"state=helper-removal-deferred" << std::endl;
            }
        }
        if (!RemoveDirectoryW(installDirectory.c_str()))
        {
            const DWORD removeError = GetLastError();
            if (helperRemovalDeferred)
                MoveFileExW(installDirectory.c_str(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT);
            if (removeError != ERROR_FILE_NOT_FOUND && removeError != ERROR_PATH_NOT_FOUND)
                g_error << L"directory-cleanup-warning=" << HResultText(HRESULT_FROM_WIN32(removeError)) << std::endl;
        }
        if (std::filesystem::exists(installedDll) || std::filesystem::exists(stagedDll))
            return HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
        g_output << L"state=files-removed" << std::endl;
        return S_OK;
    }

    HRESULT TestUnregisterRollbackElevated(const std::wstring& ownerSid)
    {
        if (!IsAdministrator()) return E_ACCESSDENIED;
        HRESULT result = ValidateOwnerSid(ownerSid);
        if (FAILED(result)) return result;

        std::filesystem::path installDirectory;
        result = ProgramFilesInstallDirectory(installDirectory);
        if (FAILED(result)) return result;
        const std::filesystem::path installedDll = installDirectory / SourceDllName;
        if (!std::filesystem::exists(installedDll)) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        result = VerifyComSource(installedDll, ownerSid);
        if (FAILED(result)) return result;

        if (!SetEnvironmentVariableW(L"VOLTURA_WEBCAM_FAULT", L"unregister-after-delete"))
            return HRESULT_FROM_WIN32(GetLastError());
        const HRESULT injectedResult = UnregisterComSource();
        SetEnvironmentVariableW(L"VOLTURA_WEBCAM_FAULT", nullptr);
        const HRESULT rollbackResult = RestoreComSource(installedDll, ownerSid);
        if (FAILED(rollbackResult))
        {
            g_error << L"unregister-test-rollback-error=" << HResultText(rollbackResult) << std::endl;
            return rollbackResult;
        }
        if (SUCCEEDED(injectedResult)) return HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
        result = VerifyComSource(installedDll, ownerSid);
        if (SUCCEEDED(result)) g_output << L"state=unregister-rollback-verified" << std::endl;
        return result;
    }

    int Finish(const HRESULT result)
    {
        if (FAILED(result))
        {
            g_error << L"error=" << HResultText(result) << std::endl;
            return static_cast<int>(HRESULT_CODE(result) == 0 ? ERROR_GEN_FAILURE : HRESULT_CODE(result));
        }
        return 0;
    }
}

int wmain(const int argumentCount, wchar_t* arguments[])
{
    if (argumentCount < 2)
    {
        g_error << L"Usage: VolturaAirWebcamSetup <install|remove|status|cleanup-required|probe>" << std::endl;
        return ERROR_INVALID_PARAMETER;
    }

    const std::wstring command = arguments[1];
    if (command == L"install")
    {
        std::wstring ownerSid;
        HRESULT result = CurrentUserSid(ownerSid);
        if (FAILED(result)) return Finish(result);
        result = RunElevatedAndWait(L"--elevated-install \"" + ownerSid + L"\"");
        if (FAILED(result)) return Finish(result);

        MediaFoundationScope mediaFoundation;
        result = mediaFoundation.Result();
        ComPtr<IMFVirtualCamera> camera;
        bool safeToRollbackSystemFiles = true;
        if (SUCCEEDED(result)) result = CreateCamera(camera, &safeToRollbackSystemFiles);
        if (FAILED(result))
        {
            if (safeToRollbackSystemFiles)
            {
                const HRESULT rollbackResult = RunElevatedAndWait(L"--elevated-remove \"" + ownerSid + L"\"");
                if (FAILED(rollbackResult))
                    g_error << L"install-rollback-error=" << HResultText(rollbackResult) << std::endl;
            }
            else
                g_error << L"Installation retained system files because failed camera-start cleanup was incomplete." << std::endl;
            return Finish(result);
        }
        g_output << L"state=camera-created" << std::endl;
        camera->Shutdown();
        return 0;
    }
    if (command == L"remove")
    {
        std::wstring ownerSid;
        HRESULT result = CurrentUserSid(ownerSid);
        if (FAILED(result)) return Finish(result);
        MediaFoundationScope mediaFoundation;
        result = mediaFoundation.Result();
        ComPtr<IMFActivate> existingCamera;
        std::wstring existingName;
        if (SUCCEEDED(result)) result = FindCamera(existingCamera, existingName);
        if (result == HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
        {
            result = S_OK;
            g_output << L"state=camera-absent" << std::endl;
        }
        else if (SUCCEEDED(result))
        {
            result = RemoveCamera();
        }
        if (FAILED(result)) return Finish(result);
        if (existingCamera) g_output << L"state=camera-removed" << std::endl;

        result = RunElevatedAndWait(L"--elevated-remove \"" + ownerSid + L"\"");
        if (FAILED(result))
        {
            ComPtr<IMFVirtualCamera> camera;
            const HRESULT rollbackResult = CreateCamera(camera);
            if (camera) camera->Shutdown();
            if (SUCCEEDED(rollbackResult))
                g_error << L"state=remove-rolled-back" << std::endl;
            else
                g_error << L"remove-rollback-error=" << HResultText(rollbackResult) << std::endl;
        }
        return Finish(result);
    }
    if (command == L"--elevated-install" && argumentCount == 3)
    {
        ElevatedOperationTimeoutScope timeout;
        if (FAILED(timeout.Result())) return Finish(timeout.Result());
        return Finish(InstallSystemFilesElevated(arguments[2]));
    }
    if (command == L"--elevated-remove" && argumentCount == 3)
    {
        ElevatedOperationTimeoutScope timeout;
        if (FAILED(timeout.Result())) return Finish(timeout.Result());
        return Finish(RemoveSystemFilesElevated(arguments[2]));
    }
    if (command == L"test-unregister-rollback")
    {
        g_allowFaultInjection = true;
        std::wstring ownerSid;
        HRESULT result = CurrentUserSid(ownerSid);
        if (FAILED(result)) return Finish(result);
        return Finish(RunElevatedAndWait(L"--elevated-test-unregister-rollback \"" + ownerSid + L"\""));
    }
    if (command == L"--elevated-test-unregister-rollback" && argumentCount == 3)
    {
        g_allowFaultInjection = true;
        ElevatedOperationTimeoutScope timeout;
        if (FAILED(timeout.Result())) return Finish(timeout.Result());
        return Finish(TestUnregisterRollbackElevated(arguments[2]));
    }
    if (command == L"verify-packaged-source" && argumentCount == 3)
    {
        bool matches = false;
        const HRESULT result = FileMatchesPackagedSource(arguments[2], matches);
        return Finish(SUCCEEDED(result) && !matches ? HRESULT_FROM_WIN32(ERROR_FILE_CORRUPT) : result);
    }

    MediaFoundationScope mediaFoundation;
    if (FAILED(mediaFoundation.Result())) return Finish(mediaFoundation.Result());
    if (command == L"status")
    {
        bool installed = false;
        bool cleanupRequired = false;
        bool updateRequired = false;
        std::wstring name;
        const HRESULT result = ReadInstallationState(installed, cleanupRequired, updateRequired, name);
        if (FAILED(result)) return Finish(result);
        g_output << L"{\"installed\":" << (installed ? L"true" : L"false")
                   << L",\"cleanupRequired\":" << (cleanupRequired ? L"true" : L"false")
                   << L",\"updateRequired\":" << (updateRequired ? L"true" : L"false");
        if (installed) g_output << L",\"name\":\"" << name << L"\"";
        g_output << L"}" << std::endl;
        return installed ? 0 : 1;
    }
    if (command == L"cleanup-required")
    {
        bool installed = false;
        bool cleanupRequired = false;
        bool updateRequired = false;
        std::wstring name;
        const HRESULT result = ReadInstallationState(installed, cleanupRequired, updateRequired, name);
        if (FAILED(result)) return Finish(result);
        g_output << L"{\"cleanupRequired\":" << (cleanupRequired ? L"true" : L"false") << L"}" << std::endl;
        return cleanupRequired ? 0 : 1;
    }
    if (command == L"probe")
    {
        return Finish(ProbeCamera());
    }

    g_error << L"Unknown command." << std::endl;
    return ERROR_INVALID_PARAMETER;
}
