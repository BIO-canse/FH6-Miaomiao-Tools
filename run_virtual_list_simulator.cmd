@echo off
cd /d "%~dp0"
if not exist bin-verify mkdir bin-verify
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:FH6SkillPointOcr.VirtualListSimulationProgram /out:bin-verify\VirtualListSimulator.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll shared-cs\FH6AutomationConstants.cs src-cs\Models.cs src-cs\OcrLanguageFilter.cs src-cs\OcrMatchFilter.cs src-cs\VehicleGridOcrPolicy.cs src-cs\VirtualVehicleList.cs src-cs\VirtualVehicleList.OcrWrite.cs sim-cs\VirtualListSimulator.cs
if errorlevel 1 exit /b 1
bin-verify\VirtualListSimulator.exe
exit /b %ERRORLEVEL%
