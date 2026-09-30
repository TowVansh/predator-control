# Query / set Acer's battery "health mode" (charge stops at 80%) via root\WMI BatteryControl
# (GUID 79772EC5-04B1-4bfd-843C-61E7F77B6CC9, same interface Linux's linuwu-sense uses).
# That WMI class is only accessible to SYSTEM, so run this as SYSTEM (see Predator\Hardware\BatteryHealth.cs).
#   battery_limit.ps1 -Mode query | on | off  -Out <log file>
param([ValidateSet('query', 'on', 'off')][string]$Mode = 'query', [string]$Out = "$env:ProgramData\PredatorControl\battery.log")

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
function Log($m) { Add-Content -Path $Out -Value "$(Get-Date -Format s)  [$env:USERNAME]  $m" }

try {
    $bc = Get-CimInstance -Namespace root\WMI -ClassName BatteryControl -ErrorAction Stop | Select-Object -First 1
    Log "instance: $($bc.InstanceName)"

    if ($Mode -ne 'query') {
        $on = [byte]($(if ($Mode -eq 'on') { 1 } else { 0 }))
        $r = Invoke-CimMethod -InputObject $bc -MethodName SetBatteryHealthControl -Arguments @{
            uBatteryNo = [byte]1; uFunctionMask = [byte]1; uFunctionStatus = $on; uReservedIn = [byte[]](0, 0, 0, 0, 0)
        }
        Log "set $Mode -> uReturn=$($r.uReturn) uReservedOut=$($r.uReservedOut)"
    }

    $q = Invoke-CimMethod -InputObject $bc -MethodName GetBatteryHealthControlStatus -Arguments @{
        uBatteryNo = [byte]1; uFunctionQuery = [byte]1; uReserved = [byte[]](0, 0)
    }
    Log ("status: functionList=0x{0:X2} health={1} calibration={2} return={3}" -f $q.uFunctionList, $q.uFunctionStatus[0], $q.uFunctionStatus[1], ($q.uReturn -join ','))
}
catch { Log "ERROR: $($_.Exception.Message)" }
