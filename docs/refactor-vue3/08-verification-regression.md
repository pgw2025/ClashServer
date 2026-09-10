# 阶段 7：端到端验证与回归

> 目的：改造后做一次总验收，尤其确保既有可靠性硬约束（卷 00 §4 R1~R8）未被破坏。
> 版本：v2。

---

## 7.1 既有硬约束回归

### 具体任务与验证
| 约束 | 验证方式 | 通过标准 |
|---|---|---|
| R1 管理接口 5s | `curl` `GET /api/dashboard`（注入不可达上游） | `≤5s` 且带 stale |
| R2 `/sub` 10s | `curl` `GET /sub?refresh=1` | `≤10s`、带头 `X-Cache: stale` |
| R3 退避刷新 | 观察后台日志连续失败 | 间隔按 15→30→60→120min |
| R4 单一数据源 | 上游恢复后用 `refresh=1` | nodes/groups/merged 同步更新 |
| R5 元数据 | 各列表接口返回体 | 含 `lastGoodUpdate`/`isStale` |
| R6 `/sub` 隔离 | 未登录访问 `/sub` | 返回 YAML（非 401） |
| R7 首屏不阻塞 | 浏览器页面上游不可达 | 首屏 5s 内出缓存/占位，非全白等待 |
| R8 鉴权 fail-closed | 空 Token 登录 / 未登录访问 `/api/*` / 写操作缺自定义头 / Token 轮换后旧会话 | 分别为 `401` 拒绝 / `401` / `400` / 旧会话 `401` |

> 用故障注入 URL `https://10.255.255.1/sub` 模拟不可达；用已备份的真实上游 URL 验证恢复路径（见 `docs/baseline-20260909.md`）。

---

## 7.2 功能回归（对照原页面 Behavior）

| 模块 | 用例 |
|---|---|
| 仪表盘 | 订阅URL展示、节点卡、颜色分级、单节点测速、全部测速（限并发 4–8）、延时排序、立即刷新、测试上游 |
| 规则管理 | 增/删/改、启停、上移/下移、批量改策略、批量删除、清空、搜索、排序、策略下拉含分组 |
| 批量导入 | 粘贴预览/上传预览、非法行过滤、确认导入、跳转回列表 |
| 配置对比 | raw/merged 展示、行数/规则数、stale 提示、手动刷新 |
| 设置 | 保存后生效、Token 生成仅填入、校验、保存即清缓存 |
| 鉴权 | 未登录 401/跳登录（redirect 回跳）、登录/登出、`/sub` 与 `sub-health` 白名单不受影响、空 Token fail-closed、Token 轮换后旧会话失效、写操作缺 `X-Requested-With` 头被拒 |

---

## 7.3 部署验证

### 具体任务
1. 按卷 07 §6.2 一键发布，公布置运行。
2. 验证：`/`（SPA 根）→ 登录 → 各页面 → `/sub` 订阅 | `/api/rules` 鉴权 | 静态资源 `assets` 加载。
3. 验证 SPA 刷新/深链接（`/settings`、`/rules`）走 fallback 不死。
4. 验证旧页面模式可切换（`VueApp:Enabled`），不影响 `/sub`。

---

## 7.4 回归报告

### 具体任务
- 输出一份 `docs/refactor-vue3/RESULTS-recap.md`（或复用本卷追加），记录：各阶段验收勾选、硬约束逐条测量值、遗留问题清单。

---

## 附：各卷与步骤的引用速查

| 阶段 | 卷 | 关键步骤 |
|---|---|---|
| 0 | `01-phase0-baseline-lock.md` | 0.1/0.2/0.3 |
| 1 | `02-backend-rest-api.md` | 1.1~1.8 |
| 2 | `03-management-auth.md` | 2.1/2.2/2.3 |
| 3 | `04-static-hosting-spa-fallback.md` | 3.1/3.2 |
| 4 | `05-frontend-vue3-skeleton.md` | 4.1~4.4 |
| 5 | `06-frontend-page-migration.md` | 5.1~5.7 |
| 6 | `07-build-release-integration.md` | 6.1/6.2/6.3 |
| 7 | `08-verification-regression.md`（本卷） | 7.1~7.4 |