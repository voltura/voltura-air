#include "pch.h"
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstdlib>
#include <new>
#include <stdexcept>
#include <string>
#include <wrl/client.h>

static std::atomic<int> frameAllocations = 0;
void* operator new(std::size_t size)
{
    constexpr std::size_t frameBytes = 1920 * 1080 * 3 / 2;
    // MSVC adds alignment space to large vector allocations.
    if (size >= frameBytes && size <= frameBytes + 64) ++frameAllocations;
    if (void* result = std::malloc(size)) return result;
    throw std::bad_alloc();
}
void operator delete(void* value) noexcept { std::free(value); }
void operator delete(void* value, std::size_t) noexcept { std::free(value); }

#include "SimpleFrameGenerator.cpp"

static void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct TestPipe
{
    HANDLE read = INVALID_HANDLE_VALUE;
    HANDLE write = INVALID_HANDLE_VALUE;
    TestPipe()
    {
        static unsigned int nextPipeId = 0;
        const std::wstring name = L"\\\\.\\pipe\\VolturaAirWebcamFrameTest-" +
            std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(++nextPipeId);
        read = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
        Check(read != INVALID_HANDLE_VALUE, "CreateNamedPipeW failed");
        write = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (write == INVALID_HANDLE_VALUE)
        {
            CloseHandle(read);
            throw std::runtime_error("Test pipe connection failed");
        }
        OVERLAPPED connection{};
        if (!ConnectNamedPipe(read, &connection) && GetLastError() != ERROR_PIPE_CONNECTED)
        {
            CloseWriter();
            CloseHandle(read);
            throw std::runtime_error("Test pipe was not connected");
        }
    }
    void CloseWriter()
    {
        if (write != INVALID_HANDLE_VALUE) CloseHandle(write);
        write = INVALID_HANDLE_VALUE;
    }
    ~TestPipe() { CloseWriter(); CloseHandle(read); }
};

static bool WriteAll(HANDLE pipe, const BYTE* bytes, DWORD length)
{
    DWORD offset = 0;
    while (offset < length)
    {
        DWORD written = 0;
        if (!WriteFile(pipe, bytes + offset, length - offset, &written, nullptr) || written == 0) return false;
        offset += written;
    }
    return true;
}

static std::array<BYTE, 40> Header(uint64_t sequence, bool clear = false)
{
    std::array<BYTE, 40> header{};
    memcpy(header.data(), clear ? "VAWC" : "VAWF", 4);
    const DWORD version = 1, width = 1920, height = 1080, format = 1, bytes = FrameBytes;
    memcpy(header.data() + 4, &version, 4);
    if (clear) return header;
    memcpy(header.data() + 8, &sequence, 8);
    memcpy(header.data() + 24, &width, 4);
    memcpy(header.data() + 28, &height, 4);
    memcpy(header.data() + 32, &format, 4);
    memcpy(header.data() + 36, &bytes, 4);
    return header;
}

static bool SendRecord(SimpleFrameGenerator& generator, TestPipe& pipe,
    const std::array<BYTE, 40>& header, const std::vector<BYTE>& payload, DWORD bytes, bool closeWriter = false)
{
    bool written = false;
    std::thread writer([&]()
    {
        written = WriteAll(pipe.write, header.data(), static_cast<DWORD>(header.size())) &&
            WriteAll(pipe.write, payload.data(), bytes);
        if (closeWriter) pipe.CloseWriter();
    });
    const bool accepted = generator.ReadFrame(pipe.read);
    writer.join();
    Check(written, "Pipe writer failed");
    return accepted;
}

