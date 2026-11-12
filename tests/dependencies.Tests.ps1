$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
. (Join-Path $root 'scripts\dependencies.ps1')
function Assert($Condition,[string]$Message){if(!$Condition){throw $Message};Write-Output "PASS: $Message"}
function Reject([scriptblock]$Action,[string]$Pattern){
    $message=$null
    try{& $Action | Out-Null}catch{$message=$_.Exception.Message}
    Assert ($message -like $Pattern) "Rejected: $Pattern"
}
# Read-only checks of the actual release payload.
Assert-GpdtPayload $root
Write-Output 'PASS: actual bundled PawnIO hash/signature and gpdt certificate/signatures'
$payloadCheck=${function:Assert-GpdtPayload}
function Get-FileHash {param($LiteralPath,$Algorithm) return [pscustomobject]@{Hash='BAD'} }
Reject {& $payloadCheck $root} '*hash mismatch*'
Remove-Item Function:\Get-FileHash
$script:expiredDate=([Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $root 'umdf\gpdt-test.cer'))).NotAfter.AddDays(1)
function Get-Date {return $script:expiredDate}
Reject {& $payloadCheck $root} '*certificate is not valid*'
Remove-Item Function:\Get-Date
function Get-CimInstance($ClassName){
    if($ClassName -eq 'Win32_OperatingSystem'){return [pscustomobject]@{BuildNumber=$script:build}}
    return [pscustomobject]@{Manufacturer='GPD';Model=$script:model}
}
function Get-PnpDevice {param([switch]$PresentOnly)
    if($script:devices){@([pscustomobject]@{InstanceId='HID\INTC816&Col0D\test'},[pscustomobject]@{InstanceId='HID\INTC816&Col0E\test'})}
}
$script:build='22631';$script:model='G1688-08';$script:devices=$true
Assert-GpdtPlatform
$script:model='Other';Reject {Assert-GpdtPlatform} '*Supported device*';$script:model='G1688-08'
$script:build='19045';Reject {Assert-GpdtPlatform} '*Windows 11*';$script:build='22631'
$script:devices=$false;Reject {Assert-GpdtPlatform} '*HID devices*';$script:devices=$true
function Assert-GpdtPayload($Root){if($script:badPayload){throw 'Invalid payload'}}
function Get-Process {param($Name,$ErrorAction) if($script:running){[pscustomobject]@{Id=1}}}
function Test-Path {param($Path,$LiteralPath) if($Path -like 'Cert:*'){return $script:trusted};if($LiteralPath){return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath};return Microsoft.PowerShell.Management\Test-Path $Path}
function Get-GpdtPawnVersion {return $script:pawnVersion}
function Invoke-GpdtDriverProcess($File,$Arguments){
    $script:calls.Add([pscustomobject]@{File=$File;Arguments=$Arguments})
    if($File -like '*PawnIO_setup.exe'){
        if($script:pawnCode -in @(0,3010)){$script:pawnVersion=[version]'2.2.0'}
        return $script:pawnCode
    }
    return $script:filterCode
}
function Reset-Scenario {
    $script:calls=New-Object 'System.Collections.Generic.List[object]'
    $script:pawnVersion=$null;$script:pawnCode=0;$script:filterCode=0
    $script:trusted=$false;$script:running=$false;$script:badPayload=$false
}
Reset-Scenario
$code=Install-GpdtDependencies $root -CheckOnly
Assert ($code -eq 0 -and $script:calls.Count -eq 0) 'Preflight never runs driver installers'
Reject {Install-GpdtDependencies $root} '*Consent*'
Assert ($script:calls.Count -eq 0) 'Missing consent makes no driver changes'
$code=Install-GpdtDependencies $root -TrustCertificate
Assert ($code -eq 0 -and $script:calls.Count -eq 2) 'Clean install runs PawnIO then the filter'
Assert (($script:calls[0].Arguments -join ' ') -eq '-install -silent') 'Only signed PawnIO edition is requested'
Assert ($script:calls[1].Arguments -contains '-TrustCertificate') 'Explicit trust is passed to filter installation'
Reset-Scenario;$script:pawnVersion=[version]'2.3.0'
$code=Install-GpdtDependencies $root -TrustCertificate
Assert ($script:calls.Count -eq 1 -and $script:calls[0].File -notlike '*PawnIO_setup.exe') 'Newer PawnIO is not downgraded'
Reset-Scenario;$script:pawnVersion=[version]'2.1.0'
$code=Install-GpdtDependencies $root -TrustCertificate
Assert ($script:calls.Count -eq 2) 'Supported PawnIO 2.1 upgrade is performed'
Reset-Scenario;$script:pawnVersion=[version]'2.0.0'
Reject {Install-GpdtDependencies $root -TrustCertificate} '*older than 2.1.0*'
Assert ($script:calls.Count -eq 0) 'Unsupported upgrade is rejected before changes'
Reset-Scenario;$script:pawnCode=3010
Assert ((Install-GpdtDependencies $root -TrustCertificate) -eq 3010) 'PawnIO reboot requirement propagates'
Reset-Scenario;$script:filterCode=3010
Assert ((Install-GpdtDependencies $root -TrustCertificate) -eq 3010) 'Filter reboot requirement propagates'
Reset-Scenario;$script:pawnCode=5
Reject {Install-GpdtDependencies $root -TrustCertificate} '*PawnIO installation failed: 5*'
Assert ($script:calls.Count -eq 1) 'PawnIO failure prevents filter installation'
Reset-Scenario;$script:filterCode=5
Reject {Install-GpdtDependencies $root -TrustCertificate} '*Brightness filter installation failed: 5*'
Reset-Scenario;$script:running=$true
Reject {Install-GpdtDependencies $root -TrustCertificate} '*Exit gpdt*'
Assert ($script:calls.Count -eq 0) 'Running gpdt prevents driver changes'
Reset-Scenario;$script:badPayload=$true
Reject {Install-GpdtDependencies $root -TrustCertificate} '*Invalid payload*'
Assert ($script:calls.Count -eq 0) 'Invalid payload prevents driver changes'
Write-Output 'All dependency orchestration tests passed; no drivers or certificates changed.'
