@echo off
rem Builds TaskbarStatus.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set ROOT=%~dp0
set MODULES_VERSION=0.2.11

rem PawnIO modules (signed by the PawnIO author, LGPL-2.1) are embedded into the exe.
if not exist "%ROOT%vendor\modules\AMDFamily17.bin" (
  echo Downloading PawnIO modules %MODULES_VERSION%...
  if not exist "%ROOT%vendor\modules" mkdir "%ROOT%vendor\modules"
  curl -sSfL -o "%ROOT%vendor\pawnio-modules.zip" https://github.com/namazso/PawnIO.Modules/releases/download/%MODULES_VERSION%/release_%MODULES_VERSION:.=_%.zip || exit /b 1
  tar -xf "%ROOT%vendor\pawnio-modules.zip" -C "%ROOT%vendor\modules" || exit /b 1
)

"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ^
  /win32manifest:"%ROOT%app.manifest" /win32icon:"%ROOT%assets\app.ico" /out:"%ROOT%TaskbarStatus.exe" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll ^
  /resource:"%ROOT%vendor\modules\AMDFamily17.bin",AMDFamily17.bin ^
  /resource:"%ROOT%vendor\modules\IntelMSR.bin",IntelMSR.bin ^
  "%ROOT%src\*.cs"
exit /b %errorlevel%
