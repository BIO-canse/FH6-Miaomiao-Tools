@echo off
setlocal

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo Cannot find csc.exe.
  exit /b 1
)

"%CSC%" /nologo /optimize+ /target:exe /out:AutoInputLoop.exe Program.cs
exit /b %ERRORLEVEL%
