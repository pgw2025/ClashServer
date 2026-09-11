#!/usr/bin/env bash
# ============================================================
# ClashServer 云端部署脚本（Alibaba Cloud Linux 4，方案 A/B）
# 用法: sudo bash deploy-alinux4.sh [--domain 域名] [--self-contained] [--remote-dir /opt/clashserver]
# 前置: /tmp/clashserver.tar.gz 与 /tmp/deploy-alinux4.sh 已上传（由 publish-and-upload.ps1 完成）
# 特性: 幂等可重复执行；Data 数据自动保留；无证书时自动降级为 HTTP(80)
# ============================================================
set -euo pipefail

# ---------- 默认值（交互式提问时回车即采用） ----------
DEFAULT_DOMAIN=""
DEFAULT_SELF_CONTAINED=0
DEFAULT_CREATE_USER=0
DEFAULT_INSTALL_DOTNET=0
DEFAULT_REMOTE_DIR="/opt/clashserver"
DEFAULT_APP_USER="root"
DEFAULT_PORT="5080"
DEFAULT_BIND="127.0.0.1"
DEFAULT_DOTNET_BIN="/root/.dotnet/dotnet"
DEFAULT_HTTPS_PORT="2130"
DEFAULT_CERT="/etc/nginx/ssl/fullchain.crt"
DEFAULT_CERT_KEY="/etc/nginx/ssl/private.key"
DEFAULT_TARBALL="/tmp/clashserver.tar.gz"

# ---------- 解析参数（命令行优先，未给则在终端交互输入） ----------
DOMAIN="$DEFAULT_DOMAIN"
SELF_CONTAINED=$DEFAULT_SELF_CONTAINED
CREATE_USER=$DEFAULT_CREATE_USER
INSTALL_DOTNET=$DEFAULT_INSTALL_DOTNET
REMOTE_DIR="$DEFAULT_REMOTE_DIR"
APP_USER="$DEFAULT_APP_USER"
PORT="$DEFAULT_PORT"
BIND="$DEFAULT_BIND"
DOTNET_BIN="$DEFAULT_DOTNET_BIN"
HTTPS_PORT="$DEFAULT_HTTPS_PORT"
CERT="$DEFAULT_CERT"
KEY="$DEFAULT_CERT_KEY"
TARBALL="$DEFAULT_TARBALL"

while [ $# -gt 0 ]; do
    case "$1" in
        --domain) DOMAIN="$2"; shift 2 ;;
        --self-contained) SELF_CONTAINED=1; shift ;;
        --create-user) CREATE_USER=1; shift ;;
        --install-dotnet) INSTALL_DOTNET=1; shift ;;
        --remote-dir) REMOTE_DIR="$2"; shift 2 ;;
        --https-port) HTTPS_PORT="${2:-$DEFAULT_HTTPS_PORT}"; shift 2 ;;
        --run-as) APP_USER="$2"; shift 2 ;;
        --listen-port) PORT="${2:-$DEFAULT_PORT}"; shift 2 ;;
        --bind) BIND="${2:-$DEFAULT_BIND}"; shift 2 ;;
        --dotnet) DOTNET_BIN="${2:-$DEFAULT_DOTNET_BIN}"; shift 2 ;;
        *) echo "[错误] 未知参数: $1"; exit 1 ;;
    esac
done

# 交互式逐项输入：仅在标准输入为终端时启用（ssh 非交互调用自动跳过）
if [ -t 0 ]; then
    prompt() { # 用法: prompt "提示文字" "默认值" 变量名
        local _default="$2"
        local _var
        printf '  %s（默认: %s）: ' "$1" "$_default"
        IFS= read -r _var
        _var="${_var:-$_default}"
        printf -v "$3" '%s' "$_var"
    }
    echo ""
    echo "===== 请按提示依次输入（直接回车采用默认值）====="
    prompt "ECS 部署目录" "$REMOTE_DIR" REMOTE_DIR
    prompt "运行用户" "$APP_USER" APP_USER
    read -p "  是否创建运行用户? [y/N]（默认: 否）: " _ans
    case "$_ans" in
        [Yy]|[Yy][Ee][Ss]) CREATE_USER=1 ;;
        *) CREATE_USER=$DEFAULT_CREATE_USER ;;
    esac
    read -p "  是否安装 .NET 运行环境? [y/N]（默认: 否）: " _ans
    case "$_ans" in
        [Yy]|[Yy][Ee][Ss]) INSTALL_DOTNET=1 ;;
        *) INSTALL_DOTNET=$DEFAULT_INSTALL_DOTNET ;;
    esac
    prompt "应用监听端口" "$PORT" PORT
    prompt "回环地址" "$BIND" BIND
    prompt "HTTPS 端口" "$HTTPS_PORT" HTTPS_PORT
    prompt "域名（可空格跳过）" "$DOMAIN" DOMAIN
    read -p "  自包含发布? [y/N]（默认: n）: " _ans
    case "$_ans" in
        [Yy]|[Yy][Ee][Ss]) SELF_CONTAINED=1 ;;
        *) SELF_CONTAINED=$DEFAULT_SELF_CONTAINED ;;
    esac
    echo ""
fi

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
echo "  远程目录   : $REMOTE_DIR"
echo "  运行用户   : $APP_USER"
echo "  域名       : ${DOMAIN:-<未配置>}"
echo "  监听端口   : $BIND:$PORT"
echo "  HTTPS 端口 : $HTTPS_PORT"
if [ "$SELF_CONTAINED" -eq 1 ]; then
    echo "  运行方式   : 自包含（免装 .NET runtime）"
else
    echo "  运行方式   : 框架依赖（需 .NET 8 runtime，路径 $DOTNET_BIN）"
fi
echo "=============================================="

