#include "../src/ObserverEngine.h"
#include <cstdio>
#include <cstdlib>
using namespace observer;
constexpr DWORD GetCode = CTL_CODE(FILE_DEVICE_KEYBOARD, 100, METHOD_OUT_DIRECT, FILE_ANY_ACCESS);
constexpr DWORD SetCode = CTL_CODE(FILE_DEVICE_KEYBOARD, 100, METHOD_IN_DIRECT, FILE_ANY_ACCESS);
HANDLE owned = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(0x1234));
int mode{};
DWORD seenIncoming{};
DWORD delayMilliseconds{};
__declspec(noinline) BOOL WINAPI FakeIoctl(HANDLE, DWORD, LPVOID, DWORD, LPVOID output, DWORD size, LPDWORD bytes, LPOVERLAPPED) {
    seenIncoming = GetLastError();
    if (delayMilliseconds) Sleep(delayMilliseconds);
    if (mode == 2) { SetLastError(ERROR_IO_PENDING); return FALSE; }
    if (mode == 1) { SetLastError(ERROR_ACCESS_DENIED); return FALSE; }
    if (output && size) static_cast<unsigned char*>(output)[0] = 0xA5;
    if (bytes) *bytes = size;
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
    Check(StartOwnedFixture({reinterpret_cast<void*>(FakeSet), reinterpret_cast<void*>(FakeGet), reinterpret_cast<void*>(FakeIoctl)}, owned), "install owned hooks");
    unsigned char buffer[12]{1,2,3}; Event e{};
    SetLastError(77);
    Check(FakeGet(owned, buffer, sizeof(buffer)) == TRUE, "original status");
    Check(GetLastError() == 123 && seenIncoming == 77, "preserve incoming and outgoing errors");
    Check(Poll(e) && e.operation == Operation::GetFeature && e.input[0] == 1 && e.output[0] == 0xA5 && e.outputLength == 12 && e.inputLength == 1 && e.input[1] == 0 && e.gap == Gap::ReturnLengthUnknown, "before and completed after");
    Check(!Poll(e), "nested ioctl duplicate suppressed");
    delayMilliseconds = 20;
    SetLastError(88);
    Check(FakeGet(owned, buffer, sizeof(buffer)) == TRUE && GetLastError() == 123 && seenIncoming == 88, "delayed HidD preserves original status and errors");
    Check(Poll(e) && e.timestampTicks < e.completionTimestampTicks, "delayed HidD records entry before actual return");
    DWORD delayedBytes{};
    SetLastError(99);
    Check(FakeIoctl(owned, GetCode, buffer, sizeof(buffer), buffer, sizeof(buffer), &delayedBytes, nullptr) == TRUE && GetLastError() == 123 && seenIncoming == 99, "delayed IOCTL preserves original status and errors");
    Check(Poll(e) && e.operation == Operation::IoctlGetFeature && e.timestampTicks < e.completionTimestampTicks, "delayed IOCTL records entry before actual return");
    delayMilliseconds = 0;
    FakeGet(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(0x9999)), buffer, sizeof(buffer));
    Check(!Poll(e), "unselected handle excluded");
    DWORD bytes{}; mode = 2; OVERLAPPED ov{};
    Check(!FakeIoctl(owned, GetCode, buffer, sizeof(buffer), buffer, sizeof(buffer), &bytes, &ov), "pending original status");
    Check(Poll(e) && e.gap == Gap::PendingCompletionNotObserved && !e.outputLength, "pending is a gap without completed output");
    mode = 1; FakeGet(owned, buffer, sizeof(buffer));
    Check(Poll(e) && !e.result && e.lastError == ERROR_ACCESS_DENIED && !e.outputLength, "failed call has no completed output");
    mode = 0;
    FakeIoctl(owned, 0, buffer, sizeof(buffer), buffer, sizeof(buffer), &bytes, nullptr);
    Check(!Poll(e), "unrelated ioctl excluded");
    buffer[0] = 4;
    FakeIoctl(owned, GetCode, buffer, sizeof(buffer), buffer, sizeof(buffer), &bytes, nullptr);
    Check(Poll(e) && e.operation == Operation::IoctlGetFeature && e.inputLength == 1 && e.outputLength == 12 && e.returnedBytes == 12 && e.gap == Gap::None, "direct completed IOCTL feature record");
    unsigned char extended[20]{};
    FakeGet(owned, extended, sizeof(extended));
    Check(Poll(e) && e.outputLength == 12 && e.requestedBytes == 20 && e.gap == Gap::ReturnLengthUnknown, "HidD response never includes tail beyond descriptor geometry");
    unsigned char huge[MaxPayload + 1]{};
    FakeSet(owned, huge, sizeof(huge));
    Check(Poll(e) && e.gap == Gap::BufferTooLarge && !e.inputLength, "oversize payload omitted");
    for (size_t i = 0; i < MaxEvents + 10; ++i) FakeSet(owned, buffer, sizeof(buffer));
    Check(Dropped() == 10, "queue is bounded");
    unsigned seen{}; while (Poll(e)) ++seen;
    Check(seen == MaxEvents, "bounded queue count");
    FakeGet(owned, buffer, sizeof(buffer));
    Check(Stop(), "detach hooks");
    Check(Poll(e) && e.outputLength == 12, "final completed call remains drainable after stop");
    FakeGet(owned, buffer, sizeof(buffer)); Check(!Poll(e), "no collection after stop");
    std::puts("PASS: real MinHook owned-process fixture; bounded safety assertions; no device handles or hardware calls");
    return 0;
}
