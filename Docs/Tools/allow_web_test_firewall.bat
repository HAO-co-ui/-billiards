@echo off
REM One-time helper: allow inbound TCP 8765 on the PRIVATE network profile so a phone
REM on the same Wi-Fi can open the local WebGL build.
REM It self-elevates (UAC prompt) because firewall rules require administrator rights.

net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo Adding firewall rule "BilliardsWebTest8765" ^(TCP 8765, private^)...
netsh advfirewall firewall delete rule name="BilliardsWebTest8765" >nul 2>&1
netsh advfirewall firewall add rule name="BilliardsWebTest8765" dir=in action=allow protocol=TCP localport=8765 profile=private
if errorlevel 1 (
  echo [ERROR] Failed to add the rule.
) else (
  echo [OK] Done. Your phone can now open: http://^<PC-LAN-IP^>:8765
)
echo.
pause
