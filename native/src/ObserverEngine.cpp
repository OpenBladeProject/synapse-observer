#include "ObserverEngine.h"
#include "../third_party/minhook/include/MinHook.h"
#include <winternl.h>
#include <algorithm>
namespace observer {
// Values from the Windows SDK hidclass.h HID feature contracts.
constexpr DWORD GetFeatureCode = CTL_CODE(FILE_DEVICE_KEYBOARD, 100, METHOD_OUT_DIRECT, FILE_ANY_ACCESS);
constexpr DWORD SetFeatureCode = CTL_CODE(FILE_DEVICE_KEYBOARD, 100, METHOD_IN_DIRECT, FILE_ANY_ACCESS);
using Feature = BOOLEAN (__stdcall*)(HANDLE, PVOID, ULONG);
using Ioctl = BOOL (WINAPI*)(HANDLE, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
Feature originalSet{}, originalGet{};
Ioctl originalIoctl{};
HANDLE admittedHandle{};
wchar_t selectedNames[8][512]{};
unsigned selectedCount{};
DWORD selectedFeatureBytes[8]{};
thread_local unsigned selectedIndex{};
using QueryObject = NTSTATUS (NTAPI*)(HANDLE, OBJECT_INFORMATION_CLASS, PVOID, ULONG, PULONG);
QueryObject queryObject{};
bool Admitted(HANDLE handle) {
    if (admittedHandle) { selectedIndex = 0; return handle == admittedHandle; }
    if (!queryObject || !selectedCount) return false;
    alignas(void*) unsigned char data[2048]{};
    ULONG returned{};
    if (queryObject(handle, static_cast<OBJECT_INFORMATION_CLASS>(1), data, sizeof(data), &returned) < 0) return false;
    const auto name = reinterpret_cast<UNICODE_STRING*>(data);
    if (!name->Buffer || name->Length > sizeof(data) - sizeof(UNICODE_STRING)) return false;
    const auto begin = reinterpret_cast<uintptr_t>(data);
    const auto value = reinterpret_cast<uintptr_t>(name->Buffer);
    if (value < begin || value + name->Length > begin + sizeof(data)) return false;
    for (unsigned i = 0; i < selectedCount; ++i) {
        if (name->Length == wcslen(selectedNames[i]) * sizeof(wchar_t) && _wcsnicmp(name->Buffer, selectedNames[i], name->Length / sizeof(wchar_t)) == 0) { selectedIndex = i; return true; }
    }
    return false;
}
std::atomic<LifecycleState> lifecycle{LifecycleState::Idle};
std::atomic<uint64_t> callbacks{0}, dropped{0}, sequence{0};
std::mutex queueLock;
std::mutex lifecycleLock;
std::array<Event, MaxEvents> events{};
size_t head{}, count{};
thread_local unsigned nesting{};
void* hookTargets[3]{};
bool initialized{};
// Copies never touch arbitrary/unbounded memory and failures cannot escape hooks.
bool Copy(void* to, const void* from, DWORD size) {
    if (!size) return true;
    if (!from) return false;
    __try { memcpy(to, from, size); return true; }
    __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
struct Callback {
    bool outer;
    Callback() : outer(nesting++ == 0) { callbacks.fetch_add(1); }
    ~Callback() { callbacks.fetch_sub(1); --nesting; }
};
Event Begin(Operation operation, void* buffer, DWORD length, DWORD outputBufferCapacity = 0) {
    Event e{};
    e.sequence = ++sequence;
    LARGE_INTEGER time{}; QueryPerformanceCounter(&time); e.timestampTicks = time.QuadPart;
    e.operation = operation; e.requestedBytes = length; e.interfaceIndex = selectedIndex;
    e.outputBufferCapacity = outputBufferCapacity;
    const DWORD copyLength = operation == Operation::GetFeature || operation == Operation::IoctlGetFeature ? (length ? 1 : 0) : length;
    if (copyLength > MaxPayload) e.gap = Gap::BufferTooLarge;
    else if (!Copy(e.input.data(), buffer, copyLength)) e.gap = Gap::UnreadableBuffer;
    else e.inputLength = copyLength;
    return e;
}
void Enqueue(const Event& e) {
    // Never wait for the consumer inside a vendor call.
    std::unique_lock<std::mutex> lock(queueLock, std::try_to_lock);
    if (!lock.owns_lock() || count == MaxEvents) { ++dropped; return; }
    events[(head + count) % MaxEvents] = e;
    ++count;
}
BOOLEAN FeatureCall(Feature original, Operation operation, HANDLE handle, PVOID buffer, ULONG length) {
    DWORD incomingError = GetLastError();
    Callback callback;
    const bool capture = callback.outer && lifecycle.load(std::memory_order_acquire) == LifecycleState::Recording && Admitted(handle);
    Event e{};
    if (capture) e = Begin(operation, buffer, length, operation == Operation::GetFeature ? length : 0);
    SetLastError(incomingError);
    const BOOLEAN result = original(handle, buffer, length);
    const DWORD error = GetLastError();
    if (capture) {
        LARGE_INTEGER completed{}; QueryPerformanceCounter(&completed);
        e.completionTimestampTicks = completed.QuadPart;
        e.result = result; e.lastError = error;
        // HidD_GetFeature has no byte-count return: retain at most descriptor geometry,
        // not a claim that the device returned that many valid bytes.
        if (result && operation == Operation::GetFeature && e.gap == Gap::None) {
            const DWORD bounded = (std::min)(length, selectedFeatureBytes[e.interfaceIndex]);
            if (bounded > MaxPayload) e.gap = Gap::BufferTooLarge;
            else if (Copy(e.output.data(), buffer, bounded)) { e.outputLength = bounded; e.gap = Gap::ReturnLengthUnknown; }
            else e.gap = Gap::UnreadableBuffer;
        }
        Enqueue(e);
    }
    SetLastError(error);
    return result;
}
BOOLEAN __stdcall HookSet(HANDLE h, PVOID b, ULONG n) { return FeatureCall(originalSet, Operation::SetFeature, h, b, n); }
BOOLEAN __stdcall HookGet(HANDLE h, PVOID b, ULONG n) { return FeatureCall(originalGet, Operation::GetFeature, h, b, n); }
BOOL WINAPI HookIoctl(HANDLE handle, DWORD code, LPVOID input, DWORD inputSize, LPVOID output, DWORD outputSize, LPDWORD returned, LPOVERLAPPED overlapped) {
    DWORD incomingError = GetLastError();
    Callback callback;
    const bool capture = callback.outer && lifecycle.load(std::memory_order_acquire) == LifecycleState::Recording && (code == GetFeatureCode || code == SetFeatureCode) && Admitted(handle);
    Event e{};
    if (capture) {
        // GET_FEATURE uses a caller-supplied report ID. Never snapshot its
        // uninitialized input/output tail before the original call.
        e = code == GetFeatureCode ? Begin(Operation::IoctlGetFeature, inputSize ? input : output, inputSize ? inputSize : outputSize, outputSize) : Begin(Operation::IoctlSetFeature, input, inputSize);
    }
    SetLastError(incomingError);
    const BOOL result = originalIoctl(handle, code, input, inputSize, output, outputSize, returned, overlapped);
    const DWORD error = GetLastError();
    if (capture) {
        LARGE_INTEGER completed{}; QueryPerformanceCounter(&completed);
        e.completionTimestampTicks = completed.QuadPart;
        e.result = result; e.lastError = error;
        if (!result && error == ERROR_IO_PENDING) e.gap = Gap::PendingCompletionNotObserved;
        // Async requests can complete later through many APIs. Never dereference
        // their buffers after return or label them completed.
        else if (result && code == GetFeatureCode) {
            DWORD bytes{};
            if (!Copy(&bytes, returned, sizeof(bytes))) e.gap = Gap::UnreadableBuffer;
            else if (bytes > e.outputBufferCapacity || bytes > MaxPayload || bytes > selectedFeatureBytes[e.interfaceIndex]) e.gap = Gap::BufferTooLarge;
            else if (!Copy(e.output.data(), output, bytes)) e.gap = Gap::UnreadableBuffer;
            else { e.returnedBytes = bytes; e.outputLength = bytes; }
        }
        Enqueue(e);
    }
    SetLastError(error);
    return result;
}
StartResult StartInternal(Targets targets, HANDLE ownedHandle, const wchar_t names[][512], const DWORD* featureBytes, unsigned interfaceCount, QueryObject query) {
    if (!targets.setFeature || !targets.getFeature || !targets.deviceIoControl || !featureBytes || interfaceCount == 0 || interfaceCount > 8) return StartResult::Invalid;
    std::lock_guard<std::mutex> guard(lifecycleLock);
    const auto state = lifecycle.load(std::memory_order_acquire);
    if (state == LifecycleState::Recording) return StartResult::Busy;
    if (state == LifecycleState::Faulted) return StartResult::Faulted;
    if (callbacks.load(std::memory_order_acquire) != 0) {
        lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
        return StartResult::Faulted;
    }
    if (!initialized) {
        if (MH_Initialize() != MH_OK) {
            lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
            return StartResult::Faulted;
        }
        initialized = true;
        hookTargets[0] = targets.setFeature; hookTargets[1] = targets.getFeature; hookTargets[2] = targets.deviceIoControl;
        if (MH_CreateHook(targets.setFeature, HookSet, reinterpret_cast<void**>(&originalSet)) != MH_OK ||
            MH_CreateHook(targets.getFeature, HookGet, reinterpret_cast<void**>(&originalGet)) != MH_OK ||
            MH_CreateHook(targets.deviceIoControl, HookIoctl, reinterpret_cast<void**>(&originalIoctl)) != MH_OK) {
            lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
            return StartResult::Faulted;
        }
    } else if (hookTargets[0] != targets.setFeature || hookTargets[1] != targets.getFeature || hookTargets[2] != targets.deviceIoControl) {
        lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
        return StartResult::Faulted;
    }
    {
        std::lock_guard<std::mutex> queueGuard(queueLock);
        SecureZeroMemory(events.data(), sizeof(events));
        head = 0; count = 0;
    }
    admittedHandle = ownedHandle;
    selectedCount = names ? interfaceCount : 0;
    queryObject = query;
    SecureZeroMemory(selectedNames, sizeof(selectedNames));
    SecureZeroMemory(selectedFeatureBytes, sizeof(selectedFeatureBytes));
    for (unsigned i = 0; i < interfaceCount; ++i) {
        selectedFeatureBytes[i] = featureBytes[i];
        if (names) wcscpy_s(selectedNames[i], names[i]);
    }
    dropped.store(0, std::memory_order_release);
    sequence.store(0, std::memory_order_release);
    lifecycle.store(LifecycleState::Recording, std::memory_order_release);
    for (void* target : hookTargets) {
        if (MH_QueueEnableHook(target) != MH_OK) {
            lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
            return StartResult::Faulted;
        }
    }
    if (MH_ApplyQueued() != MH_OK) {
        lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
        return StartResult::Faulted;
    }
    return StartResult::Started;
}
StartResult StartOwnedFixture(Targets targets, HANDLE ownedHandle, unsigned featureBytes) {
    if (!ownedHandle || !featureBytes) return StartResult::Invalid;
    const DWORD fixtureFeatureBytes = featureBytes;
    return StartInternal(targets, ownedHandle, nullptr, &fixtureFeatureBytes, 1, nullptr);
}
bool PinHookTarget(void* target) {
    HMODULE pinned{};
    return target && GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(target),
        &pinned);
}
StartResult StartLive(const wchar_t names[][512], const DWORD* featureBytes, unsigned interfaceCount) {
    if (!names || !featureBytes || interfaceCount == 0 || interfaceCount > 8) return StartResult::Invalid;
    for (unsigned i = 0; i < interfaceCount; ++i) {
        if (names[i][511] || wcsncmp(names[i], L"\\Device\\", 8) != 0 || !featureBytes[i]) return StartResult::Invalid;
    }
    const auto query = reinterpret_cast<QueryObject>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQueryObject"));
    HMODULE hid = GetModuleHandleW(L"hid.dll");
    HMODULE kernel = GetModuleHandleW(L"kernelbase.dll");
    if (!query || !hid || !kernel) return StartResult::Invalid;
    const Targets targets{
        reinterpret_cast<void*>(GetProcAddress(hid, "HidD_SetFeature")),
        reinterpret_cast<void*>(GetProcAddress(hid, "HidD_GetFeature")),
        reinterpret_cast<void*>(GetProcAddress(kernel, "DeviceIoControl"))};
    if (!PinHookTarget(targets.setFeature) || !PinHookTarget(targets.getFeature) || !PinHookTarget(targets.deviceIoControl))
        return StartResult::Invalid;
    return StartInternal(targets, nullptr, names, featureBytes, interfaceCount, query);
}
bool Stop() {
    std::lock_guard<std::mutex> guard(lifecycleLock);
    const auto state = lifecycle.load(std::memory_order_acquire);
    if (state == LifecycleState::Idle) return true;
    if (state == LifecycleState::Faulted || !initialized) return false;
    // Keep the module and trampoline allocation resident. A thread may have
    // entered a trampoline before our callback counter increments. Freeing
    // them using a counter alone is unsafe. Process exit reclaims them.
    if (MH_DisableHook(MH_ALL_HOOKS) != MH_OK) {
        lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
        return false;
    }
    const ULONGLONG callbackDeadline = GetTickCount64() + 100;
    while (callbacks.load(std::memory_order_acquire) != 0 && GetTickCount64() < callbackDeadline) Sleep(1);
    if (callbacks.load(std::memory_order_acquire) != 0) {
        lifecycle.store(LifecycleState::Faulted, std::memory_order_release);
        return false;
    }
    lifecycle.store(LifecycleState::Idle, std::memory_order_release);
    return true;
}
bool Poll(Event& result) {
    std::unique_lock<std::mutex> lock(queueLock, std::try_to_lock);
    if (!lock.owns_lock()) return false;
    if (!count) return false;
    result = events[head]; head = (head + 1) % MaxEvents; --count; return true;
}
uint64_t Dropped() { return dropped.load(); }
LifecycleState State() { return lifecycle.load(std::memory_order_acquire); }
}
