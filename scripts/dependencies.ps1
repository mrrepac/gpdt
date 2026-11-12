# Shared by the setup entry point and the dependency tests. Dot-sourcing makes no changes.
function Assert-GpdtPlatform {
    if(![Environment]::Is64BitOperatingSystem){throw 'Required: Windows 11 x64.'}
    $os=Get-CimInstance Win32_OperatingSystem
    if([int]$os.BuildNumber -lt 22000){throw 'Required: Windows 11 (build 22000 or newer).'}
    $system=Get-CimInstance Win32_ComputerSystem
    if($system.Manufacturer -ne 'GPD' -or $system.Model -ne 'G1688-08'){throw 'Supported device: GPD MicroPC 2 G1688-08 only. No drivers installed.'}
    $devices=@(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -like 'HID\INTC816&Col0D\*' -or $_.InstanceId -like 'HID\INTC816&Col0E\*'})
    if($devices.Count -ne 2){throw 'The two GPD brightness HID devices were not found. No drivers installed.'}
}
function Assert-GpdtPayload([string]$Root) {
    $expected='1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
    $pawn=Join-Path $Root 'drivers\PawnIO_setup.exe'
    if((Get-FileHash -LiteralPath $pawn -Algorithm SHA256).Hash -ne $expected){throw 'PawnIO installer hash mismatch.'}
    if((Get-AuthenticodeSignature -LiteralPath $pawn).Status -ne 'Valid'){throw 'PawnIO installer signature is not trusted.'}
    $cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $Root 'umdf\gpdt-test.cer'))
    if((Get-Date) -lt $cert.NotBefore -or (Get-Date) -gt $cert.NotAfter){throw 'The gpdt driver certificate is not valid at the current date. Obtain a newly signed gpdt release.'}
    foreach($name in @('gpdtumdf.dll','gpdtumdf.cat')) {
        $sig=Get-AuthenticodeSignature -LiteralPath (Join-Path $Root "umdf\package\$name")
        if(!$sig.SignerCertificate -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint -or $sig.Status -notin @('Valid','NotTrusted','UnknownError')){throw "Invalid gpdt signature: $name"}
    }
    if(!(Test-Path -LiteralPath (Join-Path $Root 'umdf\package\gpdtumdf.inf'))){throw 'Missing gpdt driver INF.'}
}
function Invoke-GpdtDriverProcess([string]$File,[string[]]$Arguments) {
    # All child processes inherit the elevated setup token; no second UAC prompt.
    $id=[Guid]::NewGuid().ToString('N')
    $stdout=Join-Path $env:TEMP "gpdt-driver-$id.out.log"
    $stderr=Join-Path $env:TEMP "gpdt-driver-$id.err.log"
    $process=Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    foreach($path in @($stdout,$stderr)){if(Test-Path -LiteralPath $path){Get-Content -LiteralPath $path | ForEach-Object {Write-Host $_}}}
    return $process.ExitCode
}
function Get-GpdtPawnVersion {
    $entry=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue
    if(!$entry){return $null}
    try{return [version]$entry.DisplayVersion}catch{throw 'Cannot determine the installed PawnIO version.'}
}
function Install-GpdtDependencies([string]$Root,[switch]$TrustCertificate,[switch]$CheckOnly) {
    Assert-GpdtPlatform
    Assert-GpdtPayload $Root
    if($CheckOnly){return 0}
    if(Get-Process gpdt -ErrorAction SilentlyContinue){throw 'Exit gpdt through its tray menu before installing drivers.'}
    $cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $Root 'umdf\gpdt-test.cer'))
    if(!$TrustCertificate -and (!(Test-Path "Cert:\LocalMachine\Root\$($cert.Thumbprint)") -or !(Test-Path "Cert:\LocalMachine\TrustedPublisher\$($cert.Thumbprint)"))){throw 'Consent to trust the gpdt filter certificate is required.'}
    $restart=$false
    $version=Get-GpdtPawnVersion
    if($version -and $version -lt [version]'2.1.0'){throw 'PawnIO older than 2.1.0 is installed. Remove that version using Windows Installed apps, then run gpdt setup again.'}
    if(!$version -or $version -lt [version]'2.2.0') {
        $code=Invoke-GpdtDriverProcess (Join-Path $Root 'drivers\PawnIO_setup.exe') @('-install','-silent')
        if($code -notin @(0,3010)){throw "PawnIO installation failed: $code. The gpdt filter was not installed."}
        $restart=$code -eq 3010
        $installed=Get-GpdtPawnVersion
        if(!$installed -or $installed -lt [version]'2.2.0'){throw 'PawnIO installation could not be verified.'}
    }
    $args=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $Root 'umdf\install.ps1')+'"'))
    if($TrustCertificate){$args+='-TrustCertificate'}
    $code=Invoke-GpdtDriverProcess (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') $args
    if($code -notin @(0,3010)){throw "Brightness filter installation failed: $code. See the setup log and Windows\INF\setupapi.dev.log. PawnIO may already be installed; rerun setup after resolving the error."}
    if($restart -or $code -eq 3010){return 3010}
    return 0
}
