#include "ObserverShared.h"
#include <algorithm>
using namespace observer;
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
    if (!StartLive(config.selectedObjectNames, config.featureBytes, config.interfaceCount)) {
        InterlockedExchange(&shared->failure, ERROR_NOT_SUPPORTED);
        if (!Stop()) InterlockedExchange(&shared->failure, ERROR_BUSY);
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
    }
    UnmapViewOfFile(shared); CloseHandle(mapping);
    // The DLL and trampolines remain pinned until the vendor process exits.
    return ERROR_SUCCESS;
}
BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
