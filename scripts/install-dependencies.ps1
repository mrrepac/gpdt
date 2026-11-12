#Requires -RunAsAdministrator
param([string]$Root=(Split-Path $PSScriptRoot),[switch]$TrustCertificate,[switch]$CheckOnly,[string]$LogPath)
$ErrorActionPreference='Stop'
if(!$LogPath){$LogPath=Join-Path $env:TEMP 'gpdt-driver-setup.log'}
$recording=$false
try {
    Start-Transcript -Path $LogPath -Append | Out-Null
    $recording=$true
    . (Join-Path $PSScriptRoot 'dependencies.ps1')
    $code=Install-GpdtDependencies -Root $Root -TrustCertificate:$TrustCertificate -CheckOnly:$CheckOnly
    Write-Output "gpdt dependency result: $code"
    exit $code
} catch {
    Write-Output ($_ | Out-String)
    exit 1
} finally {
    if($recording){Stop-Transcript | Out-Null}
}
