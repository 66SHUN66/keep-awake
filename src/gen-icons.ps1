using namespace System.Drawing
using namespace System.Drawing.Drawing2D
Add-Type -AssemblyName System.Drawing

$OutDir = "C:\Users\30499\Doubao\chats\2026-11-26\new-chat-9\src"

$Amber = [Drawing.Color]::FromArgb(255, 246, 186, 72)
$AmberDeep = [Drawing.Color]::FromArgb(255, 231, 146, 28)
$ScreenTop = [Drawing.Color]::FromArgb(255, 255, 238, 186)
$Ink = [Drawing.Color]::FromArgb(255, 16, 18, 26)

function RoundPath($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    if ($r -lt 0.5) { $r = 0.5 }
    $d = $r * 2
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function ScreenFill($g, $x, $y, $w, $h, $r) {
    $inner = RoundPath $x $y $w $h $r
    $lgb = New-Object Drawing.Drawing2D.LinearGradientBrush(
        [Drawing.Point]::new($x, $y),
        [Drawing.Point]::new($x + $w, $y + $h),
        $ScreenTop, $AmberDeep)
    $g.FillPath($lgb, $inner)
    $glint = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(55, 255, 255, 255))
    $g.FillEllipse($glint, ($x + $w * 0.08), ($y + $h * 0.1), ($w * 0.36), ($h * 0.16))
    $lgb.Dispose(); $glint.Dispose(); $inner.Dispose()
}

function Stand($g, $x, $y, $w, $neckW, $neckH, $baseW, $baseH) {
    # 屏幕底部正下方：颈 + 底座
    $cx = $x + $w / 2
    $neck = RoundPath ($cx - $neckW / 2) $y $neckW ($neckH + 4) 1.5
    $g.FillPath((New-Object Drawing.SolidBrush($Amber)), $neck)
    $neck.Dispose()

    $base = RoundPath ($cx - $baseW / 2) ($y + $neckH) $baseW $baseH ($baseH / 2)
    $g.FillPath((New-Object Drawing.SolidBrush($Amber)), $base)
    $base.Dispose()
}

# 大尺寸：深色圆角底板 + 光晕 + 光束 + 描边式屏幕
function DrawBig($g) {
    $bg = RoundPath 8 8 240 240 52
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush(
        [Drawing.Point]::new(0, 0), [Drawing.Point]::new(0, 256),
        [Drawing.Color]::FromArgb(255, 44, 48, 66),
        [Drawing.Color]::FromArgb(255, 15, 17, 25))
    $g.FillPath($br, $bg)
    $g.DrawPath((New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 72, 78, 102), 2)), $bg)
    $br.Dispose(); $bg.Dispose()

    $gp = New-Object Drawing.Drawing2D.GraphicsPath
    $gp.AddEllipse(24, 22, 208, 208)
    $pgb = New-Object Drawing.Drawing2D.PathGradientBrush($gp)
    $pgb.CenterPoint = [Drawing.PointF]::new(128, 128)
    $pgb.CenterColor = [Drawing.Color]::FromArgb(50, 255, 170, 56)
    $pgb.SurroundColors = @([Drawing.Color]::FromArgb(0, 255, 170, 56))
    $g.FillPath($pgb, $gp)
    $pgb.Dispose(); $gp.Dispose()

    # 光束
    $rayPen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(185, 246, 186, 72), 5)
    $rayPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $rayPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($rayPen, 98, 42, 98, 58)
    $g.DrawLine($rayPen, 128, 32, 128, 58)
    $g.DrawLine($rayPen, 158, 42, 158, 58)
    $rayPen.Dispose()

    # 屏幕：琥珀外框 + 发光内屏 + 深色分隔
    $mon = RoundPath 32 62 192 122 20
    $g.DrawPath((New-Object Drawing.Pen($Amber, 5)), $mon)
    $sep = RoundPath 38 68 180 110 17
    $g.DrawPath((New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 24, 26, 36), 4)), $sep)
    ScreenFill $g 45 75 166 96 12
    $mon.Dispose(); $sep.Dispose()

    Stand $g 45 184 166 20 16 86 10
}

