# Builds dist\FlyweightSetup.exe, running the tests first unless told not to.
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'

if (-not $SkipTests) {
    dotnet test "$PSScriptRoot\tests\Flyweight.Tests\Flyweight.Tests.csproj" --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet build "$PSScriptRoot\src\Flyweight\Flyweight.csproj" -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force "$PSScriptRoot\dist" | Out-Null
Copy-Item "$PSScriptRoot\src\Flyweight\bin\Release\net48\Flyweight.exe" "$PSScriptRoot\dist\FlyweightSetup.exe" -Force

$size = [math]::Round((Get-Item "$PSScriptRoot\dist\FlyweightSetup.exe").Length / 1KB)
Write-Output "Built dist\FlyweightSetup.exe ($size KB)"
