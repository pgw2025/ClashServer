# 阶段 2：管理端登录鉴权

> 决策：**管理端登录使用用户名+密码**（PBKDF2 加盐哈希存储），Cookie 会话。
> 核心理念：管理端登录与 `/sub` 的 accessToken **相互独立**；`/sub`、`/api/sub-health` 不并入登录。
> 版本：**v3**（v3 = 凭据源从 `accessToken` 改为用户名密码；`/sub` 的 token 语义与行为不变）。

---

## 2.1 认证中间件与登录端点（后端）

### 目标
接入 ASP.NET Core Cookie 认证，划定受保护范围，提供登录/登出/改密端点。

### 鉴权模型（v3，核心）
- **受保护范围仅限 `/api/*`**（白名单除外）；SPA 壳 `index.html` 与前端路由（`/login`、`/settings`、`/rules` 等）**保持公开**。
  - 理由：若服务端对 SPA 路由做 302 → LoginPath，会与 SPA fallback、前端守卫形成双重跳转且登录后无法回跳；正确做法是页面壳正常下发、API 返回 `401`，**登录跳转完全交给 Vue Router 守卫**（卷 05 §4.2），登录后按 `redirect` 参数回跳。
- 白名单（永不要求登录）：`/api/auth/login|logout|status`、`/api/sub-health`、`/sub`、静态资源。
- 鉴权仅 Vue 模式启用（`VueApp:Enabled=true`）；回退旧页面模式时 `/api/*` 同步放开（三联动开关见卷 02 §1.8）。
- **凭据源（v3）**：`Data/settings.json` 的 `username` + `passwordHash`。`accessToken` 仅保留给 `/sub` 订阅鉴权，管理端登录不再使用。
  - `passwordHash` 格式：`pbkdf2-sha256$<迭代次数>$<盐Base64>$<哈希Base64>`（SHA256、16 字节随机盐、210,000 次迭代）。
  - **引导机制**：`passwordHash` 为空且文件含一次性明文 `password` 字段时，首次读取自动哈希化并清除明文（幂等）。
  - 密码永不出现在 `/api/settings` 响应中；改密只能走专用端点。

### 具体任务
1. `Program.cs` 注册认证服务：
   - `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)`。
   - **CSRF 基础防线**：Cookie 属性 `HttpOnly` + `SameSite=Lax`（跨站 POST 不携带 Cookie；`Strict` 会影响外链跳回，默认 `Lax`）。
   - `SlidingExpiration`（30 分钟滑动），兼容长期打开的管理界面。
   - `Events.OnRedirectToLogin` 改写：`/api/*` 未登录一律返回 `401` JSON，**绝不 302**（SPA 路由不在保护范围，无需服务端跳转）。
2. 中间件顺序：`UseRouting` → `UseAuthentication` → `UseAuthorization`（现有 `Program.cs` 已有 `UseAuthorization`，补认证在前）。
3. **空凭据 fail-closed（红线）**：`username` 或 `passwordHash` 为空时，`/api/auth/login` 一律 `401` 拒绝（提示「服务器未配置管理员账号」）。空 accessToken 放行是 `/sub` 的既有语义，**不得带入管理端**。
4. **会话与凭据绑定**：登录成功时把凭据摘要（`SHA256($"{username}|{passwordHash}")`）写入 Claims；`Events.OnValidatePrincipal` 校验「principal 内凭据摘要 == 当前 settings 的凭据摘要」，不一致即吊销会话（改密/改名后旧 Cookie 自动失效，需重新登录）。
5. **CSRF 写操作防线**：在 `SameSite=Lax` 之上，`/api/*` 所有非 GET 请求要求携带自定义头 `X-Requested-With: fetch`（跨站表单无法伪造非简单头），缺失返回 `400`。前端由 api 模块统一注入（卷 05 §4.3），业务代码无感。
6. **登录端点加固（v3 落地）**：
   - 密码校验用 `CryptographicOperations.FixedTimeEquals`（防时序侧信道），用户名校验同用固定时间比较；
   - 失败文案统一为「登录失败：用户名或密码错误」，不区分具体原因（防用户名枚举）；
   - **简单限速（v3 实现）**：同一来源(IP)连续失败 5 次锁定 30 秒（`IMemoryCache`，失败计数 5 分钟滑动窗口），锁定期间返回 `429`。
7. 新增端点：
   | 端点 | 方法 | 说明 |
   |---|---|---|
   | `/api/auth/login` | POST | 收 `{ username, password }`，与 `settings.username/passwordHash` 比对：成功签发 Cookie（写入凭据摘要 Claim）；失败 `401` 统一文案；未配置账密 fail-closed |
   | `/api/auth/logout` | POST | 注销清除 Cookie |
   | `/api/auth/status` | GET | 返回当前登录态，供前端首屏判断 |
   | `/api/settings/password` | POST | 收 `{ currentPassword, newPassword }`，验证当前密码后更新哈希；**用新凭据摘要重签当前会话 Cookie（改密者不掉线）**，其他会话下一次请求 `401`；新密码长度 8-64 |
   - 后端用 `SignInAsync`/`SignOutAsync`，`HttpOnly` Cookie（前端读不到也无需读）。
   - `PUT /api/settings` 可改 `username`，但**永不触碰 `passwordHash`**（保留现有值，防止通用表单误清空密码）。

