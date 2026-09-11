#!/usr/bin/env bash
# ============================================================
# ClashServer 云端部署脚本（Alibaba Cloud Linux 4，方案 A/B）
# 用法: sudo bash deploy-alinux4.sh [--domain 域名] [--self-contained] [--remote-dir /opt/clashserver]
# 前置: /tmp/clashserver.tar.gz 与 /tmp/deploy-alinux4.sh 已上传（由 publish-and-upload.ps1 完成）
# 特性: 幂等可重复执行；Data 数据自动保留；无证书时自动降级为 HTTP(80)
# ============================================================
set -euo pipefail

DOMAIN=""
SELF_CONTAINED=0
REMOTE_DIR="/opt/clashserver"
TARBALL="/tmp/clashserver.tar.gz"
APP_USER="clashsvc"
PORT="5080"
BIND="127.0.0.1"

while [ $# -gt 0 ]; do
    case "$1" in
        --domain) DOMAIN="$2"; shift 2 ;;
        --self-contained) SELF_CONTAINED=1; shift ;;
        --remote-dir) REMOTE_DIR="$2"; shift 2 ;;
        *) echo "[错误] 未知参数: $1"; exit 1 ;;
    esac
done

if [ "$(id -u)" -ne 0 ]; then
    echo "[错误] 请用 root 或 sudo 执行本脚本"
    exit 1
fi
if [ ! -f "$TARBALL" ]; then
    echo "[错误] 未找到 $TARBALL"
    echo "       请先在 Windows 上运行: publish-and-upload.ps1 -ServerIp <ECS公网IP>"
    exit 1
fi

echo "=============================================="
echo "  ClashServer 云端部署开始"
echo "  远程目录 : $REMOTE_DIR"
echo "  域名     : ${DOMAIN:-<未配置>}"
if [ "$SELF_CONTAINED" -eq 1 ]; then
    echo "  运行方式 : 自包含（免装 .NET runtime）"
else
    echo "  运行方式 : 框架依赖（需 .NET 8 runtime）"
fi
echo "=============================================="

# ---- 1. 运行用户 ----
if ! id "$APP_USER" >/dev/null 2>&1; then
    echo "[步骤 1/6] 创建运行用户 $APP_USER"
    useradd -r -s /usr/sbin/nologin "$APP_USER"
else
    echo "[步骤 1/6] 运行用户 $APP_USER 已存在"
fi

# ---- 2. .NET 8 runtime（框架依赖模式才需要）----
if [ "$SELF_CONTAINED" -eq 0 ]; then
    echo "[步骤 2/6] 检查 .NET 8 ASP.NET Core Runtime ..."
    if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -q 'ASP.NET Core App 8\.'; then
        echo "          已安装，跳过"
    else
        echo "          安装中（微软官方脚本，约 1 分钟）..."
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
        bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet
        ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
        dotnet --list-runtimes | grep -q 'ASP.NET Core App 8\.' || { echo "[错误] .NET 8 安装失败"; exit 1; }
    fi
else
    echo "[步骤 2/6] 自包含发布，无需安装 .NET runtime"
fi

# ---- 3. 部署应用（保留现有 Data）----
echo "[步骤 3/6] 部署应用到 $REMOTE_DIR"
mkdir -p "$REMOTE_DIR"
if [ -d "$REMOTE_DIR/Data" ]; then
    rm -rf /tmp/clashserver-data.bak
    cp -a "$REMOTE_DIR/Data" /tmp/clashserver-data.bak
    echo "          已备份现有 Data → /tmp/clashserver-data.bak"
fi
tar -xzf "$TARBALL" -C "$REMOTE_DIR"
if [ -d /tmp/clashserver-data.bak ]; then
    rm -rf "$REMOTE_DIR/Data"
    cp -a /tmp/clashserver-data.bak "$REMOTE_DIR/Data"
    echo "          已恢复 Data/"
fi
chown -R "$APP_USER:$APP_USER" "$REMOTE_DIR"
chmod 750 "$REMOTE_DIR"

# ---- 4. systemd 服务 ----
echo "[步骤 4/6] 写入 systemd 服务"
if [ "$SELF_CONTAINED" -eq 1 ]; then
    EXEC_START="$REMOTE_DIR/ClashServer --urls http://$BIND:$PORT"
else
    EXEC_START="/usr/bin/dotnet $REMOTE_DIR/ClashServer.dll --urls http://$BIND:$PORT"
