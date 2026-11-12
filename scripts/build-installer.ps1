param([string]$Version='1.0.4',[string]$Compiler)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if(!$Compiler){
    $candidates=@("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe","$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",(Join-Path $root 'work\inno\ISCC.exe'))
    $Compiler=$candidates | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
}
if(!$Compiler){throw 'Install Inno Setup 6 or pass -Compiler with the path to ISCC.exe.'}
. (Join-Path $PSScriptRoot 'dependencies.ps1')
& (Join-Path $PSScriptRoot 'fetch-pawnio.ps1')
Assert-GpdtPayload $root
$output=& (Join-Path $PSScriptRoot 'package.ps1') -Version $Version
$output | Write-Output
$stage=($output | Where-Object {$_ -like 'Staging: *'} | Select-Object -Last 1).Substring(9)
& $Compiler "/DStageDir=$stage" "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'installer.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $root "dist\gpdt-v$Version-setup-x64.exe"
$hash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Add-Content -Encoding ASCII (Join-Path $root 'dist\SHA256SUMS.txt')
Write-Output "Installer: $setup"
