param([string]$OutputPath)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
if(!$OutputPath){$OutputPath=Join-Path $projectRoot 'gpdt.exe'}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:"$projectRoot\src\app.manifest" /win32icon:"$projectRoot\assets\gpdt.ico" "/resource:$projectRoot\assets\gpdt.ico,gpdt.ico" /out:"$OutputPath" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Core.dll /r:Microsoft.CSharp.dll "$projectRoot\src\Native.cs" "$projectRoot\src\Power.cs" "$projectRoot\src\BrightnessFilter.cs" "$projectRoot\src\Autostart.cs" "$projectRoot\src\App.cs" "$projectRoot\src\AssemblyInfo.cs"
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
Write-Output "Built $OutputPath"
