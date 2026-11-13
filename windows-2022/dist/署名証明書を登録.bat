@echo off
rem 自動コピー：デジタル署名の証明書を、このPCに登録します。
rem 登録すると、AutoCopy.exe の発行元が「自動コピー」として信頼されます（「不明な発行元」と出なくなります）。
rem この証明書はプログラムの署名専用です（ほかの証明書を発行することはできません）。
setlocal
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo このファイルは「管理者として実行」してください。
  echo （右クリック →「管理者として実行」）
  pause
  exit /b 1
)
set CER=
for %%f in (*署名証明書.cer) do set "CER=%%f"
if "%CER%"=="" for %%f in (*.cer) do set "CER=%%f"
if "%CER%"=="" (
  echo 証明書（自動コピー_署名証明書.cer）が同じフォルダにありません。
  pause
  exit /b 1
)
certutil -addstore -f Root "%CER%"
if errorlevel 1 goto fail
certutil -addstore -f TrustedPublisher "%CER%"
if errorlevel 1 goto fail
echo.
echo 登録しました。
pause
exit /b 0
:fail
echo.
echo 登録できませんでした。
pause
exit /b 1
