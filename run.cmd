@echo off
cd /d "%~dp0"
if not exist "bin\FH6SkillPointOcr.exe" (
  echo Missing bin\FH6SkillPointOcr.exe. Run build_cs.cmd first.
  pause
  exit /b 1
)
"bin\FH6SkillPointOcr.exe" --config config\default.json
pause
