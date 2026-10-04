$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\src\MouseHoldFix\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

function New-IconImage([int]$size, [bool]$enabled) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.ScaleTransform(($size / 128.0), ($size / 128.0))
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#15263E'))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#F1F7FC'))
    $accentColor = if ($enabled) { '#35DBC1' } else { '#95A0AF' }
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($accentColor))
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(2,2,32,32,180,90); $tile.AddArc(94,2,32,32,270,90)
    $tile.AddArc(94,94,32,32,0,90); $tile.AddArc(2,94,32,32,90,90); $tile.CloseFigure()
    $graphics.FillPath($background, $tile)
    $mouse = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mouse.AddArc(34,16,60,60,180,180); $mouse.AddLine(94,46,94,77)
    $mouse.AddArc(34,47,60,60,0,180); $mouse.CloseFigure()
    $graphics.FillPath($white,$mouse)
    $left = New-Object System.Drawing.Drawing2D.GraphicsPath
    $left.AddArc(40,22,48,48,180,90); $left.AddLine(64,22,64,55)
    $left.AddLine(64,55,40,55); $left.CloseFigure()
    $graphics.FillPath($accent,$left)
    $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#15263E'),4)
    $pen.StartCap='Round'; $pen.EndCap='Round'
    $graphics.DrawLine($pen,64,24,64,54)
    $graphics.FillEllipse($background,75,76,43,43)
    $graphics.FillEllipse($accent,80,81,33,33)
    $check = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#15263E'),5)
    $check.StartCap='Round'; $check.EndCap='Round'; $check.LineJoin='Round'
    if ($enabled) { $graphics.DrawLines($check, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(87,97)),(New-Object System.Drawing.PointF(94,104)),(New-Object System.Drawing.PointF(106,91)))) }
    else { $graphics.DrawLine($check,88,98,105,98) }
    $graphics.Dispose(); $background.Dispose(); $white.Dispose(); $accent.Dispose()
    $tile.Dispose(); $mouse.Dispose(); $left.Dispose(); $pen.Dispose(); $check.Dispose()
    return $bitmap
}

foreach ($enabled in @($true,$false)) {
    $name = if ($enabled) { 'MouseHoldFix' } else { 'MouseHoldFix-disabled' }
    $images = @()
    $sizes = @(16,20,24,32,40,48,64,128,256)
    foreach ($size in $sizes) {
        $bitmap = New-IconImage $size $enabled
        $memory = New-Object System.IO.MemoryStream
        $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
        $images += ,$memory.ToArray()
        if ($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory "$name.png"),[System.Drawing.Imaging.ImageFormat]::Png) }
        $memory.Dispose(); $bitmap.Dispose()
    }
    $file = [System.IO.File]::Create((Join-Path $assetDirectory "$name.ico"))
    $writer = New-Object System.IO.BinaryWriter($file)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    $writer.Dispose()
}
$readmeAssetDirectory = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Path $readmeAssetDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $assetDirectory 'MouseHoldFix.png') -Destination $readmeAssetDirectory -Force
Write-Output "Created enabled/disabled icons in $assetDirectory"
