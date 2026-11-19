# Runs scripts from tests\e2e on a hidden desktop. They drive the real program, dist\FlyweightSetup.exe,
# so build it first. Exits with 1 unless every script finished and every check passed.
#
#   pwsh tests/run-e2e.ps1 trim
#   pwsh tests/run-e2e.ps1 trim right-click-trim basics
#
# They refuse to run while Flyweight is open: a start from here would land in that window instead.
[CmdletBinding(PositionalBinding = $false)]
param([Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)][string[]]$Scripts, [int]$Seconds = 900)
. (Join-Path $PSScriptRoot 'hidden-desktop\HiddenDesktop.ps1')

$allPassed = $true
foreach ($name in $Scripts) {
    $file = Join-Path $PSScriptRoot "e2e\$name.ps1"
    if (-not (Test-Path -LiteralPath $file)) { Write-Output "No such script: $file"; $allPassed = $false; continue }
    if (Get-Process Flyweight* -ErrorAction SilentlyContinue) { Write-Output "Flyweight is open, so $name would land in that window. Close it and run this again."; exit 1 }

    Write-Output "`n##### $name"
    # Windows PowerShell can't find its own commands if it inherits this one's module path.
    $modulePath = $env:PSModulePath
    $env:PSModulePath = [Environment]::GetEnvironmentVariable('PSModulePath', 'Machine')
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-Hidden "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$file`"" "e2e-$name" $Seconds
    $env:PSModulePath = $modulePath

    $logFile = Join-Path $Logs "e2e-$name.log"
    Get-Content -LiteralPath $logFile
    Write-Output ("{0} took {1:N0} s" -f $name, $clock.Elapsed.TotalSeconds)
    $summary = Select-String -LiteralPath $logFile -Pattern '^checks: (\d+), failed: (\d+)' | Select-Object -Last 1
    $finished = Select-String -LiteralPath $logFile -Pattern '^DONE$' -Quiet
    if ($code -eq -1) { Write-Output "NOT FINISHED: $name was stopped at the $Seconds s limit."; $allPassed = $false }
    elseif (-not $finished -or -not $summary) { Write-Output "NOT FINISHED: $name stopped early. Log: $logFile"; $allPassed = $false }
    elseif ([int]$summary.Matches[0].Groups[2].Value -gt 0) { Write-Output "FAILED: $name. Log: $logFile"; $allPassed = $false }
}
if (-not $allPassed) { exit 1 }
