#pragma once
#include <windows.h>
#include <array>
#include <atomic>
#include <mutex>
#include <cstdint>
namespace observer {
constexpr size_t MaxPayload = 4096;
constexpr size_t MaxEvents = 256;
enum class Operation : uint32_t { SetFeature, GetFeature, IoctlSetFeature, IoctlGetFeature };
enum class Gap : uint32_t { None, PendingCompletionNotObserved, BufferTooLarge, UnreadableBuffer, QueueFull, ReturnLengthUnknown };
struct Event {
    uint64_t sequence{};
    uint64_t timestampTicks{};
    Operation operation{};
    Gap gap{};
    BOOL result{};
    DWORD lastError{};
    DWORD requestedBytes{};
    DWORD returnedBytes{};
    DWORD inputLength{};
    DWORD outputLength{};
    DWORD interfaceIndex{};
    std::array<unsigned char, MaxPayload> input{};
    std::array<unsigned char, MaxPayload> output{};
    DWORD outputBufferCapacity{};
    uint64_t completionTimestampTicks{};
};
struct Targets { void* setFeature; void* getFeature; void* deviceIoControl; };
enum class LifecycleState : uint32_t { Idle, Recording, Faulted };
enum class StartResult : uint32_t { Started, Invalid, Busy, Faulted };
// Deliberately internal: production has no arbitrary handle or hook-target API.
// The owned fixture is the only caller until exact-device provenance is reviewed.
StartResult StartOwnedFixture(Targets targets, HANDLE ownedHandle, unsigned featureBytes = 12);
StartResult StartLive(const wchar_t names[][512], const DWORD* featureBytes, unsigned interfaceCount);
bool Stop();
bool Poll(Event& result);
uint64_t Dropped();
LifecycleState State();
}
