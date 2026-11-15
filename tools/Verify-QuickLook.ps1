# Verify-QuickLook.ps1 - manual, opt-in live check for FlowDesk Quick Look.
# This script never runs by itself and FlowDesk never calls it: run it only when the desktop is free.
# It creates local sample files (no download, no deletion, no overwrite), prints the manual checklist
# and, unless -NoExplorer is passed, opens one File Explorer window on the samples. It skips entirely
# while an elevated window (or one whose elevation cannot be read) is in the foreground, so no sample
# is created before that check; -NoExplorer may still generate samples without touching the desktop.
# Video samples are not generated: add your own to the folder if you want to test video. Nothing is downloaded.
param(
    [string]$SampleDir = 'D:\Workspace\_tmp\opencode\QuickLookSamples',
    [switch]$NoExplorer
)
$ErrorActionPreference = 'Stop'

if (-not ('QuickLookVerifyNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class QuickLookVerifyNative
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int infoLength, out int returnLength);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    const int TOKEN_ELEVATION_CLASS = 20;

    // 1 = elevated, 0 = not elevated, -1 = could not read (treated as unsafe by the script).
    public static int ForegroundElevation()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return -1;
        uint pid;
        GetWindowThreadProcessId(foreground, out pid);
        if (pid == 0) return -1;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return -1;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out token) || token == IntPtr.Zero) return -1;
            int elevated, returned;
            if (!GetTokenInformation(token, TOKEN_ELEVATION_CLASS, out elevated, sizeof(int), out returned)) return -1;
            return elevated != 0 ? 1 : 0;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            CloseHandle(process);
        }
    }
}
'@
}

# Skip before anything is created or opened. Only -NoExplorer may generate samples while such a window is in front.
$elevation = [QuickLookVerifyNative]::ForegroundElevation()
if (-not $NoExplorer -and $elevation -ne 0) {
    $reason = if ($elevation -eq 1) { 'an elevated window is in the foreground' } else { 'the foreground elevation could not be read' }
    Write-Output "SKIP: $reason. Nothing was created or opened."
    Write-Output 'Run this again from a non-elevated window when the desktop is free, or pass -NoExplorer to create samples only.'
    return
}

if (-not (Test-Path -LiteralPath $SampleDir)) {
    New-Item -ItemType Directory -Path $SampleDir -Force | Out-Null
    Write-Output "created $SampleDir"
}

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function New-TextSample([string]$Name, [string]$Content, [System.Text.Encoding]$Encoding) {
    $path = Join-Path $SampleDir $Name
    if (Test-Path -LiteralPath $path) { Write-Output "keep  $Name"; return }
    [System.IO.File]::WriteAllText($path, $Content, $Encoding)
    Write-Output "write $Name"
}