static void CheckPendingReadCancellation()
{
    TestPipe pipe;
    SimpleFrameGenerator generator;
    OVERLAPPED read{};
    read.hEvent = generator.m_ioEvent;
    BYTE data[40]{};
    DWORD transferred = 0;
    Check(!ReadFile(pipe.read, data, sizeof(data), nullptr, &read) && GetLastError() == ERROR_IO_PENDING,
        "Idle overlapped read was not pending");
    SetEvent(generator.m_stopEvent);
    SetLastError(ERROR_IO_PENDING);
    Check(!generator.CompleteIo(pipe.read, read, FALSE, transferred), "Stop did not cancel the pending read");
    Check(!GetOverlappedResult(pipe.read, &read, &transferred, FALSE) && GetLastError() == ERROR_OPERATION_ABORTED,
        "Pending read was not retired before returning");
}

static void CheckDisconnectClearsPublishedFrame()
{
    TestPipe pipe;
    SimpleFrameGenerator generator;
    generator.m_reader = std::thread([&]() { generator.ReadConnection(pipe.read); });
    BYTE handshake[8]{};
    DWORD received = 0;
    Check(ReadFile(pipe.write, handshake, sizeof(handshake), &received, nullptr) &&
        received == sizeof(handshake) && memcmp(handshake, "VAWH\x01\0\0\0", sizeof(handshake)) == 0,
        "Frame reader did not send the version 1 handshake");

    auto header = Header(1);
    std::vector<BYTE> frame(FrameBytes, 200);
    Check(WriteAll(pipe.write, header.data(), static_cast<DWORD>(header.size())) &&
        WriteAll(pipe.write, frame.data(), FrameBytes), "Frame pipe writer failed");
    const ULONGLONG deadline = GetTickCount64() + 2000;
    while (!generator.CopyLatestFrame(frame.data(), FrameWidth) && GetTickCount64() < deadline) Sleep(1);
    Check(generator.CopyLatestFrame(frame.data(), FrameWidth), "Connected frame was never published");

    pipe.CloseWriter();
    generator.m_reader.join();
    Check(!generator.CopyLatestFrame(frame.data(), FrameWidth), "Disconnected frame remained visible");
}

static void CheckMediaBufferUnlock()
{
    struct MediaFoundationScope
    {
        MediaFoundationScope() { Check(SUCCEEDED(MFStartup(MF_VERSION, MFSTARTUP_LITE)), "MFStartup failed"); }
        ~MediaFoundationScope() { MFShutdown(); }
    } mediaFoundation;

    Microsoft::WRL::ComPtr<IMFMediaBuffer> sampleBuffer;
    Check(SUCCEEDED(MFCreate2DMediaBuffer(320, 240, MFVideoFormat_NV12.Data1, FALSE, &sampleBuffer)),
        "Could not allocate a small NV12 test sample");
    Microsoft::WRL::ComPtr<IMF2DBuffer2> buffer;
    Check(SUCCEEDED(sampleBuffer.As(&buffer)), "Test sample lacks IMF2DBuffer2");
    SimpleFrameGenerator generator;
    Check(generator.WriteSample(*buffer.Get()) == E_INVALIDARG, "Wrong NV12 pitch was accepted");

    BYTE* scanline = nullptr;
    BYTE* start = nullptr;
    LONG pitch = 0;
    DWORD length = 0;
    Check(SUCCEEDED(buffer->Lock2DSize(MF2DBuffer_LockFlags_Read, &scanline, &pitch, &start, &length)),
        "The rejected sample remained write-locked");
    Check(SUCCEEDED(buffer->Unlock2D()), "Follow-up buffer unlock failed");

    sampleBuffer.Reset();
    Check(SUCCEEDED(MFCreate2DMediaBuffer(FrameWidth, FrameHeight, MFVideoFormat_NV12.Data1, FALSE, &sampleBuffer)),
        "Could not allocate a Full HD NV12 test sample");
    Check(SUCCEEDED(sampleBuffer.As(&buffer)), "Full HD sample lacks IMF2DBuffer2");
    Check(SUCCEEDED(generator.WriteSample(*buffer.Get())), "Full HD NV12 sample was rejected");
    Check(SUCCEEDED(buffer->Lock2DSize(MF2DBuffer_LockFlags_Read, &scanline, &pitch, &start, &length)),
        "Successful sample remained write-locked");
    const bool waitingFrameWritten = scanline[0] == 32;
    Check(SUCCEEDED(buffer->Unlock2D()), "Full HD buffer unlock failed");
    Check(waitingFrameWritten, "Waiting frame was not written to the NV12 sample");
}

