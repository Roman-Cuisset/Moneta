# Run with Windows PowerShell 5.1 in STA. Only the original SVG paths in assets/branding are rendered.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName WindowsBase, PresentationCore
$branding = Join-Path (Split-Path $PSScriptRoot) 'assets/branding'
$sizes = @(16, 24, 32, 48, 64, 128, 256, 512, 1024)
$culture = [Globalization.CultureInfo]::InvariantCulture
$frames = @{}

foreach ($size in $sizes) {
    $source = if ($size -le 24) { 'moneta-compact.svg' } else { 'moneta.svg' }
    [xml]$vector = Get-Content -LiteralPath (Join-Path $branding $source) -Raw
    $width = [double]::Parse($vector.DocumentElement.GetAttribute('width'), $culture)
    $visual = New-Object Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    try {
        $ratio = $size / $width
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new($ratio, $ratio))
        foreach ($shape in $vector.DocumentElement.ChildNodes) {
            if ($shape.LocalName -notin @('rect', 'path')) { continue }
            $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($shape.GetAttribute('fill')))
            if ($shape.LocalName -eq 'path') {
                $drawing.DrawGeometry($brush, $null, [Windows.Media.Geometry]::Parse($shape.GetAttribute('d')))
            } else {
                $x, $y, $w, $h, $radius = @('x','y','width','height','rx') | ForEach-Object {
                    [double]::Parse($shape.GetAttribute($_), $culture)
                }
                $drawing.DrawRoundedRectangle($brush, $null, [Windows.Rect]::new($x, $y, $w, $h), $radius, $radius)
            }
        }
        $drawing.Pop()
    } finally { $drawing.Dispose() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $buffer = New-Object IO.MemoryStream
    try {
        $encoder.Save($buffer)
        $frames[[int]$size] = $buffer.ToArray()
        [IO.File]::WriteAllBytes((Join-Path $branding "moneta-$size.png"), $frames[[int]$size])
    } finally { $buffer.Dispose() }
}

$iconSizes = @($sizes | Where-Object { $_ -le 256 })
$stream = [IO.File]::Create((Join-Path $branding 'moneta.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + 16 * $iconSizes.Count
    foreach ($size in $iconSizes) {
        $dimension = [byte]($size % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$size].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$size].Length
    }
    foreach ($size in $iconSizes) { $writer.Write([byte[]]$frames[$size]) }
} finally { $writer.Dispose() }
