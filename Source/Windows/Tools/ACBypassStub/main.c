#include <windows.h>
#include <stdio.h>
#include <tlhelp32.h>

static const char* DetectGame(char* buf, int bufLen)
{
    if (GetFileAttributesA("CollegeFB27.exe") != INVALID_FILE_ATTRIBUTES ||
        GetFileAttributesA("CollegeFB27_Trial.exe") != INVALID_FILE_ATTRIBUTES) {
        strncpy_s(buf, bufLen, "College Football 27", _TRUNCATE);
        return "CollegeFB27";
    }
    if (GetFileAttributesA("Madden26.exe") != INVALID_FILE_ATTRIBUTES ||
        GetFileAttributesA("Madden26_Trial.exe") != INVALID_FILE_ATTRIBUTES) {
        strncpy_s(buf, bufLen, "Madden NFL 26", _TRUNCATE);
        return "Madden26";
    }
    if (GetFileAttributesA("Madden27.exe") != INVALID_FILE_ATTRIBUTES ||
        GetFileAttributesA("Madden27_Trial.exe") != INVALID_FILE_ATTRIBUTES) {
        strncpy_s(buf, bufLen, "Madden NFL 27", _TRUNCATE);
        return "Madden27";
    }
    if (GetFileAttributesA("Madden25.exe") != INVALID_FILE_ATTRIBUTES ||
        GetFileAttributesA("Madden25_Trial.exe") != INVALID_FILE_ATTRIBUTES) {
        strncpy_s(buf, bufLen, "Madden NFL 25", _TRUNCATE);
        return "Madden25";
    }
    strncpy_s(buf, bufLen, "Unknown", _TRUNCATE);
    return NULL;
}

static void SetBootOverride(const char* gameKey)
{
    if (!gameKey) return;
    char regPath[256];
    _snprintf_s(regPath, sizeof(regPath), _TRUNCATE,
        "SOFTWARE\\EA Games\\%s\\0.1", gameKey);
    HKEY hKey;
    if (RegCreateKeyExA(HKEY_CURRENT_USER, regPath, 0, NULL, 0,
        KEY_SET_VALUE, NULL, &hKey, NULL) == ERROR_SUCCESS) {
        RegSetValueExA(hKey, "LCP.BootOverride", 0, REG_SZ,
            (const BYTE*)"1", 2);
        RegCloseKey(hKey);
    }
}

static DWORD FindProcessId(const char* name)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    int wlen = MultiByteToWideChar(CP_ACP, 0, name, -1, NULL, 0);
    wchar_t* wname = (wchar_t*)malloc(wlen * sizeof(wchar_t));
    MultiByteToWideChar(CP_ACP, 0, name, -1, wname, wlen);
    DWORD pid = 0;
    if (Process32FirstW(snap, &pe)) {
        do {
            if (_wcsicmp(pe.szExeFile, wname) == 0) {
                pid = pe.th32ProcessID;
                break;
            }
        } while (Process32NextW(snap, &pe));
    }
    free(wname);
    CloseHandle(snap);
    return pid;
}

int main(int argc, char* argv[])
{
    int silent = 0;
    for (int i = 1; i < argc; i++) {
        if (_stricmp(argv[i], "--silent") == 0 || _stricmp(argv[i], "-s") == 0)
            silent = 1;
    }

    char gameName[64];
    const char* gameKey = DetectGame(gameName, sizeof(gameName));

    if (!silent) {
        printf("FMT Anti-Cheat Bypass\n");
        printf("Game: %s\n", gameName);
    }

    SetBootOverride(gameKey);

    // DLLs (CryptBase.dll, dpapi.dll) are already in the game directory
    // They get loaded via proxy DLL hijacking when the game starts

    const char* gameProcessNames[] = { NULL, NULL };
    if (gameKey) {
        if (strcmp(gameKey, "CollegeFB27") == 0) {
            gameProcessNames[0] = "CollegeFB27.exe";
            gameProcessNames[1] = "CollegeFB27_Trial.exe";
        } else if (strcmp(gameKey, "Madden26") == 0) {
            gameProcessNames[0] = "Madden26.exe";
            gameProcessNames[1] = "Madden26_Trial.exe";
        } else if (strcmp(gameKey, "Madden27") == 0) {
            gameProcessNames[0] = "Madden27.exe";
            gameProcessNames[1] = "Madden27_Trial.exe";
        } else if (strcmp(gameKey, "Madden25") == 0) {
            gameProcessNames[0] = "Madden25.exe";
            gameProcessNames[1] = "Madden25_Trial.exe";
        }
    }

    if (!silent) printf("Waiting for game...\n");

    DWORD gamePid = 0;
    for (int i = 0; i < 600; i++) {
        for (int j = 0; j < 2 && gameProcessNames[j]; j++) {
            gamePid = FindProcessId(gameProcessNames[j]);
            if (gamePid != 0) break;
        }
        if (gamePid != 0) break;
        Sleep(100);
    }

    if (gamePid != 0) {
        if (!silent) printf("Game running (PID: %u). Waiting for exit...\n", gamePid);
        HANDLE hProc = OpenProcess(SYNCHRONIZE, FALSE, gamePid);
        if (hProc) {
            WaitForSingleObject(hProc, INFINITE);
            CloseHandle(hProc);
        }
    } else {
        if (!silent) printf("Game process not found.\n");
    }

    if (!silent) printf("Done.\n");
    return 0;
}
