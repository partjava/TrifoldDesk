@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "TRIFOLD_VERSION=0.4"
if exist "current-version.txt" set /p "TRIFOLD_VERSION=" < "current-version.txt"
if not exist "dist\v%TRIFOLD_VERSION%\TrifoldDesk.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1" -Version "%TRIFOLD_VERSION%"
    if errorlevel 1 (
        echo 构建失败，请查看上方输出。
        pause
        exit /b 1
    )
)
start "" "%~dp0dist\v%TRIFOLD_VERSION%\TrifoldDesk.exe" --show
