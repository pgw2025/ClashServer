# 阶段 4：Vue3 前端工程体系

> 目标：搭建可运行的前端骨架（脚手架、路由、状态管理、api 客户端、布局壳）。
> 建议源码目录：`web/`（本方案以之为准；下同）。
> 版本：v2。

---

## 4.1 脚手架与目录

### 具体任务
1. 用 Vite 创建 Vue3 + TypeScript 工程（`npm create vite@latest web -- --template vue-ts`）。
2. 安装依赖：`vue-router@4`、`pinia`、`axios`（或 `fetch` 封装，二选一统一）；可选 `element-plus`/`naive-ui` 组件库，或继续 Bootstrap 保持观感。
3. 建立目录结构：
   ```
   web/
   ├── index.html
   ├── src/
   │   ├── main.ts          # 挂载 Vue + router + pinia
   │   ├── App.vue
   │   ├── router/index.ts  # 路由表 + 全局守卫
   │   ├── stores/          # Pinia：rule、settings、dashboard、auth
   │   ├── api/             # axios 实例与各资源 API
   │   ├── views/           # 页面级组件（登录/仪表盘/规则/导入/配置/设置）
   │   ├── components/      # 通用组件（布局、表格、TAG、stale 横幅…）
   │   ├── composables/     # useDashboard / useRules 等
   │   └── types/           # 对应后端 DTO 的 TS 类型
   ```
4. 配置 `vite.config.ts`：开发期代理 `/api` 到后端（见卷 07 §6.3）；构建输出到后端 `wwwroot`（见卷 07 §6.1）。

### 涉及文件
- `web/` 全部脚手架文件。

### 验收标准
- `npm run dev` 起本地工程，空壳可访问。
- TS 类型与卷 01 §0.3 的 DTO 对齐。

---

## 4.2 路由与状态

### 具体任务
1. 路由表：`/login`、`/`(仪表盘)、`/rules`、`/rules/import`、`/config`、`/settings`。
2. Pinia store：`useAuthStore`（登录态）、`useSettingsStore`、`useRulesStore`、`useDashboardStore`。
3. 全局守卫：未登录 → `/login?redirect=<目标路径>`，登录后回跳（联动卷 03 §2.2；v2 修正引用）。

### 验收标准
- 路由切换正确；未登录访问受保护路由被导向登录。

---

## 4.3 API 客户端模块

### 具体任务
1. `src/api/http.ts`：axios 实例，`baseURL` 固定同源空串；`credentials: 'include'`（Cookie）。
2. **写操作统一注入 `X-Requested-With: fetch` 头（v2 新增）**：请求拦截器对所有非 GET 请求自动添加，配合后端 CSRF 防线（卷 03 §2.1 任务 5），业务代码无感知。
3. 响应拦截器：`401` → 清除 store → 跳 `/login?redirect=<当前路由>`，登录后回跳。
4. 按资源拆分：`api/rules.ts`、`api/settings.ts`、`api/dashboard.ts`、`api/config.ts`、`api/auth.ts`、`api/nodes.ts`。
5. 封装统一解析：`{ ok, data, error, lastGoodUpdate, isStale }`。

### 验收标准
- 各资源函数与卷 02/03 端点一一对应，返回类型化。

---

## 4.4 布局壳与通用组件

### 具体任务
1. 布局组件：顶栏（品牌/路由导航/登出）、内容区，`<router-view>`。
2. 通用组件：
   - `<StaleBanner>`：读取响应 `isStale`/`lastGoodUpdate` 渲染"数据为缓存，上游不可达"横幅（对接 R5）。
   - `<NodeCard>`、`<RuleRow/表格>`、`<PolicySelect>`、`<StatusTag>` 等前端复用件。
3. 接入 UI 框架的按需/全量引入（若选组件库）。

### 验收标准
- 一套布局 + 复用组件可在后续页面直接引用，stale 横幅可在任意页面用。