@echo off
setlocal
cd /d "%~dp0"
rem Change this if you fork the project
set "REPO=filipembcruz/pulsedock"
set "URL=https://github.com/%REPO%.git"

where git >nul 2>nul
if errorlevel 1 (
  echo  Git not found. Install it with: winget install Git.Git
  pause
  exit /b 1
)

rem ---- 1. Local repository ----
if not exist ".git" git init -q
git remote get-url origin >nul 2>nul
if errorlevel 1 git remote add origin %URL%

rem ---- 2. Create the GitHub repository if it doesn't exist yet ----
git ls-remote origin >nul 2>nul
if not errorlevel 1 goto :have_remote
where gh >nul 2>nul
if not errorlevel 1 (
  gh repo create %REPO% --public --description "PulseDock - always-visible system monitor for Windows (CPU, memory, disk, GPU, network)" >nul
  goto :have_remote
)
echo.
echo  Create an EMPTY, PUBLIC repository named "pulsedock" at:
echo    https://github.com/new
echo  ^(no README, .gitignore or license^). Then press any key.
start "" "https://github.com/new?name=pulsedock&visibility=public"
pause

:have_remote
rem ---- 3. Continue the existing GitHub history ----
rem If GitHub already has commits, put this folder's files on top of them.
rem The files in THIS folder win (they are the newest version of the project).
git fetch -q origin main >nul 2>nul
if not errorlevel 1 (
  git reset -q --soft FETCH_HEAD
)
git branch -M main >nul 2>nul

rem ---- 4. Commit and push ----
git add -A
git commit -q -m "PulseDock update" >nul 2>nul
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