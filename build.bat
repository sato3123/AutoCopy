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
copy /y "自動コピー_署名証明書.cer" "dist\自動コピー_署名証明書.cer" >nul
copy /y "署名証明書を登録.bat" "dist\署名証明書を登録.bat" >nul

rem --- デジタル署名（署名用の .pfx があるときだけ） ---
rem   set AUTOCOPY_SIGN_PFX=C:\どこか\自動コピー_署名用.pfx
rem   set AUTOCOPY_SIGN_PASS=パスワード
rem   を設定してから build.bat を実行すると、dist\AutoCopy.exe に署名します。
if "%AUTOCOPY_SIGN_PFX%"=="" (
  echo [注意] AUTOCOPY_SIGN_PFX が設定されていないため、デジタル署名はしていません。
  echo        署名なしの版は、署名付きで動いているPCの自動更新には使われません。
  goto signed
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:AUTOCOPY_SIGN_PFX, $env:AUTOCOPY_SIGN_PASS); $r = Set-AuthenticodeSignature -FilePath 'dist\AutoCopy.exe' -Certificate $c -HashAlgorithm SHA256; if ($r.Status -ne 'Valid' -and $r.Status -ne 'UnknownError') { Write-Host $r.StatusMessage; exit 1 }; Write-Host ('署名しました：' + $c.Subject)"
if errorlevel 1 (
  echo [エラー] デジタル署名に失敗しました。.pfx の場所とパスワードを確認してください。
  goto fail
)
:signed
echo.
echo ビルドが完了しました。
echo   %CD%\dist\AutoCopy.exe
echo.
echo dist フォルダの中身（AutoCopy.exe / AutoCopy.exe.config / 操作説明書.html ほか）を
echo 好きな場所に置いて AutoCopy.exe を起動してください。
echo.
if not "%CI%"=="true" pause
exit /b 0

:fail
echo.
echo ビルドに失敗しました。上のメッセージを確認してください。
if not "%CI%"=="true" pause
exit /b 1
