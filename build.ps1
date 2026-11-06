param([switch]$Tests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
New-Item -ItemType Directory -Force dist | Out-Null
$refs = '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Runtime.Serialization.dll','/r:System.Xml.dll','/r:System.Security.dll'
$sources = @(Get-ChildItem src -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32icon:app.ico /out:dist\AutoCopy.exe @refs @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath src\AutoCopy.exe.config -Destination dist
Get-ChildItem -File | Where-Object { $_.Extension -in '.html','.cer' -or ($_.Extension -eq '.bat' -and $_.Name -ne 'build.bat') } | Copy-Item -Destination dist
if ($env:AUTOCOPY_SIGN_PFX) {
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:AUTOCOPY_SIGN_PFX, $env:AUTOCOPY_SIGN_PASS)
    $signature = Set-AuthenticodeSignature -FilePath dist\AutoCopy.exe -Certificate $certificate -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') { throw ('Signing failed: ' + $signature.StatusMessage) }
} else { Write-Host 'Built unsigned executable (signing certificate not supplied).' }
if ($Tests) {
    New-Item -ItemType Directory -Force test | Out-Null
    $testSources = @(Get-ChildItem tests -Filter '*.cs' | ForEach-Object FullName)
    & $compiler /nologo /target:exe /main:AutoCopy.Tests.TestMain /platform:anycpu /codepage:65001 /out:test\AutoCopyTests.exe @refs @sources @testSources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Get-FileHash dist\AutoCopy.exe -Algorithm SHA256
