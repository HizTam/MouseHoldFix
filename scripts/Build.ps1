param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-DotNet.ps1')
Push-Location $projectRoot
try {
    Invoke-DotNet restore MouseHoldFix.sln --configfile NuGet.Config
    Invoke-DotNet build MouseHoldFix.sln --configuration $Configuration --no-restore
    if (!$RunTests) { return }
    $testProject = Join-Path $projectRoot 'tests\MouseHoldFix.Tests\MouseHoldFix.Tests.csproj'
    if (!(Test-Path -LiteralPath $testProject)) { throw 'Local tests are not available in this checkout.' }
    $testResults = Join-Path $projectRoot ('artifacts\test-results\' + [Guid]::NewGuid().ToString('N'))
    Invoke-DotNet test $testProject --configuration $Configuration '-p:RestoreConfigFile=NuGet.Config' --logger 'trx;LogFileName=tests.trx' --results-directory $testResults
    $report = Join-Path $testResults 'tests.trx'
    if (!(Test-Path -LiteralPath $report)) { throw 'The test runner did not produce a result report.' }
    [xml]$results = Get-Content -LiteralPath $report -Raw
    $counts = $results.TestRun.ResultSummary.Counters
    if ([int]$counts.total -le 0 -or [int]$counts.passed -ne [int]$counts.total) {
        throw "Tests did not all run successfully: total=$($counts.total), passed=$($counts.passed), failed=$($counts.failed)."
    }
}
finally { Pop-Location }
