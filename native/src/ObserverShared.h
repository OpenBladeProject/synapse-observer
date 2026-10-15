#pragma once
#include "ObserverEngine.h"
namespace observer {
constexpr DWORD MaximumRecordingDurationSeconds = 600;
constexpr unsigned SharedSlots = 32;
struct SharedState {
    volatile LONG ready;
    volatile LONG finished;
    volatile LONG failure;
    volatile LONG stop;
    volatile LONG writeIndex;
    volatile LONG readIndex;
    volatile LONG dropped;
    LONG reserved;
    Event events[SharedSlots];
};
// One native writer and one managed reader. Do not dequeue engine events
// until a shared slot exists: bursts can wait in the larger engine queue.
enum class DrainResult { Empty, Blocked, Invalid };
inline DrainResult DrainShared(SharedState& shared) {
    Event event{};
    for (;;) {
        LONG write = InterlockedCompareExchange(&shared.writeIndex, 0, 0);
        LONG read = InterlockedCompareExchange(&shared.readIndex, 0, 0);
        if (write < 0 || read < 0 || read > write || write == LONG_MAX) {
            InterlockedExchange(&shared.failure, ERROR_INVALID_DATA);
            InterlockedExchange(&shared.stop, 1);
            return DrainResult::Invalid;
        }
        if (write - read >= static_cast<LONG>(SharedSlots)) return DrainResult::Blocked;
        if (!Poll(event)) return DrainResult::Empty;
        shared.events[write % SharedSlots] = event;
        InterlockedExchange(&shared.writeIndex, write + 1);
        SecureZeroMemory(&event, sizeof(event));
    }
}
inline void FinishSharedDrain(SharedState& shared, DWORD timeoutMilliseconds = 1000) {
    ULONGLONG deadline = GetTickCount64() + timeoutMilliseconds;
    while (DrainShared(shared) == DrainResult::Blocked) {
        if (GetTickCount64() >= deadline) {
            // A missing consumer must not keep the vendor worker alive forever.
            // Explicitly account for the remaining engine records on timeout.
            Event event{};
            while (Poll(event)) {
                InterlockedIncrement(&shared.dropped);
                SecureZeroMemory(&event, sizeof(event));
            }
            return;
        }
        Sleep(1);
    }
}
struct StartConfig { DWORD size; DWORD durationSeconds; wchar_t mappingName[128]; DWORD interfaceCount; wchar_t selectedObjectNames[8][512]; DWORD featureBytes[8]; };
}

static_assert(sizeof(observer::Event) == 8256);
static_assert(sizeof(observer::StartConfig) == 8492);
static_assert(offsetof(observer::SharedState, events) == 32);
static_assert(offsetof(observer::Event, outputBufferCapacity) == 8244);
static_assert(offsetof(observer::Event, completionTimestampTicks) == 8248);
