#include "../src/ObserverShared.h"
#include <cstdio>
#include <cstdlib>
using namespace observer;
void Check(bool value, const char* message) { if (!value) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); } }
int main() {
    // This process owns the DLL, mapping, and worker. No process injection,
    // device open, report, DeviceIoControl, or HID query is issued by this test.
    Check(LoadLibraryW(L"hid.dll") != nullptr, "load OS API module without calls");
    HMODULE module = LoadLibraryW(L"SynapseObserver.Native.dll");
    Check(module != nullptr, "load matching owned DLL");
    auto start = reinterpret_cast<LPTHREAD_START_ROUTINE>(GetProcAddress(module, "ObserverStart"));
    Check(start != nullptr, "typed startup export");
    StartConfig config{}; config.size = sizeof(config); config.durationSeconds = 301;
    Check(start(&config) == ERROR_INVALID_PARAMETER, "reject unbounded duration before hooks");
    swprintf_s(config.mappingName, L"Local\\SynapseObserver-OwnedTest-%lu", GetCurrentProcessId());
    config.durationSeconds = 1; config.interfaceCount = 1; config.featureBytes[0] = 12;
    wcscpy_s(config.selectedObjectNames[0], L"\\Device\\OwnedFixtureNoDevice");
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(SharedState), config.mappingName);
    Check(mapping != nullptr, "owned shared memory");
    auto shared = static_cast<SharedState*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedState)));
    Check(shared != nullptr, "map owned shared state");
    HANDLE worker = CreateThread(nullptr, 0, start, &config, 0, nullptr);
    Check(worker != nullptr, "owned DLL session worker");
    for (unsigned i = 0; i < 500 && !shared->ready && !shared->finished; ++i) Sleep(2);
    Check(shared->ready == 1 && shared->failure == 0, "startup handshake installs actual API hooks");
    InterlockedExchange(&shared->stop, 1);
    Check(WaitForSingleObject(worker, 3000) == WAIT_OBJECT_0, "bounded stop completes");
    Check(shared->finished == 1 && shared->failure == 0 && shared->writeIndex == 0, "no-data session is explicit and detached");
    CloseHandle(worker); UnmapViewOfFile(shared); CloseHandle(mapping);
    FreeLibrary(module); // pinned module remains until this owned test exits
    std::puts("PASS: owned DLL startup, bounded configuration, shared-memory handshake, stop, explicit zero data; no device calls");
    return 0;
}