int main()
{
    try
    {
        TestPipe pipe;
        SimpleFrameGenerator generator;
        std::vector<BYTE> payload(FrameBytes);
        std::vector<BYTE> packed(FrameBytes);
        constexpr LONG paddedPitch = FrameWidth + 16;
        std::vector<BYTE> padded(static_cast<size_t>(paddedPitch) * FrameHeight * 3 / 2, 0xee);
        frameAllocations = 0;
        for (uint64_t sequence = 1; sequence <= 100; ++sequence)
        {
            const BYTE value = static_cast<BYTE>(sequence);
            std::fill(payload.begin(), payload.end(), value);
            Check(SendRecord(generator, pipe, Header(sequence), payload, FrameBytes), "Valid frame rejected");
            Check(generator.CreateFrame(packed.data(), FrameBytes, FrameWidth) == S_OK, "Packed frame unavailable");
            Check(packed == payload, "Packed pixels changed");
            const DWORD bytesWithoutTrailingPadding = static_cast<DWORD>(padded.size()) - (paddedPitch - FrameWidth);
            Check(generator.CreateFrame(padded.data(), bytesWithoutTrailingPadding, paddedPitch) == S_OK, "Padded frame unavailable");
            for (DWORD row = 0; row < FrameHeight * 3 / 2; ++row)
            {
                const BYTE* pixels = padded.data() + static_cast<size_t>(row) * paddedPitch;
                Check(std::all_of(pixels, pixels + FrameWidth, [value](BYTE pixel) { return pixel == value; }), "Padded pixels changed");
                Check(std::all_of(pixels + FrameWidth, pixels + paddedPitch, [](BYTE pixel) { return pixel == 0xee; }), "Stride padding overwritten");
            }
        }
        Check(frameAllocations == 2, "Frame buffers were allocated again during steady streaming");
        Check(generator.CreateFrame(packed.data(), FrameBytes - 1, FrameWidth) == MF_E_BUFFERTOOSMALL, "Truncated consumer buffer accepted");
        Check(SendRecord(generator, pipe, Header(101, true), payload, 0), "Clear record rejected");
        Check(!generator.CopyLatestFrame(packed.data(), FrameWidth), "Cleared frame remained visible");
        Check(generator.CreateFrame(packed.data(), FrameBytes - 1, FrameWidth) == MF_E_BUFFERTOOSMALL, "Truncated waiting-frame buffer accepted");
        Check(SendRecord(generator, pipe, Header(101), payload, FrameBytes), "Frame after clear rejected");
        auto invalid = Header(102);
        invalid[24] = 0;
        Check(!SendRecord(generator, pipe, invalid, payload, 0), "Invalid geometry accepted");
        Check(!SendRecord(generator, pipe, Header(102), payload, FrameBytes / 2, true), "Truncated frame accepted");
        Check(generator.CopyLatestFrame(packed.data(), FrameWidth), "Interrupted read lost the last good frame");
        Check(packed == payload, "Interrupted read changed published pixels");
        Check(frameAllocations == 2, "Recovery allocated additional frame storage");
        CheckPendingReadCancellation();
        CheckDisconnectClearsPublishedFrame();
        CheckMediaBufferUnlock();
        std::puts("100 frames: two reusable native buffers; packed/strided pixels, clear, malformed geometry and interrupted-read ownership passed.");
        std::puts("Native pipe cancellation and disconnect cleanup passed.");
        std::puts("Media Foundation sample buffers unlocked on success and rejection.");
        return 0;
    }
    catch (const std::exception& error)
    {
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}
