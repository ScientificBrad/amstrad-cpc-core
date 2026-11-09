@echo off
echo ===================================================
echo  Amstrad CPC Emulator - Clean Build ^& Publish
echo ===================================================
echo.
echo Restoring and building solution...
dotnet build
if %errorlevel% neq 0 (
    echo Build failed!
    pause
    exit /b %errorlevel%
)
echo.
echo Publishing AmstradCPC.exe to root folder...
dotnet publish src\AmstradCPC.Emulator\AmstradCPC.Emulator.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .
if %errorlevel% neq 0 (
    echo Publish failed!
    pause
    exit /b %errorlevel%
)
echo.
echo Done! AmstradCPC.exe is ready to launch.