New-TextSample 'readme.md' "# Quick Look sample`r`n`r`nMarkdown preview sample for FlowDesk Quick Look.`r`n" $utf8Bom
New-TextSample 'data.json' "{`r`n  `"name`": `"FlowDesk Quick Look`",`r`n  `"preview`": true`r`n}`r`n" $utf8NoBom
New-TextSample 'sample.cs' "namespace QuickLookSample;`r`n`r`npublic static class Hello`r`n{`r`n    public static string Text => `"Quick Look`";`r`n}`r`n" $utf8Bom
New-TextSample 'notes.txt' "Plain text sample. Space should preview this file.`r`n" $utf8Bom
New-TextSample 'utf16.txt' ("UTF-16 text sample: caf" + [char]0x00E9 + " and " + [char]0x65E5 + [char]0x672C + [char]0x8A9E + ".`r`n") ([System.Text.Encoding]::Unicode)
New-TextSample 'utf8-no-bom.txt' ("UTF-8 without BOM: caf" + [char]0x00E9 + " and " + [char]0x65E5 + [char]0x672C + [char]0x8A9E + ".`r`n") $utf8NoBom
New-TextSample 'no-extension' "No extension: Quick Look should sniff this as text and preview it.`r`n" $utf8Bom
New-TextSample 'unsupported-office.docx' "Not a real Word file. Office types are not previewed in Quick Look v1; the fallback view should appear.`r`n" $utf8Bom

$largePath = Join-Path $SampleDir 'large-log.log'
if (-not (Test-Path -LiteralPath $largePath)) {
    $line = '2026-11-15 12:00:00 INFO Quick Look text cap sample line for the first 1 MB note.' + "`r`n"
    $repeat = [int][Math]::Ceiling(1300000 / $line.Length)
    [System.IO.File]::WriteAllText($largePath, ($line * $repeat), $utf8NoBom)
    Write-Output 'write large-log.log (~1.3 MB)'
} else { Write-Output 'keep  large-log.log' }

$corruptPath = Join-Path $SampleDir 'corrupt.jpg'
if (-not (Test-Path -LiteralPath $corruptPath)) {
    $bytes = New-Object byte[] 4096
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [byte](($i * 37) % 256) }
    [System.IO.File]::WriteAllBytes($corruptPath, $bytes)
    Write-Output 'write corrupt.jpg (invalid image for the fallback test)'
} else { Write-Output 'keep  corrupt.jpg' }

$unknownPath = Join-Path $SampleDir 'unknown.bin'
if (-not (Test-Path -LiteralPath $unknownPath)) {
    $bytes = New-Object byte[] 2048
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [byte](($i * 11) % 256) }
    [System.IO.File]::WriteAllBytes($unknownPath, $bytes)
    Write-Output 'write unknown.bin (unsupported type for the fallback test)'
} else { Write-Output 'keep  unknown.bin' }

$imagePath = Join-Path $SampleDir 'sample-image.png'
if (-not (Test-Path -LiteralPath $imagePath)) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new(640, 360)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(32, 32, 32))
        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Rectangle]::new(0, 0, 640, 360),
            [System.Drawing.Color]::FromArgb(0, 120, 215),
            [System.Drawing.Color]::FromArgb(0, 60, 120),
            45.0)
        try { $graphics.FillRectangle($brush, 0, 0, 640, 360) } finally { $brush.Dispose() }
        $font = [System.Drawing.Font]::new('Segoe UI', 28)
        $format = [System.Drawing.StringFormat]::new()
        try {
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString('FlowDesk Quick Look', $font, [System.Drawing.Brushes]::White,
                [System.Drawing.RectangleF]::new(0, 0, 640, 360), $format)
        } finally { $font.Dispose(); $format.Dispose() }
        $bitmap.Save($imagePath, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    Write-Output 'write sample-image.png (generated locally)'
} else { Write-Output 'keep  sample-image.png' }

function Add-Ascii([System.Collections.Generic.List[byte]]$Target, [string]$Text) {
    foreach ($byte in [System.Text.Encoding]::ASCII.GetBytes($Text)) { $Target.Add($byte) }
}

function New-PdfSample {
    $path = Join-Path $SampleDir 'sample.pdf'
    if (Test-Path -LiteralPath $path) { Write-Output 'keep  sample.pdf'; return }
    $content = 'BT /F1 16 Tf 30 120 Td (FlowDesk Quick Look PDF sample) Tj ET'
    $bytes = New-Object System.Collections.Generic.List[byte]
    $offsets = New-Object System.Collections.Generic.List[int]
    Add-Ascii $bytes "%PDF-1.4`n"
    for ($i = 1; $i -le 5; $i++) {
        $offsets.Add($bytes.Count)
        Add-Ascii $bytes "$i 0 obj`n"
        switch ($i) {
            1 { Add-Ascii $bytes "<< /Type /Catalog /Pages 2 0 R >>`n" }
            2 { Add-Ascii $bytes "<< /Type /Pages /Kids [3 0 R] /Count 1 >>`n" }
            3 { Add-Ascii $bytes "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>`n" }
            4 { Add-Ascii $bytes "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>`n" }
            5 { Add-Ascii $bytes "<< /Length $($content.Length) >>`nstream`n$content`nendstream`n" }
        }
        Add-Ascii $bytes "endobj`n"
    }
    $xref = $bytes.Count
    Add-Ascii $bytes "xref`n0 6`n"
    Add-Ascii $bytes "0000000000 65535 f `r`n"
    foreach ($offset in $offsets) { Add-Ascii $bytes ("{0:D10} 00000 n `r`n" -f $offset) }
    Add-Ascii $bytes "trailer`n<< /Size 6 /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"
    [System.IO.File]::WriteAllBytes($path, $bytes.ToArray())
    Write-Output 'write sample.pdf (generated locally, ASCII text page, correct xref offsets)'
}

