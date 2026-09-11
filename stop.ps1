# 一键停止 ClashServer 开发环境：结束占用 5080（后端）/ 5173（Vite）的进程
# 并顺带结束 dotnet run 包装进程，防止子进程被杀后其自动重启
# 用法: 双击 stop.bat，或 PowerShell 执行 .\stop.ps1

$ports = @(5080, 5173)
$procIds = @()

foreach ($port in $ports) {
    $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    foreach ($conn in $conns) {
        $procIds += $conn.OwningProcess
    }
}

$procIds = $procIds | Sort-Object -Unique

if ($procIds.Count -eq 0) {
    Write-Host '[信息] 未发现运行中的后端/Vite 进程，无需停止' -ForegroundColor Yellow
    exit 0
}

foreach ($procId in $procIds) {
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    $name = if ($proc) { "$($proc.ProcessName) (PID $($proc.Id))" } else { "PID $procId" }
    Write-Host "[停止] $name"
    Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue

    # 结束 dotnet run 包装进程（父进程），避免其重启子进程
    try {
        $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$procId" -ErrorAction Stop).ParentProcessId
        $pp = Get-Process -Id $parentId -ErrorAction SilentlyContinue
        if ($pp -and $pp.ProcessName -eq 'dotnet') {
            Write-Host "[停止] $($pp.ProcessName) (PID $($pp.Id)) [dotnet run 包装]"
            Stop-Process -Id $parentId -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}

Start-Sleep -Milliseconds 500
Write-Host '[完成] 已全部停止，可重新运行 start.bat' -ForegroundColor Green
