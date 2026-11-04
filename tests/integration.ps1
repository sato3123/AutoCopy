# 実際の AutoCopy.exe を動かす統合テスト（常駐・二重起動防止・/run の受け渡し・名前変更・初回画面）
$ErrorActionPreference = 'Stop'
$results = New-Object System.Collections.Generic.List[string]
$fail = 0
function Check($id, $name, $ok, $detail) {
    $s = if ($ok) { 'OK' } else { 'NG'; $script:fail++ }
    $line = "| $id | $name | $s | $detail |"
    $results.Add($line)
    Write-Host "[$s] $id $name $detail"
}
function WaitFor($cond, $sec) {
    $end = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 500 }
    return $false
}
function NewHome($name) {
    $h = Join-Path $env:RUNNER_TEMP $name
    if (Test-Path $h) { Remove-Item -Recurse -Force $h }
    New-Item -ItemType Directory -Force $h | Out-Null
    return $h
}
function Win($p) { $p.Refresh(); return $p.MainWindowTitle }

$exe = (Resolve-Path 'dist\AutoCopy.exe').Path

# ---------- I01 初回起動（設定なし）で初回セットアップ画面 ----------
$env:AUTOCOPY_HOME = NewHome 'int-first'
$p = Start-Process -FilePath $exe -PassThru
$ok = WaitFor { (Win $p) -like '*初回セットアップ*' } 20
Check 'I01' '設定がないときは初回セットアップ画面を出す' $ok ("ウィンドウ: " + (Win $p))
if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() }

