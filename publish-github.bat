@echo off
setlocal
cd /d "%~dp0"
rem Change this if you fork the project
set "REPO=filipembcruz/pulsedock"

where git >nul 2>nul
if errorlevel 1 (
  echo  Git not found. Install it with: winget install Git.Git
  pause
  exit /b 1
)

if not exist ".git" (
  git init >nul
  git checkout -b main >nul 2>nul
)
git add -A
git commit -m "PulseDock update" >nul 2>nul
git branch -M main

git remote get-url origin >nul 2>nul
if errorlevel 1 (
  where gh >nul 2>nul
  if not errorlevel 1 (
    rem GitHub CLI available: create the public repository automatically
    gh repo view %REPO% >nul 2>nul
    if errorlevel 1 gh repo create %REPO% --public --description "PulseDock - always-visible system monitor for Windows (CPU, memory, disk, GPU, network)" >nul
  ) else (
    echo.
    echo  Create an EMPTY, PUBLIC repository named "pulsedock" at:
    echo    https://github.com/new
    echo  ^(no README, .gitignore or license^). Then press any key.
    start "" "https://github.com/new?name=pulsedock&visibility=public"
    pause
  )
  git remote add origin https://github.com/%REPO%.git
)

echo Pushing to https://github.com/%REPO% ...
git push -u origin main
if errorlevel 1 (
  echo.
  echo  Push failed. See the message above.
  pause
  exit /b 1
)

echo.
echo  Done! GitHub is building the .exe now (about 3 minutes).
echo  It will appear at: https://github.com/%REPO%/releases
start "" "https://github.com/%REPO%/actions"
pause
endlocal
