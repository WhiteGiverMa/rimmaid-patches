param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '../About/Preview.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(1024, 1024)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

function Color([string]$Hex) { [System.Drawing.ColorTranslator]::FromHtml($Hex) }
function Polygon([string]$Hex, [System.Drawing.PointF[]]$Points) {
    $brush = [System.Drawing.SolidBrush]::new((Color $Hex))
    try { $graphics.FillPolygon($brush, $Points) } finally { $brush.Dispose() }
}
function Line([string]$Hex, [float]$Width, [System.Drawing.PointF]$A, [System.Drawing.PointF]$B) {
    $pen = [System.Drawing.Pen]::new((Color $Hex), $Width)
    try { $graphics.DrawLine($pen, $A, $B) } finally { $pen.Dispose() }
}
function Text([string]$Value, [float]$Size, [string]$Hex, [float]$X, [float]$Y, [bool]$Bold = $false) {
    $style = if ($Bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }
    $font = [System.Drawing.Font]::new('Microsoft YaHei UI', $Size, $style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.SolidBrush]::new((Color $Hex))
    try { $graphics.DrawString($Value, $font, $brush, $X, $Y) } finally { $font.Dispose(); $brush.Dispose() }
}
function Iso([float]$X, [float]$Y, [float]$Z = 0) {
    [System.Drawing.PointF]::new((512 + ($X - $Y) * 46), (568 + ($X + $Y) * 23 - $Z * 40))
}
function Slab([float]$X0, [float]$X1, [float]$Y0, [float]$Y1, [float]$Z0, [float]$Z1,
              [string]$Top, [string]$Front, [string]$Side) {
    Polygon $Front @((Iso $X0 $Y1 $Z0), (Iso $X1 $Y1 $Z0), (Iso $X1 $Y1 $Z1), (Iso $X0 $Y1 $Z1))
    Polygon $Side @((Iso $X1 $Y0 $Z0), (Iso $X1 $Y1 $Z0), (Iso $X1 $Y1 $Z1), (Iso $X1 $Y0 $Z1))
    Polygon $Top @((Iso $X0 $Y0 $Z1), (Iso $X1 $Y0 $Z1), (Iso $X1 $Y1 $Z1), (Iso $X0 $Y1 $Z1))
}

try {
    $graphics.Clear((Color '#17242A'))
    for ($x = 32; $x -lt 1024; $x += 48) {
        Line '#203139' 1 ([System.Drawing.PointF]::new($x, 296)) ([System.Drawing.PointF]::new($x, 872))
    }
    for ($y = 320; $y -le 872; $y += 48) {
        Line '#203139' 1 ([System.Drawing.PointF]::new(32, $y)) ([System.Drawing.PointF]::new(992, $y))
    }
    Text 'FORTIFICATIONS  /  ODYSSEY' 22 '#E7B76A' 56 34 $true
    Text '钢筋混凝土' 75 '#F2EEE3' 50 79 $true
    Text '重型桥梁' 102 '#F2EEE3' 45 159 $true
    Text 'REINFORCED CONCRETE HEAVY BRIDGE' 21 '#94B1B8' 57 281

    # 固定随机种子与纯几何图形，不引用或打包游戏贴图。
    Slab -5.1 -1.9 -3.8 3.8 -0.55 0 '#66735B' '#414F42' '#39483F'
    Slab 1.9 5.1 -3.8 3.8 -0.55 0 '#66735B' '#414F42' '#39483F'
    Polygon '#235567' @((Iso -1.9 -5.3 -0.25), (Iso 1.9 -5.3 -0.25), (Iso 1.9 5.3 -0.25), (Iso -1.9 5.3 -0.25))
    $random = [Random]::new(14014)
    for ($i = 0; $i -lt 56; $i++) {
        $x = -1.7 + $random.NextDouble() * 3.2
        $y = -5.1 + $random.NextDouble() * 10
        Line '#397787' 2 (Iso $x $y -0.24) (Iso $x ($y + 0.45) -0.24)
    }
    for ($i = 0; $i -lt 45; $i++) {
        $x = if ($i % 2 -eq 0) { -4.9 + $random.NextDouble() * 2.7 } else { 2.2 + $random.NextDouble() * 2.7 }
        $y = -3.6 + $random.NextDouble() * 7.2
        if ([Math]::Abs($y) -gt 1.45) {
            Line '#849071' 3 (Iso $x $y 0.02) (Iso ($x + 0.17) ($y + 0.06) 0.02)
        }
    }

    Slab -2.5 -1.75 -0.9 0.9 -0.2 1.15 '#98A6A3' '#697978' '#566766'
    Slab 1.75 2.5 -0.9 0.9 -0.2 1.15 '#98A6A3' '#697978' '#566766'
    Slab -4.3 4.3 -1.2 1.2 0.85 1.45 '#B9C2BA' '#7B8A86' '#60736E'
    for ($x = -4.3; $x -le 4.3; $x += 0.86) {
        Line '#8B9A95' 2 (Iso $x -1.19 1.46) (Iso $x 1.19 1.46)
    }
    Line '#D9DED2' 4 (Iso -4.3 -1.19 1.46) (Iso 4.3 -1.19 1.46)
    Line '#D9DED2' 4 (Iso -4.3 1.19 1.46) (Iso 4.3 1.19 1.46)
    Line '#344A48' 5 (Iso -4.2 1.205 1.05) (Iso 4.2 1.205 1.05)
    for ($x = -4.15; $x -lt 4.3; $x += 0.43) {
        Line '#D8A64E' 5 (Iso $x 1.207 0.98) (Iso ($x + 0.2) 1.207 1.18)
    }

    # 两个俯视货箱暗示重载支撑，而非改变桥梁的游戏外观。
    Slab -0.6 0.25 -0.55 0.25 1.45 2.15 '#DAB876' '#A07E49' '#80613C'
    Line '#705636' 3 (Iso -0.45 -0.55 2.16) (Iso -0.45 0.25 2.16)
    Line '#705636' 3 (Iso 0.1 -0.55 2.16) (Iso 0.1 0.25 2.16)
    Slab 0.5 1.12 -0.3 0.35 1.45 1.95 '#B7C1B3' '#788D81' '#5A736B'

    $badge = [System.Drawing.SolidBrush]::new((Color '#E7B76A'))
    try { $graphics.FillRectangle($badge, 56, 796, 912, 102) } finally { $badge.Dispose() }
    Text '14' 72 '#17242A' 73 796 $true
    Text '钢筋混凝土 / 格' 39 '#17242A' 191 811 $true
    Text '默认造价 · 可调' 21 '#35423F' 657 831
    Text '重型支撑  /  原版贴图  /  卸载前一键转换' 27 '#E7E5D9' 56 922 $true
    Text 'WHITEGIVERMA' 17 '#6D9197' 56 979
    Text 'RIMWORLD 1.7' 17 '#6D9197' 801 979
    $fullPath = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullPath)) | Out-Null
    $bitmap.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Preview saved: $fullPath (1024 x 1024)"
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
