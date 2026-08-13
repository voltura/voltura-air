// Adapted from Microsoft's Windows Camera virtual-camera sample.
// Copyright (C) Microsoft Corporation. All rights reserved.

#include "pch.h"

namespace
{
    constexpr wchar_t PipePath[] = L"\\\\.\\pipe\\VolturaAirWebcam-v1";
    constexpr wchar_t OwnerKey[] = L"SOFTWARE\\Classes\\CLSID\\{50AAB70E-38BA-403E-A55B-58F2BCABE4FB}";
    constexpr DWORD ProtocolVersion = 1;
    constexpr DWORD Nv12Format = 1;
    constexpr DWORD HeaderBytes = 40;
    constexpr DWORD FrameWidth = 1920;
    constexpr DWORD FrameHeight = 1080;
    constexpr DWORD FrameBytes = FrameWidth * FrameHeight * 3 / 2;

    DWORD ReadUInt32(const BYTE* value)
    {
        return static_cast<DWORD>(value[0]) |
            (static_cast<DWORD>(value[1]) << 8) |
            (static_cast<DWORD>(value[2]) << 16) |
            (static_cast<DWORD>(value[3]) << 24);
    }

    uint64_t ReadUInt64(const BYTE* value)
    {
        return static_cast<uint64_t>(ReadUInt32(value)) |
            (static_cast<uint64_t>(ReadUInt32(value + 4)) << 32);
    }
}

SimpleFrameGenerator::SimpleFrameGenerator()
{
    m_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
}

SimpleFrameGenerator::~SimpleFrameGenerator()
{
    m_stop = true;
    if (m_stopEvent != nullptr) SetEvent(m_stopEvent);
    if (m_reader.joinable())
    {
        CancelSynchronousIo(m_reader.native_handle());
        m_reader.join();
    }
    if (m_stopEvent != nullptr) CloseHandle(m_stopEvent);
}

HRESULT SimpleFrameGenerator::Initialize(_In_ IMFMediaType* mediaType)
{
    RETURN_HR_IF_NULL(E_INVALIDARG, mediaType);
    RETURN_IF_FAILED(mediaType->GetGUID(MF_MT_SUBTYPE, &m_subType));
    RETURN_HR_IF(MF_E_UNSUPPORTED_FORMAT, m_subType != MFVideoFormat_NV12);
    RETURN_IF_FAILED(MFGetAttributeSize(mediaType, MF_MT_FRAME_SIZE, &m_width, &m_height));
    RETURN_HR_IF(MF_E_INVALIDMEDIATYPE, m_width != FrameWidth || m_height != FrameHeight);
    if (!m_reader.joinable()) m_reader = std::thread([this]() { RunPipeReader(); });
    return S_OK;
}

HRESULT SimpleFrameGenerator::CreateFrame(
    _Inout_updates_bytes_(length) BYTE* buffer,
    _In_ DWORD length,
    _In_ LONG pitch,
    _In_ ULONG rgbMask)
{
    (void)rgbMask;
    RETURN_HR_IF_NULL(E_INVALIDARG, buffer);
    RETURN_HR_IF(E_INVALIDARG, pitch < static_cast<LONG>(m_width));
    if (!CopyLatestFrame(buffer, length, pitch)) FillWaitingFrame(buffer, length, pitch);
    return S_OK;
}

void SimpleFrameGenerator::RunPipeReader()
{
    while (!m_stop)
    {
        HANDLE pipe = CreateFileW(PipePath, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            WaitForSingleObject(m_stopEvent, 250);
            continue;
        }
        if (VerifyServer(pipe))
        {
            {
                std::lock_guard guard(m_frameLock);
                m_latestFrame.clear();
                m_latestSequence = 0;
                m_latestArrival = 0;
            }
            const BYTE handshake[8] = { 'V', 'A', 'W', 'H', 1, 0, 0, 0 };
            DWORD written = 0;
            if (WriteFile(pipe, handshake, sizeof(handshake), &written, nullptr) && written == sizeof(handshake))
            {
                while (!m_stop && ReadFrame(pipe)) { }
            }
        }
        CloseHandle(pipe);
    }
}

