# Dev aid for checking the UI without a person: starts (or attaches to) Timbratune, optionally
# scrolls / clicks at window-relative pixels, then saves a PNG of the window (PrintWindow;
# popups such as an open dropdown list are separate windows and are not captured).
#   Start:   ./scripts/screenshot.ps1 -Exe <Reyfen.Timbratune.Desktop.exe> -Out a.png -Wait 7 -DataDir C:	emp\eu
#            (prints "pid=NNN"; set $env:TIMBRATUNE_FAKE_MIC first to replay a WAV as the mic)
#   Attach:  ./scripts/screenshot.ps1 -ProcId NNN -Out b.png -Scroll 3 -ClickX 422 -ClickY 254
# Scroll > 0 scrolls down by that many wheel notches. Always pass -ProcId after starting, so a
# copy of Timbratune the user has open is never touched.
param([int]$ProcId = 0, [string]$Exe, [string]$Out, [int]$Wait = 6, [string]$DataDir, [int]$Scroll = 0, [int]$ClickX = -1, [int]$ClickY = -1)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public struct RECT { public int L, T, R, B; }
}
"@
if ($DataDir) { $env:TIMBRATUNE_DATA_DIR = $DataDir }
$p = if ($ProcId -gt 0) { Get-Process -Id $ProcId } else { $null }
if (-not $p) { $p = Start-Process $Exe -PassThru; Start-Sleep -Seconds $Wait; $p.Refresh() }
$h = $p.MainWindowHandle
$r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
# Windows ignores SetForegroundWindow from a background process; a synthetic Alt tap lifts
# that lock. Without it the window stays inactive and the first click only activates it.
[W]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [W]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[W]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 300
if ([W]::GetForegroundWindow() -ne $h) { Write-Warning "window is not in the foreground; the first click may only activate it" }
if ($Scroll -ne 0) {
  [W]::SetCursorPos($r.L + 600, $r.T + 500) | Out-Null
  $n = [Math]::Abs($Scroll); $d = if ($Scroll -gt 0) { -120 } else { 120 }
  for ($i = 0; $i -lt $n; $i++) { [W]::mouse_event(0x0800, 0, 0, $d, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
  Start-Sleep -Milliseconds 600
}
if ($ClickX -ge 0) {
  [W]::SetCursorPos($r.L + $ClickX, $r.T + $ClickY) | Out-Null; Start-Sleep -Milliseconds 100
  [W]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); [W]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 900
}
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
[W]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"saved $Out ${w}x${hh} pid=$($p.Id)"
