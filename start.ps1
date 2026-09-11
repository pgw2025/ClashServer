# 一键启动 ClashServer 开发环境（后端 5080 + Vite 热更新 5173，支持局域网访问）
# 用法: 双击 start.bat，或 PowerShell 执行 .\start.ps1 [-NoBrowser]
# 其他设备访问前，请先用管理员身份运行一次 firewall.bat 放行防火墙（脚本会检测并提示）
[CmdletBinding()]
param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root        = $PSScriptRoot
$serverDir   = Join-Path $root 'server'
$webDir      = Join-Path $root 'web'
$backendPort = 5080
$vitePort    = 5173
$listenUrl   = "http://0.0.0.0:$backendPort"   # 监听所有网卡，局域网其他设备可访问
$backendUrl  = "http://localhost:$backendPort" # 本机探测/API 地址
$viteUrl     = "http://localhost:$vitePort"

function Test-Port([int]$Port) {
    return $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Get-PortOwner([int]$Port) {
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $conn) { return $null }
    return Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
}

function Get-LanIp {
    try {
        $cfg = Get-NetIPConfiguration -ErrorAction Stop |
            Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
            Select-Object -First 1
        if ($cfg -and $cfg.IPv4Address) { return ($cfg.IPv4Address | Select-Object -First 1).IPAddress }
    } catch { }
    $ip = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.AddressState -eq 'Preferred' } |
        Select-Object -First 1
    if ($ip) { return $ip.IPAddress }
    return $null
}

Write-Host '==============================================' -ForegroundColor Cyan
Write-Host '  ClashServer 开发环境一键启动' -ForegroundColor Cyan
Write-Host '==============================================' -ForegroundColor Cyan

# 1. 前置检查
foreach ($cmd in @('dotnet', 'node', 'npm')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Host "[错误] 未找到 $cmd，请先安装 .NET SDK / Node.js" -ForegroundColor Red
        exit 1
    }
}
if (-not (Test-Path (Join-Path $serverDir 'ClashServer.csproj'))) {
    Write-Host "[错误] 未找到 server\ClashServer.csproj" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path (Join-Path $webDir 'package.json'))) {
    Write-Host "[错误] 未找到 web\package.json" -ForegroundColor Red
    exit 1
}

# 2. 端口占用检查
foreach ($port in @($backendPort, $vitePort)) {
    if (Test-Port $port) {
        $p = Get-PortOwner $port
        $owner = if ($p) { "$($p.ProcessName) (PID $($p.Id))" } else { '未知进程' }
        Write-Host "[警告] 端口 $port 已被占用：$owner" -ForegroundColor Yellow
        Write-Host '       请先运行 stop.bat 停止旧实例，或手动结束该进程后重试' -ForegroundColor Yellow
        exit 1
    }
}

# 3. 前端依赖
if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
    Write-Host '[信息] 首次运行，安装前端依赖 npm ci ...'
    Push-Location $webDir
    try { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci 失败' } }
    finally { Pop-Location }
}

# 4. 启动后端 (5080)：监听 0.0.0.0 + 开发环境 + Vue 模式，仅通过环境变量覆盖，不改任何文件
Write-Host "[启动] 后端 dotnet run → 0.0.0.0:$backendPort（Development + Vue 模式）" -ForegroundColor Cyan
$env:ASPNETCORE_URLS         = $listenUrl
$env:ASPNETCORE_ENVIRONMENT  = 'Development'
$env:VueApp__Enabled         = 'true'
$backendCmd = '$Host.UI.RawUI.WindowTitle = ''ClashServer Backend (5080)''; dotnet run'
Start-Process -FilePath 'powershell' -ArgumentList @('-NoExit', '-Command', $backendCmd) -WorkingDirectory $serverDir | Out-Null

# 5. 启动 Vite (5173)：--host 0.0.0.0 供局域网访问，前端热更新
# 注意: 不能用 "npm run dev -- --host" —— npm 会吞掉 --host 只把 0.0.0.0 传给 vite，
#       必须直接调用本地 vite.cmd 才能让 --host 生效
Write-Host "[启动] Vite 开发服务器 → 0.0.0.0:$vitePort（热更新）" -ForegroundColor Cyan
$viteCmd = '$Host.UI.RawUI.WindowTitle = ''Vite Dev Server (5173)''; .\node_modules\.bin\vite.cmd --host 0.0.0.0'
Start-Process -FilePath 'powershell' -ArgumentList @('-NoExit', '-Command', $viteCmd) -WorkingDirectory $webDir | Out-Null

# 6. 等待就绪
Write-Host '[等待] 后端编译启动中（首次约 30~60 秒）...'
$backendOk = $false
for ($i = 0; $i -lt 180; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $r = Invoke-WebRequest -Uri "$backendUrl/api/auth/status" -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { $backendOk = $true; break }
    } catch { }
}
if (-not $backendOk) {
    Write-Host '[错误] 后端 90 秒内未就绪，请查看后端窗口日志' -ForegroundColor Red
    exit 1
}
Write-Host "[完成] 后端已就绪：$backendUrl" -ForegroundColor Green

Write-Host '[等待] Vite 启动中...'
$viteOk = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $r = Invoke-WebRequest -Uri $viteUrl -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { $viteOk = $true; break }
    } catch { }
}
if (-not $viteOk) {
    Write-Host '[错误] Vite 30 秒内未就绪，请查看 Vite 窗口日志' -ForegroundColor Red
    exit 1
}
Write-Host "[完成] Vite 已就绪：$viteUrl" -ForegroundColor Green

# 7. 防火墙检查：仅提示，不自动提权
$fwNames = @("ClashServer Dev (TCP $backendPort)", "ClashServer Dev (TCP $vitePort)")
$missingFw = @()
foreach ($n in $fwNames) {
    if (-not (Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue)) { $missingFw += $n }
}

# 8. 打开浏览器
if (-not $NoBrowser) {
    Write-Host '[打开] 默认浏览器...'
    Start-Process $viteUrl
}

$lanIp = Get-LanIp
Write-Host ''
Write-Host '==============================================' -ForegroundColor Cyan
Write-Host '  开发环境已启动' -ForegroundColor Green
Write-Host "  本机前端（热更新） : $viteUrl" -ForegroundColor Green
Write-Host "  本机后端（API）    : $backendUrl" -ForegroundColor Green
if ($lanIp) {
    Write-Host "  局域网前端          : http://$lanIp`:$vitePort" -ForegroundColor Green
    Write-Host "  局域网后端          : http://$lanIp`:$backendPort" -ForegroundColor Green
    Write-Host "  /sub 端点(其他设备): http://$lanIp`:$backendPort/sub?token=<订阅token>" -ForegroundColor Green
} else {
    Write-Host '  [注意] 未能自动识别局域网 IP，请用 ipconfig 查看本机地址' -ForegroundColor Yellow
}
Write-Host '  停止方式            : 运行 stop.bat，或直接关闭两个窗口' -ForegroundColor Green
if ($missingFw.Count -gt 0) {
    Write-Host '' -ForegroundColor Yellow
    Write-Host '  [防火墙] 其他设备访问前需放行端口：' -ForegroundColor Yellow
    Write-Host "          请右键以【管理员身份】运行一次 firewall.bat" -ForegroundColor Yellow
    Write-Host "          （缺少规则: $($missingFw -join '、')）" -ForegroundColor Yellow
}
Write-Host '==============================================' -ForegroundColor Cyan
