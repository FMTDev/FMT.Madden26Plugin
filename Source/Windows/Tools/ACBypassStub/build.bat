@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1

cd /d "%~dp0"

cl /nologo /O2 /MT /Fe:ACBypassStub.exe /SUBSYSTEM:CONSOLE main.c kernel32.lib advapi32.lib user32.lib

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    dir ACBypassStub.exe
) else (
    echo Build failed!
)
pause
