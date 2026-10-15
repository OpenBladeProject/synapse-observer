#include "../src/ObserverShared.h"
#include <thread>
#include <memory>
#include <cstdio>
#include <cstdlib>
using namespace observer;
constexpr DWORD GetCode = CTL_CODE(FILE_DEVICE_KEYBOARD, 100, METHOD_OUT_DIRECT, FILE_ANY_ACCESS);
constexpr DWORD SetCode = CTL_CODE(FILE_DEVICE_KEYBOARD, 100, METHOD_IN_DIRECT, FILE_ANY_ACCESS);
HANDLE owned = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(0x1234));
int mode{};
DWORD seenIncoming{};
DWORD delayMilliseconds{};
DWORD returnedOverride{};
__declspec(noinline) BOOL WINAPI FakeIoctl(HANDLE, DWORD, LPVOID, DWORD, LPVOID output, DWORD size, LPDWORD bytes, LPOVERLAPPED) {
    seenIncoming = GetLastError();
    if (delayMilliseconds) Sleep(delayMilliseconds);
    if (mode == 2) { SetLastError(ERROR_IO_PENDING); return FALSE; }
    if (mode == 1) { SetLastError(ERROR_ACCESS_DENIED); return FALSE; }
    if (output && size) static_cast<unsigned char*>(output)[0] = 0xA5;
    if (bytes) *bytes = returnedOverride ? returnedOverride : size;
    SetLastError(123); return TRUE;
}
__declspec(noinline) BOOLEAN __stdcall FakeSet(HANDLE h, PVOID buffer, ULONG n) {
    DWORD bytes{}; return static_cast<BOOLEAN>(FakeIoctl(h, SetCode, buffer, n, nullptr, 0, &bytes, nullptr));
}
__declspec(noinline) BOOLEAN __stdcall FakeGet(HANDLE h, PVOID buffer, ULONG n) {
    DWORD bytes{}; return static_cast<BOOLEAN>(FakeIoctl(h, GetCode, buffer, n, buffer, n, &bytes, nullptr));
}
void Check(bool value, const char* message) { if (!value) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); } }
int main() {
    Check(StartOwnedFixture({reinterpret_cast<void*>(FakeSet), reinterpret_cast<void*>(FakeGet), reinterpret_cast<void*>(FakeIoctl)}, owned, 91), "install owned hooks");
    unsigned char buffer[12]{1,2,3}; Event e{};
    SetLastError(77);
    Check(FakeGet(owned, buffer, sizeof(buffer)) == TRUE, "original status");
    Check(GetLastError() == 123 && seenIncoming == 77, "preserve incoming and outgoing errors");
    Check(Poll(e) && e.operation == Operation::GetFeature && e.input[0] == 1 && e.output[0] == 0xA5 && e.outputLength == 12 && e.outputBufferCapacity == 12 && e.inputLength == 1 && e.input[1] == 0 && e.gap == Gap::ReturnLengthUnknown, "before and completed after");
    Check(!Poll(e), "nested ioctl duplicate suppressed");
    delayMilliseconds = 20;
    SetLastError(88);
    Check(FakeGet(owned, buffer, sizeof(buffer)) == TRUE && GetLastError() == 123 && seenIncoming == 88, "delayed HidD preserves original status and errors");
    Check(Poll(e) && e.timestampTicks < e.completionTimestampTicks && e.outputBufferCapacity == 12, "delayed HidD records entry before actual return");
    DWORD delayedBytes{};
    SetLastError(99);
    Check(FakeIoctl(owned, GetCode, buffer, sizeof(buffer), buffer, sizeof(buffer), &delayedBytes, nullptr) == TRUE && GetLastError() == 123 && seenIncoming == 99, "delayed IOCTL preserves original status and errors");
    Check(Poll(e) && e.operation == Operation::IoctlGetFeature && e.timestampTicks < e.completionTimestampTicks && e.outputBufferCapacity == 12, "delayed IOCTL records entry before actual return");
    delayMilliseconds = 0;
    FakeGet(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(0x9999)), buffer, sizeof(buffer));
    Check(!Poll(e), "unselected handle excluded");
    DWORD bytes{}; mode = 2; OVERLAPPED ov{};
    Check(!FakeIoctl(owned, GetCode, buffer, sizeof(buffer), buffer, sizeof(buffer), &bytes, &ov), "pending original status");
    Check(Poll(e) && e.gap == Gap::PendingCompletionNotObserved && !e.outputLength && e.outputBufferCapacity == 12, "pending is a gap without completed output");
    mode = 1; FakeGet(owned, buffer, sizeof(buffer));
    Check(Poll(e) && !e.result && e.lastError == ERROR_ACCESS_DENIED && !e.outputLength && e.outputBufferCapacity == 12, "failed call has no completed output");
    mode = 0;
    FakeIoctl(owned, 0, buffer, sizeof(buffer), buffer, sizeof(buffer), &bytes, nullptr);
    Check(!Poll(e), "unrelated ioctl excluded");
    buffer[0] = 4;
    FakeIoctl(owned, GetCode, buffer, sizeof(buffer), buffer, sizeof(buffer), &bytes, nullptr);
    Check(Poll(e) && e.operation == Operation::IoctlGetFeature && e.inputLength == 1 && e.outputLength == 12 && e.returnedBytes == 12 && e.outputBufferCapacity == 12 && e.gap == Gap::None, "direct completed IOCTL feature record");
    unsigned char capacityBuffer[91]{4};
    returnedOverride = 90;
    FakeIoctl(owned, GetCode, capacityBuffer, 1, capacityBuffer, 90, &bytes, nullptr);
    Check(Poll(e) && e.outputLength == 90 && e.returnedBytes == 90 && e.outputBufferCapacity == 90 && e.gap == Gap::None, "exact IOCTL output capacity retained");
    capacityBuffer[89] = 0x5A;
    capacityBuffer[90] = 0xC7;
    FakeIoctl(owned, GetCode, capacityBuffer, 1, capacityBuffer, 91, &bytes, nullptr);
    Check(Poll(e) && e.outputLength == 90 && e.returnedBytes == 90 && e.outputBufferCapacity == 91 && e.output[89] == 0x5A && e.output[90] == 0 && e.gap == Gap::None, "IOCTL copies exactly returned bytes while retaining larger capacity");
    returnedOverride = 91;
    FakeIoctl(owned, GetCode, capacityBuffer, 1, capacityBuffer, 90, &bytes, nullptr);
    Check(Poll(e) && !e.outputLength && e.returnedBytes == 0 && e.outputBufferCapacity == 90 && e.gap == Gap::BufferTooLarge, "IOCTL return cannot exceed output capacity");
    returnedOverride = 0;
    unsigned char extended[100]{};
    FakeGet(owned, extended, sizeof(extended));
    Check(Poll(e) && e.outputLength == 91 && e.requestedBytes == 100 && e.outputBufferCapacity == 100 && e.gap == Gap::ReturnLengthUnknown, "HidD response never includes tail beyond descriptor geometry");
    unsigned char huge[MaxPayload + 1]{};
    FakeSet(owned, huge, sizeof(huge));
    Check(Poll(e) && e.gap == Gap::BufferTooLarge && !e.inputLength && !e.outputBufferCapacity, "oversize set payload omitted");
    for (size_t i = 0; i < MaxEvents + 10; ++i) FakeSet(owned, buffer, sizeof(buffer));
    Check(Dropped() == 10, "queue is bounded");
    unsigned seen{}; while (Poll(e)) ++seen;
    Check(seen == MaxEvents, "bounded queue count");
    auto shared = std::make_unique<SharedState>();
    for (unsigned i = 0; i < 64; ++i) FakeSet(owned, buffer, sizeof(buffer));
    Check(DrainShared(*shared) == DrainResult::Blocked && shared->writeIndex == 32, "full shared ring leaves engine events queued");
    FinishSharedDrain(*shared, 0);
    Check(shared->dropped == 32, "missing final consumer has bounded explicitly counted loss");
    shared = std::make_unique<SharedState>();
    constexpr unsigned burst = 128;
    for (unsigned i = 0; i < burst; ++i) FakeGet(owned, buffer, sizeof(buffer));
    Check(Stop(), "detach hooks before final burst drain");
    std::thread writer([&] { FinishSharedDrain(*shared); InterlockedExchange(&shared->finished, 1); });
    unsigned retained{}; uint64_t previousSequence{};
    do {
        LONG read = InterlockedCompareExchange(&shared->readIndex, 0, 0);
        LONG write = InterlockedCompareExchange(&shared->writeIndex, 0, 0);
        while (read < write) {
            const auto& item = shared->events[read % SharedSlots];
            Check(item.outputLength == 12 && item.sequence > previousSequence, "final burst retains ordered completed responses");
            previousSequence = item.sequence;
            ++retained;
            SecureZeroMemory(&shared->events[read % SharedSlots], sizeof(Event));
            InterlockedExchange(&shared->readIndex, ++read);
        }
        Sleep(1);
    } while (!InterlockedCompareExchange(&shared->finished, 0, 0) || shared->readIndex < shared->writeIndex);
    writer.join();
    Check(retained == burst && shared->dropped == 0 && shared->failure == 0, "burst larger than shared ring drains losslessly during stop");
    FakeGet(owned, buffer, sizeof(buffer)); Check(!Poll(e), "no collection after stop");
    std::puts("PASS: real MinHook owned-process fixture; bounded safety assertions; no device handles or hardware calls");
    return 0;
}
