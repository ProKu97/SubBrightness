@echo off
rem Build SubBrightness.exe with the C# compiler that ships with Windows (no Visual Studio needed)
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" -nologo -target:winexe -codepage:65001 -optimize+ -out:SubBrightness.build.exe -reference:System.dll -reference:System.Drawing.dll -reference:System.Windows.Forms.dll -reference:System.Management.dll SubBrightness.cs
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
move /y SubBrightness.build.exe SubBrightness.exe >nul 2>&1
if errorlevel 1 (
  echo SubBrightness.exe is running - new build kept as SubBrightness.build.exe
) else (
  echo OK: SubBrightness.exe
)
