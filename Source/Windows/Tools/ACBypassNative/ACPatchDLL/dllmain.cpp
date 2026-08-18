#include <windows.h>

// TODO: Add AC patching code here
// For now, just a placeholder that loads successfully

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        // The game's AC checks will be patched here
        // For now, just return TRUE to prove injection works
    }
    return TRUE;
}
