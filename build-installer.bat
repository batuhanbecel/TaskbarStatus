@echo off
rem Builds dist\TaskbarStatus-Setup-<version>.exe (needs Inno Setup 6).
setlocal
set ROOT=%~dp0
set PAWNIO_VERSION=2.2.0
set PAWNIO_SHA256=1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032

rem --skip-exe: reuse an already built (e.g. signed) TaskbarStatus.exe.
if not "%~1"=="--skip-exe" (call "%ROOT%build.bat" || exit /b 1)
if not exist "%ROOT%vendor\modules\COPYING" (call "%ROOT%build.bat" || exit /b 1)

rem The PawnIO driver installer is bundled so the setup works offline.
if not exist "%ROOT%vendor\PawnIO_setup.exe" (
  echo Downloading PawnIO %PAWNIO_VERSION%...
  curl -sSfL -o "%ROOT%vendor\PawnIO_setup.exe" https://github.com/namazso/PawnIO.Setup/releases/download/%PAWNIO_VERSION%/PawnIO_setup.exe || exit /b 1
)
powershell -NoProfile -Command "if ((Get-FileHash '%ROOT%vendor\PawnIO_setup.exe').Hash -ne '%PAWNIO_SHA256%') { Write-Error 'PawnIO_setup.exe hash mismatch'; exit 1 }" || exit /b 1

set ISCC=
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do if exist %%P set ISCC=%%P
if not defined ISCC (
  echo Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup
  exit /b 1
)
%ISCC% /Q "%ROOT%installer\TaskbarStatus.iss"
exit /b %errorlevel%
