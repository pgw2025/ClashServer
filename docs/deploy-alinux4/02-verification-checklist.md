# 部署验证清单与故障排查（阿里云 OS4）

## 1. 部署完成验证清单

按顺序执行，全部通过即部署成功。

### 1.1 服务与进程

| # | 检查项 | 命令 | 期望结果 |
|---|--------|------|----------|
| 1 | 服务状态 | `sudo systemctl status clashserver` | `active (running)`，无 `status=1/FAILURE` |
| 2 | 开机自启 | `sudo systemctl is-enabled clashserver` | `enabled` |
| 3 | Nginx 状态 | `sudo systemctl status nginx` | `active (running)` |
| 4 | 本机探测 | `curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5080/` | `200` |

### 1.2 核心端点（先本机、后公网）

| # | 检查项 | 命令 | 期望结果 |
|---|--------|------|----------|
| 5 | Vue 首页 | `curl -s http://127.0.0.1:5080/ \| head -5` | 返回 HTML（含 `<div id="app">` 或构建产物特征） |
| 6 | 订阅端点（带 token） | `curl -s "http://127.0.0.1:5080/sub?token=<token>" \| head -30` | 返回 Clash YAML：`proxies:`、`rules:` 等段落 |
| 7 | 订阅无 token（应拒） | `curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5080/sub` | `401`（设置了 access token 时） |
| 8 | 管理 API 鉴权 | `curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5080/api/rules` | `401`（Vue 模式未登录） |
| 9 | 公网 HTTPS 首页 | 浏览器或 `curl -s -o /dev/null -w "%{http_code}\n" https://your.domain.com/` | `200` |
| 10 | 公网 HTTPS 订阅 | `curl -s "https://your.domain.com/sub?token=<token>" \| head -30` | 返回 YAML |
| 11 | HTTP 跳转 | `curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://your.domain.com/sub?token=x` | `301` 到 https |
| 12 | 管理页面登录 | 浏览器打开 `https://your.domain.com` → 登录页 → 输入用户名密码 | 进入仪表盘，规则/节点/设置页可用 |
| 13 | 首次配置 | 设置页填上游 URL + token 并保存 | 保存成功；`/opt/clashserver/Data/settings.json` 生成 |
| 14 | 上游健康检查 | `curl -s -o /dev/null -w "%{http_code}\n" https://your.domain.com/api/sub-health` | `200`（上游可达）或 `503`（上游不可达，属正常降级） |

### 1.3 容灾与行为验证

| # | 检查项 | 操作 | 期望结果 |
|---|--------|------|----------|
| 15 | 崩溃自愈 | `sudo kill -9 $(pgrep -f ClashServer)` | 5 秒内 systemd 自动拉起，服务回到 `active` |
| 16 | 重启自启 | `sudo reboot`，等 1~2 分钟后登录 | `clashserver`、`nginx` 均自动运行 |
| 17 | 订阅超时降级 | 临时把上游 URL 改成不可达地址 → 请求 `/sub` | **10 秒内**返回 200 + 旧 YAML（响应头 `X-Cache: stale`） |
| 18 | 数据持久化 | 修改几条规则 → 重启服务 | 规则仍在（`Data/rules.json` 生效） |

## 2. 常见故障排查

| 现象 | 可能原因 | 排查/解决 |
|------|----------|-----------|
| `systemctl status` 显示失败 | 端口占用 / 启动参数错 / Data 目录无权限 | `journalctl -u clashserver -n 100 --no-pager` 看报错；确认 `--urls` 端口没被占；`ls -l /opt/clashserver/Data` 检查属主 |
| 页面 502 Bad Gateway | Kestrel 没起来或监听地址不对 | 本机 `curl http://127.0.0.1:5080/`；确认 Nginx `proxy_pass` 端口与 systemd `--urls` 一致 |
| 首页是旧的 Razor 页面（非 Vue 界面） | `VueApp__Enabled` 未生效 | 确认 systemd 里 `Environment=VueApp__Enabled=true`；`systemctl daemon-reload && restart` |
| 登录后 `/api/*` 全部 401 | Vue 模式下未登录/登录态丢失 | 确认用管理账号登录；检查浏览器 Cookie（`SameSite=Lax`，同源 https 正常） |
| `/sub` 返回 `Unauthorized` | token 不对 | 用设置页里配置的 access token；URL 编码特殊字符 |
| `/sub` 一直返回 stale 旧数据 | 上游不可达（国内 ECS 直连海外订阅源失败） | 本机/服务器 `curl -I <上游URL>` 测试；见 00 号文档"网络可达性"对策 |
| 管理页"节点延迟"全红/超时 | ECS 出方向 ICMP 或目标节点不响应 | 不影响订阅功能，属预期；延迟测试仅管理页展示用 |
| HTTPS 证书不生效 | 域名未解析 / 证书路径错 | `curl -v https://your.domain.com` 看握手；`nginx -t`；确认 DNS A 记录指向 ECS 公网 IP |
| 80 端口从公网不通 | 安全组未放行 | 阿里云控制台 ECS 安全组入方向放行 80/443；确认实例内 firewalld 未拦截 |

## 3. 数据备份建议

`Data/` 是唯一需要保护的数据（规则 + 设置），体积小，用 cron 每天打包即可：

```bash
sudo mkdir -p /opt/backup
# 每天 02:30 备份
(crontab -l 2>/dev/null; echo "30 2 * * * tar czf /opt/backup/clashserver-data-\$(date +\%F).tar.gz -C /opt/clashserver Data") | crontab -
```

恢复：解包后 `chown -R clashsvc:clashsvc /opt/clashserver/Data && systemctl restart clashserver`。
