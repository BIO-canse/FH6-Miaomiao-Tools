@echo off
setlocal

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo Cannot find csc.exe.
  exit /b 1
)

set BASE=%~dp0..
if not exist "%BASE%\bin" mkdir "%BASE%\bin"

"%CSC%" /nologo /optimize+ /target:exe /out:"%BASE%\bin\EnterTapLoop.exe" "%BASE%\src\EnterTapLoop.cs"
exit /b %ERRORLEVEL%