function New-WavSample {
    $path = Join-Path $SampleDir 'sample.wav'
    if (Test-Path -LiteralPath $path) { Write-Output 'keep  sample.wav'; return }
    $rate = 22050
    $count = [int]($rate / 2)
    $dataSize = $count * 2
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int](36 + $dataSize))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]1)
        $writer.Write([int]$rate)
        $writer.Write([int]($rate * 2))
        $writer.Write([int16]2)
        $writer.Write([int16]16)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$dataSize)
        for ($i = 0; $i -lt $count; $i++) {
            $writer.Write([int16](2200 * [Math]::Sin(2 * [Math]::PI * 440 * $i / $rate)))
        }
    } finally {
        $writer.Flush()
        [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
        $writer.Dispose()
        $stream.Dispose()
    }
    Write-Output 'write sample.wav (generated locally, PCM 16-bit mono quiet tone, RIFF header)'
}

function New-GifSample {
    $path = Join-Path $SampleDir 'sample-image.gif'
    if (Test-Path -LiteralPath $path) { Write-Output 'keep  sample-image.gif'; return }
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new(200, 120)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(0, 90, 158))
        $font = [System.Drawing.Font]::new('Segoe UI', 12)
        try { $graphics.DrawString('GIF sample', $font, [System.Drawing.Brushes]::White, 16, 48) }
        finally { $font.Dispose() }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Gif)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    Write-Output 'write sample-image.gif (generated locally)'
}

New-PdfSample
New-WavSample
New-GifSample

$folderPath = Join-Path $SampleDir 'sample-folder'
if (-not (Test-Path -LiteralPath $folderPath)) {
    New-Item -ItemType Directory -Path $folderPath | Out-Null
    Set-Content -LiteralPath (Join-Path $folderPath 'inside.txt') -Value 'Folder preview sample.' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $folderPath 'inside.md') -Value '# Folder sample' -Encoding UTF8
    Write-Output 'write sample-folder (2 files inside)'
} else { Write-Output 'keep  sample-folder' }

function Show-Supplied([string]$Label, [string[]]$Extensions) {
    $found = @(Get-ChildItem -LiteralPath $SampleDir -File | Where-Object { $Extensions -contains $_.Extension.ToLowerInvariant() })
    if ($found.Count -gt 0) {
        Write-Output ("supplied {0}: {1}" -f $Label, (($found | ForEach-Object { $_.Name }) -join ', '))
    } else {
        Write-Output ("supplied {0}: none - add your own file to {1} to test it; this script downloads nothing." -f $Label, $SampleDir)
    }
}

Write-Output ''
Write-Output "Samples: $SampleDir"
Write-Output 'No content was downloaded and no file was deleted or overwritten.'
Show-Supplied 'PDF (sample.pdf is generated locally)' @('.pdf')
Show-Supplied 'video (not generated - add your own file to test video)' @('.mp4', '.mkv', '.webm', '.mov')
Show-Supplied 'audio (sample.wav is generated locally; others supplied by you)' @('.mp3', '.wav', '.flac', '.m4a')
Show-Supplied 'animated image / codec (sample-image.gif is generated locally)' @('.gif', '.heic', '.avif')

if (-not (Get-Process FlowDesk.App -ErrorAction SilentlyContinue)) {
    Write-Output 'NOTE: FlowDesk.App is not running. Start it yourself and turn on Settings > Quick Look before testing.'
}

