# Screenshot a top-level window by title into a PNG. Usage: snap.ps1 -Title "Predator Control" -Out path.png
param([string]$Title = 'Predator Control', [string]$Out = "$env:TEMP\predator_snap.png")

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
# Without this, GetWindowRect returns DPI-virtualised (shrunk) bounds and the capture is clipped.
[Win]::SetProcessDPIAware() | Out-Null

$h = [Win]::FindWindow($null, $Title)
if ($h -eq [IntPtr]::Zero) {
    $p = Get-Process | Where-Object { $_.MainWindowTitle -eq $Title } | Select-Object -First 1
    if ($p) { $h = $p.MainWindowHandle }
}
if ($h -eq [IntPtr]::Zero) { throw "window '$Title' not found" }
$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hgt = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win]::PrintWindow($h, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"saved $Out ($w x $hgt)"
