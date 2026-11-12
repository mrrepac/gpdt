#Requires -RunAsAdministrator
param([switch]$TrustCertificate)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$system=Get-CimInstance Win32_ComputerSystem
if($system.Manufacturer -ne 'GPD' -or $system.Model -ne 'G1688-08'){throw 'Supported device: GPD MicroPC 2 G1688-08 only.'}
$devices=@(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -like 'HID\INTC816&Col0D\*' -or $_.InstanceId -like 'HID\INTC816&Col0E\*'})
if($devices.Count -ne 2){throw 'Expected the two INTC816 brightness collections. No changes made.'}
if(Get-Process gpdt -ErrorAction SilentlyContinue){throw 'Exit gpdt through its tray menu before installing the filter.'}
$certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $root 'gpdt-test.cer'))
if((Get-Date) -gt $certificate.NotAfter){throw 'Driver certificate expired. Rebuild and sign the package locally; see docs/RESTORE.md.'}
if($TrustCertificate){
    Import-Certificate -FilePath (Join-Path $root 'gpdt-test.cer') -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Import-Certificate -FilePath (Join-Path $root 'gpdt-test.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
}
foreach($file in @('gpdtumdf.dll','gpdtumdf.cat')){
    $signature=Get-AuthenticodeSignature (Join-Path $root "package\$file")
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint){throw 'Package signature is not trusted or does not match the supplied certificate. Read docs/RESTORE.md before using -TrustCertificate.'}
}
& pnputil.exe /add-driver (Join-Path $root 'package\gpdtumdf.inf') /install
$driverExitCode=$LASTEXITCODE
if($driverExitCode -eq 259) {
    . (Join-Path $root 'installed-filter.ps1')
    if(Test-GpdtFilterInstalled $devices (Join-Path $root 'package\gpdtumdf.inf')) {
        Write-Output 'Both brightness devices already use this gpdt filter package.'
        $driverExitCode=0
    }
}
if($driverExitCode -ne 0 -and $driverExitCode -ne 3010){throw "Driver installation failed: $driverExitCode"}
Write-Output 'UMDF package installed. Restart Windows manually if requested. No boot settings were changed.'
exit $driverExitCode
