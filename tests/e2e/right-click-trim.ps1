# Installs the build, then uses Trim first through the real right-click menu, on the hidden desktop.
# Changes no level and closes no full window that someone sized, so nothing here saves settings.
. (Join-Path $PSScriptRoot 'common.ps1')
"running on desktop: " + [Win32Capture]::DesktopName()
if (Test-SomeoneElsesApp) { throw 'Flyweight is open, so this test would land in that window. Not running.' }

$exe = Join-Path $Project 'dist\FlyweightSetup.exe'
$settingsFile = Join-Path $env:APPDATA 'Flyweight\settings.json'
$settingsBefore = if (Test-Path -LiteralPath $settingsFile) { (Get-FileHash -LiteralPath $settingsFile).Hash } else { '' }
$work = Join-Path $Results 'trim-clips'
$version = [regex]::Match((Get-Content -Raw (Join-Path $Project 'src\Flyweight\Flyweight.csproj')), '<Version>([^<]+)</Version>').Groups[1].Value
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null

$results = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    [void]$results.Add($ok)
    ("{0}  {1}{2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $(if ($detail) { "  [$detail]" } else { '' }))
}
function Get-AppWindows {
    # A window opened from another one is listed under it, not beside it, so look there as well.
    $isWindow = New-Object System.Windows.Automation.PropertyCondition $AE::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Window)
    $found = @()
    foreach ($top in $AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ((Get-Process -Id $top.Current.ProcessId -ErrorAction SilentlyContinue).ProcessName -notlike 'Flyweight*') { continue }
        $found += $top
        $found += @($top.FindAll($TS::Descendants, $isWindow))
    }
    return $found
}
function Wait-WindowLike([string]$pattern, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Get-AppWindows | Where-Object { $_.Current.Name -like $pattern } | Select-Object -First 1
        if ($w) { return $w }
        [System.Threading.Thread]::Sleep(100)
    }
    throw "No window like '$pattern' appeared within $seconds s"
}
function Wait-NoWindowLike([string]$pattern, [int]$seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-AppWindows | Where-Object { $_.Current.Name -like $pattern })) { return $true }
        [System.Threading.Thread]::Sleep(100)
    }
    return $false
}
function Set-Field($window, [string]$name, [string]$text) {
    $condition = New-Object System.Windows.Automation.AndCondition (New-Object System.Windows.Automation.PropertyCondition $AE::NameProperty, $name), (New-Object System.Windows.Automation.PropertyCondition $AE::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Edit))
    $field = $window.FindFirst($TS::Descendants, $condition)
    if (-not $field) { throw "No text box named '$name'" }
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}
function Get-Field($window, [string]$name) {
    $condition = New-Object System.Windows.Automation.AndCondition (New-Object System.Windows.Automation.PropertyCondition $AE::NameProperty, $name), (New-Object System.Windows.Automation.PropertyCondition $AE::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Edit))
    $field = $window.FindFirst($TS::Descendants, $condition)
    if (-not $field) { return $null }
    return $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Find-NameLike($parent, [string]$pattern) {
    foreach ($e in $parent.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($e.Current.Name -like $pattern) { return $e }
    }
    return $null
}
function Get-Frames([string]$file) {
    [int](& ffprobe -v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames -of csv=p=0 -i $file)
}
function Get-SoundPackets([string]$file) {
    # Everything really in the file, including sound a player would be told to skip.
    [int](& ffprobe -v error -ignore_editlist 1 -select_streams a:0 -count_packets -show_entries stream=nb_read_packets -of csv=p=0 -i $file)
}
function Section([string]$title, [scriptblock]$body) {
    "`n=== $title ===  [$([int]$Clock.Elapsed.TotalSeconds) s in]"
    if (Test-SomeoneElsesApp) { Check 'not run: someone opened Flyweight meanwhile' $false; return }
    try { & $body } catch { Check "section ran without an error" $false "$_" }
    Get-AppProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    [void](Wait-AppGone 10)
}
function Get-Videos { @(Get-ChildItem -LiteralPath $work -Filter '*.mp4' | ForEach-Object { $_.Name } | Sort-Object) }

# Noisy clips, so there's something real to compress. 12 s at 30 frames a second.
$clip = Join-Path $work 'clip.mp4'
& ffmpeg -y -hide_banner -v error -f lavfi -i "testsrc2=size=1920x1080:rate=30" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 12 -vf "noise=alls=12:allf=t" -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 96k -shortest $clip
$second = Join-Path $work 'second.mp4'
Copy-Item -LiteralPath $clip $second
$clipBytes = (Get-Item -LiteralPath $clip).Length
"test clip: {0:N1} MB, {1} frames" -f ($clipBytes / 1MB), (Get-Frames $clip)

"`n=== installing over what's there ==="
$p = Start-Process $exe -ArgumentList '--install', '--quiet' -PassThru -Wait
Check 'installer succeeded' ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
Check 'installed copy matches the build' ((Get-FileHash -LiteralPath $InstalledExe).Hash -eq (Get-FileHash -LiteralPath $exe).Hash)
$entry = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Flyweight'
Check 'Apps & features shows the new version' ($version -and $entry.DisplayVersion -like "$version*") "$($entry.DisplayName) $($entry.DisplayVersion), building $version"

