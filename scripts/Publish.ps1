param([ValidateSet('FrameworkDependent','SelfContained','All')][string]$Mode = 'SelfContained')
$ErrorActionPreference = 'Stop'
function Assert-ReleaseOutput([string]$Path) {
    $workspacePath = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
    $target = [System.IO.Path]::GetFullPath($Path)
    $releaseBoundary = [System.IO.Path]::GetFullPath((Join-Path $workspacePath 'artifacts\releases')).TrimEnd('\') + '\'
    if (!$target.StartsWith($releaseBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release output must stay inside artifacts/releases.'
    }
    $ancestor = $target
    while ($ancestor -ne $workspacePath) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "Release output contains a link: $ancestor"
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if (Test-Path -LiteralPath $target -PathType Container) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($target)
        while ($pending.Count -gt 0) {
            foreach ($entry in @(Get-ChildItem -LiteralPath $pending.Pop() -Force)) {
                if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                    throw "Release output contains a link: $($entry.FullName)"
                }
                if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            }
        }
    }
}

function Copy-RuntimeNotices([string]$Destination, [string]$AssetsFile, [string]$PackagesRoot) {
    $assets = Get-Content -LiteralPath $AssetsFile -Encoding UTF8 -Raw | ConvertFrom-Json
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $versions = @(
            foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
                foreach ($dependency in $framework.Value.downloadDependencies) {
                    if ($dependency.name -ieq $package) {
                        if ($dependency.version -notmatch '^\[([0-9]+\.[0-9]+\.[0-9]+),\s*\1\]$') {
                            throw "Runtime package does not have an exact resolved version: $package"
                        }
                        $Matches[1]
                    }
                }
            }
        )
        $versions = @($versions | Sort-Object -Unique)
        if (@($versions).Count -ne 1) { throw "Could not resolve the published runtime version: $package" }
        $packageDirectory = Join-Path (Join-Path $PackagesRoot $package) $versions[0]
        $licenseCopied = $false
        foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
            $source = Join-Path $packageDirectory $notice
            if (Test-Path -LiteralPath $source -PathType Leaf) {
                Copy-Item -LiteralPath $source -Destination (Join-Path $Destination "$package-$notice") -Force
                if ($notice -like 'LICENSE*') { $licenseCopied = $true }
            }
        }
        if (!$licenseCopied) { throw "The published runtime license is missing: $package $($versions[0])" }
    }
}

function Copy-ReadmeAssets([string]$Destination) {
    $workspaceBoundary = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    $destinationRoot = [System.IO.Path]::GetFullPath($Destination)
    if (!$destinationRoot.StartsWith($workspaceBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release assets must be copied inside the workspace.'
    }
    $assetBoundary = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'assets')).TrimEnd('\') + '\'
    $assetPaths = @(
        foreach ($readme in @('README.md','README.ja.md')) {
            $content = [System.IO.File]::ReadAllText((Join-Path $projectRoot $readme))
            foreach ($match in [regex]::Matches($content, '(?:src="|\]\()(assets/[^"\)]+)')) {
                $match.Groups[1].Value
            }
        }
    )
    foreach ($relativePath in @($assetPaths | Sort-Object -Unique)) {
        $source = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
        if (!$source.StartsWith($assetBoundary, [System.StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Invalid README asset: $relativePath"
        }
        $target = Join-Path $destinationRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
}

. (Join-Path $PSScriptRoot 'Use-DotNet.ps1')
Push-Location $projectRoot
try {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
    $version = $buildProperties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
    $releaseRoot = Join-Path $projectRoot 'artifacts\releases'
    $modes = if ($Mode -eq 'All') { @('FrameworkDependent','SelfContained') } else { @($Mode) }
    foreach ($profile in $modes) {
        $folder = if ($profile -eq 'FrameworkDependent') { 'framework-dependent' } else { 'self-contained' }
        $output = Join-Path $releaseRoot "$version\$folder"
        $resolvedOutput = [System.IO.Path]::GetFullPath($output)
        Assert-ReleaseOutput -Path $resolvedOutput
        $zip = Join-Path $releaseRoot "MouseHoldFix-$version-win-x64-$folder.zip"
        Assert-ReleaseOutput -Path $zip
        Assert-ReleaseOutput -Path "$zip.sha256"
        if (Test-Path -LiteralPath $resolvedOutput) {
            $running = Get-Process MouseHoldFix -ErrorAction SilentlyContinue | Where-Object {
                $_.Path -and $_.Path.StartsWith($resolvedOutput + '\', [System.StringComparison]::OrdinalIgnoreCase)
            }
            if ($running) { throw 'Exit this release from the tray before publishing it again.' }
            Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
        }
        Invoke-DotNet publish src\MouseHoldFix\MouseHoldFix.csproj --configuration Release --runtime win-x64 --self-contained ($profile -eq 'SelfContained').ToString().ToLowerInvariant() "-p:PublishProfile=$profile" '-p:RestoreConfigFile=NuGet.Config' '-p:DebugType=None' '-p:DebugSymbols=false' --output $output
        Copy-Item -LiteralPath 'README.md','README.ja.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md','LICENSE.txt','Unblock-MouseHoldFix.ps1','Prepare-MouseHoldFix.cmd' -Destination $output -Force
        Copy-ReadmeAssets -Destination $output
        if ($profile -eq 'SelfContained') {
            $licensesOutput = Join-Path $output 'licenses'
            Copy-RuntimeNotices -Destination $licensesOutput -AssetsFile (Join-Path $projectRoot 'src\MouseHoldFix\obj\project.assets.json') -PackagesRoot $env:NUGET_PACKAGES
        }
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText("$zip.sha256", "$hash  $([System.IO.Path]::GetFileName($zip))`r`n")
        Write-Output "Created $zip"
    }
    [System.IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\current-version.txt'), "$version`r`n", [System.Text.Encoding]::ASCII)
}
finally { Pop-Location }
