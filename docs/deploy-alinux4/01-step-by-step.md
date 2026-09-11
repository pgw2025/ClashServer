# 阿里云 OS4 部署完整操作步骤（不修改代码）

> 目标拓扑：
> ```
> Clash 客户端 ──HTTPS──> 阿里云 ECS (Alibaba Cloud Linux 4)
>                           └─ Nginx :80/:443 ──HTTP──> Kestrel :127.0.0.1:5080
>                                                         ├─ wwwroot/   (Vue 产物，后端托管)
>                                                         └─ Data/      (JSON 数据)
> ```

## 前置条件

- 阿里云 ECS 一台，系统镜像 **Alibaba Cloud Linux 4 LTS 64 位**，公网 IP，安全组已放行 22。
- 一个已解析到该 IP 的域名（HTTPS 需要；没有域名也可纯 HTTP 跑通，但不推荐）。
- 开发机：Windows + .NET 8 SDK + Node.js 18+（构建用）。
- 准备：上游订阅 URL、`/sub` 的 access token、管理端用户名密码。

---

## Step 1 本地构建发布（Windows 开发机，不改任何源码）

```powershell
# 在项目根目录 d:\CSharp\ClashServer 下执行

# ① 前端构建（产物输出到 server/wwwroot）
cd web
npm ci
npm run build

# ② 后端发布（方案 A：框架依赖，发布物小）
cd ..\server
dotnet publish -c Release -o ..\publish-linux

# （方案 B 备选：自包含 linux-x64 单文件，云上免装 runtime）
# dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true -o ..\publish-linux
```

验证点：

- `publish-linux/` 下存在 `ClashServer.dll`（方案 B 为 `ClashServer` 可执行文件）、`wwwroot/index.html`、`appsettings.json`。
- `wwwroot/assets` 下是构建产物（说明 Vue 构建已打进发布）。

## Step 2 上传到 ECS

```powershell
# 含 Data 数据（若开发机 server/Data 下已有配置）
scp -r .\publish-linux\* root@<公网IP>:/tmp/clashserver/
scp -r .\server\Data  root@<公网IP>:/tmp/clashserver/
```

或打包后单文件传输：

```powershell
Compress-Archive -Path .\publish-linux\* -DestinationPath .\clashserver.zip
scp .\clashserver.zip root@<公网IP>:/tmp/
```

## Step 3 云上环境准备

以下命令在 ECS 上执行（`root` 或 sudo 用户）。

### 3.1 部署目录与运行用户

```bash
# 创建专用运行用户（无登录 shell）
sudo useradd -r -s /usr/sbin/nologin clashsvc || true

# 放置应用
sudo mkdir -p /opt/clashserver
sudo cp -r /tmp/clashserver/* /opt/clashserver/     # 或 unzip 后复制
sudo chown -R clashsvc:clashsvc /opt/clashserver
sudo chmod 750 /opt/clashserver
```

### 3.2 安装 .NET 8 ASP.NET Core Runtime（仅方案 A 需要）

Alibaba Cloud Linux 4 基于 Anolis OS 23（RHEL 9 兼容系）。用微软官方安装脚本最稳，不依赖 dnf 源：

```bash
# 方式一（推荐）：微软官方脚本装到 /usr/share/dotnet
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
sudo bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet

# 验证
dotnet --list-runtimes   # 应看到 Microsoft.AspNetCore.App 8.0.x
```

> 备选：若 `dnf search dotnet` / `dnf search aspnetcore` 在阿里云/龙蜥源里有包，可 `sudo dnf install -y aspnetcore-runtime-8.0` 更省事；没有就用上面的官方脚本，二者选一。

## Step 4 systemd 服务

创建 `/etc/systemd/system/clashserver.service`：

```ini
[Unit]
Description=ClashServer (ASP.NET Core 8)
After=network.target

[Service]
Type=simple
User=clashsvc
Group=clashsvc
WorkingDirectory=/opt/clashserver
# 方案 A（框架依赖）：
ExecStart=/usr/bin/dotnet /opt/clashserver/ClashServer.dll --urls http://127.0.0.1:5080
# 方案 B（自包含单文件）：
# ExecStart=/opt/clashserver/ClashServer --urls http://127.0.0.1:5080

# 生产开关：Vue 模式（托管 wwwroot + /api 全量鉴权）
Environment=VueApp__Enabled=true
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1
# 注意：不要设置 ASPNETCORE_HTTPS_PORT（避免 Kestrel 层 HTTPS 重定向与 Nginx 冲突）

Restart=always
RestartSec=5

# 安全加固
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true

[Install]
WantedBy=multi-user.target
```

