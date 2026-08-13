// Adapted from Microsoft's Windows Camera virtual-camera sample.
// Copyright (C) Microsoft Corporation. All rights reserved.

#pragma once

class SimpleFrameGenerator
{
public:
    SimpleFrameGenerator();
    ~SimpleFrameGenerator();
    SimpleFrameGenerator(const SimpleFrameGenerator&) = delete;
    SimpleFrameGenerator& operator=(const SimpleFrameGenerator&) = delete;

    HRESULT Initialize(_In_ IMFMediaType* mediaType);
    HRESULT CreateFrame(_Inout_updates_bytes_(length) BYTE* buffer, _In_ DWORD length, _In_ LONG pitch, _In_ ULONG rgbMask);

private:
    void RunPipeReader();
    bool ReadFrame(HANDLE pipe);
    bool VerifyServer(HANDLE pipe) const;
    bool ReadExact(HANDLE pipe, BYTE* destination, DWORD length) const;
    void FillWaitingFrame(BYTE* buffer, DWORD length, LONG pitch) const;
    bool CopyLatestFrame(BYTE* buffer, DWORD length, LONG pitch);

    UINT32 m_width = 0;
    UINT32 m_height = 0;
    GUID m_subType = GUID_NULL;
    HANDLE m_stopEvent = nullptr;
    std::thread m_reader;
    std::atomic<bool> m_stop = false;
    std::mutex m_frameLock;
    std::vector<BYTE> m_latestFrame;
    ULONGLONG m_latestArrival = 0;
    uint64_t m_latestSequence = 0;
};
