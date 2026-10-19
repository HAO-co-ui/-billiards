@echo off
REM Start a local static server for the WebGL build, then open it in the default browser.
REM Usage: double-click this file, or run it from a terminal.
REM Stop the server with Ctrl+C.

setlocal
set "PROJECT_DIR=%~dp0..\.."
set "WEB_DIR=%PROJECT_DIR%\Build\Web"

if not exist "%WEB_DIR%\index.html" (
  echo [ERROR] WebGL build not found: "%WEB_DIR%\index.html"
  echo         Build it first with:
  echo         unity_batch.sh buildweb
  pause
  exit /b 1
)

echo Serving: "%WEB_DIR%"
echo   PC   : http://127.0.0.1:8765
for /f "tokens=2 delims=:" %%i in ('ipconfig ^| findstr /c:"IPv4"') do (
  for /f "tokens=* delims= " %%j in ("%%i") do echo   Phone: http://%%j:8765
)
echo.
echo Keep this window open while testing. Press Ctrl+C to stop.
echo.

cd /d "%WEB_DIR%"
start "" http://127.0.0.1:8765/index.html
python -m http.server 8765 --bind 0.0.0.0

endlocal
