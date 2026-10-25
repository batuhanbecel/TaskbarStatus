@echo off
setlocal
pushd "%~dp0.."
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /out:tests\OverlayLayoutTests.exe /r:System.Drawing.dll src\OverlayLayout.cs tests\OverlayLayoutTests.cs
if errorlevel 1 (
  popd
  exit /b 1
)
tests\OverlayLayoutTests.exe
set RESULT=%errorlevel%
popd
exit /b %RESULT%
