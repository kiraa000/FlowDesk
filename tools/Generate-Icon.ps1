# Builds the FlowDesk .ico and XAML PNG from assets\FlowDesk.svg using only System.Drawing.
# No downloads, no online converters, no desktop capture. Rects are parsed from the SVG so the
# generated bitmap and the SVG source share one geometry.
# Usage: powershell -ExecutionPolicy Bypass -File tools\Generate-Icon.ps1 [-ValidateOnly]
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\assets\FlowDesk.svg"),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\assets"),
    [int[]]$Sizes = @(16, 20, 24, 32, 48, 64, 128, 256),
    [int]$PngSize = 256,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$svgSize = 256.0

function Read-SvgRects([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Icon source not found: $Path" }
    $text = [System.IO.File]::ReadAllText($Path)
    $shapes = New-Object System.Collections.Generic.List[object]
    foreach ($match in [regex]::Matches($text, '<rect\b([^>]*)/?>')) {
        $attributes = @{}
        foreach ($attribute in [regex]::Matches($match.Groups[1].Value, '([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*"([^"]*)"')) {
            $attributes[$attribute.Groups[1].Value] = $attribute.Groups[2].Value
        }
        $fill = $attributes["fill"]
        if (-not $fill -or $fill -eq "none") { continue }
        $radius = 0.0
        if ($attributes.ContainsKey("rx")) { $radius = [double]$attributes["rx"] }
        elseif ($attributes.ContainsKey("ry")) { $radius = [double]$attributes["ry"] }
        $shapes.Add([pscustomobject]@{
            X = [double]$attributes["x"]
            Y = [double]$attributes["y"]
            Width = [double]$attributes["width"]
            Height = [double]$attributes["height"]
            Radius = $radius
            Fill = $fill
        })
    }
    if ($shapes.Count -eq 0) { throw "No filled <rect> elements found in $Path" }
    return ,$shapes
}

function Convert-HexColor([string]$Hex) {
    if ($Hex -notmatch '^#([0-9A-Fa-f]{6})$') { throw "Only #RRGGBB fills are supported: $Hex" }
    return [System.Drawing.Color]::FromArgb(
        255,
        [Convert]::ToInt32($Hex.Substring(1, 2), 16),
        [Convert]::ToInt32($Hex.Substring(3, 2), 16),
        [Convert]::ToInt32($Hex.Substring(5, 2), 16))
}

function New-RoundedPath([double]$X, [double]$Y, [double]$Width, [double]$Height, [double]$Radius, [double]$Scale) {
    $x = $X * $Scale; $y = $Y * $Scale
    $w = $Width * $Scale; $h = $Height * $Scale
    $r = [Math]::Min($Radius * $Scale, [Math]::Min($w, $h) / 2.0)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF([float]$x, [float]$y, [float]$w, [float]$h)))
        return $path
    }
    $diameter = $r * 2.0
    $path.AddArc([float]$x, [float]$y, [float]$diameter, [float]$diameter, 180, 90)
    $path.AddArc([float]($x + $w - $diameter), [float]$y, [float]$diameter, [float]$diameter, 270, 90)
    $path.AddArc([float]($x + $w - $diameter), [float]($y + $h - $diameter), [float]$diameter, [float]$diameter, 0, 90)
    $path.AddArc([float]$x, [float]($y + $h - $diameter), [float]$diameter, [float]$diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$Size, $Shapes) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $scale = [double]$Size / $svgSize
        foreach ($shape in $Shapes) {
            $path = New-RoundedPath $shape.X $shape.Y $shape.Width $shape.Height $shape.Radius $scale
            $brush = New-Object System.Drawing.SolidBrush((Convert-HexColor $shape.Fill))
            try { $graphics.FillPath($brush, $path) }
            finally { $brush.Dispose(); $path.Dispose() }
        }
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

function Get-PngBytes($Bitmap) {
    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally { $stream.Dispose() }
}

function Get-DibBytes($Bitmap) {
    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $raw = New-Object byte[] ($data.Stride * $height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
        $stride = $data.Stride
    }
    finally { $Bitmap.UnlockBits($data) }

    $xorSize = $width * $height * 4
    $maskStride = [int]([Math]::Floor(($width + 31) / 32) * 4)
    $andSize = $maskStride * $height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint32]40)                 # BITMAPINFOHEADER
        $writer.Write([int32]$width)
        $writer.Write([int32]($height * 2))        # XOR + AND
        $writer.Write([uint16]1)                   # planes
        $writer.Write([uint16]32)                  # bit count
        $writer.Write([uint32]0)                   # BI_RGB
        $writer.Write([uint32]($xorSize + $andSize))
        $writer.Write([int32]0); $writer.Write([int32]0)
        $writer.Write([uint32]0); $writer.Write([uint32]0)
        for ($row = $height - 1; $row -ge 0; $row--) {
            $writer.Write($raw, $row * $stride, $width * 4) # bottom-up BGRA
        }
        $writer.Write((New-Object byte[] $andSize))        # alpha is in the BGRA pixels
        return ,$stream.ToArray()
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

