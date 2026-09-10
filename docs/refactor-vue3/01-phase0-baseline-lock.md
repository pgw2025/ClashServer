# 阶段 0：现状盘点与基线锁定

> 目的：在动手前把「现状映射」和「前后端接口边界」定死，避免改造中各自为政。
> 关联：`docs/refactor-vue3/00-overview-and-decisions.md`；已有基线 `docs/baseline-20260909.md`。

---

## 0.1 环境与构建基线

### 目标
确认改造前工程可构建、可运行，作为回退锚点。

### 涉及文件 / 输入
- `ClashServer.csproj`（net8.0，已引用 `YamlDotNet`、`Microsoft.Extensions.Caching.Memory`）
- 现有运行配置 `appsettings.json` / `appsettings.Development.json`

### 任务
1. 记录当前 `dotnet build -c Debug` 是否通过、残留警告数。
2. 记录当前测试启动方式（参考已有基线：`ASPNETCORE_URLS=http://localhost:5080`）。
3. 记录上游可达性现状（可用 `docs/baseline-20260909.md` 的故障注入 URL：`https://10.255.255.1/sub`）。

### 验收标准
- 归档一份「当前可构建/可运行/上游状态」快照，改造过程中每阶段对照。

### 验证命令（示例）
```powershell
dotnet build -c Debug
$env:ASPNETCORE_URLS = "http://localhost:5080"; dotnet run
curl.exe -s -o NUL -w "%{http_code} %{time_total}s\n" http://localhost:5080/
curl.exe -s -o NUL -w "%{http_code} %{time_total}s\n" "http://localhost:5080/sub?refresh=1"
```

---

## 0.2 API 缺口清单

### 目标
把「页面代码-behind 里『读服务 + 渲染』与『收表单 + 调服务』」的能力，全部映射为候选 REST API，标记「已有 / 需新增」。

### 现状盘点结论（已核实）

| 页面 | 当前行为（代码-behind） | 所需 API |
|---|---|---|
| 仪表盘 `Pages/Index` | 读设置/规则/节点缓存、拼订阅URL、测上游、测延迟、全部测速、延时排序、强制刷新 | 概览、测上游、测延迟、刷新 |
| 规则 `Pages/Rules/Index` | 增删改查、启停、上移下移、批量改策略/删除、清空、读策略组 | 规则 CRUD + 批量 + 策略组 |
| 导入 `Pages/Rules/Import` | 解析 YAML、预览、导入（粘贴/上传） | 解析、导入 |
| 配置 `Pages/Config` | 读 raw/merged YAML、统计行数/规则数、手动刷新 | raw / merged、刷新 |
| 设置 `Pages/Settings` | 读/存设置、生成 Token、触发清缓存 | 设置读写、生成Token |
| 公共端点 | `/sub`、`/api/sub-health`、`/api/nodes`、`/api/groups` | **保留** |

### 已有 / 需新增速览

| 端点 | 方法 | 现状 |
|---|---|---|
| `/sub` | GET | 保留（原样） |
| `/api/sub-health` | GET | 已有 |
| `/api/nodes` | GET | 已有 |
| `/api/groups` | GET | 已有 |
| `/api/dashboard` 、规则/设置/配置 CRUD、导入、测速、刷新、登录 | — | **需新增**（详见卷 02/03） |

### 任务
1. 逐页核对上表，补全遗漏能力。
2. 区分「机器接口（Clash）」「管理接口（SPA）」两类，管理接口标注需鉴权。

### 验收标准
- 缺口清单无遗漏，每项能追溯到对应页面代码-behind 方法。

---

## 0.3 接口契约与 DTO

### 目标
定义前后端交互的数据结构，作为卷 02 实现的「契约先行」。

### 任务
1. 为 `AppSettings`、`CustomRule`、`ProxyNode` 各定义一组**对外 DTO**（JSON 字段沿用现有 camelCase）；**时间字段统一用 `DateTimeOffset`**（v2：现状模型是 `DateTime.Now` 本地时间，序列化无时区后缀，前端 `new Date()` 解析存在歧义）。
2. 定义统一响应壳约定（成功/失败/错误信息），例如：
   ```json
   { "ok": true, "data": { ... } }
   { "ok": false, "error": "..." }
   ```
3. 列表类接口在 `data` 外**追加元数据字段**以承载 stale 信号：
   ```json
   { "ok": true, "lastGoodUpdate": "...", "isStale": false, "data": [ ... ] }
   ```
   **`isStale` 只在后端计算一次（v2）**：现状三个页面各自的 `IsDataStale` 口径不一致（仪表盘/配置页用 `LastUpstreamUpdate ?? LastGoodUpdate`，规则页仅用 `LastGoodUpdate`）；API 化时收敛为后端共享 helper（建议采用 `LastUpstreamUpdate ?? LastGoodUpdate`，超过 `2×cacheMinutes` 判 stale），所有端点复用同一结果，前端不做二次计算。
4. 约定状态码语义（v2 统一口径，全方案以此为准）：
   - `200` 成功；
   - `200` + `isStale:true`：业务数据降级（last-good 兜底），**不用 5xx 表达**——与 `/sub` 降级仍返回 `200` + `X-Cache: stale` 的既有语义对齐；
   - `400` 入参错误；`401` 未登录；`404` 资源不存在；
   - `503` **仅用于** `/api/sub-health` 探活端点，不用于业务列表降级出口。

### 示例：规则 DTO（建议）
```json
{
  "id": "guid",
  "ruleType": "DOMAIN-SUFFIX",
  "target": "example.com",
  "policy": "DIRECT",
  "enabled": true,
  "remark": "备注",
  "updatedAt": "2026-09-10T...Z"
}
```

### 验收标准
- DTO、响应壳、错误约定成文，三份既有模型都有对应转换说明。