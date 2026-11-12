#Requires -RunAsAdministrator
param([switch]$Disable)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$name='gpdt - user logon'
if($Disable){Disable-ScheduledTask -TaskName $name | Out-Null;return}
$exe=Join-Path $root 'gpdt.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build or unpack gpdt.exe first.'}
$user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$action=New-ScheduledTaskAction -Execute $exe -Argument '--enable --tray' -WorkingDirectory $root
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $user
$principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Output "Startup configured for $user -> $exe"