bool SimpleFrameGenerator::ReadFrame(HANDLE pipe)
{
    BYTE header[HeaderBytes]{};
    if (!ReadExact(pipe, header, HeaderBytes)) return false;
    if (memcmp(header, "VAWF", 4) != 0 || ReadUInt32(header + 4) != ProtocolVersion ||
        ReadUInt32(header + 24) != FrameWidth || ReadUInt32(header + 28) != FrameHeight ||
        ReadUInt32(header + 32) != Nv12Format || ReadUInt32(header + 36) != FrameBytes)
    {
        return false;
    }
    const uint64_t sequence = ReadUInt64(header + 8);
    if (sequence == 0 || sequence <= m_latestSequence) return false;
    std::vector<BYTE> frame(FrameBytes);
    if (!ReadExact(pipe, frame.data(), FrameBytes)) return false;
    {
        std::lock_guard guard(m_frameLock);
        m_latestFrame.swap(frame);
        m_latestSequence = sequence;
        m_latestArrival = GetTickCount64();
    }
    return true;
}

bool SimpleFrameGenerator::VerifyServer(HANDLE pipe) const
{
    DWORD serverProcessId = 0;
    if (!GetNamedPipeServerProcessId(pipe, &serverProcessId)) return false;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, serverProcessId);
    if (process == nullptr) return false;
    HANDLE token = nullptr;
    if (!OpenProcessToken(process, TOKEN_QUERY, &token))
    {
        CloseHandle(process);
        return false;
    }
    DWORD tokenBytes = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &tokenBytes);
    std::vector<BYTE> tokenBuffer(tokenBytes);
    const bool readToken = tokenBytes != 0 && GetTokenInformation(token, TokenUser, tokenBuffer.data(), tokenBytes, &tokenBytes);
    CloseHandle(token);
    CloseHandle(process);
    if (!readToken) return false;

    wchar_t ownerSid[184]{};
    DWORD ownerBytes = sizeof(ownerSid);
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, OwnerKey, 0, KEY_QUERY_VALUE, &key) != ERROR_SUCCESS) return false;
    DWORD type = 0;
    const LSTATUS readOwner = RegQueryValueExW(key, L"OwnerSid", nullptr, &type, reinterpret_cast<BYTE*>(ownerSid), &ownerBytes);
    RegCloseKey(key);
    if (readOwner != ERROR_SUCCESS || type != REG_SZ) return false;
    PSID expectedSid = nullptr;
    if (!ConvertStringSidToSidW(ownerSid, &expectedSid)) return false;
    const bool matches = EqualSid(reinterpret_cast<TOKEN_USER*>(tokenBuffer.data())->User.Sid, expectedSid) != FALSE;
    LocalFree(expectedSid);
    return matches;
}

bool SimpleFrameGenerator::ReadExact(HANDLE pipe, BYTE* destination, DWORD length) const
{
    DWORD offset = 0;
    while (offset < length && !m_stop)
    {
        DWORD read = 0;
        if (!ReadFile(pipe, destination + offset, length - offset, &read, nullptr) || read == 0) return false;
        offset += read;
    }
    return offset == length;
}

bool SimpleFrameGenerator::CopyLatestFrame(BYTE* buffer, DWORD length, LONG pitch)
{
    std::lock_guard guard(m_frameLock);
    if (m_latestFrame.size() != FrameBytes || GetTickCount64() - m_latestArrival > 500) return false;
    const DWORD required = static_cast<DWORD>(pitch) * FrameHeight * 3 / 2;
    if (length < required) return false;
    for (DWORD row = 0; row < FrameHeight; ++row)
        memcpy(buffer + row * pitch, m_latestFrame.data() + row * FrameWidth, FrameWidth);
    BYTE* destinationUv = buffer + static_cast<size_t>(pitch) * FrameHeight;
    const BYTE* sourceUv = m_latestFrame.data() + FrameWidth * FrameHeight;
    for (DWORD row = 0; row < FrameHeight / 2; ++row)
        memcpy(destinationUv + row * pitch, sourceUv + row * FrameWidth, FrameWidth);
    return true;
}

void SimpleFrameGenerator::FillWaitingFrame(BYTE* buffer, DWORD length, LONG pitch) const
{
    const DWORD required = static_cast<DWORD>(pitch) * FrameHeight * 3 / 2;
    if (length < required) return;
    for (DWORD row = 0; row < FrameHeight; ++row)
    {
        BYTE* y = buffer + row * pitch;
        memset(y, 32, FrameWidth);
        if (row >= 500 && row < 580) memset(y + 560, 180, 800);
    }
    BYTE* uv = buffer + static_cast<size_t>(pitch) * FrameHeight;
    for (DWORD row = 0; row < FrameHeight / 2; ++row) memset(uv + row * pitch, 128, FrameWidth);
}