### 涉及文件
- `Program.cs`（无改动：`/sub` token 校验、`/api` 鉴权范围、三联动开关均保持原样）。
- `AuthSetup.cs`：`HashPassword`/`VerifyPassword`（PBKDF2）、`BuildPrincipal`（凭据摘要 Claim）、`OnValidatePrincipal`（凭据绑定）。
- `ApiEndpoints.cs`：登录端点重写 + 限速 + `/api/settings/password`；`PUT /api/settings` 透传 username、保留 passwordHash。
- `Services/StorageService.cs`：`GetSettingsAsync` 明文密码引导自动哈希化（幂等，锁内直接落盘防重入）。
- 复用：`IStorageService.GetSettingsAsync()` 读账密配置。

### 验收标准
- 未登录访问 `/api/rules` → `401`；登录后可正常访问。
- `/sub`、`/api/sub-health` 无需登录可访问（R6）。
- 未配置账密时登录被拒（fail-closed）；改密/改名后旧会话 `401`；写操作缺 `X-Requested-With` 头 → `400`；同来源连续失败 5 次 → 第 6 次 `429`。

---

## 2.2 前端登录接入（配合卷 05/06）

### 具体任务
1. Vue Router 加全局前置守卫：未登录重定向 `/login?redirect=<原路径>`（v2 补充回跳参数，避免登录后丢失深链接）。
2. 登录页改**用户名 + 密码**两个输入（v3），调 `POST /api/auth/login`；成功进入 `redirect` 指向路由（缺省为 `/`），失败展示后端统一文案。
3. 统一封装：api 模块对 `401` 响应统一跳登录并携带 `redirect`（联动卷 05 §4.3）。
4. 设置页（v3）：「访问 Token」改名「订阅 Token」（提示仅用于 `/sub`）；新增「管理员用户名」字段与「修改密码」卡片（当前密码/新密码/确认，调 `/api/settings/password`）。

### 验收标准
- 未登录刷新任意路由被导向登录；登录后可进入且刷新不掉线（Cookie 自动携带）。
- 改密成功提示「其他登录会话已下线」，当前会话保持在线。

---

## 2.3 鉴权测试

### 具体任务
1. 造三种状态覆盖：未登录、登录成功、密码错误。
2. 验证受保护端点与白名单端点边界无遗漏/无过度拦截。
3. **空凭据 fail-closed**：`settings.json` 无 `username/passwordHash` 时尝试登录 → 必须 `401`，绝不能免鉴权放行。
4. **改密失效**：登录 A、B 两会话 → A 改密 → B 会话下一次请求应 `401`；A 会话（重签 Cookie）应保持可用。
5. **CSRF 防线**：不带 `X-Requested-With` 头的 `POST /api/rules` → `400`；带头的正常请求成功。
6. **限速**：同来源连续错误密码 5 次 → 第 6 次 `429`。
7. **引导**：`settings.json` 写入明文 `password` 后首次启动 → 自动变 `passwordHash`，明文消失，登录成功。

### 验证命令（示例）
```powershell
# 未登录 → 401（Vue 模式）
curl.exe -s -o NUL -w "%{http_code}\n" http://localhost:5080/api/rules
# 登录（先配置 username/password）→ 拿到 Cookie
curl.exe -s -c cookies.txt -H "Content-Type: application/json" -d '{"username":"admin","password":"xxx"}' http://localhost:5080/api/auth/login
# 带 Cookie → 200
curl.exe -s -b cookies.txt -o NUL -w "%{http_code}\n" http://localhost:5080/api/rules
# 改密（带自定义头）→ 200
curl.exe -s -b cookies.txt -c cookies.txt -H "Content-Type: application/json" -H "X-Requested-With: fetch" -d '{"currentPassword":"xxx","newPassword":"newpass123"}' http://localhost:5080/api/settings/password
# /sub 无需登录（仅需订阅 token）
curl.exe -s -o NUL -w "%{http_code}\n" "http://localhost:5080/sub?token=<订阅token>"
# 写操作缺自定义头（模拟跨站表单）→ 400
curl.exe -s -b cookies.txt -o NUL -w "%{http_code}\n" -X POST -H "Content-Type: application/json" -d "{\"ruleType\":\"DOMAIN\",\"target\":\"a.com\",\"policy\":\"DIRECT\"}" http://localhost:5080/api/rules
```
> 初始账密来源：手动在 `Data/settings.json` 写入 `username` 与一次性明文 `password`（首次读取自动哈希化），或登录后在设置页「修改密码」。
