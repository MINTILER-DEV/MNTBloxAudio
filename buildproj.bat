@echo off
setlocal

cd /d "%~dp0"

echo.
echo Publishing MNTBloxAudio as a single-file Release build...
echo.
echo This will place the published executable in the repo's publish folder.
echo Close MNTBloxAudio first if it is currently running.
echo.

if exist ".\publish" (
    echo Cleaning existing publish folder...
    rmdir /s /q ".\publish"
)

dotnet publish ".\MNTBloxAudio.App\MNTBloxAudio.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o ".\publish\"

if errorlevel 1 (
    echo.
    echo Publish failed.
    exit /b %errorlevel%
)

echo.
echo Publish complete.
exit /b 0