"`n=== right-click menu, read through the real shell ==="
$menu = @([ShellHelper]::List([string[]]@($clip)) | Where-Object { $_.Path -like 'Compress video > *' } | ForEach-Object { $_.Text })
$menu | ForEach-Object { "   $_" }
Check 'the three levels still come first' ((($menu | Select-Object -First 3) -join '|') -eq 'Light (full resolution)|Balanced (up to 1080p)|Maximum (720p, 30 fps)')
Check 'Trim or crop first is the last entry' ($menu.Count -ge 4 -and $menu[-1] -like 'Trim or crop first*') $menu[-1]
$typeScriptFile = Join-Path $work 'code.ts'; Set-Content -LiteralPath $typeScriptFile 'export {}'
Check '.ts files still get no menu' (@([ShellHelper]::List([string[]]@($typeScriptFile)) | Where-Object { $_.Path -like 'Compress video > *' }).Count -eq 0)

Section 'right-click, Trim first, then cancel' {
    "invoked: " + [ShellHelper]::Invoke([string[]]@($clip), 'Trim or crop first')
    $trim = Wait-WindowLike 'Flyweight*trimming clip.mp4*'
    Start-Sleep -Milliseconds 800
    Check 'it is the only window on screen' (@(Get-AppWindows).Count -eq 1) ((Get-AppWindows | ForEach-Object { $_.Current.Name }) -join ' | ')
    Check 'it runs from the installed copy' (@(Get-AppProcesses)[0].Path -eq $InstalledExe) @(Get-AppProcesses)[0].Path
    Check 'it is the whole window, with the levels under the trim controls' ($trim.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Current.CanResize -and [bool](Find-Named $trim 'Balanced: Up to 1080p') -and [bool](Find-Named $trim 'Advanced options') -and [bool](Find-ById $trim 'ConfirmButton'))
    Invoke-Button $trim 'Cancel'
    Check 'the app closed' (Wait-AppGone 10)
    Check 'no files were made' ((Get-Videos) -join ',' -eq 'clip.mp4,second.mp4') ((Get-Videos) -join ',')
}

Section 'right-click, Trim first, keep two and a half seconds' {
    [void][ShellHelper]::Invoke([string[]]@($clip), 'Trim or crop first')
    $trim = Wait-WindowLike 'Flyweight*trimming clip.mp4*'
    Start-Sleep -Milliseconds 2000
    Set-Field $trim 'Start time' '4'
    Set-Field $trim 'End time' '0:06.5'
    Invoke-ById $trim 'ConfirmButton'
    $main = Wait-AppWindow 'Flyweight' 20
    $sw2 = [Diagnostics.Stopwatch]::StartNew()
    while ($main.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Current.CanResize -and $sw2.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 100 }
    Check 'it became the small corner window' (-not $main.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Current.CanResize)
    Check 'it closed by itself' (Wait-AppGone 90)
    $out = Join-Path $work 'clip_trimmed.mp4'
    Check 'the trimmed copy is exactly 2.5 seconds' ((Test-Path -LiteralPath $out) -and (Get-Frames $out) -eq 75) $(if (Test-Path -LiteralPath $out) { "{0} frames, {1:N2} MB" -f (Get-Frames $out), ((Get-Item -LiteralPath $out).Length / 1MB) })
    Check 'the original is untouched' ((Get-Item -LiteralPath $clip).Length -eq $clipBytes)
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
}

Section 'two videos selected, then Trim first' {
    [void][ShellHelper]::Invoke([string[]]@($clip, $second), 'Trim or crop first')
    $first = Wait-WindowLike 'Flyweight*trimming*'
    # Explorer starts a copy of the app per video, so the second one arrives a moment after the first.
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($first.Current.Name -notlike '*1 more after this' -and $sw.Elapsed.TotalSeconds -lt 15) { Start-Sleep -Milliseconds 200 }
    Check 'the window says another is waiting' ($first.Current.Name -like '*1 more after this') $first.Current.Name
    Check 'still one copy of the app' (@(Get-AppProcesses).Count -eq 1) "processes: $(@(Get-AppProcesses).Count)"
    $firstName = $first.Current.Name
    Invoke-Button $first 'Skip this one'
    $next = $null
    while (-not $next -and $sw.Elapsed.TotalSeconds -lt 30) {
        $next = Get-AppWindows | Where-Object { $_.Current.Name -like 'Flyweight*trimming*' -and $_.Current.Name -notlike '*more after this' } | Select-Object -First 1
        Start-Sleep -Milliseconds 200
    }
    $other = if ($firstName -like '*clip.mp4*') { '*second.mp4*' } else { '*clip.mp4*' }
    Check 'then the other video is asked about' ([bool]$next -and $next.Current.Name -like $other) $(if ($next) { "$firstName  ->  $($next.Current.Name)" })
    if ($next) { Invoke-Button $next 'Cancel' }
    Check 'the app closed' (Wait-AppGone 10)
    Check 'no files were made' ((Get-Videos) -join ',' -eq 'clip.mp4,second.mp4') ((Get-Videos) -join ',')
}

$settingsAfter = if (Test-Path -LiteralPath $settingsFile) { (Get-FileHash -LiteralPath $settingsFile).Hash } else { '' }
"`n=== afterwards ==="
Check 'no app processes left' (@(Get-AppProcesses).Count -eq 0)
Check 'settings were not changed by any of this' ($settingsBefore -eq $settingsAfter)
Check 'what is installed is this build' ((Get-FileHash -LiteralPath $InstalledExe).Hash -eq (Get-FileHash -LiteralPath $exe).Hash)
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
$failed = @($results | Where-Object { -not $_ }).Count
"`nchecks: $($results.Count), failed: $failed"
"DONE"
