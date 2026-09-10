# ClashSub - Clash 订阅管理服务器

基于 ASP.NET Core 8 Razor Pages 构建的 Clash 订阅管理平台。从上游获取 Clash YAML 配置，注入自定义规则，按国家/地区自动分组代理节点，生成可直接导入 Clash 客户端的订阅链接。

## 功能概览

### 订阅管理
- 从上游 URL 获取 Clash YAML 配置（模拟 OpenClash 客户端请求头）
- 将自定义规则注入上游配置的 `rules:` 段（支持插入到最前或替换全部）
- 按国家/地区关键词自动生成代理分组（新加坡、香港、日本、美国等）
- 生成聚合组：节点选择、自动测速、故障转移
- 通过 `/sub` 端点输出最终配置供 Clash 客户端订阅
- 支持 Access Token 访问鉴权

### 规则管理
- 可视化增删改查自定义规则
- 支持 22 种规则类型（DOMAIN、IP-CIDR、GEOIP、RULE-SET 等）
- 策略可选择预置策略或上游/自动生成的代理组
- 批量修改策略、批量删除
- 批量导入：粘贴 YAML 规则文本或上传文件解析导入
- 规则排序：表头点击按类型/匹配内容/策略/更新时间排序，支持上移下移调整顺序
- 实时搜索过滤
- 规则顺序与输出到 Clash 客户端的 YAML 中规则顺序完全一致

### 节点管理
- 解析上游配置中的代理节点（支持 block 和 flow 两种 YAML 映射格式）
- 卡片式展示节点信息（名称、类型、服务器、端口）
- ICMP Ping 延迟测试，按延迟颜色分级（绿/蓝/黄/红）
- 显示最后从上游更新的时间

### 配置对比
- 查看上游原始 YAML 配置
- 查看合并自定义规则后的最终配置

### 后台服务
- 定时主动从上游刷新缓存（间隔可配置，默认 15 分钟）
- 详细日志记录：启动/关闭事件、刷新耗时、各阶段详情、错误堆栈

## 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | ASP.NET Core 8 Razor Pages |
| YAML 解析 | YamlDotNet 15.3.0 |
| 缓存 | IMemoryCache (Microsoft.Extensions.Caching.Memory 8.0) |
| 前端 UI | Bootstrap 5.3 + Bootstrap Icons 1.11 |
| 数据存储 | 本地 JSON 文件（无数据库） |
| 后台任务 | BackgroundService |
| 延迟测试 | System.Net.NetworkInformation.Ping (ICMP) |

## 项目结构

前后端分离：后端（ASP.NET Core API）与前端（Vue3）各自独立目录。

```
ClashServer/
├── server/                        # 后端 ASP.NET Core 程序
│   ├── Program.cs                 # 应用入口，配置服务和中间件，定义 /sub 和 /api/* 端点
│   ├── ClashServer.csproj         # 项目文件
│   ├── appsettings.json           # 应用配置（日志级别、缓存参数、VueApp 开关）
│   ├── ApiEndpoints.cs            # 管理端 REST API（/api/rules、/api/settings 等）
│   ├── AuthSetup.cs               # 管理端 Cookie 认证 + CSRF 防护
│   ├── Models/
│   │   ├── AppSettings.cs         # 设置模型（上游URL、Token、缓存时长、自动分组等）
│   │   ├── CustomRule.cs          # 自定义规则模型 + YAML 行输出
│   │   ├── Dtos.cs                # 前后端交互 DTO（统一响应壳、Rule/Settings/Config DTO）
│   │   └── ProxyNode.cs           # 代理节点模型 + 延迟颜色分级
│   ├── Services/
│   │   ├── IClashSubService.cs    # 订阅服务接口
│   │   ├── ClashSubService.cs     # 核心服务：获取/合并/转换 YAML、节点解析、延迟测试、自动分组
│   │   ├── IStorageService.cs     # 存储服务接口
│   │   ├── StorageService.cs      # JSON 文件读写（Data/rules.json、Data/settings.json）
│   │   └── BackgroundRefreshService.cs  # 后台定时刷新缓存
│   ├── Pages/                     # 旧 Razor 页面（VueApp:Enabled=false 时启用）
│   ├── wwwroot/                   # Vue 构建产物（由 web 构建输出，已 gitignore）
│   └── Data/                      # 运行时数据（自动创建，已 gitignore）
│       ├── rules.json             # 自定义规则存储
│       └── settings.json          # 应用设置存储
│
├── web/                          # 前端 Vue3 + Vite + Pinia + TS
│   ├── vite.config.ts            # 构建输出到 ../server/wwwroot，开发代理 /api 到后端
│   └── src/
│       ├── views/                # 页面视图（登录/仪表盘/规则/导入/配置/设置）
│       ├── components/           # 通用组件（布局、StaleBanner 等）
│       ├── stores/               # Pinia 状态（auth 等）
│       ├── api/                  # axios 封装与 API 方法
│       └── router/               # Vue Router 与登录守卫
│
└── docs/                         # 改造方案与回归文档
```

