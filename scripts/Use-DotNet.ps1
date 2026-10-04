$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localDotNet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $localDotNet) {
    $script:DotNetExe = $localDotNet
    $env:DOTNET_ROOT = Split-Path $localDotNet
}
elseif ($dotnetCommand) { $script:DotNetExe = $dotnetCommand.Source }
else { throw '.NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools\nuget-http'

function Invoke-DotNet {
    & $script:DotNetExe @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $args" }
}
