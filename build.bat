@echo off
setlocal enabledelayedexpansion

set APPNAME=IDManager
set SRC=src
set BIN=bin
set OUTEXE=%BIN%\%APPNAME%.exe

if not exist "%BIN%" mkdir "%BIN%"

set VBC=
for %%v in (v4.0.30319 v4.0.30128 v4.0.21006 v4.0.20506) do (
    if exist "%WINDIR%\Microsoft.NET\Framework\%%v\vbc.exe" (
        set "VBC=%WINDIR%\Microsoft.NET\Framework\%%v\vbc.exe"
    )
)
if "%VBC%"=="" (
    if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\vbc.exe" (
        set "VBC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\vbc.exe"
    )
)

if "%VBC%"=="" (
    echo [LOI] Khong tim thay vbc.exe cho .NET Framework 4.x
    echo Kiem tra thu muc %WINDIR%\Microsoft.NET\Framework\ hoac Framework64\
    pause
    exit /b 1
)

echo Dung trinh bien dich: %VBC%
echo Dang build %OUTEXE% ...
echo.

"%VBC%" /nologo /target:winexe /out:%OUTEXE% /optimize+ /optionstrict+ /optionexplicit+ ^
    /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll ^
    %SRC%\Program.vb %SRC%\Form1.vb %SRC%\FileDownloadData.vb %SRC%\FileListBuilder.vb ^
    %SRC%\DownloadItem.vb %SRC%\FileDownloader.vb %SRC%\HlsDownloader.vb ^
    %SRC%\DownloadQueueManager.vb %SRC%\DownloadQueueState.vb %SRC%\BrowserBridgeServer.vb ^
    %SRC%\AddUrlDialog.vb %SRC%\CreateListDialog.vb %SRC%\DownloadFromListDialog.vb ^
    %SRC%\SettingsDialog.vb %SRC%\BrowserDownloadPromptDialog.vb

if errorlevel 1 (
    echo.
    echo [LOI] Build that bai.
    pause
    exit /b 1
) else (
    echo.
    echo [OK] Build thanh cong: %OUTEXE%
)

pause
