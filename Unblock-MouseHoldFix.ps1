#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='Low')]
param()

$ErrorActionPreference = 'Stop'
$isJapanese = [System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'ja'
$executablePath = Join-Path $PSScriptRoot 'MouseHoldFix.exe'
if (!(Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    if ($isJapanese) { throw 'MouseHoldFix.exe が見つかりません。このスクリプトを展開したアプリと同じフォルダーに置いてください。' }
    throw 'MouseHoldFix.exe was not found. Keep this script beside the extracted application.'
}

# Use the script directory, regardless of the caller's current working directory.
# Only the project's known binaries are processed; missing DLLs are normal in the self-contained package.
$relativePaths = @('MouseHoldFix.exe', 'MouseHoldFix.dll', 'MouseHoldFix.Core.dll', 'ja\MouseHoldFix.resources.dll')
$prepared = 0
foreach ($relativePath in $relativePaths) {
    $path = Join-Path $PSScriptRoot $relativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $item = Get-Item -LiteralPath $path
    $parent = Get-Item -LiteralPath (Split-Path -Parent $path)
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or ($parent.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        if ($isJapanese) { throw "リンク先のファイルは処理しません: $relativePath" }
        throw "Linked files are not processed: $relativePath"
    }
    if ($PSCmdlet.ShouldProcess($path, 'Remove download-origin information')) {
        Unblock-File -LiteralPath $path -ErrorAction Stop
        $prepared++
        if ($isJapanese) { Write-Output "解除しました: $relativePath" }
        else { Write-Output "Unblocked: $relativePath" }
    }
}

if ($prepared -gt 0) {
    if ($isJapanese) { Write-Output '解除処理が完了しました。PowerShell を閉じ、MouseHoldFix.exe を通常どおり起動してください。' }
    else { Write-Output 'Preparation completed. Close PowerShell and start MouseHoldFix.exe normally.' }
}
