param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected version X.Y.Z'}
$root=Split-Path $PSScriptRoot
$dist=Join-Path $root 'dist'
$stage=Join-Path $dist ('stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
& (Join-Path $root 'build.ps1') -OutputPath (Join-Path $stage 'gpdt.exe')
foreach($dir in @('assets','defaults','docs','scripts','third_party')){Copy-Item -LiteralPath (Join-Path $root $dir) -Destination $stage -Recurse}
New-Item -ItemType Directory -Force (Join-Path $stage 'drivers'),(Join-Path $stage 'umdf/package') | Out-Null
foreach($file in @('drivers/IntelMSR.bin','drivers/COPYING','umdf/install.ps1','umdf/gpdt-test.cer','umdf/README.md','umdf/package/gpdtumdf.dll','umdf/package/gpdtumdf.cat','umdf/package/gpdtumdf.inf','README.md','CHANGELOG.md')){
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $stage $file)
}
# Include build sources so the downloaded archive can rebuild the application.
Copy-Item -LiteralPath (Join-Path $root 'src') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $root 'build.ps1') -Destination $stage
foreach($file in @('filter.c','gpdtumdf.inf','build.ps1','sign.ps1')){Copy-Item -LiteralPath (Join-Path $root "umdf/$file") -Destination (Join-Path $stage "umdf/$file")}
$zip=Join-Path $dist "gpdt-v$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -Encoding ASCII (Join-Path $dist 'SHA256SUMS.txt')
Write-Output "Package: $zip"
Write-Output "Staging: $stage"
