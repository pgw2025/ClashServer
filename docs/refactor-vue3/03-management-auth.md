# 阶段 2：管理端登录鉴权

> 决策：**新增管理端登录**，复用现有 `accessToken` 作凭据，Cookie 会话。
> 核心理念：管理端登录与 `/sub` 的 accessToken **相互独立**；`/sub`、`/api/sub-health` 不并入登录。
> 版本：v2。

---

## 2.1 认证中间件与登录端点（后端）

### 目标
接入 ASP.NET Core Cookie 认证，划定受保护范围，提供登录/登出端点。

### 鉴权模型（v2 修订，核心）
- **受保护范围仅限 `/api/*`**（白名单除外）；SPA 壳 `index.html` 与前端路由（`/login`、`/settings`、`/rules` 等）**保持公开**。
  - 理由：若服务端对 SPA 路由做 302 → LoginPath，会与 SPA fallback、前端守卫形成双重跳转且登录后无法回跳；正确做法是页面壳正常下发、API 返回 `401`，**登录跳转完全交给 Vue Router 守卫**（卷 05 §4.2），登录后按 `redirect` 参数回跳。
- 白名单（永不要求登录）：`/api/auth/login|logout|status`、`/api/sub-health`、`/sub`、静态资源。
- 鉴权仅 Vue 模式启用（`VueApp:Enabled=true`）；回退旧页面模式时 `/api/*` 同步放开（三联动开关见卷 02 §1.8）。

### 具体任务
1. `Program.cs` 注册认证服务：
   - `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)`。
   - **CSRF 基础防线**：Cookie 属性 `HttpOnly` + `SameSite=Lax`（跨站 POST 不携带 Cookie；`Strict` 会影响外链跳回，默认 `Lax`）。
   - `SlidingExpiration`（建议 30 分钟滑动），兼容长期打开的管理界面。
   - `Events.OnRedirectToLogin` 改写：`/api/*` 未登录一律返回 `401` JSON，**绝不 302**（SPA 路由不在保护范围，无需服务端跳转）。
2. 中间件顺序：`UseRouting` → `UseAuthentication` → `UseAuthorization`（现有 `Program.cs` 已有 `UseAuthorization`，补认证在前）。
3. **空 Token fail-closed（红线）**：`settings.AccessToken` 为空/空白时，`/api/auth/login` 一律 `401` 拒绝（提示「未配置 accessToken，请先在服务器 Data/settings.json 配置」）。空 Token 放行是 `/sub` 的既有语义，**不得带入管理端**。
4. **会话与 Token 绑定**：登录成功时把当前 accessToken 的摘要（如 SHA-256）写入 Claims；`Events.OnValidatePrincipal` 校验「principal 内 Token 摘要 == 当前 settings 的 Token 摘要」，不一致即吊销会话（用户重新生成/修改 Token 后，旧 Cookie 自动失效，需重新登录）。
5. **CSRF 写操作防线**：在 `SameSite=Lax` 之上，`/api/*` 所有非 GET 请求要求携带自定义头 `X-Requested-With: fetch`（跨站表单无法伪造非简单头），缺失返回 `400`。前端由 api 模块统一注入（卷 05 §4.3），业务代码无感。
6. **登录端点加固**：
   - Token 比较使用 `CryptographicOperations.FixedTimeEquals`（防时序侧信道）；
   - 失败文案统一为「登录失败：Token 无效」，不区分具体原因；
   - 简单限速：同一来源连续失败 5 次锁定 30 秒。
7. 新增端点：
   | 端点 | 方法 | 说明 |
   |---|---|---|
   | `/api/auth/login` | POST | 收 `{ accessToken }`，与 `settings.AccessToken` 比对：成功签发 Cookie（写入 Token 摘要 Claim）；失败 `401 + 登录失败`；空 Token 配置 fail-closed |
   | `/api/auth/logout` | POST | 注销清除 Cookie |
   | `/api/auth/status` | GET | 返回当前登录态，供前端首屏判断 |
   - 后端用 `SignInAsync`/`SignOutAsync`，`HttpOnly` Cookie（前端读不到也无需读）。

### 涉及文件
- `Program.cs`（新增）。
- 复用：`IStorageService.GetSettingsAsync()` 读 accessToken。

### 验收标准
- 未登录访问 `/api/rules` → `401`；登录后可正常访问。
- `/sub`、`/api/sub-health` 无需登录可访问（R6）。
- 空 Token 配置时登录被拒（fail-closed）；Token 轮换后旧会话 `401`；写操作缺 `X-Requested-With` 头 → `400`（v2 新增）。

---

## 2.2 前端登录接入（配合卷 05/06）

### 具体任务
1. Vue Router 加全局前置守卫：未登录重定向 `/login?redirect=<原路径>`（v2 补充回跳参数，避免登录后丢失深链接）。
2. 登录页调 `POST /api/auth/login`；成功进入 `redirect` 指向路由（缺省为 `/`），失败展示后端统一文案。
3. 统一封装：api 模块对 `401` 响应统一跳登录并携带 `redirect`（联动卷 05 §4.3）。

### 验收标准
- 未登录刷新任意路由被导向登录；登录后可进入且刷新不掉线（Cookie 自动携带）。

---

## 2.3 鉴权测试

### 具体任务
1. 造三种状态覆盖：未登录、登录成功、Token 错误。
2. 验证受保护端点与白名单端点边界无遗漏/无过度拦截。
3. **空 Token fail-closed**：把 `settings.accessToken` 置空后尝试登录 → 必须 `401`，绝不能免鉴权放行。
4. **Token 轮换失效**：登录 A 会话 → 修改 accessToken → A 会话下一次请求应 `401`。
5. **CSRF 防线**：不带 `X-Requested-With` 头的 `POST /api/rules` → `400`；带头的正常请求成功。

### 验证命令（示例）
```powershell
# 未登录 → 401
curl.exe -s -o NUL -w "%{http_code}\n" http://localhost:5080/api/rules
# 登录（先取有效 accessToken）→ 拿到 Cookie
curl.exe -s -c cookies.txt -H "Content-Type: application/json" -d '{"accessToken":"xxx"}' http://localhost:5080/api/auth/login
# 带 Cookie → 200
curl.exe -s -b cookies.txt -o NUL -w "%{http_code}\n" http://localhost:5080/api/rules
# /sub 无需登录
curl.exe -s -o NUL -w "%{http_code}\n" http://localhost:5080/sub
# 写操作缺自定义头（模拟跨站表单）→ 400
curl.exe -s -b cookies.txt -o NUL -w "%{http_code}\n" -X POST -H "Content-Type: application/json" -d "{\"ruleType\":\"DOMAIN\",\"target\":\"a.com\",\"policy\":\"DIRECT\"}" http://localhost:5080/api/rules
# 写操作带自定义头 → 200
curl.exe -s -b cookies.txt -o NUL -w "%{http_code}\n" -X POST -H "X-Requested-With: fetch" -H "Content-Type: application/json" -d "{\"ruleType\":\"DOMAIN\",\"target\":\"a.com\",\"policy\":\"DIRECT\"}" http://localhost:5080/api/rules
```
> `accessToken` 来源：`Data/settings.json` 或 `GET /api/settings`（开发期可临时放开）。