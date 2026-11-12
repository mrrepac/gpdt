$ErrorActionPreference='Stop'
Import-Module Microsoft.PowerShell.Utility
. (Join-Path (Split-Path $PSScriptRoot) 'umdf\installed-filter.ps1')
function Get-FileHash {param($LiteralPath,$Algorithm)
    return [pscustomobject]@{Hash=$(if($LiteralPath -like '*oem2.inf'){'different'}else{'expected'})}
}
function Test-Path {param($LiteralPath) return $true}
function Get-PnpDeviceProperty {param($InstanceId,$KeyName,$ErrorAction)
    return [pscustomobject]@{Data=$script:configs[$InstanceId]}
}
function Assert($Value,$Message){if(!$Value){throw $Message};Write-Output "PASS: $Message"}
$devices=@([pscustomobject]@{InstanceId='up'},[pscustomobject]@{InstanceId='down'})
$script:configs=@{up=@('oem1.inf:HID\up');down=@('oem1.inf:HID\down')}
Assert (Test-GpdtFilterInstalled $devices 'expected.inf') 'Both installed extension packages match'
$script:configs.down=@('oem2.inf:HID\down')
Assert (!(Test-GpdtFilterInstalled $devices 'expected.inf')) 'Different package on one button is rejected'
$script:configs.down=@()
Assert (!(Test-GpdtFilterInstalled $devices 'expected.inf')) 'Missing extension on one button is rejected'
$script:configs.down=@('oem1.inf:HID\down')
Assert (!(Test-GpdtFilterInstalled @($devices[0]) 'expected.inf')) 'One device is insufficient'
Assert (!(Test-GpdtFilterInstalled @() 'expected.inf')) 'No devices is not success'
