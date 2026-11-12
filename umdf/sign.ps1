param([string]$ToolchainRoot,[ValidateRange(1,30)][int]$ValidityYears=10,[switch]$Renew)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$tc=if($ToolchainRoot){$ToolchainRoot}else{Join-Path (Split-Path $root) 'work\toolchain'}
$sign="$tc\sdk\c\bin\10.0.26100.0\x64\signtool.exe"
if(!(Test-Path -LiteralPath $sign)){throw 'SignTool not found.'}
$cert=$null
if(!$Renew){$cert=Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=gpdt local driver' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddYears($ValidityYears).AddDays(-30) } | Sort-Object NotAfter -Descending | Select-Object -First 1}
if(!$cert){$cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=gpdt local driver' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears($ValidityYears)}
Export-Certificate -Cert $cert -FilePath "$root\gpdt-test.cer" -Force | Out-Null
& $sign sign /fd SHA256 /s My /sha1 $cert.Thumbprint "$root\package\gpdtumdf.dll"
if($LASTEXITCODE){throw 'DLL signing failed'}
& "$tc\wdk\c\bin\10.0.26100.0\x86\Inf2Cat.exe" "/driver:$root\package" /os:10_X64 /uselocaltime
if($LASTEXITCODE){throw 'Catalog generation failed'}
& $sign sign /fd SHA256 /s My /sha1 $cert.Thumbprint "$root\package\gpdtumdf.cat"
if($LASTEXITCODE){throw 'CAT signing failed'}
Write-Output ("Certificate: {0}; valid until {1:yyyy-MM-dd HH:mm:ss}" -f $cert.Thumbprint,$cert.NotAfter)
Write-Output 'Signed locally. Certificate not installed into machine trust stores. Driver not loaded.'
