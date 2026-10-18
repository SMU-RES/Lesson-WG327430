@echo off
chcp 936 >nul
setlocal
set "PLC_APP=%~dp0汽车上位机.exe"
if not exist "%PLC_APP%" (
  echo 请先完整解压压缩包，再启动程序。
  pause
  exit /b 1
)
start "" /D "%~dp0" "%PLC_APP%"
exit /b 0
