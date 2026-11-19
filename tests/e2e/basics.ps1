# The product under its new name, driven through the real shell and real windows on the hidden desktop.
# Uses a generated clip, and only does things that don't write settings: the user has really used the
# app now, so creating or deleting presets or uninstalling here would change their real file.
. (Join-Path $PSScriptRoot 'common.ps1')
"running on desktop: " + [Win32Capture]::DesktopName()
if (Test-SomeoneElsesApp) { throw 'Flyweight is open, so this test would land in that window. Not running.' }

$setupExe = Join-Path $Project 'dist\FlyweightSetup.exe'
$settingsFile = Join-Path $env:APPDATA 'Flyweight\settings.json'
$settingsBefore = if (Test-Path -LiteralPath $settingsFile) { (Get-FileHash -LiteralPath $settingsFile).Hash } else { '' }
$work = Join-Path $Results 'basics-clips'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null

$results = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    [void]$results.Add($ok)
    ("{0}  {1}{2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $(if ($detail) { "  [$detail]" } else { '' }))
}
function Close-AppWindow($window) {
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    return Wait-AppGone 8
}
function Get-MenuEntries([string]$file) {
    @([ShellHelper]::List([string[]]@($file)) | Where-Object { $_.Path -like 'Compress video > *' } | ForEach-Object { $_.Text })
}

# A noisy clip, so there's something real to compress.
$clip = Join-Path $work 'clip.mp4'
& ffmpeg -y -hide_banner -v error -f lavfi -i "testsrc2=size=1920x1080:rate=30" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 12 -vf "noise=alls=12:allf=t" -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 192k -shortest $clip
$clipBytes = (Get-Item -LiteralPath $clip).Length
"test clip: {0:N1} MB" -f ($clipBytes / 1MB)

"`n=== installing ==="
$p = Start-Process $setupExe -ArgumentList '--install', '--quiet' -PassThru -Wait
Check 'installer succeeded' ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
Check 'installed copy matches the build' ((Get-FileHash -LiteralPath $InstalledExe).Hash -eq (Get-FileHash -LiteralPath $setupExe).Hash)
Check 'Start menu shortcut' (Test-Path -LiteralPath "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Flyweight.lnk")
Check 'desktop shortcut' (Test-Path -LiteralPath "$env:USERPROFILE\Desktop\Flyweight.lnk")
$entry = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Flyweight'
Check 'Apps & features entry' ($entry.DisplayName -eq 'Flyweight') "$($entry.DisplayName) $($entry.DisplayVersion)"

"`n=== right-click menu, read through the real shell ==="
$menu = @(Get-MenuEntries $clip)
$menu | ForEach-Object { "   $_" }
Check 'three levels with the right labels' ((($menu | Select-Object -First 3) -join '|') -eq 'Light (full resolution)|Balanced (up to 1080p)|Maximum (720p, 30 fps)')
$typeScriptFile = Join-Path $work 'code.ts'; Set-Content -LiteralPath $typeScriptFile 'export {}'
Check '.ts files get no menu' (@(Get-MenuEntries $typeScriptFile).Count -eq 0)
Check 'no leftovers under the old name' (-not (Test-Path 'HKCU:\Software\Classes\VideoCompressor.Menu'))

"`n=== right-clicking, then Compress video > Balanced ==="
$sw = [Diagnostics.Stopwatch]::StartNew()
Assert-NoOneElsesApp
[void][ShellHelper]::Invoke([string[]]@($clip), 'Balanced')
$w = Wait-AppWindow 'Flyweight' 20
Check 'it opened as the small corner window' (-not $w.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Current.CanResize)
Check 'it closed by itself' (Wait-AppGone 120)
$out = Join-Path $work 'clip_compressed.mp4'
Check 'a smaller copy appeared next to it' ((Test-Path -LiteralPath $out) -and (Get-Item -LiteralPath $out).Length -lt $clipBytes) ("{0:N1} MB -> {1:N1} MB in {2:N0} s" -f ($clipBytes/1MB), ((Get-Item -LiteralPath $out).Length/1MB), $sw.Elapsed.TotalSeconds)
Check 'the original is untouched' ((Get-Item -LiteralPath $clip).Length -eq $clipBytes)
Remove-Item -LiteralPath $out -Force

if ($menu -contains 'My preset') {
    "`n=== right-clicking, then Compress video > My preset ==="
    Assert-NoOneElsesApp
    [void][ShellHelper]::Invoke([string[]]@($clip), 'My preset')
    [void](Wait-AppWindow 'Flyweight' 20)
    Check 'it closed by itself' (Wait-AppGone 120)
    Check 'a smaller copy appeared next to it' ((Test-Path -LiteralPath $out) -and (Get-Item -LiteralPath $out).Length -lt $clipBytes)
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
}

"`n=== updating while the app is open ==="
Assert-NoOneElsesApp
[void](Start-Process $InstalledExe -PassThru)
$w = Wait-AppWindow 'Flyweight'
$running = @(Get-AppProcesses)[0].Id
$p = Start-Process $setupExe -ArgumentList '--install', '--quiet' -PassThru -Wait
Check 'installer succeeded with the app open' ($p.ExitCode -eq 0)
Check 'open window undisturbed' ([bool](Get-Process -Id $running -ErrorAction SilentlyContinue) -and [bool](Find-Named $w 'Advanced options'))
Check 'closed' (Close-AppWindow $w)

"`n=== idle cost with the window open ==="
Assert-NoOneElsesApp
[void](Start-Process $InstalledExe -PassThru)
$w = Wait-AppWindow 'Flyweight'
Start-Sleep -Seconds 2
$app = @(Get-AppProcesses)[0]
$before = $app.TotalProcessorTime
Start-Sleep -Seconds 3
$app.Refresh()
$used = ($app.TotalProcessorTime - $before).TotalSeconds
Check 'no CPU used while sitting idle' ($used -lt 0.05) ("{0:N3} s of CPU over 3 s" -f $used)
"memory: {0:N0} MB" -f ($app.WorkingSet64 / 1MB)
Check 'closed' (Close-AppWindow $w)

"`n=== final install, so what's installed is this build with nothing left over ==="
$p = Start-Process $setupExe -ArgumentList '--install', '--quiet' -PassThru -Wait
Check 'installed copy matches the build' ((Get-FileHash -LiteralPath $InstalledExe).Hash -eq (Get-FileHash -LiteralPath $setupExe).Hash)
Check 'old copy cleaned up' (-not (Test-Path -LiteralPath "$InstalledExe.old"))
Check 'no app processes left' (@(Get-AppProcesses).Count -eq 0)
$settingsAfter = if (Test-Path -LiteralPath $settingsFile) { (Get-FileHash -LiteralPath $settingsFile).Hash } else { '' }
Check 'your settings were not changed by any of this' ($settingsBefore -eq $settingsAfter)

Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
$failed = @($results | Where-Object { -not $_ }).Count
"`nchecks: $($results.Count), failed: $failed"
"DONE"
