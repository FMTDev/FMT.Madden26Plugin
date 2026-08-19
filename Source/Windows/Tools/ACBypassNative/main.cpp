#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>

#pragma comment(lib, "advapi32.lib")

BOOL EnablePrivilege(PCSTR name) {
    HANDLE hToken;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES, &hToken))
        return FALSE;
    TOKEN_PRIVILEGES tp;
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    LookupPrivilegeValueA(NULL, name, &tp.Privileges[0].Luid);
    BOOL ok = AdjustTokenPrivileges(hToken, FALSE, &tp, sizeof(tp), NULL, NULL);
    CloseHandle(hToken);
    return ok && GetLastError() == ERROR_SUCCESS;
}

DWORD FindProcessId(const char* name) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe = { sizeof(pe) };
    int nameLen = MultiByteToWideChar(CP_ACP, 0, name, -1, NULL, 0);
    wchar_t* wname = (wchar_t*)malloc(nameLen * 2);
    MultiByteToWideChar(CP_ACP, 0, name, -1, wname, nameLen);
    DWORD pid = 0;
    if (Process32FirstW(snap, &pe)) do {
        if (_wcsicmp(pe.szExeFile, wname) == 0) { pid = pe.th32ProcessID; break; }
    } while (Process32NextW(snap, &pe));
    free(wname);
    CloseHandle(snap);
    return pid;
}

BOOL InjectDLL(HANDLE hProcess, const char* dllPath) {
    size_t pathLen = strlen(dllPath) + 1;
    void* remoteMem = VirtualAllocEx(hProcess, NULL, pathLen, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remoteMem) { printf("VirtualAllocEx failed: %lu\n", GetLastError()); return FALSE; }
    if (!WriteProcessMemory(hProcess, remoteMem, dllPath, pathLen, NULL)) {
        printf("WriteProcessMemory failed: %lu\n", GetLastError());
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        return FALSE;
    }
    HMODULE hKernel32 = GetModuleHandleA("kernel32.dll");
    FARPROC loadLibW = GetProcAddress(hKernel32, "LoadLibraryW");
    // Convert dll path to wide char for LoadLibraryW
    int wlen = MultiByteToWideChar(CP_ACP, 0, dllPath, -1, NULL, 0);
    wchar_t* wpath = (wchar_t*)malloc(wlen * 2);
    MultiByteToWideChar(CP_ACP, 0, dllPath, -1, wpath, wlen);
    WriteProcessMemory(hProcess, remoteMem, wpath, wlen * 2, NULL);
    free(wpath);
    HANDLE hThread = CreateRemoteThread(hProcess, NULL, 0, (LPTHREAD_START_ROUTINE)loadLibW, remoteMem, 0, NULL);
    if (!hThread) {
        printf("CreateRemoteThread failed: %lu\n", GetLastError());
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        return FALSE;
    }
    WaitForSingleObject(hThread, INFINITE);
    CloseHandle(hThread);
    VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
    return TRUE;
}

int main() {
    const char* gameDir = "C:\\Program Files\\EA Games\\EA SPORTS College Football 27";
    const char* gameExe = "CollegeFB27_Trial.exe";
    const char* dllName = "ACPatchDLL.dll";

    char dllPath[MAX_PATH];
    GetCurrentDirectoryA(MAX_PATH, dllPath);
    strcat_s(dllPath, MAX_PATH, "\\");
    strcat_s(dllPath, MAX_PATH, dllName);

    char gamePath[MAX_PATH];
    strcpy_s(gamePath, MAX_PATH, gameDir);
    strcat_s(gamePath, MAX_PATH, "\\");
    strcat_s(gamePath, MAX_PATH, gameExe);

    printf("AC Bypass for College Football 27\n");
    printf("Game: %s\n", gamePath);
    printf("DLL:  %s\n", dllPath);

    EnablePrivilege(SE_DEBUG_NAME);

    // Check if game is already running
    DWORD pid = FindProcessId(gameExe);
    if (pid == 0) {
        // Launch game suspended
        printf("Launching game...\n");
        STARTUPINFOA si = { sizeof(si) };
        PROCESS_INFORMATION pi;
        if (!CreateProcessA(NULL, (LPSTR)gamePath, NULL, NULL, FALSE, CREATE_SUSPENDED, NULL, gameDir, &si, &pi)) {
            printf("CreateProcessA failed: %lu\n", GetLastError());
            printf("Error: Could not create game process\n");
            getchar();
            return 1;
        }
        printf("Game created (PID: %u, suspended)\n", pi.dwProcessId);
        pid = pi.dwProcessId;

        // Inject DLL
        if (!InjectDLL(pi.hProcess, dllPath)) {
            printf("DLL injection failed. Injecting by waiting for process...\n");
            // Fallback: resume and try process discovery
            ResumeThread(pi.hThread);
            Sleep(2000);
            pid = FindProcessId(gameExe);
            if (pid != 0) {
                HANDLE hProc = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
                if (hProc) {
                    if (!InjectDLL(hProc, dllPath))
                        printf("Injection into running process also failed\n");
                    CloseHandle(hProc);
                }
            }
        } else {
            printf("DLL injected successfully!\n");
            ResumeThread(pi.hThread);
        }
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    } else {
        printf("Game already running (PID: %u). Injecting...\n", pid);
        HANDLE hProc = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
        if (hProc) {
            if (!InjectDLL(hProc, dllPath))
                printf("DLL injection failed\n");
            CloseHandle(hProc);
        }
    }

    printf("Game is running. Keep this window open.\n");
    printf("Press Enter to exit (game will continue running).\n");
    getchar();
    return 0;
}
