# 阶段 1：后端 REST API 化

> 对象：现有 5 个页面代码-behind 的能力，改写为 `/api/*` JSON 端点。
> 总原则：**只新增/改造 API 与 DTO 层，业务服务层（`IClashSubService`/`IStorageService`/`BackgroundRefreshService`）原样复用**。
> 版本：v2。

---

## 1.1 建立 REST 分层与 DTO 映射

### 目标
给后端引入「过滤层」：Controller/Endpoint 只做「校验 → 调服务 → 映射 DTO → 返回」。

### 具体任务
1. 新增 DTO 定义（对应卷 01 §0.3）：`DashboardDto`、`RuleDto`、`SettingsDto`、`NodeDto`、`ConfigDto`。
2. 为 `CustomRule`/`AppSettings`/`ProxyNode` 各写一个 `ToDto` / 反序列化用映射（保持 JSON 字段 camelCase）。
3. 定义统一响应助手（`ok/error`、`lastGoodUpdate`/`isStale` 元数据）。
4. 确定实现形态：**优先用 minimal API（`Program.cs` 的 `Map*` 延续现有风格）**，或拆成单独 `Controllers/`（MVC）——二选一，全项目统一。
5. **API 专用 JSON 异常处理（v2）**：现有 `UseExceptionHandler("/Error")` 依赖 Razor 错误页，Vue 模式下不可用；为 `/api/*` 增加异常处理中间件/过滤器，未捕获异常统一返回 `{ ok:false, error }` JSON（可参考 ProblemDetails 风格），绝不返回 HTML。

### 涉及文件 / 新增
- `Models/DTOs/*.cs`（新增）
- `Program.cs` 或 `Controllers/*.cs`（新增）

### 验收标准
- `dotnet build` 通过；无页面依赖 DTO 直接读写服务。
- 每种 DTO 有来源模型的转换说明。

### 验证命令
```powershell
dotnet build -c Debug
```

---

## 1.2 规则管理 API

> 对应原 `Pages/Rules/Index.cshtml.cs` 的全部操作。

### 具体任务
| 端点 | 方法 | 说明 / 源自原方法 |
|---|---|---|
| `/api/rules` | GET | 规则列表 + 附带 `ProxyGroups`、`stale` 元数据（源自 `OnGet`） |
| `/api/rules` | POST | 新增规则（源自 `OnPostUpsert`，`Id==Empty` 分支） |
| `/api/rules/{id}` | PUT | 更新规则（源自 `OnPostUpsert` 编辑分支） |
| `/api/rules/{id}` | DELETE | 删除规则（源自 `OnPostDelete`） |
| `/api/rules/{id}/toggle` | POST | 启停（源自 `OnPostToggle`） |
| `/api/rules/{id}/move-up` | POST | 上移（源自 `OnPostMoveUp`） |
| `/api/rules/{id}/move-down` | POST | 下移（源自 `OnPostMoveDown`） |
| `/api/rules/batch` | POST | 批量改策略 / 批量删除（源自 `OnPostBatchPolicy`/`OnPostBatchDelete`） |
| `/api/rules/clear` | POST | 清空所有规则（源自 `OnPostClearAll`） |

### 通用注意事项（全部写操作共性）
1. 每个写操作成功后，必须调用 `_subService.ClearCache()`（与现状一致，避免缓存不失效）。
2. 写操作前用服务端校验（沿用模型 DataAnnotations：规则字段 `Required`；设置字段 `Url`/`Range`/`StringLength`，`Url` 只属于设置、不适用于规则），错误返回 `400` + 明确消息。
3. 所有写操作返回 `{ ok: true }` 或带变更实体的 `{ ok: true, data }`；**不要做页面跳转/重定向**。

### 涉及文件
- `Program.cs`（新增路由）或新增规则端点。
- 复用：`IStorageService.GetRulesAsync/SaveRulesAsync`、`ClashSubService.GetCachedGroups/GetProxyGroupsAsync/ClearCache`。

### 验收标准
- 用 `curl` 覆盖上述每个端点，增删改/启停/移动/批量各自验证一次。
- 探针：每次写操作后再次 `GET /api/rules` 确认变更已生效（说明缓存已清）。

---

## 1.3 节点与概览 API

> 对应原 `Pages/Index.cshtml.cs`（仪表盘）与既有 `/api/nodes`、`/api/groups`、`/api/sub-health`。

### 具体任务
1. `GET /api/nodes`（已有）— 保持；响应补 `lastGoodUpdate`/`isStale` 元数据。
2. `GET /api/groups`（已有）— 保持；同上补充元数据。
3. **新增** `POST /api/nodes/latency` — 单节点测速（**v2 修订**：节点名常含中文/空格，可能含斜杠等特殊字符，不能作 URL 路径参数，改为 body/query 传 `name`；源自 `OnGetTestLatencyAsync`；内部 `TestNodeLatencyAsync` 服务端 ICMP，单请求可到 5s，前端需异步）。
4. **新增** `GET /api/sub-health` — 已存在，保留即可。
5. **新增** `GET /api/dashboard` — 一次返回概览足够仪表盘首屏：订阅URL、启用规则数、节点缓存、`lastGoodUpdate`、`isStale`（源自 `OnGet` 组装）。

### 关键设计（R5）
- `/api/nodes`、`/api/groups`、`/api/dashboard` 均带 `lastGoodUpdate` + `isStale`，前端据此渲染 stale 警告，无需前端自己猜。
- `isStale` 由后端共享 helper 统一计算（口径见卷 01 §0.3），修复现状三页面口径不一致导致的横幅时有时无。

