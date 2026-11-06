@echo off
chcp 936 >nul
setlocal enabledelayedexpansion

echo ��ʼ���...
set "ReleasePath=%~1"
set "CurrentTerminalVersion=%~2"
if "%~1"=="" (
    set "PackagePath=%~dp0..\Release"
    set "ReleasePath=%PackagePath%"
)
if not defined CurrentTerminalVersion set /p "CurrentTerminalVersion=请输入版本号: "
set "CurrentTerminalVersion=%CurrentTerminalVersion: =%"
if not defined CurrentTerminalVersion (
    echo 必须输入版本号。
    exit /b 1
)
if not "%~1"=="" if not "%~2"=="" set "AUTOMATED=1"
if not exist "%ReleasePath%\" (
    echo δ�ҵ������ļ�Ŀ¼: "%ReleasePath%"
    exit /b 1
)

set "NsisCmd=D:\soft\NSIS3.12\NSIS\makensis.exe"
if not exist "%NsisCmd%" (
    echo δ�ҵ� NSIS ������: "%NsisCmd%"
    exit /b 1
)

for %%I in ("%~dp0.") do set "MakeDIR=%%~fI"
set "NsisScriptFileName=%MakeDIR%\CEIS.nsi"
for %%I in ("%ReleasePath%\..") do set "ReleaseRoot=%%~fI"
set "OutputPath=%ReleaseRoot%\output\setup"
if not exist "%OutputPath%" md "%OutputPath%"
if not exist "%OutputPath%" (
    echo �޷��������Ŀ¼: "%OutputPath%"
    exit /b 1
)
set "BackupPath=%ReleaseRoot%\output"

echo ���ڴ��...
"%NsisCmd%" /V1 /DRUNDIR="%BackupPath%" /DISREMOVEUSERS=0 /DSCRIPTDIR="%MakeDIR%" /DPRODUCT_VERSION="%CurrentTerminalVersion%" /DPRODUCT_MAINVERSION="%CurrentTerminalVersion%" /DReleaseFile="%ReleasePath%" /DOutPutFile="%OutputPath%\%CurrentTerminalVersion%.exe" "%NsisScriptFileName%"
if errorlevel 1 (
    echo ���ʧ�ܡ�
    if not defined AUTOMATED pause
    exit /b %errorlevel%
)

echo ����ɹ�: %OutputPath%\%CurrentTerminalVersion%.exe
if not defined AUTOMATED pause