# 小尺寸 / 托盘：双色剪影（深色外框 + 琥珀屏），无底板
function DrawGlyph($g, $x, $y, $w, $h, $bezel) {
    $outer = RoundPath $x $y $w $h ($h * 0.22)
    $g.FillPath((New-Object Drawing.SolidBrush($Ink)), $outer)
    $outer.Dispose()

    ScreenFill $g ($x + $bezel) ($y + $bezel) ($w - $bezel * 2) ($h - $bezel * 2) ([Math]::Max(2, $h * 0.22 - $bezel * 0.9))
    $iw = $w - $bezel * 2
    Stand $g ($x + $bezel) ($y + $h) $iw ($iw * 0.16) 42 ($iw * 0.52) 24
}

function DrawAppSmall($g) {
    DrawGlyph $g 20 30 216 132 11
}

function RenderIcon($size, $kind) {
    $bmp = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    $small = ($size -le 32)
    if ($kind -eq "app") {
        if ($small) { DrawAppSmall $g } else { DrawBig $g }
    } else {
        if ($small) { DrawGlyph $g 14 26 228 140 8 } else { DrawGlyph $g 22 34 212 130 9 }
    }
    $g.ResetTransform()
    $g.Dispose()
    return $bmp
}

function WriteIco($path, $frames) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $s = $f.Size
        $dim = 0
        if ($s -lt 256) { $dim = $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim)
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$f.Bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $bw.Write([byte[]]$f.Bytes) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
}

function MakeFrames($kind, $sizes) {
    $list = @()
    foreach ($s in $sizes) {
        $bmp = RenderIcon $s $kind
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $list += [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
        $ms.Dispose(); $bmp.Dispose()
    }
    return $list
}

WriteIco "$OutDir\app.ico" (MakeFrames "app" @(256, 128, 64, 48, 32, 24, 16))
WriteIco "$OutDir\tray.ico" (MakeFrames "tray" @(48, 32, 24, 20, 16))

function MakeStrip($kind, $sizes, $path, $bgLight) {
    $cell = 200
    $sheet = New-Object Drawing.Bitmap(($cell * $sizes.Count + 20), 250, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($sheet)
    $bgColor = [Drawing.Color]::FromArgb(255, 26, 28, 38)
    if ($bgLight) { $bgColor = [Drawing.Color]::FromArgb(255, 242, 242, 246) }
    $g.Clear($bgColor)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
    $brushColor = [Drawing.Color]::White
    if ($bgLight) { $brushColor = [Drawing.Color]::FromArgb(255, 40, 44, 60) }
    $brush = New-Object Drawing.SolidBrush($brushColor)
    $font = New-Object Drawing.Font("Segoe UI", 9)
    $x = 10
    foreach ($s in $sizes) {
        $z = [Math]::Floor(170 / $s)
        $bmp = RenderIcon $s $kind
        $g.DrawImage($bmp, ($x + (180 - $s * $z) / 2), 16, ($s * $z), ($s * $z))
        $g.DrawString("$s px", $font, $brush, $x, 200)
        $x += $cell
        $bmp.Dispose()
    }
    $g.Dispose()
    $sheet.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
}

# 窗口内小图标用 PNG（避免 Icon.ToBitmap 对 PNG 帧的透明处理问题）
$mk = RenderIcon 128 "tray"
$mk.Save("$OutDir\mark.png", [Drawing.Imaging.ImageFormat]::Png)
$mk.Dispose()

$big = RenderIcon 256 "app"
$big.Save("$OutDir\preview-app.png", [Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

MakeStrip "app" @(48, 32, 24, 16) "$OutDir\preview-app-small.png" $true
MakeStrip "tray" @(48, 32, 24, 16) "$OutDir\preview-tray-dark.png" $false
MakeStrip "tray" @(48, 32, 24, 16) "$OutDir\preview-tray-light.png" $true
"icons done"


