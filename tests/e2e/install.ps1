# Installs the build over what's there and nothing else, so it can be tried from the right-click menu.
# No windows are driven and no app is closed. Like any install it puts back the Start menu and
# desktop shortcuts.
. (Join-Path $PSScriptRoot 'common.ps1')
"running on desktop: " + [Win32Capture]::DesktopName()

$exe = Join-Path $Project 'dist\FlyweightSetup.exe'
$results = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    [void]$results.Add($ok)
    ("{0}  {1}{2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $(if ($detail) { "  [$detail]" } else { '' }))
}

$p = Start-Process $exe -ArgumentList '--install', '--quiet' -PassThru -Wait
Check 'installer succeeded' ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
Check 'installed copy matches the build' ((Get-FileHash -LiteralPath $InstalledExe).Hash -eq (Get-FileHash -LiteralPath $exe).Hash) (Get-FileHash -LiteralPath $exe).Hash

$failed = @($results | Where-Object { -not $_ }).Count
"`nchecks: $($results.Count), failed: $failed"
"DONE"
