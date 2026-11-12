function Test-GpdtFilterInstalled($Devices,[string]$InfPath) {
    $expected=(Get-FileHash -LiteralPath $InfPath -Algorithm SHA256).Hash
    foreach($device in $Devices) {
        $matched=$false
        $configs=(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_ExtendedConfigurationIds' -ErrorAction SilentlyContinue).Data
        foreach($config in $configs) {
            if($config -match '^(oem\d+\.inf):') {
                $path=Join-Path $env:WINDIR ('INF\'+$Matches[1])
                if((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $expected){$matched=$true;break}
            }
        }
        if(!$matched){return $false}
    }
    return @($Devices).Count -eq 2
}
