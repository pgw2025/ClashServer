# ============================================================
# ClashServer 一键发布脚本（方案 A：框架依赖 + systemd + Nginx）
# 用法（在项目根目录执行）:
#   ./docs/deploy-alinux4/scripts/publish-and-upload.ps1 -ServerIp <ECS公网IP> [-Domain <域名>]
# 前置条件:
#   - 本机: .NET 8 SDK + Node.js 18+（构建用）
#   - 网络: 本机到 ECS 已配置 ssh/scp 免密登录（或接受交互输入密码）
# 说明:
#   - 前端构建产物输出到 server/wwwroot，后端 publish 后整体打包
#   - 若 server/Data 已存在配置，会一并打包（云端脚本自动保留旧数据）
#   - 上传后自动在云端执行 deploy-alinux4.sh 完成部署
# ============================================================
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipDeploy
)
$ErrorActionPreference = 'Stop'

# ============================================================
# 交互式输入：逐项提示，回车即采用括号内的默认值
# ============================================================
function Read-Default {
    param(
        [string]$Prompt,
        [string]$Default = '',
        [switch]$Required
    )
    if ($Default) { $line = "  $Prompt（默认: $Default）" }
    else          { $line = "  $Prompt" }
    $val = Read-Host $line
    if ([string]::IsNullOrWhiteSpace($val)) { $val = $Default }
    if ($Required -and [string]::IsNullOrWhiteSpace($val)) {
        throw "「$Prompt」不能为空"
    }
    return $val
}

Write-Host "`n===== 请按提示依次输入（直接回车采用默认值）=====" -ForegroundColor Cyan
$ServerIp     = Read-Default -Prompt 'ECS 公网 IP' -Required
$RemoteUser   = Read-Default -Prompt 'SSH 远程用户' -Default 'root'
$RemoteDir    = Read-Default -Prompt '集群部署目录（远端）' -Default '/opt/clashserver'
$answer       = Read-Default -Prompt '是否创建运行用户? [y/N]' -Default '否'
$CreateUser   = ($answer -match '^[Yy]$')
$answer       = Read-Default -Prompt '是否安装 .NET 运行环境? [y/N]' -Default '否'
$InstallDotnet = ($answer -match '^[Yy]$')
$ListenPort   = Read-Default -Prompt '应用监听端口' -Default '5080'
$Bind         = Read-Default -Prompt '监听回环地址' -Default '127.0.0.1'
$HttpsPort    = Read-Default -Prompt 'HTTPS 端口' -Default '2130'
$Domain       = Read-Default -Prompt '域名（没有则留空，跳过 HTTPS 域名绑定）' -Default ''
$OutDir       = Read-Default -Prompt '本地打包输出目录' -Default '.deploy'
$answer       = Read-Default -Prompt '自包含发布? [y/N]' -Default '否'
$SelfContained = ($answer -match '^[Yy]$')
Write-Host ""

$root         = $PSScriptRoot
$serverDir    = Join-Path $root 'server'
$webDir       = Join-Path $root 'web'
$outDir       = Join-Path $root $OutDir
$publishDir   = Join-Path $outDir 'publish'
$tarball      = Join-Path $outDir 'clashserver.tar.gz'
$deployScript = Join-Path $PSScriptRoot 'deploy-alinux4.sh'

function Write-Step([string]$m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

# ---- 0. 前置检查 ----
foreach ($cmd in @('dotnet', 'node', 'npm', 'tar', 'ssh', 'scp')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "缺少工具: $cmd" }
}
if (-not (Test-Path (Join-Path $serverDir 'ClashServer.csproj'))) { throw '未找到 server\ClashServer.csproj' }
if (-not (Test-Path (Join-Path $webDir 'package.json'))) { throw '未找到 web\package.json' }

# SSH 免密探测（不阻塞，仅提示）
ssh -o BatchMode=yes -o ConnectTimeout=10 "$RemoteUser@$ServerIp" 'echo ok' *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host '[提示] SSH 免密未就绪或网络不通，后续命令会提示输入密码。' -ForegroundColor Yellow
    Write-Host '       建议先配置免密: 本机 ssh-keygen 生成密钥，再把公钥加入服务器 ~/.ssh/authorized_keys' -ForegroundColor Yellow
}

# ---- 1. 构建 ----
if (-not $SkipBuild) {
    Write-Step '1/4 构建前端 (web -> server/wwwroot)'
    Push-Location $webDir
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci 失败' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build 失败' }
    } finally { Pop-Location }

    Write-Step "2/4 发布后端 (dotnet publish -> $publishDir)"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    Push-Location $serverDir
    try {
        $pubArgs = @('publish', '-c', 'Release', '-o', $publishDir)
        if ($SelfContained) { $pubArgs += @('-r', 'linux-x64', '--self-contained', 'true', '/p:PublishSingleFile=true') }
        dotnet @pubArgs
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }
    } finally { Pop-Location }

    # 删除仅 IIS 用的 web.config（Linux 上无害但冗余）
    $webCfg = Join-Path $publishDir 'web.config'
    if (Test-Path $webCfg) { Remove-Item $webCfg -Force }

    # 附带本地已有数据（若有）；发布目录里若已被 dotnet publish 内置 Data，先删除再以本地为准
    $dataDir = Join-Path $serverDir 'Data'
    if (Test-Path $dataDir) {
        $pubData = Join-Path $publishDir 'Data'
        if (Test-Path $pubData) { Remove-Item $pubData -Recurse -Force }
        Copy-Item -Path $dataDir -Destination $publishDir -Recurse
        Write-Host '[信息] 已附带 server/Data 数据' -ForegroundColor Green
    }

    Write-Step "3/4 打包 ($tarball)"
    if (Test-Path $tarball) { Remove-Item $tarball -Force }
    tar -czf $tarball -C $publishDir .
    if ($LASTEXITCODE -ne 0) { throw 'tar 打包失败' }
}
else {
    if (-not (Test-Path $tarball)) { throw "未找到部署包: $tarball （请先不加 -SkipBuild 运行一次）" }
}

# ---- 2. 上传 + 云端部署 ----
if (-not $SkipDeploy) {
    Write-Step "4/4 上传并部署到 $RemoteUser@$ServerIp"
    scp $tarball "$RemoteUser@${ServerIp}:/tmp/clashserver.tar.gz"
    if ($LASTEXITCODE -ne 0) { throw 'scp 上传部署包失败' }
    scp $deployScript "$RemoteUser@${ServerIp}:/tmp/deploy-alinux4.sh"
    if ($LASTEXITCODE -ne 0) { throw 'scp 上传部署脚本失败' }

    $extra = @()
    if ($Domain) { $extra += "--domain $Domain" }
    if ($SelfContained) { $extra += '--self-contained' }
    if ($CreateUser) { $extra += '--create-user' }
    if ($InstallDotnet) { $extra += '--install-dotnet' }
    $extra += "--https-port $HttpsPort"
    $extra += "--remote-dir $RemoteDir"
    $extra += "--listen-port $ListenPort"
    $extra += "--bind $Bind"
    $sudoPrefix = if ($RemoteUser -eq 'root') { '' } else { 'sudo ' }
    ssh "$RemoteUser@$ServerIp" "$sudoPrefix bash /tmp/deploy-alinux4.sh $($extra -join ' ')"
    if ($LASTEXITCODE -ne 0) { throw '云端部署失败，请查看上方日志' }
}

Write-Host "`n[完成] 发布包: $tarball" -ForegroundColor Green
Write-Host '       后续仅更新: 重新运行本脚本（会自动重新构建并部署）' -ForegroundColor Green