function Write-Icon([string]$Path, $Entries) {
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$Entries.Count)
        $offset = 6 + (16 * $Entries.Count)
        foreach ($entry in $Entries) {
            $dimension = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $entry.Bytes.Length
        }
        foreach ($entry in $Entries) { $writer.Write([byte[]]$entry.Bytes) }
        [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

function Test-Icon([string]$Path, [int[]]$ExpectedSizes) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $stream = New-Object System.IO.MemoryStream(, $bytes)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        $reserved = $reader.ReadUInt16(); $type = $reader.ReadUInt16(); $count = $reader.ReadUInt16()
        if ($reserved -ne 0 -or $type -ne 1) { throw "Invalid ICO header" }
        if ($count -ne $ExpectedSizes.Count) { throw "ICO has $count entries, expected $($ExpectedSizes.Count)" }
        $found = @()
        $expectedOffset = 6 + (16 * $count)
        for ($i = 0; $i -lt $count; $i++) {
            $widthByte = $reader.ReadByte(); $heightByte = $reader.ReadByte()
            $null = $reader.ReadByte(); $null = $reader.ReadByte()
            $planes = $reader.ReadUInt16(); $bits = $reader.ReadUInt16()
            $length = $reader.ReadUInt32(); $offset = $reader.ReadUInt32()
            $size = if ($widthByte -eq 0) { 256 } else { [int]$widthByte }
            if ($heightByte -ne $widthByte) { throw "Entry $i is not square" }
            if ($planes -ne 1 -or $bits -ne 32) { throw "Entry $i is not 32-bit" }
            if ($offset -ne $expectedOffset) { throw "Entry $i data is not contiguous" }
            if (($offset + $length) -gt $bytes.Length) { throw "Entry $i points past the file" }
            $expectedOffset += $length
            $isPng = $length -ge 8 -and $bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50 -and
                $bytes[$offset + 2] -eq 0x4E -and $bytes[$offset + 3] -eq 0x47
            if ($isPng) {
                $image = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream(, $bytes[$offset..($offset + $length - 1)])))
                try {
                    if ($image.Width -ne $size -or $image.Height -ne $size) { throw "PNG entry $i is $($image.Width)x$($image.Height), expected ${size}x${size}" }
                }
                finally { $image.Dispose() }
                $kind = "PNG"
            }
            else {
                $dibWidth = [BitConverter]::ToInt32($bytes, [int]$offset + 4)
                $dibHeight = [BitConverter]::ToInt32($bytes, [int]$offset + 8)
                if ($dibWidth -ne $size -or $dibHeight -ne ($size * 2)) { throw "DIB entry $i has wrong dimensions" }
                $kind = "BGRA"
            }
            $found += "$size/$kind"
        }
        if ($expectedOffset -ne $bytes.Length) { throw "ICO has trailing bytes" }
        Write-Host ("ICO validated: {0} entries ({1})" -f $count, ($found -join ", "))
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

$shapes = Read-SvgRects $Source
if (-not $ValidateOnly) {
    if (-not (Test-Path -LiteralPath $OutputDirectory)) { throw "Output directory not found: $OutputDirectory" }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    foreach ($name in @("FlowDesk.ico", "FlowDesk.png")) {
        $existing = Join-Path $OutputDirectory $name
        if (Test-Path -LiteralPath $existing) {
            Copy-Item -LiteralPath $existing -Destination "$existing.bak-$stamp"
        }
    }

    $entries = @()
    foreach ($size in $Sizes) {
        $bitmap = New-IconBitmap $size $shapes
        try {
            # Small sizes use classic 32-bit BGRA DIBs; large sizes use PNG-compressed entries.
            $entryBytes = if ($size -le 48) { Get-DibBytes $bitmap } else { Get-PngBytes $bitmap }
            $entries += [pscustomobject]@{ Size = $size; Bytes = $entryBytes }
        }
        finally { $bitmap.Dispose() }
    }
    $icoPath = Join-Path $OutputDirectory "FlowDesk.ico"
    Write-Icon $icoPath $entries

    $pngBitmap = New-IconBitmap $PngSize $shapes
    try { [System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory "FlowDesk.png"), (Get-PngBytes $pngBitmap)) }
    finally { $pngBitmap.Dispose() }

    Write-Host ("Generated {0} ({1:N0} bytes) and FlowDesk.png ({2}px)" -f $icoPath, (Get-Item -LiteralPath $icoPath).Length, $PngSize)
}

Test-Icon (Join-Path $OutputDirectory "FlowDesk.ico") $Sizes
$png = [System.Drawing.Image]::FromFile((Join-Path $OutputDirectory "FlowDesk.png"))
try {
    if ($png.Width -ne $PngSize -or $png.Height -ne $PngSize) { throw "FlowDesk.png is $($png.Width)x$($png.Height)" }
    Write-Host ("PNG validated: {0}x{1}" -f $png.Width, $png.Height)
}
finally { $png.Dispose() }