if ($NoExplorer) {
    Write-Output 'SKIP Explorer: -NoExplorer was passed; samples were generated without touching the desktop; open the folder yourself when ready.'
} else {
    $first = Get-ChildItem -LiteralPath $SampleDir -File | Sort-Object Name | Select-Object -First 1
    $arguments = if ($first) { '/select,"' + $first.FullName + '"' } else { '"' + $SampleDir + '"' }
    Start-Process -FilePath explorer.exe -ArgumentList $arguments | Out-Null
    Write-Output 'OPENED one Explorer window on the samples (no key injection, no app launch other than Explorer).'
}

Write-Output ''
Write-Output 'MANUAL CHECKLIST - Quick Look (desktop free; no elevated window in front):'
Write-Output '  Enable: Settings > Quick Look is OFF by default; turn it on and confirm the toggle stays on.'
Write-Output '  Basic: select sample-image.png, press Space -> the preview appears at once; Space again closes it.'
Write-Output '  Esc / Enter: press Space to open; Esc closes; Enter opens the file in its default app and closes the preview.'
Write-Output '  Selection follows: with the preview open, press Down/Up arrows in Explorer; the preview swaps and Explorer keeps keyboard focus.'
Write-Output '  Rename: press F2 on a file and type a space in the name -> the space is typed, no preview opens.'
Write-Output '  Address/search: Ctrl+L address bar and Ctrl+E/Ctrl+F search box -> Space types a space, no preview.'
Write-Output '  Modifiers: Shift+Space, Ctrl+Space, Alt+Space and Win+Space do not open Quick Look and keep their normal Explorer/Windows action.'
Write-Output '  Desktop: click the desktop, select an icon, press Space -> preview; arrows follow; Esc closes.'
Write-Output '  Focus loss: click another app or the taskbar while the preview is open -> the preview closes.'
Write-Output '  Disable: turn Quick Look off while a preview is open -> the preview closes and Space is no longer taken.'
Write-Output '  Media stop: preview sample.wav (or a supplied video), then close the preview or move the selection -> playback stops; no sound continues.'
Write-Output '  Codecs: playback depends on the codecs Windows has; a supplied video that will not play in Windows Media Player is not a Quick Look bug.'
Write-Output '  Corrupt: select corrupt.jpg -> fallback view with a short reason (or thumbnail); no crash.'
Write-Output '  PDF: select the generated sample.pdf -> the page renders, pages scroll lazily and the page count is in the header. Opening it in Edge/Reader too proves the xref is valid.'
Write-Output '  Text cap: open large-log.log (~1.3 MB) -> shows "showing first 1 MB"; utf16.txt and utf8-no-bom.txt display correctly.'
Write-Output '  No extension: select no-extension -> it is sniffed as text and previews as text (this is not the fallback view).'
Write-Output '  Unsupported/Office: unsupported-office.docx and unknown.bin -> fallback thumbnail with name/type/size/dates and Open; Office is not previewed in v1.'
Write-Output '  Folders: select sample-folder -> item count and modified date (no recursive size, no hang).'
Write-Output '  Virtual desktop: with a preview open, switch virtual desktop (Win+Ctrl+Left/Right) -> the preview closes; no stale preview is left behind.'
Write-Output '  Quick Look only: Window shortcuts OFF, Quick Look ON -> no window shortcut registers (Alt+1, Alt+W, Alt+Q etc. do nothing); Space previews.'
Write-Output '  Shortcuts only: Window shortcuts ON, Quick Look OFF -> existing window shortcuts work unchanged and Space is left to Explorer.'
Write-Output '  IME: with a Chinese/Japanese/Korean IME composing text in Explorer (rename or search), press Space -> the space reaches the IME composition; no preview opens.'
Write-Output '  Conflict note: with QuickLook.exe or PowerToys Peek running, the Settings card shows one InfoBar (no dialog); QuickLook uses Space, Peek uses Ctrl+Space by default.'
Write-Output '  Themes: check Light, Dark and High Contrast (Windows Settings > Accessibility > Contrast themes) -> all text and borders readable.'
Write-Output '  DPI: at 150% and 200% scaling on the Explorer monitor, the preview is sized and centred correctly, not blurry or clipped.'
Write-Output ''
Write-Output 'If anything fails, note the file type, the Explorer window state (normal/maximized/desktop) and the theme/DPI.'
