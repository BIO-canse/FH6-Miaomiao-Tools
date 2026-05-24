@echo off
cd /d "%~dp0"
if not exist bin mkdir bin
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.EmergencyStopWatcherProgram /out:bin\FH6EmergencyStopWatcher.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\*.cs src-cs\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.Program /out:bin\FH6SkillPointOcr.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\*.cs src-cs\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.Program /out:bin\FH6VehicleDeleteOcr.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\*.cs src-cs\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.Program /out:bin\FH6FullAuto.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\*.cs src-cs\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.Program /out:bin\FH6BlueprintCycleTest.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\*.cs src-cs\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.BuyPreludeDebugProgram /out:bin\FH6BuyPreludeStepDebug.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\*.cs src-cs\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
if exist legacy-scripts\src\MinuteWLoop.cs (
  "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:exe /platform:x64 /out:bin\MinuteWLoop.exe shared-cs\*.cs legacy-scripts\src\MinuteWLoop.cs
  if errorlevel 1 (
    pause
    exit /b 1
  )
) else (
  echo Skipped bin\MinuteWLoop.exe source not found
)
if exist legacy-scripts\src\AutoInputLoop.cs (
  "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:exe /platform:x64 /out:bin\AutoInputLoop.exe shared-cs\*.cs legacy-scripts\src\AutoInputLoop.cs
  if errorlevel 1 (
    pause
    exit /b 1
  )
) else (
  echo Skipped bin\AutoInputLoop.exe source not found
)
if exist legacy-scripts\src\EnterTapLoop.cs (
  "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:exe /platform:x64 /out:bin\EnterTapLoop.exe shared-cs\*.cs legacy-scripts\src\EnterTapLoop.cs
  if errorlevel 1 (
    pause
    exit /b 1
  )
) else (
  echo Skipped bin\EnterTapLoop.exe source not found
)
if exist legacy-scripts\src\SpaceDownEnterLoop.cs (
  "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:exe /platform:x64 /out:bin\SpaceDownEnterLoop.exe shared-cs\*.cs legacy-scripts\src\SpaceDownEnterLoop.cs
  if errorlevel 1 (
    pause
    exit /b 1
  )
) else (
  echo Skipped bin\SpaceDownEnterLoop.exe source not found
)
echo Built bin\FH6SkillPointOcr.exe
echo Built bin\FH6EmergencyStopWatcher.exe
echo Built bin\FH6VehicleDeleteOcr.exe
echo Built bin\FH6FullAuto.exe
echo Built bin\FH6BlueprintCycleTest.exe
echo Built bin\FH6BuyPreludeStepDebug.exe
echo Built bin\MinuteWLoop.exe
echo Built bin\AutoInputLoop.exe
echo Built bin\EnterTapLoop.exe
echo Built bin\SpaceDownEnterLoop.exe
pause
