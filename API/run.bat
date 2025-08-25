@echo off
setlocal

REM Change working directory to solution root (parent of API)
pushd "%~dp0.."

echo Starting Bot.Worker...
start "Bot.Worker" cmd /c dotnet run --project "Bot.Worker/Bot.Worker.csproj"
timeout /t 2 /nobreak >nul

echo Starting API...
start "API" cmd /c dotnet run --project "API/API.csproj"
timeout /t 2 /nobreak >nul

echo Starting WebApp...
start "WebApp" cmd /c dotnet run --project "WebApp/WebApp.csproj"

echo All processes started. Close the opened windows or stop the processes to shut down.
pause >nul

popd
endlocal
