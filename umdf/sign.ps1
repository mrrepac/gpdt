param([string]$ToolchainRoot)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$tc=if($ToolchainRoot){$ToolchainRoot}else{Join-Path (Split-Path $root) 'work\toolchain'}
$sign="$tc\sdk\c\bin\10.0.26100.0\x64\signtool.exe"
$cert=Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=gpdt local driver test' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7) } | Select-Object -First 1
if(!$cert){$cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=gpdt local driver test' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddMonths(6)}
Export-Certificate -Cert $cert -FilePath "$root\gpdt-test.cer" -Force | Out-Null
& $sign sign /fd SHA256 /s My /sha1 $cert.Thumbprint "$root\package\gpdtumdf.dll"
if($LASTEXITCODE){throw 'DLL signing failed'}
& "$tc\wdk\c\bin\10.0.26100.0\x86\Inf2Cat.exe" "/driver:$root\package" /os:10_X64 /uselocaltime
if($LASTEXITCODE){throw 'Catalog generation failed'}
& $sign sign /fd SHA256 /s My /sha1 $cert.Thumbprint "$root\package\gpdtumdf.cat"
if($LASTEXITCODE){throw 'CAT signing failed'}
$cert.Thumbprint | Set-Content "$root\test-cert-thumbprint.txt"
Write-Output 'Signed locally. Certificate not installed into machine trust stores. Driver not loaded.'
