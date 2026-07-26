param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\RiftLens\Assets\RiftLens-ai-icon.png'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\RiftLens\Assets\RiftLens.ico'),
    [string] $PreviewPath = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-BrandBitmap([int] $Size) {
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $sourceSide = [Math]::Min($source.Width, $source.Height)
        # The AI master intentionally has generous whitespace. Crop it optically so
        # the ring stays recognizable at 16-32 px without touching the icon edge.
        $cropSide = [float]($sourceSide * 0.76)
        $crop = [System.Drawing.RectangleF]::new(
            ($source.Width - $cropSide) / 2.0,
            ($source.Height - $cropSide) / 2.0,
            $cropSide,
            $cropSide)
        $destination = [System.Drawing.Rectangle]::new(0, 0, $Size, $Size)
        $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
        try {
            $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage(
                $source,
                $destination,
                $crop.X,
                $crop.Y,
                $crop.Width,
                $crop.Height,
                [System.Drawing.GraphicsUnit]::Pixel,
                $attributes)
        }
        finally {
            $attributes.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$resolvedSource = [System.IO.Path]::GetFullPath($SourcePath)
if (-not [System.IO.File]::Exists($resolvedSource)) {
    throw "Icon source not found: $resolvedSource"
}
$source = [System.Drawing.Bitmap]::new($resolvedSource)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
$preview = $null
try {
    foreach ($size in $sizes) {
        $bitmap = New-BrandBitmap $size
        try {
            $stream = [System.IO.MemoryStream]::new()
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
            $stream.Dispose()
            if ($size -eq 256 -and -not [string]::IsNullOrWhiteSpace($PreviewPath)) {
                $preview = [System.Drawing.Bitmap]$bitmap.Clone()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
}

if ($preview -ne $null) {
    $resolvedPreview = [System.IO.Path]::GetFullPath($PreviewPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedPreview)) | Out-Null
    $preview.Save($resolvedPreview, [System.Drawing.Imaging.ImageFormat]::Png)
    $preview.Dispose()
}

Write-Host "Generated RiftLens icon: $resolvedOutput"