启动并验证：

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now clashserver
sudo systemctl status clashserver          # active (running)
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5080/     # 期望 200
curl -s "http://127.0.0.1:5080/sub?token=<你的token>" | head -20    # 期望 YAML
journalctl -u clashserver -n 50 --no-pager   # 看启动日志
```

## Step 5 Nginx 反向代理 + HTTPS

### 5.1 安装 Nginx

```bash
sudo dnf install -y nginx
```

### 5.2 HTTPS 证书（二选一）

**方式一：阿里云数字证书管理服务（免费 DV 证书）**

1. 控制台申请免费证书 → 绑定你的域名 → 域名 DNS 添加 CNAME 验证 → 签发后下载 **Nginx** 格式证书。
2. 上传到服务器：

```bash
sudo mkdir -p /etc/nginx/certs
# 把 .pem / .key 放到 /etc/nginx/certs/，例如 fullchain.pem、privkey.pem
```

**方式二：Let's Encrypt（certbot）**

```bash
# 若仓库有 certbot 可直接装；没有则用 snap 或 pip 安装
sudo dnf install -y certbot python3-certbot-nginx
sudo certbot --nginx -d your.domain.com --redirect
```

### 5.3 Nginx 配置

创建 `/etc/nginx/conf.d/clashserver.conf`：

```nginx
# HTTP → HTTPS
server {
    listen 80;
    server_name your.domain.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name your.domain.com;

    ssl_certificate     /etc/nginx/certs/fullchain.pem;
    ssl_certificate_key /etc/nginx/certs/privkey.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;

    client_max_body_size 10m;

    # /sub 订阅端点：不记日志（token 在 URL 上，防泄露）
    location /sub {
        access_log off;
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }
}
```

> `/api/*`、Vue 静态资源、SPA fallback 全部由 Kestrel 处理，Nginx 无需区分，统一反代即可。

```bash
sudo nginx -t                    # 配置语法检查
sudo systemctl enable --now nginx
sudo systemctl reload nginx
```

## Step 6 安全组与防火墙

**阿里云控制台（必须做）：**

- ECS 安全组 → 入方向放行：`80`、`443`。
- 建议：`22` 改为"指定源 IP"（你的办公网 IP），或至少使用密钥登录。

**实例内 firewalld（可选，若启用）：**

```bash
sudo systemctl enable --now firewalld
sudo firewall-cmd --permanent --add-service={http,https}
sudo firewall-cmd --reload
```

## Step 7 数据迁移与首次配置

- 已在 Step 2 上传 `server/Data`（`rules.json`、`settings.json`）→ 检查 `/opt/clashserver/Data/` 归属是否为 `clashsvc`，可写：

```bash
sudo chown -R clashsvc:clashsvc /opt/clashserver/Data
ls -l /opt/clashserver/Data/
```

- 若没有历史数据：登录管理页面后到 **设置** 页填写上游订阅 URL、access token、缓存时长并保存，`Data/settings.json` 会自动生成。

---

## 一键发布脚本（推荐）

两份脚本已随文档提供（`docs/deploy-alinux4/scripts/`）：

| 脚本 | 运行位置 | 作用 |
|------|----------|------|
| `publish-and-upload.ps1` | Windows 开发机 | 构建前端 + 发布后端 + 打包 + scp 上传 + SSH 触发云端部署 |
| `deploy-alinux4.sh` | 云端（由上面自动调用） | 装 runtime / 建用户 / 解包 / systemd / Nginx / 防火墙 / 自检，幂等可重复 |

**一次性准备**：

1. 本机到 ECS 配好 SSH 免密：`ssh-keygen` 生成密钥，将公钥加入服务器 `~/.ssh/authorized_keys`。
2. HTTPS 证书（可选）：把 `.pem/.key` 放到服务器 `/etc/nginx/certs/`（文件名 `fullchain.pem`、`privkey.pem`）。有证书则自动启用 HTTPS，没有则自动降级为 HTTP 并提示。
3. 阿里云控制台安全组入方向放行 80/443。

**一条命令完成部署**（在项目根目录 PowerShell 执行，IP 换成你的 ECS 公网 IP）：

```powershell
./docs/deploy-alinux4/scripts/publish-and-upload.ps1 -ServerIp 47.100.xx.xx -Domain your.domain.com
```

常用变体：

```powershell
# 未绑定域名 / 暂无证书：先纯 HTTP 跑通（推荐事后补证书）
./docs/deploy-alinux4/scripts/publish-and-upload.ps1 -ServerIp 47.100.xx.xx

# 只重新构建打包，不上传（本地检查产物）
./docs/deploy-alinux4/scripts/publish-and-upload.ps1 -ServerIp 47.100.xx.xx -SkipDeploy

# 只上传部署已打好的包，不重新构建
./docs/deploy-alinux4/scripts/publish-and-upload.ps1 -ServerIp 47.100.xx.xx -SkipBuild
```

> 说明：脚本默认方案 A（框架依赖发布）。想切换方案 B（自包含、云上免装 .NET），加 `-SelfContained` 参数即可，其余不变。

---

## 常用运维命令速查

```bash
sudo systemctl restart clashserver          # 重启应用
sudo systemctl status clashserver           # 状态
journalctl -u clashserver -f                # 跟踪日志
journalctl -u clashserver --since "1 hour ago" | grep -i error
sudo systemctl reload nginx                 # 重载 Nginx
sudo certbot renew --dry-run                # 证书续期演练（certbot 方式）
```
