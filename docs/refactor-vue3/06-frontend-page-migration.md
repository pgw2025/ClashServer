# 阶段 5：页面逐项迁移（5 页面 + 登录页）

> 目标：把现有 5 个 Razor 页面的交互，逐项迁移为 Vue3 视图。
> 每页迁移后独立验证；顺序建议从**简单 → 复杂**：设置 → 导入 → 仪表盘 → 配置 →（最重）规则管理。
> 版本：v2。

---

## 5.1 登录页 `views/Login.vue`

### 具体任务
- 表单：accessToken 输入 + 登录按钮 + 错误提示。
- 调 `POST /api/auth/login`；成功进入 `/`，失败展示后端 `error`。
- 交互兜底：提交防抖、回车提交。
- 对接：已在卷 03 §2.2、卷 05 §4.2 接入守卫（v2 修正引用）；登录成功后按 `redirect` 回跳。

### 验收
- 未登录访问任意页被导向此页；登录成功跳转且刷新不掉线。

---

## 5.2 仪表盘 `views/Dashboard.vue`（Index）

### 具体任务
1. 首屏：顶栏概览卡（订阅URL（含 accessToken 拼装）、启用规则数、节点数、`lastGoodUpdate`）。
2. `<StaleBanner>` 依据 `/api/dashboard` 的 `isStale` 显示。
3. 节点列表：节点卡片（名称/类型/服务器/端口），类型徽标按 `ProxyNode.TypeBadgeColor`、延迟颜色按 `LatencyColor` 前端换算 → 用 TAG/颜色样式复现。
4. 单节点测速：调 `POST /api/nodes/latency`（body 传 `name`，见卷 02 §1.3），**逐节点异步请求、独立 loading**，避免一次性阻塞（R7）。
5. **「全部测速」按钮（v2 补充，对齐现有页面 `testAllLatency()`）**：遍历节点测速，**前端限制并发 4–8 路**（分批/信号量），避免瞬间向后端发起大量 ICMP 请求。
6. **延时排序（v2 补充，对齐现有页面）**：支持按延时 低→高 / 高→低（对应现有 `latency-asc/latency-desc`），未测速节点排最后。
7. 「立即刷新」按钮：调 `POST /api/cache/refresh`，完成后重拉 dashboard/nodes。
8. 「测试上游」：调 `/api/sub-health`，结果用 ok 反馈。

### 涉及 API
`GET /api/dashboard`、`GET /api/nodes`、`POST /api/nodes/latency`、`POST /api/cache/refresh`、`GET /api/sub-health`。

### 验收
- 上游不可达时首屏 5 秒内带 stale 提示渲染（R1/R5）；节点卡片、单节点/全部测速（限并发）、延时排序、刷新交互完成。

---

## 5.3 规则管理 `views/Rules.vue`（最重）

### 具体任务
1. 列表：表格展示 id/类型/匹配内容/策略/启用/更新时间；表头可排序（类型/匹配/策略/时间），实时搜索过滤。
2. 增删改：新增/编辑共用表单组件（类型下拉用 `CustomRule.AvailableRuleTypes`，策略下拉用 `ProxyGroups` + `AvailablePolicies`）。
3. 行内操作：启用/禁用（toggle）、上移、下移、删除。
4. 批量：多选 → 批量改策略 / 批量删除；清空按钮带二次确认。
5. 写操作成功后重拉列表（后端已 `ClearCache`，列表即时反映）。
6. 每条写操作都走 store，loading/错误态统一。

### 涉及 API（卷 02 §1.2 全部）
`GET/POST/PUT/DELETE /api/rules`、toggle/move-up/move-down、batch、clear，以及 `GET /api/groups` 供策略下拉。

### 验收
- 增删改/启停/移动/批量/搜索/排序全通；策略下拉含自动分组组名；操作后缓存被清。

---

## 5.4 批量导入 `views/RulesImport.vue`

### 具体任务
1. 两种入口：粘贴 YAML 文本 / 上传 `.yaml|.yml|.txt` 文件。
2. 预览：调 `POST /api/rules/parse`（粘贴走 JSON body；上传走 `multipart/form-data`），渲染预览规则表（类型/目标/策略/备注）。
3. 确认导入：调 `POST /api/rules/import`，成功跳 `/rules` 并提示导入条数。
4. 预览中标记非法行/被过滤规则，便于用户核对。

### 涉及 API
`POST /api/rules/parse`、`POST /api/rules/import`。

### 验收
- 粘贴与上传均可预览；确认后条数正确入库；非法行有说明。

---

## 5.5 配置对比 `views/Config.vue`

### 具体任务
1. 两个等宽代码面板：原始 YAML / 合并后 YAML，并排或分签。
2. 头部统计：各自行数、规则数、自定义规则数、`lastGoodUpdate`。
3. `<StaleBanner>` 依 `isStale` 显示。
4. 「手动刷新」：调 `POST /api/config/refresh`，成功重拉两面板。
5. （进阶）YAML 语法高亮，可切换，非必须。

### 涉及 API
`GET /api/config/raw`、`GET /api/config/merged`、`POST /api/config/refresh`。

### 验收
- 上游不可达 5 秒内带 stale 返回两面板；刷新后内容更新。

---

## 5.6 设置 `views/Settings.vue`

### 具体任务
1. 表单字段：上游 URL、accessToken、缓存分钟、两个超时秒数、插入位置、替换模式、自动分组（复现 `AppSettings`）。
2. 前端校验对应后端 DataAnnotations（URL / Range / Token 长度）。
3. 保存：`PUT /api/settings`，成功提示并重拉 settings store。
4. 「生成 Token」：`POST /api/settings/generate-token`，把返回 Token 填入表单（仍提示需点保存生效）。

### 涉及 API
`GET/PUT /api/settings`、`POST /api/settings/generate-token`。

### 验收
- 保存后 `GET /api/settings` 返回新值；Token 生成仅填不落盘；字段校验生效。

---

## 5.7 迁移总验收

### 具体任务
- 五页四入口（设置/导入/仪表盘/配置/规则）逐页在 SPA 内走通；stale 横幅在仪表盘、配置、规则三处都能一致触发。
- 与卷 07 开发代理配合联调，确认同源 Cookie 生效、无 CORS。