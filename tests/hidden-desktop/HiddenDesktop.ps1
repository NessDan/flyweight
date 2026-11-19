# Dot-source this, then use Invoke-Hidden. It starts nothing on the desktop anyone is looking at:
# PowerShell compiles HiddenDesktop.cs in-process, and that starts the command on a private desktop
# called VideoCompressorTests, with no console window, in a job that ends everything it started.
# Keep that name: OnHiddenDesktopFact and tests\e2e\common.ps1 look for it, and anywhere else the
# window tests skip themselves rather than fail.
$script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:Logs = Join-Path $Root 'TestResults\logs'
if (-not ('HiddenDesktop' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'HiddenDesktop.cs') }

# Runs a command line on the hidden desktop, with everything it prints in TestResults\logs\<LogName>.log.
# Returns its exit code, or -1 if it was stopped at the time limit.
function Invoke-Hidden([string]$Command, [string]$LogName, [int]$TimeoutSeconds = 570, [switch]$LowPriority) {
    New-Item -ItemType Directory -Force $Logs | Out-Null
    $log = Join-Path $Logs "$LogName.log"
    $line = "cmd.exe /d /s /c `"$Command > `"$log`" 2>&1`""
    return [HiddenDesktop]::Run($line, $Root, $TimeoutSeconds * 1000, [bool]$LowPriority)
}

# The totals from the summary dotnet test prints, or $null when there isn't one (it didn't build, or
# no test matched the filter).
function Read-TestTotals([string]$LogFile) {
    $text = Get-Content -LiteralPath $LogFile -Raw
    if (-not $text) { return $null }
    # Its usual summary, on one line.
    $lines = [regex]::Matches($text, '(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)')
    if ($lines.Count -gt 0) {
        $g = $lines[$lines.Count - 1].Groups
        return [pscustomobject]@{ Failed = [int]$g[2].Value; Passed = [int]$g[3].Value; Skipped = [int]$g[4].Value; Total = [int]$g[5].Value }
    }
    # The longer one it prints with --logger "console;verbosity=normal", which leaves out Failed and
    # Skipped when there are none.
    $at = $text.LastIndexOf('Total tests:')
    if ($at -lt 0) { return $null }
    $tail = $text.Substring($at)
    $count = { param($label) $m = [regex]::Match($tail, "(?m)^\s*$label\s*:\s*(\d+)"); if ($m.Success) { [int]$m.Groups[1].Value } else { 0 } }
    return [pscustomobject]@{ Total = (& $count 'Total tests'); Passed = (& $count 'Passed'); Failed = (& $count 'Failed'); Skipped = (& $count 'Skipped') }
}

# Says what the run came to, and returns true only if tests ran and none failed or were skipped.
# It prints with Write-Host, since anything written as output would come back mixed into the answer.
function Test-AllPassed($Totals, [int]$ExitCode, [int]$TimeoutSeconds, [string]$LogFile) {
    if ($ExitCode -eq -1) { Write-Host "NOT FINISHED: stopped at the $TimeoutSeconds s limit. Log: $LogFile"; return $false }
    if (-not $Totals -or $Totals.Total -eq 0) { Write-Host "NO TEST RESULTS: it didn't build, or nothing matched the filter. Log: $LogFile"; return $false }
    Write-Host ("Total {0}: {1} passed, {2} failed, {3} skipped" -f $Totals.Total, $Totals.Passed, $Totals.Failed, $Totals.Skipped)
    if ($Totals.Failed -gt 0) { Write-Host "FAILED. Log: $LogFile"; return $false }
    if ($Totals.Skipped -gt 0) { Write-Host "NOT A PASS: tests were skipped, so they didn't run on the hidden desktop. Log: $LogFile"; return $false }
    if ($ExitCode -ne 0) { Write-Host "FAILED: exit code $ExitCode. Log: $LogFile"; return $false }
    return $true
}
