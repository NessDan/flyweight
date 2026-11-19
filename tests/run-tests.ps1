# Runs the tests on a hidden desktop, where the ones that show a window or open the sound output run
# too, without anything appearing on screen. Exits with 1 unless every test ran and passed.
#
#   pwsh tests/run-tests.ps1                                       all of them, about 3 minutes
#   pwsh tests/run-tests.ps1 -Filter "FullyQualifiedName~TrimSelectionTests"
#   pwsh tests/run-tests.ps1 -Filter "FullyQualifiedName~Snapshots_when_asked" -SnapshotDir <folder>
#       pictures of the windows, including the ones on the website (site-*.png)
#
# -Folder runs the tests of a copy of the project instead (tests/mutate.py uses it).
param([string]$Filter = '', [string]$Log = 'tests', [string]$Folder = '', [string]$SnapshotDir = '', [int]$Seconds = 570)
. (Join-Path $PSScriptRoot 'hidden-desktop\HiddenDesktop.ps1')
if (-not $Folder) { $Folder = $Root }
if ($SnapshotDir) { $env:FLYWEIGHT_SNAPSHOT_DIR = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SnapshotDir) }

$project = Join-Path $Folder 'tests\Flyweight.Tests\Flyweight.Tests.csproj'
$filterArg = if ($Filter) { " --filter `"$Filter`"" } else { '' }
$code = Invoke-Hidden "dotnet test `"$project`" --nologo -v quiet --logger `"console;verbosity=normal`"$filterArg" $Log $Seconds

$logFile = Join-Path $Logs "$Log.log"
# What went wrong, if anything. Passed tests are left out, since their names can contain these words.
Get-Content -LiteralPath $logFile | Where-Object { $_ -notmatch '^\s+Passed ' } | Select-String -CaseSensitive -Pattern 'error |\[FAIL\]|Failed |Expected:|Actual:|Exception|Error Message|Assert\.' | Select-Object -First 50 | ForEach-Object { $_.Line }
if (-not (Test-AllPassed (Read-TestTotals $logFile) $code $Seconds $logFile)) { exit 1 }
