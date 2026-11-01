#include "pch.h"
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstdlib>
#include <new>
#include <stdexcept>

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
    HANDLE read = nullptr;
    HANDLE write = nullptr;
    TestPipe() { Check(CreatePipe(&read, &write, nullptr, 0) != FALSE, "CreatePipe failed"); }
    ~TestPipe() { if (read) CloseHandle(read); if (write) CloseHandle(write); }
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
        if (closeWriter) { CloseHandle(pipe.write); pipe.write = nullptr; }
    });
    const bool accepted = generator.ReadFrame(pipe.read);
    writer.join();
    Check(written, "Pipe writer failed");
    return accepted;
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
            Check(generator.CopyLatestFrame(packed.data(), FrameBytes, FrameWidth), "Packed frame unavailable");
            Check(packed == payload, "Packed pixels changed");
            Check(generator.CopyLatestFrame(padded.data(), static_cast<DWORD>(padded.size()), paddedPitch), "Padded frame unavailable");
            for (DWORD row = 0; row < FrameHeight * 3 / 2; ++row)
            {
                const BYTE* pixels = padded.data() + static_cast<size_t>(row) * paddedPitch;
                Check(std::all_of(pixels, pixels + FrameWidth, [value](BYTE pixel) { return pixel == value; }), "Padded pixels changed");
                Check(std::all_of(pixels + FrameWidth, pixels + paddedPitch, [](BYTE pixel) { return pixel == 0xee; }), "Stride padding overwritten");
            }
        }
        Check(frameAllocations == 2, "Frame buffers were allocated again during steady streaming");
        Check(!generator.CopyLatestFrame(packed.data(), FrameBytes - 1, FrameWidth), "Truncated consumer buffer accepted");
        Check(SendRecord(generator, pipe, Header(101, true), payload, 0), "Clear record rejected");
        Check(!generator.CopyLatestFrame(packed.data(), FrameBytes, FrameWidth), "Cleared frame remained visible");
        Check(SendRecord(generator, pipe, Header(101), payload, FrameBytes), "Frame after clear rejected");
        auto invalid = Header(102);
        invalid[24] = 0;
        Check(!SendRecord(generator, pipe, invalid, payload, 0), "Invalid geometry accepted");
        Check(!SendRecord(generator, pipe, Header(102), payload, FrameBytes / 2, true), "Truncated frame accepted");
        Check(generator.CopyLatestFrame(packed.data(), FrameBytes, FrameWidth), "Interrupted read lost the last good frame");
        Check(packed == payload, "Interrupted read changed published pixels");
        Check(frameAllocations == 2, "Recovery allocated additional frame storage");
        std::puts("100 frames: two reusable native buffers; packed/strided pixels, clear, malformed geometry and interrupted-read ownership passed.");
        return 0;
    }
    catch (const std::exception& error)
    {
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}
