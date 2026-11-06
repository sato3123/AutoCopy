param([string]$DataRoot)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not $DataRoot) { $DataRoot = Join-Path $env:TEMP ('AutoCopy-visual-' + [guid]::NewGuid().ToString('N')) }
foreach ($mode in '1|', '1.25|', '1.5|', '1|1093x582') {
    $scale, $screen = $mode.Split('|')
    $name = if ($screen) { 'small' } else { $scale.Replace('.', '') }
    $output = Join-Path (Get-Location) ('verified-screenshots-' + $name)
    $env:AUTOCOPY_HOME = Join-Path $DataRoot $name
    $env:AUTOCOPY_FONTSCALE = $scale
    $env:AUTOCOPY_SCREEN = if ($screen) { $screen } else { '1366x728' }
    $process = Start-Process -FilePath '.\dist\AutoCopy.exe' -ArgumentList '/screenshots', ('"' + $output + '"') -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(420000)) {
        $process.Kill()
        throw ('Screenshot test timed out: ' + $mode)
    }
    Write-Output ($mode + ': ' + (Get-Content -LiteralPath (Join-Path $output 'layout-count.txt')))
    if ($process.ExitCode -ne 0) { throw ('Screenshot checks failed: ' + $mode) }
}
