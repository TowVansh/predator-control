# Laptop tune-up for the Predator PH16-71. Run elevated.
#   .\optimize.ps1          -> back up current state, then apply
#   .\optimize.ps1 -Revert  -> restore from the backup
param([switch]$Revert)

$backup = Join-Path $PSScriptRoot 'optimize_backup.json'
$services = 'AASSvc', 'AcerLightingService', 'Killer Analytics Service', 'Killer Network Service'
$tasks = 'PredatorSenseLauncher'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$startupPatterns = '^MicrosoftEdgeAutoLaunch_', '^MicrosoftCopilotAutoLaunch_', '^GoogleChromeAutoLaunch_',
                   '^Overwolf$', '^EpicGamesLauncher$', '^RiotClient$', '^Steam$', '^RobloxPlayerBeta$',
                   '^BakkesMod$', '^Free Download Manager$', '^ScreenRec$', '^Dawn Launcher$'

# StartupApproved: first byte 02 = enabled, 03 = disabled (what Task Manager writes).
$disabledBlob = [byte[]](3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)

if ($Revert) {
    $b = Get-Content $backup -Raw | ConvertFrom-Json
    foreach ($s in $b.services) {
        Set-Service -Name $s.Name -StartupType $s.StartMode -ErrorAction SilentlyContinue
        if ($s.State -eq 'Running') { Start-Service -Name $s.Name -ErrorAction SilentlyContinue }
        "service $($s.Name) -> $($s.StartMode)"
    }
    foreach ($t in $b.tasks) { if ($t.Enabled) { Enable-ScheduledTask -TaskPath ($t.Path) -TaskName $t.Name | Out-Null; "task $($t.Name) enabled" } }
    foreach ($a in $b.approved) {
        if ($null -eq $a.Value) { Remove-ItemProperty $approvedKey -Name $a.Name -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $approvedKey -Name $a.Name -Value ([byte[]]$a.Value) -Type Binary }
        "startup $($a.Name) restored"
    }
    return
}

# --- back up ---
$state = [ordered]@{
    services = @(foreach ($n in $services) {
        $s = Get-CimInstance Win32_Service -Filter "Name='$n'"
        if ($s) { [ordered]@{ Name = $n; StartMode = @{ Auto = 'Automatic'; Manual = 'Manual'; Disabled = 'Disabled' }[$s.StartMode]; State = $s.State } }
    })
    tasks = @(foreach ($p in $tasks) {
        $t = Get-ScheduledTask -TaskName $p -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($t) { [ordered]@{ Path = $t.TaskPath; Name = $t.TaskName; Enabled = $t.State -ne 'Disabled' } }
    })
    approved = @()
}
$runNames = (Get-Item $runKey).Property | Where-Object { $n = $_; $startupPatterns | Where-Object { $n -match $_ } }
foreach ($n in $runNames) {
    $cur = (Get-ItemProperty $approvedKey -Name $n -ErrorAction SilentlyContinue).$n
    $state.approved += [ordered]@{ Name = $n; Value = $cur }
}
if (-not (Test-Path $backup)) { $state | ConvertTo-Json -Depth 5 | Set-Content $backup -Encoding utf8 }
"backup: $backup"

# --- apply ---
if (-not (Test-Path $approvedKey)) { New-Item $approvedKey -Force | Out-Null }
foreach ($n in $runNames) {
    Set-ItemProperty $approvedKey -Name $n -Value $disabledBlob -Type Binary
    "startup disabled: $n"
}
foreach ($t in $state.tasks) { Disable-ScheduledTask -TaskPath $t.Path -TaskName $t.Name | Out-Null; "task disabled: $($t.Name)" }
foreach ($s in $state.services) {
    Set-Service -Name $s.Name -StartupType Disabled
    Stop-Service -Name $s.Name -Force -ErrorAction SilentlyContinue
    "service disabled: $($s.Name) (now $((Get-Service $s.Name).Status))"
}
