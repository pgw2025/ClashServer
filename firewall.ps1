# 放行防火墙：允许局域网其他设备访问 5080（后端）/ 5173（Vite）
# 用法: 双击 firewall.bat，或右键以管理员身份运行本文件（脚本会自动请求提权）
# 仅在当前网络配置文件 + Private/Domain 上放行，Public（公共网络）不放行

# 自动请求管理员权限
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host '[信息] 需要管理员权限，正在请求 UAC 确认...'
    Start-Process -FilePath 'powershell' -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    )
    exit
}

# 收集当前活动网络配置文件（Public 不放行，避免在公共网络开放管理端口）
$profiles = @()
foreach ($cp in Get-NetConnectionProfile -ErrorAction SilentlyContinue) {
    $cat = switch ([int]$cp.NetworkCategory) {
        1 { 'Public' }
        2 { 'Private' }
        3 { 'Domain' }
        default { $null }
    }
    if ($cat) { $profiles += $cat }
}
if ($profiles.Count -eq 0) { $profiles = @('Private', 'Domain') }
$profiles = $profiles | Select-Object -Unique

$rules = @(
    @{ Port = 5080; Name = 'ClashServer Dev (TCP 5080)' },
    @{ Port = 5173; Name = 'ClashServer Dev (TCP 5173)' }
)

foreach ($r in $rules) {
    if (Get-NetFirewallRule -DisplayName $r.Name -ErrorAction SilentlyContinue) {
        Write-Host "[跳过] 规则已存在：$($r.Name)"
        continue
    }
    New-NetFirewallRule -DisplayName $r.Name -Direction Inbound -Protocol TCP -LocalPort $r.Port -Action Allow -Profile $profiles | Out-Null
    Write-Host "[完成] 已放行 TCP $($r.Port)：$($r.Name)"
}

Write-Host "[信息] 生效网络配置文件：$($profiles -join ', ')"
Write-Host '[完成] 防火墙配置结束，可重新运行 start.bat 启动'
