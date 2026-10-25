# Builds the Windows icons in packaging\ from the artwork there:
#   whale-tray.ico     tray: the DeepSeek whale of the Mac app icon (whale-mark.png) on a blue tile that fills the
#                      icon, the size other tray icons such as WeChat's are drawn at
#   whale-windows.ico  exe and Start menu: the same tile with the usual margin
# -Classic <folder> also writes the original cartoon whale (whale-windows.png) as whale-tray.custom.ico and
# whale-app.custom.ico into <folder>; put beside DSHWhale.exe, they take the place of the packaged icons.
param([string]$Classic)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing

function New-Canvas([int]$size) { New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppPArgb) }
function Set-Quality($g) {
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
}
function New-RoundedPath([single]$x, [single]$y, [single]$size, [single]$radius) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath; $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $size - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $size - $d, $y + $size - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $size - $d, $d, $d, 90, 90)
    $path.CloseFigure(); $path
}
# Halves while more than twice too big, then one bicubic pass: thin parts stay smooth at 16-32 px.
function Get-Scaled($source, [int]$size) {
    $current = $source
    while ($current.Width / 2 -ge $size * 2) {
        $half = New-Canvas ([int]($current.Width / 2)); $g = [Drawing.Graphics]::FromImage($half); Set-Quality $g
        $g.DrawImage($current, 0, 0, $half.Width, $half.Height); $g.Dispose()
        if (-not [object]::ReferenceEquals($current, $source)) { $current.Dispose() }
        $current = $half
    }
    $result = New-Canvas $size; $g = [Drawing.Graphics]::FromImage($result); Set-Quality $g
    $g.DrawImage($current, 0, 0, $size, $size); $g.Dispose()
    if (-not [object]::ReferenceEquals($current, $source)) { $current.Dispose() }
    $result
}
function Write-Icon($art, [int[]]$sizes, [string]$path) {
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = Get-Scaled $art $size; $stream = New-Object IO.MemoryStream
        try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png); $frames += @{ Size = $size; Bytes = $stream.ToArray() } }
        finally { $stream.Dispose(); $bitmap.Dispose() }
    }
    $writer = New-Object IO.BinaryWriter([IO.File]::Create($path))
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
    Write-Output "Built $path"
}

$N = 1024
$traySizes = @(16, 20, 24, 32, 40, 48, 64)
$appSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

# The whale as white with alpha taken from the Mac art's brightness (white whale on black).
$mark = [Drawing.Image]::FromFile((Join-Path $root 'packaging\whale-mark.png'))
$whale = New-Canvas $N
$matrix = New-Object Drawing.Imaging.ColorMatrix
$matrix.Matrix00 = 0; $matrix.Matrix11 = 0; $matrix.Matrix22 = 0; $matrix.Matrix33 = 0
$matrix.Matrix40 = 1; $matrix.Matrix41 = 1; $matrix.Matrix42 = 1
$matrix.Matrix03 = 0.3333; $matrix.Matrix13 = 0.3333; $matrix.Matrix23 = 0.3334
$attributes = New-Object Drawing.Imaging.ImageAttributes; $attributes.SetColorMatrix($matrix)
$g = [Drawing.Graphics]::FromImage($whale)
$g.DrawImage($mark, (New-Object Drawing.Rectangle(0, 0, $N, $N)), 0, 0, $mark.Width, $mark.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
$g.Dispose(); $attributes.Dispose(); $mark.Dispose()

# Blue tile: the colours of the original cartoon icon, light at the top, deep at the bottom.
function New-Tile([single]$inset) {
    $art = New-Canvas $N; $g = [Drawing.Graphics]::FromImage($art); Set-Quality $g
    $side = $N - 2 * $inset
    $rect = New-Object Drawing.RectangleF($inset, $inset, $side, $side)
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush($rect, [Drawing.Color]::Black, [Drawing.Color]::Black, [single]90)
    $blend = New-Object Drawing.Drawing2D.ColorBlend(3)
    $blend.Colors = [Drawing.Color[]]@([Drawing.ColorTranslator]::FromHtml('#36B4FD'), [Drawing.ColorTranslator]::FromHtml('#0A72F6'), [Drawing.ColorTranslator]::FromHtml('#0244D6'))
    $blend.Positions = [single[]]@(0, 0.45, 1)
    $brush.InterpolationColors = $blend
    $path = New-RoundedPath $inset $inset $side ($side * 0.225)
    $g.FillPath($brush, $path)
    $g.DrawImage($whale, $inset, $inset, $side, $side)
    $g.Dispose(); $brush.Dispose(); $path.Dispose()
    $art
}
$tray = New-Tile 0
Write-Icon $tray $traySizes (Join-Path $root 'packaging\whale-tray.ico'); $tray.Dispose()
$app = New-Tile ($N * 0.04)
Write-Icon $app $appSizes (Join-Path $root 'packaging\whale-windows.ico'); $app.Dispose()
$whale.Dispose()

if ($Classic) {
    New-Item -ItemType Directory -Force -Path $Classic | Out-Null
    $cartoon = [Drawing.Image]::FromFile((Join-Path $root 'packaging\whale-windows.png'))
    try {
        # Cropped to its tile so it fills the tray icon; the app icon keeps the drawing's own margin.
        $crop = New-Object Drawing.Rectangle(50, 50, 1155, 1155)
        $filled = New-Canvas $N; $g = [Drawing.Graphics]::FromImage($filled); Set-Quality $g
        $g.DrawImage($cartoon, (New-Object Drawing.Rectangle(0, 0, $N, $N)), $crop, [Drawing.GraphicsUnit]::Pixel); $g.Dispose()
        Write-Icon $filled $traySizes (Join-Path $Classic 'whale-tray.custom.ico'); $filled.Dispose()
        $whole = New-Canvas $N; $g = [Drawing.Graphics]::FromImage($whole); Set-Quality $g
        $g.DrawImage($cartoon, 0, 0, $N, $N); $g.Dispose()
        Write-Icon $whole $appSizes (Join-Path $Classic 'whale-app.custom.ico'); $whole.Dispose()
    } finally { $cartoon.Dispose() }
}