### 涉及文件
- `Program.cs` + 新增端点。

### 验收标准
- 上游不可达时 `/api/dashboard`、`/api/nodes` 在 **5 秒内**带 stale 元数据返回（R1/R5）。
- 节点测速端点可正确返回 `ok/latency/error`。

---

## 1.4 设置 API

> 对应原 `Pages/Settings.cshtml.cs`。

### 具体任务
| 端点 | 方法 | 说明 |
|---|---|---|
| `/api/settings` | GET | 读设置（源自 `OnGet`） |
| `/api/settings` | PUT | 保存设置 + 触发 `ClearCache`（源自 `OnPostSave`） |
| `/api/settings/generate-token` | POST | 生成新 Token，返回给前端填入（源自 `OnPostGenerateToken`），**不自动保存** |

### 注意
- `PUT /api/settings` 成功后必须 `ClearCache()`（订阅缓存刷新，与现状一致）。
- 若确认「新增管理端登录复用 accessToken」，保存设置时必须校验 accessToken 不能留空导致登录失效（联动卷 03）。此外，accessToken 一旦修改，既有登录会话应失效（会话绑定，见卷 03 §2.1）。

### 涉及文件
- `Program.cs` + 设置端点。

### 验收标准
- 保存后 `GET /api/settings` 返回新值；缓存被清（再次 `GET /api/dashboard` 触发一次新抓取）。
- `generate-token` 不落盘，仅返回。

---

## 1.5 配置查看/刷新 API

> 对应原 `Pages/Config.cshtml.cs`。

### 具体任务
| 端点 | 方法 | 说明 |
|---|---|---|
| `/api/config/raw` | GET | 原始上游 YAML + 行数/规则数统计（源自 `OnGet` 分支） |
| `/api/config/merged` | GET | 合并后 YAML + 统计（同上） |
| `/api/config/refresh` | POST | 强制刷新 raw+merged（源自 `OnPostRefresh`），成功返回 `{ ok }`，失败 `{ ok:false, error }` |

### 设计
- raw/merged 各自返回 `{ ok, yaml, lineCount, ruleCount, lastGoodUpdate, isStale }`。
- YAML 文本用 JSON 字符串承载；前端负责等宽展示（高亮可选，进阶）。

### 验收标准
- 两个只读端点在上游不可达时 5 秒内返回（R1）。
- `refresh` 幂等可重复调用，错误信息透出。

---

## 1.6 导入预览与执行 API

> 对应原 `Pages/Rules/Import.cshtml.cs`。解析逻辑必须留在后端（`ParseYamlRules` 复用于卷 05）。

### 具体任务
1. `POST /api/rules/parse` — 接收 YAML 文本/文件，解析并返回预览规则列表（源自 `OnPostPreview`/`OnPostUpload` 的解析部分）。
2. `POST /api/rules/import` — 接收解析好的规则（或原文重解析），批量插入 `rules.json` 并 `ClearCache()`（源自 `OnPostImport`）。
3. 上传语义：前端先以 `multipart/form-data` 传文件到 `parse`，得到预览后用户确认再 `import`。

### 涉及文件
- `Program.cs` + 解析/导入端点。
- 复用：`ClashSubService.ParseYamlRules`。

### 验收标准
- 粘贴 YAML 与上传 `.yaml/.txt` 两种入口均可返回预览；确认后规则数正确入库。
- 符合校验（`CustomRule.AvailableRuleTypes`/策略）的规则被保留，非法行被跳过并说明。

---

## 1.7 缓存刷新与健康 API（收尾）

### 具体任务
1. `POST /api/cache/refresh` — 仪表盘「立即刷新」（源自 `OnPostRefreshCache`；复用 `GetMergedSubAsync`+`GetProxyNodesAsync` 强制刷新路径，失败给明确错误）。
2. `GET /api/sub-health` — 确认保留（健康探针，可放于登录体系之外）。

### 验收标准
- 手动刷新后节点/订阅缓存更新；失败时返回可读错误。
- `sub-health` 保持无鉴权可探测（供负载探活，与 `/sub` 同理隔离于登录）。

---

## 1.8 旧 Pages 下线策略（本阶段收尾）

### 目标
API 化完成后，决定管理页面 Razor 如何处置，避免新旧并存混乱。

### 任务
1. 策略可选：**(A)** 保留 Pages 文件作为阶段回退锚点（推荐，改造期不删）；**(B)** 全部删除，由 Vue SPA 完全接管。
2. 若选 A：`VueApp:Enabled` 定义为**整体模式开关，三处联动（v2 修订）**：
   - `MapRazorPages()` 仅在 `false` 时启用；
   - SPA fallback（卷 04 §3.2）仅在 `true` 时启用——**两者绝不可同时开**（路由大小写不敏感，SPA 路由 `/settings` 会被 Razor 页面 `/Settings` 截获）；
   - `/api/*` 鉴权（卷 03）仅在 `true` 时启用——回退模式下旧页面内联 JS 直接调 `/api/nodes`、`/api/groups` 等公开端点，若鉴权仍生效，回退模式即损坏。
3. 清除与页面强耦合、已无调用方的 handler（确认无引用后删，避免死代码）；保留与 `/sub`/`/api/*` 相关逻辑。

### 验收标准
- 后端纯 API 模式与旧页面模式可切换，二选一稳定运行，`/sub` 不受影响。