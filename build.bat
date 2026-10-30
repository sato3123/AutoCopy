@echo off
setlocal
cd /d "%~dp0"
echo.
echo 自動コピー をビルドします...
echo.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [エラー] .NET Framework 4 のコンパイラ csc.exe が見つかりません。
  goto fail
)

rem --- .NET Framework 4.8（Release 528040 以上）が入っているか確認 ---
set REL=0
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set REL=%%a
set /a RELNUM=%REL%
if %RELNUM% LSS 528040 (
  echo [エラー] .NET Framework 4.8 がインストールされていません。
  echo          先に .NET Framework 4.8 をインストールしてから、もう一度実行してください。
  goto fail
)

if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32icon:app.ico /out:dist\AutoCopy.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Security.dll src\*.cs
if errorlevel 1 goto fail

copy /y src\AutoCopy.exe.config dist\AutoCopy.exe.config >nul
copy /y "操作説明書.html" "dist\操作説明書.html" >nul
echo.
echo ビルドが完了しました。
echo   %CD%\dist\AutoCopy.exe
echo.
echo dist フォルダの中身（AutoCopy.exe / AutoCopy.exe.config / 操作説明書.html）を
echo 好きな場所に置いて AutoCopy.exe を起動してください。
echo.
if not "%CI%"=="true" pause
exit /b 0

:fail
echo.
echo ビルドに失敗しました。上のメッセージを確認してください。
if not "%CI%"=="true" pause
exit /b 1
