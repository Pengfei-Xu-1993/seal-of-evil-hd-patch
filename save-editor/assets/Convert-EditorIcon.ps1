param(
    [string]$SourcePng = (Join-Path $PSScriptRoot 'seal-editor-icon-ai.png'),
    [string]$DestinationIco = (Join-Path $PSScriptRoot 'seal-editor-icon-ai.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourcePng).Path)
$frames = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16,32,48,64,128,256)
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
            try {
                $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $rect = [System.Drawing.Rectangle]::new(0,0,$size,$size)
                $graphics.DrawImage($source,$rect,0,0,$source.Width,$source.Height,[System.Drawing.GraphicsUnit]::Pixel,$attributes)
            } finally { $attributes.Dispose() }
        } finally { $graphics.Dispose() }
        try {
            $bitmap.Save((Join-Path $PSScriptRoot ('seal-editor-icon-'+$size+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
            $frame = [System.IO.MemoryStream]::new()
            $writer = [System.IO.BinaryWriter]::new($frame)
            try {
                $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                $imageBytes = $size * $size * 4
                $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size*2))
                $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
                $writer.Write([uint32]$imageBytes); $writer.Write([int]0); $writer.Write([int]0)
                $writer.Write([uint32]0); $writer.Write([uint32]0)
                for ($y=$size-1; $y -ge 0; $y--) {
                    for ($x=0; $x -lt $size; $x++) {
                        $color=$bitmap.GetPixel($x,$y)
                        $writer.Write([byte]$color.B); $writer.Write([byte]$color.G)
                        $writer.Write([byte]$color.R); $writer.Write([byte]$color.A)
                    }
                }
                for ($y=$size-1; $y -ge 0; $y--) {
                    $mask=[byte[]]::new($maskStride)
                    for ($x=0; $x -lt $size; $x++) {
                        if ($bitmap.GetPixel($x,$y).A -eq 0) {
                            $byteIndex=[int][Math]::Floor($x/8.0)
                            $mask[$byteIndex]=$mask[$byteIndex] -bor (128 -shr ($x % 8))
                        }
                    }
                    $writer.Write($mask)
                }
                $writer.Flush(); $frames.Add($frame.ToArray())
            } finally { $writer.Dispose(); $frame.Dispose() }
        } finally { $bitmap.Dispose() }
    }
    $iconStream=[System.IO.File]::Create([System.IO.Path]::GetFullPath($DestinationIco))
    $iconWriter=[System.IO.BinaryWriter]::new($iconStream)
    try {
        $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$sizes.Count)
        $offset=6+16*$sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $dimension=if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
            $iconWriter.Write([byte]$dimension); $iconWriter.Write([byte]$dimension)
            $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
            $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
            $iconWriter.Write([uint32]$frames[$i].Length); $iconWriter.Write([uint32]$offset)
            $offset+=$frames[$i].Length
        }
        foreach ($bytes in $frames) {$iconWriter.Write($bytes)}
    } finally {$iconWriter.Dispose(); $iconStream.Dispose()}
    [pscustomobject]@{Source=$SourcePng;SourceSize=('{0}x{1}' -f $source.Width,$source.Height);Icon=$DestinationIco;Frames=$sizes}
} finally { $source.Dispose() }
