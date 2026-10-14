#pragma once
#include "ObserverEngine.h"
namespace observer {
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
struct StartConfig { DWORD size; DWORD durationSeconds; wchar_t mappingName[128]; DWORD interfaceCount; wchar_t selectedObjectNames[8][512]; DWORD featureBytes[8]; };
}

static_assert(sizeof(observer::Event) == 8256);
static_assert(sizeof(observer::StartConfig) == 8492);
static_assert(offsetof(observer::SharedState, events) == 32);
static_assert(offsetof(observer::Event, outputBufferCapacity) == 8244);
static_assert(offsetof(observer::Event, completionTimestampTicks) == 8248);
