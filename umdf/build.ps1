param([string]$ToolchainRoot)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$tc=if($ToolchainRoot){$ToolchainRoot}else{Join-Path (Split-Path $root) 'work\toolchain'}
$vc=(Get-ChildItem "$tc\BuildTools\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$wdk="$tc\wdk\c";$sdk="$tc\sdk\c";$sdk64="$tc\sdk-x64\c";$ver='10.0.26100.0'
$out="$root\package"
New-Item -ItemType Directory -Force $out | Out-Null
$incs=@("/I$wdk\Include\wdf\umdf\2.33","/I$sdk\Include\$ver\um","/I$sdk\Include\$ver\shared","/I$sdk\Include\$ver\ucrt","/I$vc\include")
& "$vc\bin\Hostx64\x64\cl.exe" /nologo /c /TC /W4 /WX /wd4324 /O2 /MT /GS /guard:cf /D_AMD64_ /D_WIN64 /D_WIN32_WINNT=0x0A00 /DUMDF_VERSION_MAJOR=2 /DUMDF_VERSION_MINOR=33 @incs "/Fo$out\filter.obj" "$root\filter.c"
if($LASTEXITCODE){throw 'UMDF compilation failed'}
& "$vc\bin\Hostx64\x64\link.exe" /nologo /DLL /NXCOMPAT /DYNAMICBASE /GUARD:CF /MACHINE:X64 /EXPORT:DriverEntry /EXPORT:FxDriverEntryUm "/OUT:$out\gpdtumdf.dll" "$out\filter.obj" "/LIBPATH:$wdk\Lib\wdf\umdf\x64\2.33" "/LIBPATH:$vc\lib\x64" "/LIBPATH:$sdk64\ucrt\x64" "/LIBPATH:$sdk64\um\x64" WdfDriverStubUm.lib kernel32.lib advapi32.lib ntdll.lib
if($LASTEXITCODE){throw 'UMDF linking failed'}
Copy-Item "$root\gpdtumdf.inf" $out -Force
& "$wdk\tools\$ver\x64\infverif.exe" /w "$out\gpdtumdf.inf"
if($LASTEXITCODE){throw 'INF verification failed'}
