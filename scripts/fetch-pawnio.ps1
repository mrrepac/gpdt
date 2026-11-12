$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$target=Join-Path $root 'drivers\PawnIO_setup.exe'
$expected='1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if(!(Test-Path -LiteralPath $target)) {
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('gpdt-pawnio-'+[Guid]::NewGuid().ToString('N')+'.exe')
    try {
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $temp -UseBasicParsing
        if((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $expected){throw 'Unexpected PawnIO download hash.'}
        if((Get-AuthenticodeSignature -LiteralPath $temp).Status -ne 'Valid'){throw 'PawnIO download signature is not trusted.'}
        Copy-Item -LiteralPath $temp -Destination $target
    } finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected){throw 'Unexpected PawnIO installer hash; existing file was not replaced.'}
