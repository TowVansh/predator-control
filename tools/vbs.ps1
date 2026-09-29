# Turn Windows virtualization-based security (VBS / Memory Integrity) off or back on. Run elevated, then reboot.
# The hypervisor itself stays on (hypervisorlaunchtype Auto) so WSL2 keeps working.
#   .\vbs.ps1          -> VBS off (small gaming/CPU gain)
#   .\vbs.ps1 -Revert  -> back to Windows defaults
param([switch]$Revert)

$k = 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard'
$h = "$k\Scenarios\HypervisorEnforcedCodeIntegrity"

if ($Revert) {
    Remove-ItemProperty $k -Name EnableVirtualizationBasedSecurity, RequirePlatformSecurityFeatures -ErrorAction SilentlyContinue
    Remove-ItemProperty $h -Name Enabled -ErrorAction SilentlyContinue
    'VBS settings back to Windows defaults. Reboot to apply.'
    return
}

New-ItemProperty $k -Name EnableVirtualizationBasedSecurity -Value 0 -PropertyType DWord -Force | Out-Null
New-ItemProperty $k -Name RequirePlatformSecurityFeatures -Value 0 -PropertyType DWord -Force | Out-Null
if (-not (Test-Path $h)) { New-Item $h -Force | Out-Null }
New-ItemProperty $h -Name Enabled -Value 0 -PropertyType DWord -Force | Out-Null
'VBS off. Reboot to apply.'
