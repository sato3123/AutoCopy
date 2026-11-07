# 前の版（v1.13.0）から今の版へ、実際に自動更新されるかを確かめる。
# v1.13.0 をそのソース（コミット 6b9509f）からビルドし、今の版と同じテスト用の証明書で署名して、
# v1.13.0 を動かした状態で置き場所に今の版を置き、更新・起動し直し・設定と記録の引き継ぎを確かめる。
# （正式な証明書は CI に置かないため、テスト用の証明書で同じ仕組みを確かめる）
$ErrorActionPreference = 'Stop'
$results = New-Object System.Collections.Generic.List[string]
$fail = 0
function Check($id, $name, $ok, $detail) {
    $s = if ($ok) { 'OK' } else { 'NG'; $script:fail++ }
    $results.Add("| $id | $name | $s | $detail |")
    Write-Host "[$s] $id $name $detail"
}
function WaitFor($cond, $sec) {
    $end = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 500 }
    return $false
}
function Fresh($name) {
    $d = Join-Path $env:RUNNER_TEMP $name
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    New-Item -ItemType Directory -Force $d | Out-Null
    return $d
}
function Ver($p) { (Get-Item $p).VersionInfo.FileVersion }

$oldCommit = '6b9509f5cfb25fb08353a7c8dad788f789a0e7d7'
$newVer = Ver 'dist\AutoCopy.exe'
Write-Host "今の版: $newVer"

# ---------- v1.13.0 をビルド ----------
$oldSrc = Fresh 'v113-src'
git fetch --quiet --depth=1 origin $oldCommit
if ($LASTEXITCODE -ne 0) { throw "v1.13.0 のコミットを取得できません" }
git archive --format=zip -o "$env:RUNNER_TEMP\v113.zip" $oldCommit src app.ico
Expand-Archive "$env:RUNNER_TEMP\v113.zip" $oldSrc
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$oldBuild = Fresh 'v113-build'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/win32icon:$oldSrc\app.ico" "/out:$oldBuild\AutoCopy.exe" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Security.dll `
  (Get-ChildItem "$oldSrc\src" -Filter *.cs | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) { throw "v1.13.0 をビルドできません" }
Check 'U01' 'v1.13.0 をそのソースからビルド' ((Ver "$oldBuild\AutoCopy.exe") -eq '1.13.0.0') ("版: " + (Ver "$oldBuild\AutoCopy.exe"))

# ---------- 両方を同じテスト用の証明書で署名 ----------
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=自動コピー 更新テスト' -CertStoreLocation Cert:\CurrentUser\My
$app = Fresh 'u-app'; $upd = Fresh 'u-upd'
Copy-Item "$oldBuild\AutoCopy.exe" $app
Copy-Item "$oldSrc\src\AutoCopy.exe.config" $app
Set-AuthenticodeSignature -FilePath "$app\AutoCopy.exe" -Certificate $cert -HashAlgorithm SHA256 | Out-Null
Copy-Item 'dist\*' $upd
Set-AuthenticodeSignature -FilePath "$upd\AutoCopy.exe" -Certificate $cert -HashAlgorithm SHA256 | Out-Null
$appExe = "$app\AutoCopy.exe"
function AppProcs { Get-Process -Name AutoCopy -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe } }

