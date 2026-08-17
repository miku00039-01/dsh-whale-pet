# build.ps1 —— 从源码构建 DSH 桌宠 exe
# 用法:在项目根目录执行  pwsh -File build.ps1 [-OutDir <输出目录>]
# 产物:DSH桌宠.exe(默认项目根目录;指定 -OutDir 时输出到该目录);依赖:Windows 自带 csc.exe + .NET Framework
param([string]$OutDir = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcCs = Join-Path $root 'src\DSHPet.cs'
$whale = Join-Path $root 'assets\whale.png'
$ico   = Join-Path $root 'assets\pet.ico'
$outTmp = Join-Path $root 'build\DSHPet.exe'
if ($OutDir -ne '') {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $outExe = Join-Path $OutDir 'DSH桌宠.exe'
} else {
    $outExe = Join-Path $root 'DSH桌宠.exe'
}
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '未找到 csc.exe(.NET Framework 编译器)' }
New-Item -ItemType Directory -Force -Path (Join-Path $root 'build') | Out-Null

# ── 1) 从 whale.png 生成多尺寸 ICO(256/64/48/32/16,PNG 压缩,保留透明) ──
Write-Host '[1/3] 生成 pet.ico ...'
$sizes = @(256, 64, 48, 32, 16)
$src = New-Object System.Drawing.Bitmap($whale)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}
$src.Dispose()
$count = $sizes.Count
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$count)
$offset = 6 + 16 * $count
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($ico, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

# ── 2) 源码补 UTF-8 BOM(csc 需要正确读取中文) ──
Write-Host '[2/3] 编译源码 ...'
$text = [System.IO.File]::ReadAllText($srcCs)
[System.IO.File]::WriteAllText($srcCs, $text, (New-Object System.Text.UTF8Encoding($true)))

# ── 3) 编译(经 cmd 传参,避开 PowerShell/csc 参数解析问题;先 ASCII 名再改名) ──
$cmd = '"' + $csc + '" /nologo /target:winexe /win32icon:' + $ico + ' /out:' + $outTmp +
       ' /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll' +
       ' /resource:' + $whale + ',DSHWhalePet.pet.png "' + $srcCs + '"'
cmd /c $cmd | Out-Null
if ($LASTEXITCODE -ne 0) { throw '编译失败(见上方 csc 输出)' }
if (Test-Path $outExe) { Remove-Item $outExe -Force }
Move-Item $outTmp $outExe -Force
Write-Host ('[3/3] 构建完成: ' + $outExe + ' (' + (Get-Item $outExe).Length + ' 字节)')
