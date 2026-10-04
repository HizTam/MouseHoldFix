param([switch]$CheckOnly, [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$language = if ([System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'ja') { 'ja' } else { 'en' }
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Launcher.Messages.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$messages = $catalog.$language

function Show-LauncherMessage([string]$message, [switch]$ErrorMessage) {
    Add-Type -AssemblyName System.Windows.Forms
    $icon = if ($ErrorMessage) { [System.Windows.Forms.MessageBoxIcon]::Error } else { [System.Windows.Forms.MessageBoxIcon]::Information }
    [System.Windows.Forms.MessageBox]::Show($message, 'MouseHoldFix', [System.Windows.Forms.MessageBoxButtons]::OK, $icon) | Out-Null
}

try {
    $versionFile = Join-Path $projectRoot 'artifacts\current-version.txt'
    $selectedVersion = if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Encoding ASCII -Raw).Trim() } else { '' }
    if ($selectedVersion) {
        if ($selectedVersion -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw $messages.InvalidVersion }
        # A selected release must never silently fall back to an older build.
        $candidates = @(
            (Join-Path $projectRoot "artifacts\releases\$selectedVersion\self-contained\MouseHoldFix.exe"),
            (Join-Path $projectRoot "artifacts\releases\$selectedVersion\framework-dependent\MouseHoldFix.exe"),
            (Join-Path $projectRoot "artifacts\$selectedVersion\self-contained\MouseHoldFix.exe"),
            (Join-Path $projectRoot "artifacts\$selectedVersion\framework-dependent\MouseHoldFix.exe")
        )
    }
    else {
        $candidates = @(
            (Join-Path $projectRoot 'artifacts\self-contained\MouseHoldFix.exe'),
            (Join-Path $projectRoot 'artifacts\framework-dependent\MouseHoldFix.exe'),
            (Join-Path $projectRoot 'src\MouseHoldFix\bin\Release\net10.0-windows\MouseHoldFix.exe')
        )
    }
    if ($FrameworkDependent) {
        $candidates = @($candidates | Where-Object { $_ -match '[\\/]framework-dependent[\\/]' })
    }
    $launcherCommand = if ($FrameworkDependent) { 'Start-MouseHoldFix.cmd -FrameworkDependent' } else { 'Start-MouseHoldFix.cmd' }
    $executable = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$executable) { throw $messages.BuildFirst }
    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion.Split('+')[0]
    if ($selectedVersion -and $productVersion -ne $selectedVersion) { throw ($messages.VersionMismatch -f $selectedVersion, $productVersion) }
    $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    $running = @(Get-Process -Name MouseHoldFix -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sessionId })
    $runningVersions = @($running | ForEach-Object {
        try { [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.Path).ProductVersion.Split('+')[0] }
        catch { $messages.UnknownVersion }
    })
    if ($CheckOnly) {
        [pscustomobject]@{ SelectedVersion = $productVersion; Executable = $executable; RunningVersions = $runningVersions } | ConvertTo-Json
        exit 0
    }
    if ($running.Count -gt 0) {
        Show-LauncherMessage ($messages.AlreadyRunning -f ($runningVersions -join ', '), $productVersion, $launcherCommand)
        exit 2
    }
    $localRuntime = Join-Path $projectRoot '.tools\dotnet'
    if (Test-Path -LiteralPath (Join-Path $localRuntime 'dotnet.exe')) { $env:DOTNET_ROOT = $localRuntime }
    $process = Start-Process -FilePath $executable -PassThru -WindowStyle Hidden
    try {
        if ($process.WaitForExit(2000) -and $process.ExitCode -ne 0) { throw ($messages.StartupExit -f $productVersion, $process.ExitCode) }
    }
    finally { $process.Dispose() }
    exit 0
}
catch {
    if (!$CheckOnly) { Show-LauncherMessage ($messages.StartFailed -f $_.Exception.Message) -ErrorMessage }
    Write-Error $_ -ErrorAction Continue
    exit 1
}