# ---- 1. 运行用户 ----
if [ "$CREATE_USER" -eq 1 ]; then
    if [ "$APP_USER" != "root" ] && ! id "$APP_USER" >/dev/null 2>&1; then
        echo "[步骤 1/6] 创建运行用户 $APP_USER"
        useradd -r -s /usr/sbin/nologin "$APP_USER"
    else
        echo "[步骤 1/6] 运行用户 $APP_USER 已存在，无需创建"
    fi
else
    echo "[步骤 1/6] 跳过创建用户（未开启）"
fi

# ---- 2. .NET 8 runtime（自包含模式不需要）----
# 无论是否安装，都必须解析出真实可用的 dotnet 路径，避免 systemd 203/EXEC
if [ "$SELF_CONTAINED" -eq 1 ]; then
    echo "[步骤 2/6] 自包含发布，无需 .NET runtime"
    DOTNET_BIN=""
else
    DOTNET_BIN_RESOLVED=""
    if [ -n "$DOTNET_BIN" ] && [ -x "$DOTNET_BIN" ] && "$DOTNET_BIN" --list-runtimes 2>/dev/null | grep -qi 'microsoft.aspnetcore.app 8\.'; then
        DOTNET_BIN_RESOLVED="$DOTNET_BIN"
    elif command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -qi 'microsoft.aspnetcore.app 8\.'; then
        DOTNET_BIN_RESOLVED="$(command -v dotnet)"
    fi
    if [ -n "$DOTNET_BIN_RESOLVED" ]; then
        DOTNET_BIN="$DOTNET_BIN_RESOLVED"
        echo "[步骤 2/6] .NET 8 运行时：$DOTNET_BIN"
    elif [ "$INSTALL_DOTNET" -eq 1 ]; then
        echo "[步骤 2/6] 未找到 .NET 8，开始安装到 $DOTNET_BIN（微软官方脚本，约 1 分钟）..."
        _dotnet_install_dir="$(dirname "$DOTNET_BIN")"
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
        bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir "$_dotnet_install_dir"
        "$DOTNET_BIN" --list-runtimes | grep -qi 'microsoft.aspnetcore.app 8\.' || { echo "[错误] .NET 8 安装失败"; exit 1; }
        echo "          .NET 8 运行时：$DOTNET_BIN"
    else
        echo "[错误] 未找到可用的 .NET 8 ASP.NET Core 运行时。"
        echo "       请在交互提示中选择「是否安装 .NET 运行环境: y」，或配置 --install-dotnet 后再试。"
        exit 1
    fi
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
    EXEC_START="$DOTNET_BIN $REMOTE_DIR/ClashServer.dll --urls http://$BIND:$PORT"
fi
cat > /etc/systemd/system/clashserver.service <<EOF
[Unit]
Description=ClashServer .NET Application
Documentation=https://docs.microsoft.com/dotnet/
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$REMOTE_DIR
ExecStart=$EXEC_START
Restart=always
RestartSec=10
SyslogIdentifier=clashserver

# 应用配置（生产必需）
Environment=VueApp__Enabled=true
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1

# 安全与权限
User=$APP_USER
Group=$APP_USER
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
HAS_CERT=0
[ -f "$CERT" ] && [ -f "$KEY" ] && HAS_CERT=1
SERVER_NAME="${DOMAIN:-_}"
PROXY_BLOCK="    location /sub {
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
    }"

if [ "$HAS_CERT" -eq 1 ]; then
cat > /etc/nginx/conf.d/clashserver.conf <<EOF
server {
    listen 80;
    server_name $SERVER_NAME;
    return 301 https://\$host:${HTTPS_PORT}\$request_uri;
}
server {
    listen $HTTPS_PORT ssl;
    listen [::]:$HTTPS_PORT ssl;
    server_name $SERVER_NAME;
    charset utf-8;

    ssl_certificate $CERT;
    ssl_certificate_key $KEY;

    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    client_max_body_size 10m;
$PROXY_BLOCK
}
EOF
    echo "          HTTPS 已启用（$HTTPS_PORT，证书: $CERT）"
else
cat > /etc/nginx/conf.d/clashserver.conf <<EOF
server {
    listen 80;
    server_name $SERVER_NAME;
    client_max_body_size 10m;
$PROXY_BLOCK
}
EOF
    echo "          [提示] 未检测到证书，当前仅 HTTP(:80)。"
    echo "                 申请证书放入 /etc/nginx/ssl/（fullchain.crt、private.key）后重跑本脚本即可启用 HTTPS:$HTTPS_PORT。"
fi

# ---- 6. 防火墙 + 启动 + 验证 ----
echo "[步骤 6/6] 防火墙与启动"
if systemctl is-active --quiet firewalld; then
    firewall-cmd --permanent --add-service=http >/dev/null 2>&1 || true
    firewall-cmd --permanent --add-port=$HTTPS_PORT/tcp >/dev/null 2>&1 || true
    firewall-cmd --reload >/dev/null 2>&1 || true
    echo "          firewalld 已放行 80 与 $HTTPS_PORT"
else
    echo "          [提示] firewalld 未运行，请确认阿里云安全组已放行 80 与 $HTTPS_PORT"
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
    echo "  管理页面 : https://${DOMAIN:-<公网IP>}:$HTTPS_PORT/"
    echo "  订阅链接 : https://${DOMAIN:-<公网IP>}:$HTTPS_PORT/sub?token=<你的token>"
else
    echo "  管理页面 : http://<公网IP>/   （建议尽快配置 HTTPS 证书）"
    echo "  订阅链接 : http://<公网IP>/sub?token=<你的token>"
fi
echo "  日志查看 : journalctl -u clashserver -f"
echo "  数据备份 : $REMOTE_DIR/Data  （请定期备份）"
echo "=============================================="
