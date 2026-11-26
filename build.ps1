# 编译「保持亮屏」：只用系统自带的 csc，不需要 VS / dotnet SDK
param(
    [string]$OutName = "保持亮屏.exe"
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (!(Test-Path $csc)) {
    Write-Error "找不到 csc.exe，请安装 .NET Framework 4.x"
    exit 1
}

$args = @(
    "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+",
    # 源码里有中文，必须指定 65001，否则乱码
    "/codepage:65001",
    # exe 文件图标（资源管理器 / 任务栏大图标）
    "/win32icon:$src\app.ico",
    # 内嵌资源：托盘图标、窗口图标、标题栏小图标
    "/resource:$src\tray.ico,TrayIco",
    "/resource:$src\app.ico,AppIco",
    "/resource:$src\mark.png,MarkPng",
    "/out:$root\$OutName",
    "/r:System.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll",
    "$src\KeepAwake.UI.cs", "$src\KeepAwake.Main.cs", "$src\KeepAwake.Logic.cs"
)

& $csc @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-Item (Join-Path $root $OutName) | Select-Object Name, Length, LastWriteTime