## 快速开始

### 环境要求

- .NET 8 SDK
- 一个上游 Clash 订阅链接

### 构建运行

```bash
# 克隆项目
git clone <repo-url>
cd ClashServer

# 后端（在 server/ 目录）
cd server
dotnet restore
dotnet run

# 前端（可选，开发模式，另开终端）
cd ../web
npm install
npm run dev          # 开发代理 http://localhost:5173，/api 转发到后端
```

浏览器打开后端地址（`https://localhost:5001` 或 `http://localhost:5000`），
或开发模式下访问 `http://localhost:5173`。

> 说明：`VueApp:Enabled` 为 `true` 时后端托管 `wwwroot` 内的 Vue 构建产物（生产模式，单端口同源）；
> 为 `false` 时启用旧 Razor 页面。产物需先 `cd web && npm run build` 生成。

### 首次配置

1. 进入 **设置** 页面
2. 填写 **上游订阅 URL**（你的 Clash 订阅链接）
3. 设置 **访问 Token**（保护 /sub 端点，留空则不鉴权）
4. 配置 **缓存时长**（定时刷新间隔，默认 15 分钟）
5. 选择是否 **自动分组节点**（按国家/地区关键词分组，默认开启）
6. 保存后前往 **规则管理** 添加自定义规则

### Clash 客户端订阅

在 Clash 客户端中使用以下格式的订阅链接：

```
http://your-server:port/sub?token=your_token
```

如未设置 Token，直接使用：

```
http://your-server:port/sub
```

## 端点说明

| 端点 | 方法 | 说明 |
|------|------|------|
| `/sub` | GET | 获取合并后的 Clash 配置（供客户端订阅） |
| `/sub?token=xxx` | GET | 带 Token 鉴权的订阅 |
| `/sub?refresh=1` | GET | 强制刷新缓存并获取最新配置 |
| `/api/sub-health` | GET | 上游连通性健康检查 |
| `/` | GET | 仪表盘页面 |
| `/Rules` | GET | 规则管理页面 |
| `/Rules/Import` | GET | 批量导入页面 |
| `/Config` | GET | 配置对比页面 |
| `/Settings` | GET | 设置页面 |

## 设置项说明

| 设置项 | 字段 | 默认值 | 说明 |
|--------|------|--------|------|
| 上游订阅 URL | `upstreamUrl` | - | 上游 Clash 配置的订阅链接 |
| 访问 Token | `accessToken` | - | 保护 /sub 端点的访问令牌，留空则不鉴权 |
| 缓存时长 | `cacheMinutes` | 15 | 后台定时刷新间隔（1-1440 分钟） |
| 规则插入位置 | `insertRulesBefore` | true | true=插入到上游规则前，false=追加到后 |
| 完全替换模式 | `replaceMode` | false | true=删除上游所有规则只保留自定义 |
| 自动分组节点 | `autoGroupNodes` | true | 按国家/地区关键词自动生成代理组 |

## 自动分组说明

开启自动分组后，系统会：

1. 扫描所有代理节点名称，按关键词匹配国家/地区
2. 为每个地区生成 `url-test` 类型的策略组
3. 生成三个聚合组：
   - **节点选择** - select 类型，包含所有地区组
   - **自动测速** - url-test 类型，包含所有节点
   - **故障转移** - fallback 类型，包含所有节点

支持识别的地区关键词：新加坡(SG)、香港(HK)、台湾(TW)、日本(JP)、美国(US)、韩国(KR)、英国(UK)、德国(DE)、法国(FR)、加拿大(CA)、澳大利亚(AU) 等。

## 数据存储

所有数据以 JSON 文件存储在 `server/Data/` 目录下，无需数据库：

- `server/Data/rules.json` - 自定义规则列表
- `server/Data/settings.json` - 应用设置

这两个文件已在 `.gitignore` 中排除。

## 部署

### 开发环境

```bash
cd server
dotnet run
```

### 生产环境

```bash
# 1) 构建前端产物（输出到 server/wwwroot）
cd web
npm install
npm run build

# 2) 发布后端（server/ 目录内，含 wwwroot 产物）
cd ../server
dotnet publish -c Release -o ./publish

# 3) 运行
cd publish
dotnet ClashServer.dll
```

生产模式请将 `appsettings.json` 中 `VueApp:Enabled` 设为 `true`，
以便后端托管 Vue 产物并启用 `/api` 全量鉴权；设为 `false` 则回到旧 Razor 页面模式（两者互斥）。

可通过 `appsettings.json` 或环境变量配置监听端口。

## License

MIT
