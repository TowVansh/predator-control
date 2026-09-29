# Stop background junk that does nothing useful on this laptop. Run elevated.
#   .\debloat.ps1          -> back up current state, then apply
#   .\debloat.ps1 -Revert  -> restore from the backup
# Nothing is uninstalled; services/tasks/autostarts are only disabled, so every app still opens normally.
param([switch]$Revert)

$backup = Join-Path $PSScriptRoot 'debloat_backup.json'

# Services: Disabled unless listed in $manual (those only stop auto-starting).
$services = 'ROG Live Service', 'asus', 'asusm', 'NativePushService', 'PCManager Service Store', 'MySQL80',
            'DiagTrack', 'MapsBroker', 'TrkWks'
# Manual = starts on demand instead of at boot.
#   MySQL80: local DB for coursework; MySQL Workbench / Services can start it when needed.
#   MapsBroker: only the Maps app uses it.
$manual = 'MySQL80', 'MapsBroker'

# Scheduled tasks, by path + name.
$tasks = @(
    @{ Path = '\ASUS\'; Name = 'AcPowerNotification' }, @{ Path = '\ASUS\'; Name = 'ArmourySocketServer' },
    @{ Path = '\ASUS\'; Name = 'ASUSUpdateTaskMachineCore1dc7e65d01c94d7' }, @{ Path = '\ASUS\'; Name = 'ASUSUpdateTaskMachineUA' },
    @{ Path = '\ASUS\'; Name = 'P508PowerAgent_sdk' }, @{ Path = '\'; Name = 'RemotlyLauncher_Towering' },
    @{ Path = '\'; Name = 'Overwolf Updater Task' }
)

# Registry autostarts, disabled the way Task Manager does it (StartupApproved, first byte 03).
$autostarts = @(
    @{ Approved = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';   Name = 'CloudflareWARP' },
    @{ Approved = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32'; Name = 'SunJavaUpdateSched' },
    @{ Approved = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32'; Name = 'Wondershare Helper Compact.exe' },
    @{ Approved = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';   Name = 'Mem Reduct' }
)

# Store-app startup tasks: State 1 = disabled by user, 2 = enabled.
$appxRoot = 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData'
$appxStartups = 'Microsoft.MicrosoftPCManager_8wekyb3d8bbwe\WindowsMasterUI_Store', 'AppUp.IntelArcSoftware_8j3eq9eme6ctt\IntelGraphicsSoftwareStartup'

# Processes to end now (they'd otherwise run until the next reboot).
$kill = 'ArmourySocketServer', 'AcPowerNotification', 'RemotlyLauncher', 'WsToastNotification', 'WsNativePushService',
        'MSPCManager', 'MSPCManagerCore', 'memreduct', 'WSHelper', 'jusched'

# Browser policies: no preloading at logon, no running after the window is closed.
# (Edge will show "managed by your organization" in settings; that's just these two values.)
$policies = @(
    @{ Key = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name = 'StartupBoostEnabled' },
    @{ Key = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name = 'BackgroundModeEnabled' }
)

$disabledBlob = [byte[]](3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
$startModeName = @{ Auto = 'Automatic'; Manual = 'Manual'; Disabled = 'Disabled' }

if ($Revert) {
    $b = Get-Content $backup -Raw | ConvertFrom-Json
    foreach ($s in $b.services) {
        Set-Service -Name $s.Name -StartupType $s.StartMode -ErrorAction SilentlyContinue
        if ($s.State -eq 'Running') { Start-Service -Name $s.Name -ErrorAction SilentlyContinue }
        "service $($s.Name) -> $($s.StartMode)"
    }
    foreach ($t in $b.tasks) { if ($t.Enabled) { Enable-ScheduledTask -TaskPath $t.Path -TaskName $t.Name | Out-Null; "task $($t.Name) enabled" } }
    foreach ($a in $b.autostarts) {
        if ($null -eq $a.Value) { Remove-ItemProperty $a.Approved -Name $a.Name -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $a.Approved -Name $a.Name -Value ([byte[]]$a.Value) -Type Binary }
        "autostart $($a.Name) restored"
    }
    foreach ($x in $b.appx) { Set-ItemProperty (Join-Path $appxRoot $x.Key) -Name State -Value $x.State; "appx startup $($x.Key) -> $($x.State)" }
    foreach ($p in $policies) { Remove-ItemProperty $p.Key -Name $p.Name -ErrorAction SilentlyContinue; "policy $($p.Name) removed" }
    return
}

# --- back up (only the first time, so re-running never overwrites the original state) ---
if (-not (Test-Path $backup)) {
    $state = [ordered]@{
        services   = @(foreach ($n in $services) {
            $s = Get-CimInstance Win32_Service -Filter "Name='$n'"
            if ($s) { [ordered]@{ Name = $n; StartMode = $startModeName[$s.StartMode]; State = $s.State } }
        })
        tasks      = @(foreach ($t in $tasks) {
            $st = Get-ScheduledTask -TaskPath $t.Path -TaskName $t.Name -ErrorAction SilentlyContinue
            if ($st) { [ordered]@{ Path = $t.Path; Name = $t.Name; Enabled = $st.State -ne 'Disabled' } }
        })
        autostarts = @(foreach ($a in $autostarts) {
            [ordered]@{ Approved = $a.Approved; Name = $a.Name; Value = (Get-ItemProperty $a.Approved -Name $a.Name -ErrorAction SilentlyContinue).($a.Name) }
        })
        appx       = @(foreach ($k in $appxStartups) {
            $p = Join-Path $appxRoot $k
            if (Test-Path $p) { [ordered]@{ Key = $k; State = (Get-ItemProperty $p).State } }
        })
    }
    $state | ConvertTo-Json -Depth 5 | Set-Content $backup -Encoding utf8
}
else {
    # Items added to the lists after the first run: record their original state too.
    $b = Get-Content $backup -Raw | ConvertFrom-Json
    $known = @($b.services | ForEach-Object { $_.Name })
    $added = @(foreach ($n in $services | Where-Object { $known -notcontains $_ }) {
        $s = Get-CimInstance Win32_Service -Filter "Name='$n'"
        if ($s) { [pscustomobject]@{ Name = $n; StartMode = $startModeName[$s.StartMode]; State = $s.State } }
    })
    if ($added.Count) {
        $b.services = @($b.services) + $added
        $b | ConvertTo-Json -Depth 5 | Set-Content $backup -Encoding utf8
    }
}
"backup: $backup"

# --- apply ---
foreach ($n in $services) {
    if (-not (Get-Service -Name $n -ErrorAction SilentlyContinue)) { continue }
    $mode = if ($manual -contains $n) { 'Manual' } else { 'Disabled' }
    Set-Service -Name $n -StartupType $mode
    Stop-Service -Name $n -Force -ErrorAction SilentlyContinue
    "service ${n}: $mode, now $((Get-Service $n).Status)"
}
foreach ($t in $tasks) {
    if (Get-ScheduledTask -TaskPath $t.Path -TaskName $t.Name -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskPath $t.Path -TaskName $t.Name -ErrorAction SilentlyContinue
        Disable-ScheduledTask -TaskPath $t.Path -TaskName $t.Name | Out-Null
        "task disabled: $($t.Path)$($t.Name)"
    }
}
foreach ($a in $autostarts) {
    if (-not (Test-Path $a.Approved)) { New-Item $a.Approved -Force | Out-Null }
    Set-ItemProperty $a.Approved -Name $a.Name -Value $disabledBlob -Type Binary
    "autostart disabled: $($a.Name)"
}
foreach ($k in $appxStartups) {
    $p = Join-Path $appxRoot $k
    if (Test-Path $p) { Set-ItemProperty $p -Name State -Value 1; "appx startup disabled: $k" }
}
foreach ($p in $policies) {
    if (-not (Test-Path $p.Key)) { New-Item $p.Key -Force | Out-Null }
    New-ItemProperty $p.Key -Name $p.Name -Value 0 -PropertyType DWord -Force | Out-Null
    "policy set: $($p.Name) = 0"
}
foreach ($p in $kill) { Get-Process -Name $p -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
"ended: " + (($kill | Where-Object { -not (Get-Process -Name $_ -ErrorAction SilentlyContinue) }) -join ', ')