# ---------- v1.13.0 の設定（コピーのジョブ1つ・自動更新あり） ----------
$homeDir = Fresh 'u-home'
$env:AUTOCOPY_HOME = $homeDir
$src = Fresh 'u-src'; $dst = Fresh 'u-dst'
Set-Content -Path "$src\a.txt" -Value 'v1.13 でコピー' -Encoding utf8
(Get-Item "$src\a.txt").LastWriteTime = (Get-Date).AddMinutes(-5)
$settings = [ordered]@{
  FormatVersion = 1; InstanceId = 'u-instance'; FolderName = 'UPG'; LocalLogDays = 90; NetLogDays = 365; TrashDays = 30
  AutoStart = $false; NotifyErrors = $true; NotifyConfirm = $true; NotifyDone = $false; DistVersion = 0
  UpdateFolder = ''; AutoUpdate = $true; AttentionBlink = $true; AttentionPopup = $false; AttentionRemindMinutes = 120; ShortcutRenamed = $true
  Jobs = @([ordered]@{
    Id = 'upg1'; Name = '更新テスト'; Enabled = $true; Scope = 'local'; Source = $src; IncludeSubdirs = $true; Dest = $dst
    IntervalMinutes = 600; UseStartupDelay = $true; StartupDelayMinutes = 60; NewerDestPolicy = 'ask'; ToleranceSeconds = 2
    FollowRename = $true; RetryCount = 0; RetryIntervalSeconds = 5; MirrorMaxCount = 100; MirrorMaxPercent = 10
    DupCheckEnabled = $false; DupSchedule = 'manual'; DupTime = '12:00'; WindowStart = '08:00'; WindowEnd = '20:00'
  })
}
New-Item -ItemType Directory -Force "$homeDir\roaming" | Out-Null
$settings | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "$homeDir\roaming\settings.json"
$log = Join-Path $homeDir ("local\logs\" + (Get-Date -Format 'yyyy-MM-dd') + '.log')
function Log { if (Test-Path $log) { Get-Content -Encoding utf8 $log -Raw } else { '' } }

# ---------- v1.13.0 で動かしてコピー（記録を作る） ----------
$p13 = Start-Process -FilePath $appExe -ArgumentList '/startup' -PassThru
Start-Sleep -Seconds 5
Start-Process -FilePath $appExe -ArgumentList '/run "更新テスト"' -Wait
$ok = WaitFor { (Test-Path "$dst\a.txt") -and ((Log) -match "`tSUMMARY`t") } 30
Check 'U02' 'v1.13.0 が常駐してコピーする（記録ができる）' ($ok -and -not $p13.HasExited) ''
$stateFile = "$homeDir\local\state\upg1.dat"
Check 'U03' 'v1.13.0 の状態ファイルがある' (Test-Path $stateFile) $stateFile

# ---------- 置き場所を設定して、更新の確認 ----------
# 置き場所は動いている間は設定画面から変えるので、ここでは設定ファイルを書き換えてから起動し直す
AppProcs | Stop-Process -Force
Start-Sleep -Seconds 2
$settings.UpdateFolder = $upd
$settings | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "$homeDir\roaming\settings.json"
$p13 = Start-Process -FilePath $appExe -ArgumentList '/startup' -PassThru
Start-Sleep -Seconds 5
Start-Process -FilePath $appExe -ArgumentList '/checkupdate' -Wait
$ok = WaitFor { (Ver $appExe) -eq $newVer -and $p13.HasExited -and (AppProcs) } 90
Check 'U04' "v1.13.0 が今の版（$newVer）に自動更新し、起動し直す" $ok ("版: " + (Ver $appExe) + "・v1.13 終了: " + $p13.HasExited)
$ok = WaitFor { (Log) -match ("新しい版（" + [regex]::Escape($newVer) + "）で起動し直しました") } 30
Check 'U05' '更新したことをログに記録' $ok ''
Check 'U06' '前の版を AutoCopy.exe.old として残す' ((Test-Path "$appExe.old") -and ((Get-Item "$appExe.old").VersionInfo.FileVersion -eq '1.13.0.0')) ''
$manualOk = (Test-Path "$app\操作説明書.html") -and ((Get-FileHash "$app\操作説明書.html").Hash -eq (Get-FileHash "$upd\操作説明書.html").Hash)
$certOk = (Test-Path "$app\自動コピー_署名証明書.cer") -and (Test-Path "$app\署名証明書を登録.bat")
Check 'U07' '説明書・証明書・登録用の bat も新しいものになる' ($manualOk -and $certOk) ("説明書: $manualOk・証明書: $certOk")
Start-Sleep -Seconds 8
Check 'U08' '今の版が動き続ける（すぐ終わらない・前の版に戻らない）' ((AppProcs) -and (Ver $appExe) -eq $newVer) ''

# ---------- 今の版で：設定と記録を引き継いでいる ----------
$cut = (Log).Length
Set-Content -Path "$src\b.txt" -Value '今の版でコピー' -Encoding utf8
(Get-Item "$src\b.txt").LastWriteTime = (Get-Date).AddMinutes(-5)
Start-Process -FilePath $appExe -ArgumentList '/run "更新テスト"' -Wait
$ok = WaitFor { (Test-Path "$dst\b.txt") -and ((Log).Substring($cut) -match "`tSUMMARY`t") } 30
$after = (Log).Substring($cut)
Check 'U09' '今の版でも同じジョブ（設定を引き継ぐ）でコピーする' $ok ''
$recopy = $after -match "`tCOPY`ta\.txt" -or $after -match "`tOVERWRITE`ta\.txt"
Check 'U10' 'v1.13.0 の記録を読める（コピー済みのものをコピーし直さない）' (-not $recopy -and $after -notmatch '状態ファイル') ''
$errors = @(($after -split "`n") | Where-Object { $_ -match "`tERROR`t" })
Check 'U11' '更新後にエラーが出ない' ($errors.Count -eq 0) ($errors -join ' / ')
Check 'U12' 'コピー元は変更されていない' (((Get-Content -Encoding utf8 "$src\a.txt") -eq 'v1.13 でコピー') -and ((Get-Content -Encoding utf8 "$dst\a.txt") -eq 'v1.13 でコピー')) ''

AppProcs | Stop-Process -Force
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue

$md = @('| No | テスト | 結果 | メモ |', '|---|---|---|---|') + $results
$md | Out-File -Encoding utf8 upgrade-results.md
Write-Host ""
Write-Host ("v1.13.0 からの更新：" + $results.Count + " 件中 NG " + $fail + " 件")
if ($fail -gt 0) { exit 1 }
