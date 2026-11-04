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

$md = @('| No | テスト | 結果 | メモ |', '| --- | --- | --- | --- |') + $results
$md | Out-File -Encoding utf8 integration-results.md
"## Integration tests" | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY
$md | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY
Write-Host "integration NG: $fail"
exit $fail
