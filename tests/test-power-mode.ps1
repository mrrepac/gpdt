$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$folder=Join-Path $root ('work\power-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $folder | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 "/out:$folder\test.exe" (Join-Path $root 'src\Native.cs') (Join-Path $root 'src\Power.cs') (Join-Path $PSScriptRoot 'PowerModeTests.cs')
if($LASTEXITCODE -ne 0){throw 'Power test compilation failed'}
& (Join-Path $folder 'test.exe') $folder
if($LASTEXITCODE -ne 0){throw 'Power mode tests failed'}