# ---------- 準備：設定ファイル ----------
$home2 = NewHome 'int-home'
$env:AUTOCOPY_HOME = $home2
$src = Join-Path $env:RUNNER_TEMP 'int-src'; $dst = Join-Path $env:RUNNER_TEMP 'int-dst'
foreach ($d in $src, $dst) { if (Test-Path $d) { Remove-Item -Recurse -Force $d }; New-Item -ItemType Directory $d | Out-Null }
Set-Content -Path "$src\a.txt" -Value 'hello' -Encoding utf8
(Get-Item "$src\a.txt").LastWriteTime = (Get-Date).AddMinutes(-5)
$settings = [ordered]@{
  FormatVersion = 1; InstanceId = 'int-instance'; FolderName = 'INT'; LocalLogDays = 90; NetLogDays = 365; TrashDays = 30
  AutoStart = $false; NotifyErrors = $true; NotifyConfirm = $true; NotifyDone = $false; DistVersion = 0
  Jobs = @([ordered]@{
    Id = 'job1'; Name = '統合テスト'; Enabled = $true; Scope = 'local'; Source = $src; IncludeSubdirs = $true; Dest = $dst
    IntervalMinutes = 600; UseStartupDelay = $true; StartupDelayMinutes = 60; NewerDestPolicy = 'ask'; ToleranceSeconds = 2
    FollowRename = $true; RetryCount = 0; RetryIntervalSeconds = 5; MirrorMaxCount = 100; MirrorMaxPercent = 10
    DupCheckEnabled = $false; DupSchedule = 'manual'; DupTime = '12:00'; WindowStart = '08:00'; WindowEnd = '20:00'
  })
}
New-Item -ItemType Directory -Force "$home2\roaming" | Out-Null
$settings | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "$home2\roaming\settings.json"
$log = Join-Path $home2 ("local\logs\" + (Get-Date -Format 'yyyy-MM-dd') + '.log')

# ---------- I02 スタートアップ起動で常駐 ----------
$main = Start-Process -FilePath $exe -ArgumentList '/startup' -PassThru
Start-Sleep -Seconds 6
Check 'I02' 'スタートアップ起動で常駐する（画面は出さない）' (-not $main.HasExited) ("ウィンドウ: '" + (Win $main) + "'")
$ok = WaitFor { (Test-Path $log) -and ((Get-Content -Encoding utf8 $log -Raw) -match 'サインインにより起動') } 10
Check 'I03' '起動をログに記録' $ok ''
Check 'I04' '起動直後（待ち時間60分の設定）はまだコピーしない' (-not (Test-Path "$dst\a.txt")) ''

# ---------- I05 2つ目の起動 /run "ジョブ名" が動作中のアプリに届く ----------
$t0 = Get-Date
$second = Start-Process -FilePath $exe -ArgumentList '/run "統合テスト"' -PassThru
$exited = $second.WaitForExit(15000)
Check 'I05' '2つ目の起動はすぐ終わる（二重起動しない）' $exited ("{0:0.0}秒" -f ((Get-Date) - $t0).TotalSeconds)
$ok = WaitFor { Test-Path "$dst\a.txt" } 30
Check 'I06' 'ショートカット（/run "ジョブ名"）でコピーされる' $ok ''
Check 'I07' 'メインのアプリは動き続ける' (-not $main.HasExited) ''

# ---------- I08 /run（すべて） ----------
Set-Content -Path "$src\b.txt" -Value 'second' -Encoding utf8
(Get-Item "$src\b.txt").LastWriteTime = (Get-Date).AddMinutes(-5)
Start-Process -FilePath $exe -ArgumentList '/run' -Wait
$ok = WaitFor { Test-Path "$dst\b.txt" } 30
Check 'I08' 'ショートカット（/run）ですべて実行' $ok ''

# ---------- I09 名前変更への追従（実アプリ） ----------
Start-Sleep -Seconds 2
Rename-Item "$src\a.txt" 'renamed.txt'
Start-Process -FilePath $exe -ArgumentList '/run' -Wait
$ok = WaitFor { (Test-Path "$dst\renamed.txt") -and -not (Test-Path "$dst\a.txt") } 30
Check 'I09' 'ファイル名の変更がコピー先に反映' $ok ''
$ok = WaitFor { (Get-Content -Encoding utf8 $log -Raw) -match 'RENAME\ta.txt → renamed.txt' } 10
Check 'I10' 'ログに RENAME' $ok ''
Check 'I11' 'ログに SUMMARY' ((Get-Content -Encoding utf8 $log -Raw) -match "`tSUMMARY`t") ''
Check 'I12' 'コピー元は変更されていない' ((Get-Content "$src\renamed.txt") -eq 'hello') ''

# ---------- I13 引数なしの起動で動作中のアプリがメイン画面を開く ----------
Start-Process -FilePath $exe -Wait
$ok = WaitFor { (Win $main) -like '自動コピー*' } 15
Check 'I13' '引数なしで起動するとメイン画面が開く' $ok ("ウィンドウ: '" + (Win $main) + "'")

# ---------- I14 メイン画面を閉じても終了せず、通知領域に入る ----------
$closed = $main.CloseMainWindow()
Start-Sleep -Seconds 2
$ok = (-not $main.HasExited) -and ((Win $main) -eq '')
Check 'I14' '閉じるボタンでは終了せず通知領域に入る（ウィンドウは隠れる）' $ok ("終了: " + $main.HasExited + "・タイトル: '" + (Win $main) + "'")
Start-Process -FilePath $exe -Wait
$ok = WaitFor { (Win $main) -like '自動コピー*' } 15
Check 'I15' '通知領域に入った後も、もう一度起動するとメイン画面が開く' $ok ("ウィンドウ: '" + (Win $main) + "'")

# ---------- I16 コピー先の _AutoCopy は隠しフォルダ ----------
$meta = Get-Item -Force (Join-Path $dst '_AutoCopy') -ErrorAction SilentlyContinue
$ok = ($meta -ne $null) -and (($meta.Attributes -band [IO.FileAttributes]::Hidden) -ne 0)
Check 'I16' 'コピー先の _AutoCopy フォルダは隠しフォルダ' $ok ("属性: " + $(if ($meta) { $meta.Attributes } else { 'なし' }))

if (-not $main.HasExited) { $main.Kill(); $main.WaitForExit() }

# ---------- I17〜I21 自動更新（NAS などの置き場所に新しい版を置く） ----------
$updApp = Join-Path $env:RUNNER_TEMP 'upd-app'; $updSrc = Join-Path $env:RUNNER_TEMP 'upd-src'
foreach ($d in $updApp, $updSrc) { if (Test-Path $d) { Remove-Item -Recurse -Force $d }; New-Item -ItemType Directory $d | Out-Null }
Copy-Item 'dist\AutoCopy.exe', 'dist\AutoCopy.exe.config' $updApp
$appExe = Join-Path $updApp 'AutoCopy.exe'
$nextExe = (Resolve-Path 'dist-next\AutoCopy.exe').Path
function Ver($p) { (Get-Item $p).VersionInfo.FileVersion }
function AppProcs { Get-Process -Name AutoCopy -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe } }
$homeU = NewHome 'int-upd'
$env:AUTOCOPY_HOME = $homeU
$us = [ordered]@{
  FormatVersion = 1; InstanceId = 'upd-instance'; FolderName = 'UPD'; LocalLogDays = 90; NetLogDays = 365; TrashDays = 30
  AutoStart = $false; NotifyErrors = $true; NotifyConfirm = $true; NotifyDone = $false; DistVersion = 0
  UpdateFolder = $updSrc; AutoUpdate = $true; AttentionBlink = $true; AttentionPopup = $false; AttentionRemindMinutes = 120; ShortcutRenamed = $true
  Jobs = @()
}
New-Item -ItemType Directory -Force "$homeU\roaming" | Out-Null
$us | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "$homeU\roaming\settings.json"
$ulog = Join-Path $homeU ("local\logs\" + (Get-Date -Format 'yyyy-MM-dd') + '.log')
function ULog { if (Test-Path $ulog) { Get-Content -Encoding utf8 $ulog -Raw } else { '' } }

# 署名なしで動いているとき：署名なしの新しい版に更新できる
Copy-Item $nextExe $updSrc
$p0 = Start-Process -FilePath $appExe -ArgumentList '/startup' -PassThru
Start-Sleep -Seconds 5
Start-Process -FilePath $appExe -ArgumentList '/checkupdate' -Wait
$ok = WaitFor { (Ver $appExe) -eq '99.0.0.0' -and $p0.HasExited -and (AppProcs) } 60
Check 'I17' '置き場所に新しい版を置くと、自動で入れ替えて起動し直す' $ok ("版: " + (Ver $appExe) + "・前のプロセス終了: " + $p0.HasExited + "・動作中: " + [bool](AppProcs))
$ok = WaitFor { (ULog) -match "`tUPDATE`t" -and (ULog) -match '新しい版（99.0.0.0）で起動し直しました' } 15
Check 'I18' '更新をログに記録し、新しい版で動き続ける' $ok ''
Check 'I19' '前の版を AutoCopy.exe.old として残す' (Test-Path "$appExe.old") ''
AppProcs | Stop-Process -Force
Start-Sleep -Seconds 2

# 署名付きで動いているとき：同じ証明書で署名された、改ざんされていない新しい版だけを使う
$certA = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=自動コピー テストA' -CertStoreLocation Cert:\CurrentUser\My
$certB = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=別の発行元 テストB' -CertStoreLocation Cert:\CurrentUser\My
Copy-Item 'dist\AutoCopy.exe' $appExe -Force
Remove-Item "$appExe.old" -ErrorAction SilentlyContinue
Set-AuthenticodeSignature -FilePath $appExe -Certificate $certA -HashAlgorithm SHA256 | Out-Null
$sig = Get-AuthenticodeSignature $appExe
Write-Host ("今の版の署名: " + $sig.Status + " " + $sig.SignerCertificate.Subject)
Copy-Item $nextExe "$updSrc\AutoCopy.exe" -Force
Set-AuthenticodeSignature -FilePath "$updSrc\AutoCopy.exe" -Certificate $certB -HashAlgorithm SHA256 | Out-Null
$p1 = Start-Process -FilePath $appExe -ArgumentList '/startup' -PassThru
Start-Sleep -Seconds 5
Start-Process -FilePath $appExe -ArgumentList '/checkupdate' -Wait
$ok = WaitFor { (ULog) -match '違う証明書で署名されています' } 30
Check 'I20' '違う証明書で署名された版には更新しない' ($ok -and (Ver $appExe) -ne '99.0.0.0' -and -not $p1.HasExited) ("版: " + (Ver $appExe))

# 改ざん：同じ証明書で署名した後に中身を1バイト変える
Copy-Item $nextExe "$updSrc\AutoCopy.exe" -Force
Set-AuthenticodeSignature -FilePath "$updSrc\AutoCopy.exe" -Certificate $certA -HashAlgorithm SHA256 | Out-Null
$bytes = [IO.File]::ReadAllBytes("$updSrc\AutoCopy.exe")
$pos = [int]($bytes.Length / 2); $bytes[$pos] = $bytes[$pos] -bxor 0xFF
[IO.File]::WriteAllBytes("$updSrc\AutoCopy.exe", $bytes)
Write-Host ("改ざんした版の署名: " + (Get-AuthenticodeSignature "$updSrc\AutoCopy.exe").Status)
$before = ([regex]::Matches((ULog), '更新の確認')).Count
Start-Process -FilePath $appExe -ArgumentList '/checkupdate' -Wait
$ok = WaitFor { ([regex]::Matches((ULog), '更新の確認')).Count -gt $before } 30
Check 'I21' '署名の後で中身が変えられた版には更新しない' ($ok -and (Ver $appExe) -ne '99.0.0.0' -and -not $p1.HasExited) ("版: " + (Ver $appExe) + "・" + ((ULog) -split "`n" | Select-String '更新の確認' | Select-Object -Last 1))

# 同じ証明書で正しく署名された版には更新する
Copy-Item $nextExe "$updSrc\AutoCopy.exe" -Force
Set-AuthenticodeSignature -FilePath "$updSrc\AutoCopy.exe" -Certificate $certA -HashAlgorithm SHA256 | Out-Null
Start-Process -FilePath $appExe -ArgumentList '/checkupdate' -Wait
$ok = WaitFor { (Ver $appExe) -eq '99.0.0.0' -and $p1.HasExited -and (AppProcs) } 60
Check 'I22' '同じ証明書で署名された新しい版には更新する' $ok ("版: " + (Ver $appExe))
AppProcs | Stop-Process -Force

$md = @('| No | テスト | 結果 | メモ |', '| --- | --- | --- | --- |') + $results
$md | Out-File -Encoding utf8 integration-results.md
"## Integration tests" | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY
$md | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY
Write-Host "integration NG: $fail"
exit $fail
