@echo off
chcp 65001 >nul
setlocal

set "PROJECT_PATH=D:\code\AIDATA_F1\src\WarrenqF1.MarketServer\WarrenqF1.MarketServer.csproj"
set "OUTPUT_DIR=D:\Publish\WarrenqF1\marketserver\linux-x64"

where dotnet >nul 2>&1
if errorlevel 1 (
	echo 错误：未找到 dotnet，请先安装 .NET 10 SDK。
	exit /b 1
)

if not exist "%PROJECT_PATH%" (
	echo 错误：项目文件不存在："%PROJECT_PATH%"
	exit /b 1
)

echo 正在发布 WarrenqF1.MarketServer Linux x64 AOT 程序...
dotnet publish "%PROJECT_PATH%" ^
	--configuration Release ^
	--runtime linux-x64 ^
	--self-contained true ^
	--output "%OUTPUT_DIR%" ^
	-p:PublishAot=true ^
	-p:PublishTrimmed=true ^
	-p:GLibcVersion=2.28 ^
	-p:StripSymbols=true ^
	-p:DebugSymbols=false ^
	-p:DebugType=None ^
	-p:CopyOutputSymbolsToPublishDirectory=false

set "PUBLISH_EXIT_CODE=%ERRORLEVEL%"
if not "%PUBLISH_EXIT_CODE%"=="0" (
	echo 错误：Linux x64 AOT 发布失败。
	exit /b %PUBLISH_EXIT_CODE%
)

if not exist "%OUTPUT_DIR%\WarrenqF1.MarketServer" (
	echo 错误：发布目录中未找到 Linux 可执行文件。
	exit /b 1
)

echo 发布完成："%OUTPUT_DIR%"
exit /b 0
