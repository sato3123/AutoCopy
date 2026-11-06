param([switch]$Tests)
# Works on Windows PowerShell 2.0 (Windows 7) and later.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
New-Item -ItemType Directory -Force dist | Out-Null
$refs = '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Runtime.Serialization.dll','/r:System.Xml.dll','/r:System.Security.dll'
$sources = @(Get-ChildItem src -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32icon:app.ico /out:dist\AutoCopy.exe $refs $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath src\AutoCopy.exe.config -Destination dist
Get-ChildItem | Where-Object { -not $_.PSIsContainer } | Where-Object { ('.html','.cer') -contains $_.Extension.ToLower() -or ($_.Extension -eq '.bat' -and $_.Name -ne 'build.bat') } | Copy-Item -Destination dist

if ($env:AUTOCOPY_SIGN_PFX) {
    # Dual signature (SHA1 primary + SHA256 nested), the same as previous releases:
    # Windows 7 without the SHA-2 update can only verify the SHA1 signature.
    $signtool = $null
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { $signtool = @($cmd)[0].Path }
    if (-not $signtool) {
        foreach ($root in @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "${env:ProgramFiles(x86)}\Windows Kits\8.1\bin", "$env:ProgramFiles\Windows Kits\10\bin")) {
            if (-not (Test-Path $root)) { continue }
            $found = @(Get-ChildItem $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending)
            if ($found.Count -gt 0) { $signtool = $found[0].FullName; break }
        }
    }
    if ($signtool) {
        & $signtool sign /q /f $env:AUTOCOPY_SIGN_PFX /p $env:AUTOCOPY_SIGN_PASS /fd sha1 dist\AutoCopy.exe
        if ($LASTEXITCODE -ne 0) { throw 'Signing (SHA1) failed.' }
        & $signtool sign /q /as /f $env:AUTOCOPY_SIGN_PFX /p $env:AUTOCOPY_SIGN_PASS /fd sha256 dist\AutoCopy.exe
        if ($LASTEXITCODE -ne 0) { throw 'Signing (SHA256, nested) failed.' }
    } elseif ($env:AUTOCOPY_SIGN_SHA256_ONLY -eq '1') {
        Write-Warning 'signtool.exe not found: signing with SHA256 only. Windows 7 PCs without the SHA-2 update cannot verify this signature and will refuse the auto-update.'
        $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:AUTOCOPY_SIGN_PFX, $env:AUTOCOPY_SIGN_PASS)
        Set-AuthenticodeSignature -FilePath dist\AutoCopy.exe -Certificate $certificate -HashAlgorithm SHA256 | Out-Null
    } else {
        throw 'signtool.exe (Windows SDK) not found. It is needed for the dual SHA1+SHA256 signature that Windows 7 can verify. Install the Windows SDK signing tools, or set AUTOCOPY_SIGN_SHA256_ONLY=1 to sign with SHA256 only.'
    }
    $signature = Get-AuthenticodeSignature dist\AutoCopy.exe
    if ($signature.Status -ne 'Valid') { throw ('Signing failed: ' + $signature.StatusMessage) }
} else { Write-Host 'Built unsigned executable (signing certificate not supplied).' }

if ($Tests) {
    New-Item -ItemType Directory -Force test | Out-Null
    $testSources = @(Get-ChildItem tests -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler /nologo /target:exe /main:AutoCopy.Tests.TestMain /platform:anycpu /codepage:65001 /out:test\AutoCopyTests.exe $refs $sources $testSources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$sha = [System.Security.Cryptography.SHA256]::Create()
$fs = [System.IO.File]::OpenRead((Join-Path $here 'dist\AutoCopy.exe'))
try { $hash = [System.BitConverter]::ToString($sha.ComputeHash($fs)).Replace('-', '') } finally { $fs.Close() }
Write-Host ('SHA256 ' + $hash + '  dist\AutoCopy.exe')
