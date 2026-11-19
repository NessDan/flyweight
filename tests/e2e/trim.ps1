# Trimming inside the main window, driven through the real program on the hidden desktop. Runs the
# freshly built exe without installing it, and changes no level, so the real settings stay as they are.
. (Join-Path $PSScriptRoot 'common.ps1')
"running on desktop: " + [Win32Capture]::DesktopName()
if (@(Get-Process Flyweight* -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Flyweight is open, so this test would land in that window. Not running.' }

$exe = Join-Path $Project 'dist\FlyweightSetup.exe'
$settingsFile = Join-Path $env:APPDATA 'Flyweight\settings.json'
$settingsBefore = if (Test-Path -LiteralPath $settingsFile) { (Get-FileHash -LiteralPath $settingsFile).Hash } else { '' }
# The folder's name matters: the check that no ffmpeg is left running looks for it.
$work = Join-Path $Results 'trim-clips'
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
# Only ever the copy under test, which runs under the setup file's name. The installed app, if its
# owner opens it meanwhile, is theirs: it's never closed from here, and the checks stop instead.
function Get-AppProcesses { Get-Process FlyweightSetup -ErrorAction SilentlyContinue }
function Test-OwnersAppOpen { [bool](Get-Process Flyweight -ErrorAction SilentlyContinue) }
function Section([string]$title, [scriptblock]$body) {
    "`n=== $title ===  [$([int]$Clock.Elapsed.TotalSeconds) s in]"
    if (Test-OwnersAppOpen) { Check 'not run: the installed app was opened meanwhile' $false; return }
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

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Dpi
{
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@
# Sizes as the app counts them, whatever the display scaling.
function Get-Box($element, $window) {
    $scale = [Dpi]::GetDpiForWindow([IntPtr]$window.Current.NativeWindowHandle) / 96.0
    $r = $element.Current.BoundingRectangle
    return [pscustomobject]@{ Left = $r.Left / $scale; Top = $r.Top / $scale; Right = $r.Right / $scale; Bottom = $r.Bottom / $scale; Width = $r.Width / $scale; Height = $r.Height / $scale }
}
function Test-Resizable($window) { $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Current.CanResize }
function Set-Toggle($parent, [string]$name) {
    $e = Find-Named $parent $name
    if (-not $e) { throw "Nothing named '$name'" }
    $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}
function Wait-Until([scriptblock]$test, [int]$seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if (& $test) { return $true }; [System.Threading.Thread]::Sleep(100) }
    return $false
}

Section 'right-click style start: the whole window opens on the trim controls, then the part is cut' {
    [void](Start-Process $exe -ArgumentList '--trim', "`"$clip`"" -PassThru)
    $w = Wait-WindowLike 'Flyweight*trimming clip.mp4'
    Check 'the window opened saying which video it is trimming' $true $w.Current.Name
    Start-Sleep -Milliseconds 2500
    Check 'it is the only window on screen' (@(Get-AppWindows).Count -eq 1) ((Get-AppWindows | ForEach-Object { $_.Current.Name }) -join ' | ')
    Check 'it is the full window, not the corner one' (Test-Resizable $w)
    Check 'it opens keeping the whole video' ((Get-Field $w 'Start time') -eq '0:00.000' -and (Get-Field $w 'End time') -eq '0:12.000') ("{0} to {1}" -f (Get-Field $w 'Start time'), (Get-Field $w 'End time'))
    Check 'the levels are right there under the trim controls' ([bool](Find-Named $w 'Light: Full resolution') -and [bool](Find-Named $w 'Balanced: Up to 1080p') -and [bool](Find-Named $w 'Maximum: 720p, 30 fps'))
    Check 'so are Advanced options and New preset' ([bool](Find-Named $w 'Advanced options') -and [bool](Find-Named $w '+ New preset'))
    Check 'the drop zones made way' (-not (Find-Named $w 'Choose videos to compress') -and -not (Find-Named $w 'Choose a video to trim or crop first'))
    # Only looked for. Pressing it would play sound through the real speakers, so nothing here ever does.
    Check 'there is a Play button on the picture' ([bool](Find-Named $w 'Play'))
    Check 'with nothing chosen yet, the main button just says Compress' ((Find-ById $w 'ConfirmButton').Current.Name -eq 'Compress') (Find-ById $w 'ConfirmButton').Current.Name
    $box = Get-Box $w $w
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $scale = [Dpi]::GetDpiForWindow([IntPtr]$w.Current.NativeWindowHandle) / 96.0
    Check 'it is wide enough for the picture' ([Math]::Abs($box.Width - 592) -le 1) ("{0:N0} wide, {1:N0} tall, on a screen {2:N0} by {3:N0}" -f $box.Width, $box.Height, ($screen.Width / $scale), ($screen.Height / $scale))
    $go = Get-Box (Find-ById $w 'ConfirmButton') $w
    $levels = Get-Box (Find-Named $w 'Balanced: Up to 1080p') $w
    Check 'everything fits on screen without scrolling' ($levels.Bottom -lt $box.Bottom -and $box.Bottom -le ($screen.Bottom / $scale) + 8 -and $box.Top -ge -8) ("levels end at {0:N0}, window at {1:N0}" -f $levels.Bottom, $box.Bottom)
    Check 'nothing was compressed while it was open' ((Get-Videos) -join ',' -eq 'clip.mp4,second.mp4') ((Get-Videos) -join ',')

    Set-Toggle $w 'Advanced options'
    [void](Wait-Until { [Math]::Abs((Get-Box $w $w).Width - 1052) -le 1 } 5)
    $wide = Get-Box $w $w
    $format = Find-Named $w 'Format'
    $formatBox = if ($format) { Get-Box $format $w } else { $null }
    Check 'Advanced options open beside the picture, by widening the window' ([Math]::Abs($wide.Width - 1052) -le 1) ("{0:N0} wide" -f $wide.Width)
    Check 'and sit level with it rather than underneath' ($formatBox -and $formatBox.Left -gt $go.Right -and $formatBox.Top -lt $go.Top) $(if ($formatBox) { "Format is at {0:N0},{1:N0}; Trim and compress ends at {2:N0},{3:N0}" -f $formatBox.Left, $formatBox.Top, $go.Right, $go.Top })
    Check 'still all on screen' ($wide.Right -le ($screen.Right / $scale) + 8 -and $wide.Left -ge -8) ("from {0:N0} to {1:N0}" -f $wide.Left, $wide.Right)
    [void](Save-WindowPicture $w 'trim-inside-advanced.png')
    Set-Toggle $w 'Advanced options'
    Check 'closing them narrows it again' (Wait-Until { [Math]::Abs((Get-Box $w $w).Width - 592) -le 1 } 5) ("{0:N0} wide" -f (Get-Box $w $w).Width)
    [void](Save-WindowPicture $w 'trim-inside.png')

    # Times that fall between frames, where a cut is easiest to get one frame out.
    Set-Field $w 'Start time' '2.066'
    Set-Field $w 'End time' '5.033'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-ById $w 'ConfirmButton'
    $main = Wait-AppWindow 'Flyweight' 20
    Check 'it then became the small corner window' (Wait-Until { -not (Test-Resizable $main) } 5)
    Check 'with the trim controls gone from it' (-not (Find-ById $main 'ConfirmButton'))
    Check 'still the only window' (@(Get-AppWindows).Count -eq 1)
    $row = $null
    while (-not $row -and $sw.Elapsed.TotalSeconds -lt 10) { $row = Find-NameLike $main 'clip.mp4*0:02 to 0:05'; Start-Sleep -Milliseconds 100 }
    Check 'its row says which part is kept' ([bool]$row) $(if ($row) { $row.Current.Name })
    $corner = Get-Box $main $main
    Check 'in the corner at its usual size' ([Math]::Abs($corner.Width - 480) -le 1 -and $corner.Right -gt ($screen.Right / $scale) - 40) ("{0:N0} wide, right edge at {1:N0}" -f $corner.Width, $corner.Right)
    $done = $null
    while (-not $done -and $sw.Elapsed.TotalSeconds -lt 60) { $done = Find-NameLike $main 'Trimmed to*'; Start-Sleep -Milliseconds 200 }
    Check 'it finished' ([bool]$done) $(if ($done) { $done.Current.Name })
    Check 'the app closed by itself' (Wait-AppGone 30)
    $out = Join-Path $work 'clip_trimmed.mp4'
    Check 'the trimmed copy holds exactly frames 62 to 150' ((Test-Path -LiteralPath $out) -and (Get-Frames $out) -eq 89) $(if (Test-Path -LiteralPath $out) { "{0} frames, {1:N2} MB in {2:N0} s" -f (Get-Frames $out), ((Get-Item -LiteralPath $out).Length / 1MB), $sw.Elapsed.TotalSeconds })
    Check 'no sound from before the cut is left inside the file' ((Get-SoundPackets $out) -ge 136 -and (Get-SoundPackets $out) -le 146) "$(Get-SoundPackets $out) packets; 2.967 s is 139"
    Check 'the original is untouched' ((Get-Item -LiteralPath $clip).Length -eq $clipBytes)
    Check 'no full-length copy was made' (-not (Test-Path -LiteralPath (Join-Path $work 'clip_compressed.mp4')))
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
}

Section 'cancelling leaves nothing behind' {
    [void](Start-Process $exe -ArgumentList '--trim', "`"$clip`"" -PassThru)
    $w = Wait-WindowLike 'Flyweight*trimming clip.mp4'
    Start-Sleep -Milliseconds 800

    # Nothing is played, so here is the line on the strip, which starts on the start handle.
    Check 'Start here and End here are on the picture' ((Find-ById $w 'StartHereButton').Current.Name -eq 'Start here' -and (Find-ById $w 'EndHereButton').Current.Name -eq 'End here')
    Set-Field $w 'End time' '9'
    Invoke-ById $w 'StartHereButton'
    Check 'Start here counts an end still being typed, and starts the part on the line' (Wait-Until { (Get-Field $w 'End time') -eq '0:09.000' -and (Get-Field $w 'Start time') -eq '0:00.000' } 5) ("{0} to {1}" -f (Get-Field $w 'Start time'), (Get-Field $w 'End time'))
    Invoke-ById $w 'EndHereButton'
    Check 'End here ends it on the line, as near the start as a part can be' (Wait-Until { (Get-Field $w 'End time') -eq '0:00.100' } 5) (Get-Field $w 'End time')

    Invoke-Button $w 'Cancel'
    Check 'the app closed' (Wait-AppGone 10)
    Check 'no files were made' ((Get-Videos) -join ',' -eq 'clip.mp4,second.mp4') ((Get-Videos) -join ',')
}

Section 'closing the window with its X does the same' {
    [void](Start-Process $exe -ArgumentList '--trim', "`"$clip`"" -PassThru)
    $w = Wait-WindowLike 'Flyweight*trimming clip.mp4'
    Start-Sleep -Milliseconds 300
    $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Check 'the app closed' (Wait-AppGone 10)
    Check 'no ffmpeg left running' (@(Get-CimInstance Win32_Process -Filter "Name='ffmpeg.exe'" | Where-Object { $_.CommandLine -like "*trim-clips*" }).Count -eq 0)
    Check 'no files were made' ((Get-Videos) -join ',' -eq 'clip.mp4,second.mp4') ((Get-Videos) -join ',')
}

Section 'two videos at once are asked about one after the other, in the same window' {
    [void](Start-Process $exe -ArgumentList '--trim', "`"$clip`"", "`"$second`"" -PassThru)
    $first = Wait-WindowLike 'Flyweight*trimming clip.mp4*'
    Check 'the first says another is waiting' ($first.Current.Name -like '*1 more after this') $first.Current.Name
    Check 'and offers a way out of them all' ([bool](Find-Named $first 'Skip them all'))
    $handle = $first.Current.NativeWindowHandle
    Invoke-Button $first 'Skip this one'
    $next = Wait-WindowLike 'Flyweight*trimming second.mp4'
    Check 'then the second is asked about' ($next.Current.Name -notlike '*more*') $next.Current.Name
    Check 'in the same window, and the only one' ($next.Current.NativeWindowHandle -eq $handle -and @(Get-AppWindows).Count -eq 1)
    Check 'with nothing left to skip to' (-not (Find-Named $next 'Skip them all'))
    Invoke-Button $next 'Cancel'
    Check 'the app closed' (Wait-AppGone 10)
}

Section 'skipping them all drops the rest' {
    [void](Start-Process $exe -ArgumentList '--trim', "`"$clip`"", "`"$second`"" -PassThru)
    $first = Wait-WindowLike 'Flyweight*trimming clip.mp4*'
    Invoke-Button $first 'Skip them all'
    Check 'the app closed without asking about the other one' (Wait-AppGone 10)
    Check 'no files were made' ((Get-Videos) -join ',' -eq 'clip.mp4,second.mp4') ((Get-Videos) -join ',')
}

Section 'a broadcast-style file is cut on the frame chosen, not the next keyframe' {
    # Keyframes every second. A cut from 1.566 s used to start at the next one, half a second late.
    $stream = Join-Path $work 'stream.ts'
    & ffmpeg -y -hide_banner -v error -f lavfi -i "testsrc2=size=640x360:rate=30" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 6 -c:v libx264 -preset ultrafast -g 30 -pix_fmt yuv420p -c:a aac -b:a 96k -f mpegts $stream
    [void](Start-Process $exe -ArgumentList '--trim', "`"$stream`"" -PassThru)
    $w = Wait-WindowLike 'Flyweight*trimming stream.ts'
    Start-Sleep -Milliseconds 1500
    Set-Field $w 'Start time' '1.566'
    Set-Field $w 'End time' '2.533'
    Invoke-ById $w 'ConfirmButton'
    Check 'the app closed by itself' (Wait-AppGone 60)
    $out = Join-Path $work 'stream_trimmed.mp4'
    Check 'all 29 frames of the part are there' ((Test-Path -LiteralPath $out) -and (Get-Frames $out) -eq 29) $(if (Test-Path -LiteralPath $out) { "$(Get-Frames $out) frames, $(Get-SoundPackets $out) packets of sound" })
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
    Remove-Item -LiteralPath $stream -Force
}

Section 'with the app already open, a second copy hands the trim over and the open window shows it' {
    # A file that isn't a video opens the full window without going through setup.
    $notes = Join-Path $work 'notes.txt'; Set-Content -LiteralPath $notes 'hello'
    [void](Start-Process $exe -ArgumentList "`"$notes`"" -PassThru)
    $main = Wait-AppWindow 'Flyweight'
    Start-Sleep -Milliseconds 600
    Check 'the full window has a Trim first target' ([bool](Find-Named $main 'Choose a video to trim or crop first'))
    $before = Get-Box $main $main
    Check 'it opened only as tall as what is in it, whatever height an earlier version remembered' ($before.Height -lt 450) ("{0:N0} by {1:N0}" -f $before.Width, $before.Height)
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $dots = [Dpi]::GetDpiForWindow([IntPtr]$main.Current.NativeWindowHandle) / 96.0
    $state = $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $state.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep -Milliseconds 800
    $filling = Get-Box $main $main
    Check 'maximized, it fills the screen from top to bottom' ($filling.Height -ge ($area.Height / $dots) - 4 -and $filling.Width -ge ($area.Width / $dots) - 4) ("{0:N0} by {1:N0} on a screen {2:N0} by {3:N0}" -f $filling.Width, $filling.Height, ($area.Width / $dots), ($area.Height / $dots))
    $state.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Check 'put back, it is as tall as what is in it again' (Wait-Until { $now = Get-Box $main $main; [Math]::Abs($now.Height - $before.Height) -le 2 -and [Math]::Abs($now.Width - $before.Width) -le 1 } 5) ("{0:N0} by {1:N0}" -f (Get-Box $main $main).Width, (Get-Box $main $main).Height)
    $handle = $main.Current.NativeWindowHandle
    $p = Start-Process $exe -ArgumentList '--trim', "`"$clip`"" -PassThru
    $w = Wait-WindowLike 'Flyweight*trimming clip.mp4'
    Check 'the second copy handed over and left' ($p.WaitForExit(5000) -and @(Get-AppProcesses).Count -eq 1) "processes: $(@(Get-AppProcesses).Count)"
    Check 'the window that was open is the one trimming' ($w.Current.NativeWindowHandle -eq $handle -and @(Get-AppWindows).Count -eq 1)
    Start-Sleep -Milliseconds 1500
    Check 'its drop zones made way for the trim controls' (-not (Find-Named $w 'Choose videos to compress') -and [bool](Find-ById $w 'ConfirmButton'))
    $during = Get-Box $w $w
    Check 'it made room, and got no shorter than it had been made' ($during.Width -ge 591 -and $during.Height -ge [Math]::Max($before.Height, 600) - 1) ("{0:N0} by {1:N0}, from {2:N0} by {3:N0}" -f $during.Width, $during.Height, $before.Width, $before.Height)
    Set-Field $w 'Start time' '10.5'
    Invoke-ById $w 'ConfirmButton'
    $done = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $done -and $sw.Elapsed.TotalSeconds -lt 60) { $done = Find-NameLike $w 'Trimmed to*'; Start-Sleep -Milliseconds 200 }
    Check 'it joined the window''s queue and finished' ([bool]$done) $(if ($done) { $done.Current.Name })
    $out = Join-Path $work 'clip_trimmed.mp4'
    Check 'the last second and a half was kept' ((Test-Path -LiteralPath $out) -and (Get-Frames $out) -eq 45) $(if (Test-Path -LiteralPath $out) { "$(Get-Frames $out) frames" })
    $after = Find-AppWindow 'Flyweight'
    Check 'the window stayed open under its own name' ([bool]$after)
    Check 'with the drop zones back' ($after -and [bool](Find-Named $after 'Choose videos to compress') -and -not (Find-ById $after 'ConfirmButton'))
    $afterBox = if ($after) { Get-Box $after $after } else { $null }
    Check 'and back at the width it had' ($afterBox -and [Math]::Abs($afterBox.Width - $before.Width) -le 1 -and (Test-Resizable $after)) $(if ($afterBox) { "{0:N0} wide, was {1:N0}" -f $afterBox.Width, $before.Width })
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
}

Section 'a corner window that is busy opens out for a trim and goes back to the corner' {
    # Long enough to still be compressing when the trim is asked for.
    $long = Join-Path $work 'long.mp4'
    & ffmpeg -y -hide_banner -v error -f lavfi -i "testsrc2=size=1920x1080:rate=30" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 45 -vf "noise=alls=12:allf=t" -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 96k -shortest $long
    [void](Start-Process $exe -ArgumentList "`"$long`"" -PassThru)
    $corner = Wait-AppWindow 'Flyweight'
    Check 'a right-click compress starts in the corner window' (-not (Test-Resizable $corner))
    $handle = $corner.Current.NativeWindowHandle
    [void](Start-Process $exe -ArgumentList '--trim', "`"$clip`"" -PassThru)
    $w = Wait-WindowLike 'Flyweight*trimming clip.mp4'
    Start-Sleep -Milliseconds 1200
    Check 'the same window opened out in full for the trim' ($w.Current.NativeWindowHandle -eq $handle -and (Test-Resizable $w) -and [bool](Find-ById $w 'ConfirmButton'))
    Check 'with the video it was compressing still listed' ([bool](Find-NameLike $w 'long.mp4*'))
    Invoke-Button $w 'Cancel'
    $back = Wait-AppWindow 'Flyweight' 10
    Check 'cancelled, it went back to being the corner window' (Wait-Until { -not (Test-Resizable $back) } 5)
    Check 'and the app closed by itself once the compress was done' (Wait-AppGone 120)
    Check 'which was not disturbed' (Test-Path -LiteralPath (Join-Path $work 'long_compressed.mp4'))
    Check 'no trimmed copy was made' (-not (Test-Path -LiteralPath (Join-Path $work 'clip_trimmed.mp4')))
}

$settingsAfter = if (Test-Path -LiteralPath $settingsFile) { (Get-FileHash -LiteralPath $settingsFile).Hash } else { '' }
"`n=== afterwards ==="
Check 'no app processes left' (@(Get-AppProcesses).Count -eq 0)
Check 'settings were not changed by any of this' ($settingsBefore -eq $settingsAfter)
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
$failed = @($results | Where-Object { -not $_ }).Count
"`nchecks: $($results.Count), failed: $failed"
"DONE"
