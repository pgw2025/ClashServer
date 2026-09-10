# 阶段 3：静态托管与 SPA 回退

> 目标：让后端在**同一端口**直接托管 Vue 构建产物，实现单源同域、无 CORS。
> 决策：采用**后端托管 Vue 产物**（见卷 00 §2）。Vue 构建产物最终落进后端 `wwwroot/`。
> 版本：v2。

---

## 3.1 后端托管配置

### 具体任务
1. 确认工程已有 `UseStaticFiles()`（当前 `Program.cs` 已具备）。
2. `wwwroot/` 成为 Vue 产物目标目录；确保发布时含 `wwwroot/`（`csproj` 默认会附带）。
3. 前端构建产物命名约定：`index.html` + 带哈希的 `/assets/*`（由 Vite 默认输出，自然命中静态缓存）。

### 涉及文件
- `Program.cs`（校验/微调 `UseStaticFiles`）。
- Vue 构建配置（见卷 05/06）。

### 验收标准
- 访问 `http://host:port/` 直接命中 `wwwroot/index.html`；`/assets/*` 正常加载。
- 可直接打开 `http://host:port/` 完整渲染 SPA 首屏。

---

## 3.2 SPA 回退路由与 `/sub` 优先级

### 关键点
SPA 使用 `history` 路由（如 `/settings`、`/rules`），刷新时后端必须把**前端路由**回退到 `index.html`，但**不能拦截**真实路径 `/sub`、`/api/*` 和静态资源。

### 具体任务
1. 使用 `MapFallbackToFile("index.html")`。
2. **优先级必须保证**：`/sub`、`/api/*` 的真实端点先于 fallback 命中；`/assets/*` 静态文件先命中。
3. **fallback 显式排除 `/api` 与 `/sub` 前缀（v2 修订）**：`MapFallbackToFile` 是全兜底，会把打错的 `/api/xyz` 也吞成 `200 index.html`；必须自定义 fallback 策略，对 `/api`、`/sub` 前缀返回 `404`（JSON）——否则前端调错路径收到一段 HTML，排查极难。
4. **模式互斥红线（v2 新增）**：`MapRazorPages()` 与 SPA fallback **绝不可同时启用**——ASP.NET Core 路由大小写不敏感，SPA 路由 `/settings` 会被 Razor 页面 `/Settings` 截获，fallback 永远不命中。开关三联动（Razor / fallback / 鉴权）见卷 02 §1.8；迁移过渡期若确需新旧并存，SPA 必须挂独立前缀（如 `/app`），不允许裸路径并存。

### 涉及文件
- `Program.cs`（`MapRazorPages()` 被替换为 `MapFallbackToFile("index.html")` 的时机/开关，联动卷 02 §1.8 的 `VueApp:Enabled` 开关）。

### 验收标准
- 浏览器访问 `/settings`（无此后端物理路由）→ 返回 `index.html`，SPA 接管（Razor 未截获）。
- 访问 `/sub?token=...` → 仍返回 Clash YAML（未被 fallback 吞掉，R6）。
- 访问 `/api/rules` 未登录 → `401`（未被 fallback 吞掉）。
- 访问不存在的 `/api/xyz` → `404` JSON，而非 `200` 的 `index.html`。
- 未启用 `VueApp` 时旧页面模式仍可运行（可切换，三联动见卷 02 §1.8）。