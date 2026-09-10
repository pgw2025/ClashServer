# 阶段 6：构建/发布一体化

> 目标：把「前端构建 → 产物进后端 → 后端发布」串成单命令，并与开发期代理并存。
> 版本：v2。

---

## 6.1 后端托管 Vue 产物（落地）

### 具体任务
1. 让 Vite 输出目录指向后端可直接托管的位置。两种做法选一：
   - **(推荐)** 保留 `web/dist` 构建，再用脚本/`msbuild` 目标拷到后端 `wwwroot/`；或
   - 直接把 `web.vite.config.ts` 的 `build.outDir` 设为 `../wwwroot`（注意要与后端 `.gitignore` / 发布策略配合，防止 `bin/obj` 混入）。
2. 确保 `wwwroot/` 随 `dotnet publish` 输出（`Microsoft.NET.Sdk.Web` 默认包含 `wwwroot`）。
3. 确认 `index.html` 引用相对路径 `./assets/...`（以免部署子路径时资源 404）。

### 涉及文件
- 后端 `.csproj`（如需要加 `Content`/清理旧产物目标）。
- `web/vite.config.ts`（`outDir`、`base`）。
- 后端 `.gitignore`（若直接输出到 `wwwroot`，需排除旧产物）。

### 验收标准
- 重新构建前端后，后端启动即托管最新产物，`/` 与 `/assets/*` 可访问。

---

## 6.2 一键发布脚本/命令

### 具体任务
1. 提供一条发布命令（`package.json` 加 `build`+`publish` 脚本；后端可加 `dotnet publish` 汇总说明）。
2. 脚本流水线：
   ```bash
   # 1) 前端构建
   cd web && npm ci && npm run build && cd ..
   # 2) 清空/更新 wwwroot
   # 3) 后端发布
   dotnet publish -c Release -o ./publish
   # 4) 运行
   cd publish && dotnet ClashServer.dll
   ```
3. 配置切换：发布时 `VueApp:Enabled=true`（后端启用 fallback 托管），如需保留旧页面模式可置 `false`；该开关为**三联动**（Razor 映射 / SPA fallback / `/api/*` 鉴权，见卷 02 §1.8）。

### 验收标准
- 上述命令一次通过；发布目录运行后 SPA + API + `/sub` 全可用。

---

## 6.3 开发期代理

### 具体任务
1. `web/vite.config.ts` 加 server.proxy：`/api` → `http://localhost:5080`（后端开发地址）。
2. 开发期 Cookie 说明（v2 简化）：所有 `/api` 请求经 Vite 代理转发，浏览器视角始终同源（`localhost:5173` → 代理 → `localhost:5080`），后端 Set-Cookie 正常生效、无 CORS/跨站问题；仅当 Cookie 带 `Secure`（HTTPS-only）而开发环境是 HTTP 导致丢 Cookie 时，在开发配置关闭 `Secure`。
3. Vite 仅代理 `/api`，`.html`/路由由后端托管或由 Vite 直出均可（联调以代理为准）。

### 验收标准
- `npm run dev` 下登录/接口调用全部走通，浏览器无 CORS 报错，登录 Cookie 生效。