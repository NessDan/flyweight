# Shared helpers for driving the real program. These scripts only ever run on the hidden desktop
# (tests\run-e2e.ps1 puts them there), so nothing here can affect the screen, focus or mouse of
# whoever is using the PC. Windows PowerShell runs them, so keep them ASCII.
$ErrorActionPreference = 'Stop'
# Sections print how far in they start, so a run that stalls shows where.
$script:Clock = [Diagnostics.Stopwatch]::StartNew()
$script:Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:Results = Join-Path $Project 'TestResults\e2e'
$script:Snap = Join-Path $Results 'pictures'
$script:InstalledExe = Join-Path $env:LOCALAPPDATA 'Programs\Flyweight\Flyweight.exe'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition (Get-Content (Join-Path $PSScriptRoot 'ShellHelper.cs') -Raw) -ReferencedAssemblies System.Core, System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Capture
{
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(int thread);
    [DllImport("kernel32.dll")] public static extern int GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static string DesktopName()
    {
        var name = new StringBuilder(256);
        int needed;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 512, out needed);
        return name.ToString();
    }
}
'@
[void][Win32Capture]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

# A guard: if these scripts ever ran on the user's desktop, stop before opening anything.
if ([Win32Capture]::DesktopName() -ne 'VideoCompressorTests') { throw "Refusing to run on desktop '$([Win32Capture]::DesktopName())'" }

$script:AE = [System.Windows.Automation.AutomationElement]
$script:TS = [System.Windows.Automation.TreeScope]

function Find-AppWindow([string]$title) {
    $condition = New-Object System.Windows.Automation.PropertyCondition $AE::NameProperty, $title
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $condition)) {
        $name = (Get-Process -Id $w.Current.ProcessId -ErrorAction SilentlyContinue).ProcessName
        if ($name -like 'Flyweight*') { return $w }
    }
    return $null
}

function Wait-AppWindow([string]$title, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Find-AppWindow $title
        if ($w) { return $w }
        [System.Threading.Thread]::Sleep(100)
    }
    throw "Window '$title' didn't appear within $seconds s"
}

function Find-Named($parent, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition $AE::NameProperty, $name
    return $parent.FindFirst($TS::Descendants, $condition)
}

function Find-ById($parent, [string]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition $AE::AutomationIdProperty, $id
    return $parent.FindFirst($TS::Descendants, $condition)
}

function Invoke-ById($parent, [string]$id) {
    $button = Find-ById $parent $id
    if (-not $button) { throw "Nothing with the id '$id'" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Invoke-Button($parent, [string]$name) {
    $condition = New-Object System.Windows.Automation.AndCondition (New-Object System.Windows.Automation.PropertyCondition $AE::NameProperty, $name), (New-Object System.Windows.Automation.PropertyCondition $AE::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Button))
    $button = $parent.FindFirst($TS::Descendants, $condition)
    if (-not $button) { throw "No button named '$name'" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Test-Topmost($window) {
    $exStyle = [Win32Capture]::GetWindowLong([IntPtr]$window.Current.NativeWindowHandle, -20)
    return ($exStyle -band 0x8) -ne 0
}

function Save-WindowPicture($window, [string]$fileName) {
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    $rect = New-Object Win32Capture+RECT
    [void][Win32Capture]::GetWindowRect($hwnd, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $g.GetHdc()
    [void][Win32Capture]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    New-Item -ItemType Directory -Force $Snap | Out-Null
    $path = Join-Path $Snap $fileName
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return "$fileName (${w}x$h)"
}

# Copies of Flyweight that this script started are its own. Any other is someone's real one. Copies
# share one channel per signed-in user, hidden desktop or not, so a start from here would go to that
# window: the checks stop as soon as one appears, and never stop it themselves.
function Get-AppProcesses {
    $mine = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'Flyweight%'" | Where-Object { $_.ParentProcessId -eq $PID } | ForEach-Object { [int]$_.ProcessId })
    @(Get-Process Flyweight* -ErrorAction SilentlyContinue | Where-Object { $mine -contains $_.Id })
}
function Test-SomeoneElsesApp { [bool](Get-CimInstance Win32_Process -Filter "Name LIKE 'Flyweight%'" | Where-Object { $_.ParentProcessId -ne $PID }) }
function Assert-NoOneElsesApp { if (Test-SomeoneElsesApp) { throw 'Flyweight was opened by someone else, so the checks stopped: a start from here would go to that window.' } }

# ffmpeg started by this app, leaving out anything else on the machine (like the tuning run).
function Get-AppFfmpeg {
    $app = @(Get-AppProcesses | ForEach-Object { $_.Id })
    if ($app.Count -eq 0) { return @() }
    @(Get-CimInstance Win32_Process -Filter "Name='ffmpeg.exe'" | Where-Object { $app -contains $_.ParentProcessId } | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } | Where-Object { $_ })
}

function Wait-AppGone([int]$seconds = 30) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (@(Get-AppProcesses).Count -gt 0 -and $sw.Elapsed.TotalSeconds -lt $seconds) { Start-Sleep -Milliseconds 200 }
    return @(Get-AppProcesses).Count -eq 0
}