fi
cat > /etc/systemd/system/clashserver.service <<EOF
[Unit]
Description=ClashServer (ASP.NET Core 8)
After=network.target

[Service]
Type=simple
User=$APP_USER
Group=$APP_USER
WorkingDirectory=$REMOTE_DIR
ExecStart=$EXEC_START
Environment=VueApp__Enabled=true
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1
Restart=always
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF

# ---- 5. Nginx 反代 + HTTPS ----
echo "[步骤 5/6] 配置 Nginx"
command -v nginx >/dev/null 2>&1 || dnf install -y nginx
rm -f /etc/nginx/conf.d/default.conf
CERT="/etc/nginx/certs/fullchain.pem"
KEY="/etc/nginx/certs/privkey.pem"
HAS_CERT=0
[ -f "$CERT" ] && [ -f "$KEY" ] && HAS_CERT=1
SERVER_NAME="${DOMAIN:-_}"

if [ "$HAS_CERT" -eq 1 ]; then
cat > /etc/nginx/conf.d/clashserver.conf <<EOF
server {
    listen 80;
    server_name $SERVER_NAME;
    return 301 https://\$host\$request_uri;
}
server {
    listen 443 ssl;
    http2 on;
    server_name $SERVER_NAME;
    ssl_certificate     $CERT;
    ssl_certificate_key $KEY;
    ssl_protocols       TLSv1.2 TLSv1.3;
    client_max_body_size 10m;
    location /sub {
        access_log off;
        proxy_pass http://$BIND:$PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 60s;
    }
    location / {
        proxy_pass http://$BIND:$PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 60s;
    }
}
EOF
    echo "          HTTPS 已启用（证书: $CERT）"
else
cat > /etc/nginx/conf.d/clashserver.conf <<EOF
server {
    listen 80;
    server_name $SERVER_NAME;
    client_max_body_size 10m;
    location /sub {
        access_log off;
        proxy_pass http://$BIND:$PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 60s;
    }
    location / {
        proxy_pass http://$BIND:$PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 60s;
    }
}
EOF
    echo "          [提示] 未检测到证书，当前仅 HTTP(:80)。"
    echo "                 申请证书放入 /etc/nginx/certs/ 后重跑本脚本即可启用 HTTPS。"
fi

# ---- 6. 防火墙 + 启动 + 验证 ----
echo "[步骤 6/6] 防火墙与启动"
if systemctl is-active --quiet firewalld; then
    firewall-cmd --permanent --add-service=http >/dev/null 2>&1 || true
    firewall-cmd --permanent --add-service=https >/dev/null 2>&1 || true
    firewall-cmd --reload >/dev/null 2>&1 || true
    echo "          firewalld 已放行 80/443"
else
    echo "          [提示] firewalld 未运行，请确认阿里云安全组已放行 80/443"
fi

systemctl daemon-reload
systemctl enable clashserver >/dev/null 2>&1 || true
systemctl restart clashserver
systemctl enable nginx >/dev/null 2>&1 || true
nginx -t >/dev/null
systemctl restart nginx

echo "[等待] 服务就绪（最长 30 秒）..."
CODE=""
for _ in $(seq 1 30); do
    CODE=$(curl -s -o /dev/null -w '%{http_code}' "http://$BIND:$PORT/" || true)
    [ "$CODE" = "200" ] && break
    sleep 1
done
if [ "$CODE" != "200" ]; then
    echo "[错误] Kestrel 未就绪，最近日志："
    journalctl -u clashserver -n 30 --no-pager || true
    exit 1
fi

echo ""
echo "=============================================="
echo "  ClashServer 部署完成 ✔"
echo "  本机探测 : http://127.0.0.1:$PORT  (200)"
if [ "$HAS_CERT" -eq 1 ]; then
    echo "  管理页面 : https://${DOMAIN:-<公网IP>}/"
    echo "  订阅链接 : https://${DOMAIN:-<公网IP>}/sub?token=<你的token>"
else
    echo "  管理页面 : http://<公网IP>/   （建议尽快配置 HTTPS 证书）"
    echo "  订阅链接 : http://<公网IP>/sub?token=<你的token>"
fi
echo "  日志查看 : journalctl -u clashserver -f"
echo "  数据备份 : $REMOTE_DIR/Data  （请定期备份）"
echo "=============================================="
