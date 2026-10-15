#include "ObserverShared.h"
#include <algorithm>
#include <atomic>
using namespace observer;
namespace {
enum class WorkerState : DWORD { Idle, Recording, Faulted };
enum class WorkerClaim : DWORD { Acquired, Busy, Faulted };
std::atomic<WorkerState> workerState{WorkerState::Idle};

WorkerClaim TryClaimWorker() {
    WorkerState expected = WorkerState::Idle;
    if (workerState.compare_exchange_strong(expected, WorkerState::Recording, std::memory_order_acq_rel))
        return WorkerClaim::Acquired;
    return expected == WorkerState::Faulted ? WorkerClaim::Faulted : WorkerClaim::Busy;
}

void CompleteWorker(bool cleanupConfirmed) {
    WorkerState expected = WorkerState::Recording;
    if (cleanupConfirmed && workerState.compare_exchange_strong(expected, WorkerState::Idle, std::memory_order_acq_rel))
        return;
    workerState.store(WorkerState::Faulted, std::memory_order_release);
}
}
extern "C" __declspec(dllexport) DWORD WINAPI ObserverProtocolVersion() { return 2; }
extern "C" __declspec(dllexport) DWORD WINAPI ObserverStart(LPVOID argument) {
    if (!argument) return ERROR_INVALID_PARAMETER;
    StartConfig config{};
    memcpy(&config, argument, sizeof(config));
    if (config.size != sizeof(config) || config.durationSeconds == 0 || config.durationSeconds > 300 ||
        config.mappingName[127] || config.interfaceCount == 0 || config.interfaceCount > 8) return ERROR_INVALID_PARAMETER;
    HMODULE pinned{};
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&ObserverStart), &pinned)) return GetLastError();
    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, config.mappingName);
    if (!mapping) return GetLastError();
    auto shared = static_cast<SharedState*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(SharedState)));
    if (!shared) { DWORD error = GetLastError(); CloseHandle(mapping); return error; }
    const WorkerClaim claim = TryClaimWorker();
    if (claim != WorkerClaim::Acquired) {
        InterlockedExchange(&shared->failure, claim == WorkerClaim::Faulted ? ERROR_INVALID_STATE : ERROR_BUSY);
        InterlockedExchange(&shared->finished, 1);
        UnmapViewOfFile(shared); CloseHandle(mapping);
        return ERROR_SUCCESS;
    }
    bool cleanupConfirmed = false;
    const auto start = StartLive(config.selectedObjectNames, config.featureBytes, config.interfaceCount);
    if (start != StartResult::Started) {
        const DWORD error = start == StartResult::Invalid ? ERROR_INVALID_PARAMETER :
            start == StartResult::Busy ? ERROR_BUSY : ERROR_INVALID_STATE;
        InterlockedExchange(&shared->failure, error);
        InterlockedExchange(&shared->finished, 1);
    } else {
        InterlockedExchange(&shared->ready, 1);
        ULONGLONG deadline = GetTickCount64() + config.durationSeconds * 1000ULL;
        do {
            DrainShared(*shared);
            Sleep(10);
        } while (!InterlockedCompareExchange(&shared->stop, 0, 0) && GetTickCount64() < deadline);
        bool detached = Stop();
        FinishSharedDrain(*shared); // reader remains active until this completes
        InterlockedExchangeAdd(&shared->dropped, static_cast<LONG>(std::min<uint64_t>(Dropped(), LONG_MAX)));
        if (!detached) InterlockedExchange(&shared->failure, ERROR_BUSY);
        InterlockedExchange(&shared->finished, 1);
        cleanupConfirmed = detached && InterlockedCompareExchange(&shared->failure, 0, 0) == 0;
    }
    const bool unmapped = UnmapViewOfFile(shared) != FALSE;
    const bool closed = CloseHandle(mapping) != FALSE;
    const bool reusable = cleanupConfirmed && unmapped && closed;
    CompleteWorker(reusable);
    // The shared view is already unmapped; the thread result reports final
    // cleanup failures that could not be published in the shared header.
    // The DLL and trampolines remain pinned until the vendor process exits.
    return reusable ? ERROR_SUCCESS : ERROR_INVALID_STATE;
}
BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
