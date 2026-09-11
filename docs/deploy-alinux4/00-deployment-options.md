# 阿里云 OS4（Alibaba Cloud Linux 4）部署方案对比与推荐

> 适用项目：ClashServer（ASP.NET Core 8 + Vue3 单端口托管）
> 约束：**不修改任何源码**，仅部署/运维层面操作
> 日期：2026-09-11

## 1. 项目形态回顾（决定部署方式的三个事实）

| 事实 | 影响 |
|------|------|
| 后端 ASP.NET Core 8（net8.0），`dotnet publish` 产出即运行，无原生依赖 | 可交叉发布 linux-x64，Windows 开发机即可完成构建 |
| 前端 Vue3 构建产物输出到 `server/wwwroot`，由后端 Kestrel 统一托管（`VueApp:Enabled=true` 时） | 生产只需**一个进程 + 一个端口**，无需单独部署静态站 |
| 数据为本地 JSON 文件（`server/Data/`），无数据库 | 部署时需把 `Data/` 一并迁移或首次页面配置；不支持多实例 |

## 2. 目标环境：Alibaba Cloud Linux 4

- 阿里云自研操作系统，2025 年 7 月发布，上游基础为 **Anolis OS 23**（不再兼容 CentOS），内核 6.6 LTS，包管理为 **dnf**。
- 注意存在两类镜像：默认 **RPM 版（Anolis 23 基础）** 与 **Deb Edition**（4.2404.x，基于 Debian/Ubuntu 系）。本文默认 RPM 版，Deb 版将 `dnf` 换成 `apt` 即可。
- 参考：Alibaba Cloud Linux 4 与 3 的差异说明、特性说明、4.x 版本说明。

## 3. 方案对比

| 方案 | 云上要装什么 | 优点 | 缺点 | 结论 |
|------|-------------|------|------|------|
| **A. 框架依赖发布 + systemd + Nginx + HTTPS** | .NET 8 runtime、Nginx | 运维标准；日志集中 `journalctl`；崩溃自愈、开机自启；HTTPS 灵活；发布物小 | 云上要先装 runtime | **推荐（长期运行）** |
| **B. 自包含单文件发布 + systemd + Nginx** | 仅 Nginx | 云上**零 .NET 依赖**，部署极简，绕开 dnf 源问题 | 发布物约 80~120MB，每次更新需重传 | 推荐（怕折腾环境） |
| **C. Docker / docker compose** | Docker | 环境一致、升级回滚方便、便于以后多服务 | 需新增 Dockerfile（不改源码但加文件）；多一层运维 | 可选进阶 |
| D. 云上源码编译（装 SDK + Node） | SDK + Node | 无需本地构建 | 重、慢、浪费 ECS 资源；污染环境 | 不推荐 |
| E. SAE / 函数计算 / 无服务器 | 无 | 免运维 | 本地 JSON 文件持久化别扭；个人项目成本/复杂度不值 | 不推荐 |

## 4. 推荐组合

**首选：方案 A（框架依赖 + systemd + Nginx + HTTPS）**，理由：

1. 项目本身就是"单进程 + 单端口"，systemd 托管天作之合：`Restart=always` 崩溃自动拉起，开机自启，日志统一进 journald。
2. TLS 终止放在 Nginx：Clash 客户端订阅走 **HTTPS** 才安全（`/sub?token=` 在 URL 上，明文 HTTP 会泄露 token，且易被运营商污染）。
3. Kestrel 只监听 `127.0.0.1:5080`，对外只暴露 80/443，攻击面最小。
4. 以后想加第二个 .NET 应用，runtime 是共享的。

**如果不想在云上折腾 .NET 安装源：直接选方案 B**（自包含发布），部署命令数量最少，完全不影响任何源码。

**进阶（强烈建议以后做）**：接 CI 自动构建发布（云效 Flow 或 GitHub Actions），本地 `git push` → 云上自动更新，彻底告别手动 scp。

## 5. 比"能跑起来"更重要的几个风险点

| 风险 | 说明 | 对策 |
|------|------|------|
| **上游订阅源网络可达性（最关键）** | Clash 订阅上游多为海外地址。若上游需要"科学上网"才能访问，**国内 ECS 直连会超时**。项目虽有 10s 超时 + stale 降级 + 指数退避，但持续不可达时订阅长期是旧数据 | 部署前先在本机确认上游 URL 是否可直连；若不可达，考虑：① 换一个国内可直连的订阅源；② 把服务部署到香港/新加坡轻量服务器（同样按本文操作）；③ 在 Nginx/网络层加代理出口（不依赖代码） |
| `UseHttpsRedirection` 行为 | `Program.cs` 生产模式启用了它，但**只要不配置 `https_port`/`ASPNETCORE_HTTPS_PORT`，它只记一条警告并跳过重定向**，HTTP 反代不受影响 | systemd 里**不要**设置 `ASPNETCORE_HTTPS_PORT`；Nginx 负责 80→443 跳转 |
| `VueApp:Enabled` 必须为 `true` | 为 `false` 时走旧 Razor 页面、`/api` 不鉴权 | 通过 systemd 环境变量 `VueApp__Enabled=true` 覆盖（或改发布目录里的 `appsettings.json`） |
| `server/Data/` 写权限 | 服务运行用户必须可读写 `Data/rules.json`、`Data/settings.json` | 目录 `chown` 给 systemd 里的运行用户；定期备份该目录 |
| `/sub` token 进 Nginx 日志 | `?token=` 在 URL 上，默认 access_log 会记录 | Nginx 对 `/sub` 单独 `access_log off` |
| 安全组与防火墙 | 阿里云安全组默认全关入方向 | 只放行 80/443；22 端口建议密钥登录 + 限 IP |

## 6. 结论

- **要 HTTPS、要省心运维** → 方案 A（本文 `01-step-by-step.md` 完整操作）。
- **不想装 .NET runtime** → 方案 B，步骤同 A，仅跳过 Step 3 的 runtime 安装（把构建命令换成自包含发布）。
- 无论哪种：**先确认上游订阅源能直连**，否则先解决网络问题再部署。
