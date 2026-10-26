# 逐步执行记录

> 本文件记录**每一步**：先写的测试用例 → 测试不通过时的原因分析 → 这一步具体做了什么 → 验证结果 → 提交信息。
> 时间顺序：Step 0 → Step 1 → Step 2 → Step 3 → Step 4 → Step 5。

---

## Step 0 — 仓库骨架与文档框架

**目标**：把任务书拆成可验收的条目，搭好三端目录结构，让后面每一步都有明确的落点。

### 这一步做了什么
1. 读取并解析任务书 `Unity-AR研发实操题-候选人任务书-简略版.docx`（用 Python `zipfile` 解出 `word/document.xml` 再去标签，避免手工复制丢字）。
2. 初始化 Git 仓库（`git init -b main`）。
3. 写 `.gitignore`：忽略 `web/node_modules`、`web/dist`、`unity/Library|Temp|obj|Build`、`server/data/*.json`、`tools/**/bin|obj`。
4. 建立目录：`server/`（Go）、`web/`（React）、`unity/`（Unity）、`docs/`（文档）、`scripts/`（脚本）。
5. 写 `docs/PLAN.md`：需求拆解表 R1–R17、接口约定、5 个执行步骤、已知限制与应对。

### 测试 / 验收
- 验收项：`git status` 能正确忽略构建产物；`docs/PLAN.md` 覆盖任务书所有必做功能点。
- 对照任务书第 3 章逐条勾选，17 条需求全部映射到具体步骤，无遗漏。

### 结果
通过。步骤 0 不涉及可执行代码，无自动化测试。

---

## Step 1 — Go 后端（核心：接口 + 持久化）

**目标**：实现任务书 3.3 全部要求（R1–R8），这是 Unity 与 React 共同的依赖，必须先打通。

### 1.1 先写测试用例（TDD，测试先行）

在写任何实现代码之前，先提交了三个测试文件：

| 文件 | 覆盖的用例 | 对应需求 |
| --- | --- | --- |
| `server/internal/model/issue_test.go` | `TestParsePriority`（合法/非法/空串/大小写/带空格）、`TestParseStatus`、`TestNewIDIsUniqueAndPrefixed`（生成 2000 个 ID 不得重复且须以 `ISS-` 开头）、`TestNewIDDifferentAcrossMilliseconds` | R5 |
| `server/internal/store/store_test.go` | `TestNewFileStoreMissingFileStartsEmpty`、`TestCreateAssignsIDStatusAndTimestamps`（默认 `open`、时间戳非零）、`TestListReturnsCreatedIssuesInOrder`、`TestUpdateStatusChangesStatusAndUpdatedAt`（状态变更且 `updatedAt` 变新、`createdAt` 不变）、`TestUpdateStatusUnknownID`、`TestDeleteIssue`、`TestDataPersistsAcrossRestart`（**新建 store 重新读盘后数据仍在**）、`TestPersistedFileIsValidJSONWithExpectedShape`（落盘 JSON 必须含 `id/title/description/priority/status/position{x,y,z}/createdAt/updatedAt`） | R2、R4、R5、R7 |
| `server/internal/api/server_test.go` | `TestHealthEndpoint`、`TestCreateIssueSuccess`（201 + 回显 + 坐标原样）、`TestCreateIssueEmptyTitleReturns400`（空串与纯空格都要 400，且 `error.field=title`）、`TestCreateIssueInvalidPriorityReturns400`（`urgent`/`LOW`/`1`/`null`）、`TestCreateIssueMissingPriorityReturns400`、`TestCreateIssueMalformedJSONReturns400`、`TestListIssues`、`TestUpdateStatusSuccess`、`TestUpdateStatusInvalidValueReturns400`、`TestUpdateStatusUnknownIDReturns404`、`TestDeleteIssueEndpoint`、`TestCORSPreflightAndHeaders`（预检必须回 `Access-Control-Allow-Methods` 且含 `PATCH`）、`TestUnknownRouteReturns404` | R1、R3、R4、R6、R9/R10 的后端前提 |

### 1.2 测试不通过（红灯）——原因分析

第一次执行 `go test ./...`：

```
github.com/xuanliu805-jpg/Unity-AR/server/internal/store: no non-test Go files in ...\internal\store
github.com/xuanliu805-jpg/Unity-AR/server/internal/model: no non-test Go files in ...\internal\model
# .../internal/model [build failed]
internal\model\issue_test.go:12:9: undefined: Priority
internal\model\issue_test.go:16:14: undefined: ParsePriority
...
FAIL	github.com/xuanliu805-jpg/Unity-AR/server/internal/model [build failed]
FAIL	github.com/xuanliu805-jpg/Unity-AR/server/internal/api [build failed]
```

**原因**：这是 TDD 的预期红灯——测试文件已经写好，但被测的 `model` / `store` / `api` 包还没有任何非测试源文件，所有被调用的类型与函数都未定义，编译直接失败。**不是环境问题**：Go 1.27.3 工具链、`GOPATH`、`GOCACHE` 均已就绪（`go version` 输出正常，`gofmt`/`go vet` 也能运行）。

### 1.3 实现（这一阶段具体做了什么）

**`internal/model/issue.go`**
- 定义 `Priority`（`low/medium/high`）、`Status`（`open/in_progress/resolved`）、`Position{X,Y,Z}`、`Issue` 八个字段，**字段名与任务书第 4 章完全一致**，`position` 内嵌 `x/y/z`。
- `ParsePriority` / `ParseStatus`：**严格匹配**，不做大小写折叠、不裁剪空白。理由：任务书要求「枚举值错误时返回明确的参数错误」，若把 `"HIGH"` 静默当成 `high`，就掩盖了客户端 bug。
- `NewID(now)` → `ISS-<unix毫秒>-<base32随机>`：`crypto/rand` 提供 8 字节随机量（同一毫秒内并发也不会撞），毫秒时间戳让 ID 大致有序、便于排查。`crypto/rand` 极端失败时降级为纳秒后缀，**不因为一条数据让服务挂掉**。

**`internal/store/store.go`**
- `Store` 带 `sync.RWMutex`，保证 Unity 与 React 并发访问安全。
- 持久化用本地 JSON 文件（任务书允许「SQLite 或本地 JSON 文件」），结构 `{"issues":[...]}`。
- 写入采用**临时文件 + `os.Rename` 原子替换**：先写 `issues-*.tmp` → `Sync()` 刷盘 → `Rename` 覆盖目标文件。直接 `os.WriteFile` 会在进程被 kill 的瞬间留下半截 JSON，重启后整个库都读不出来；`Rename` 在同一文件系统内是原子的。
- `Create` 在写盘失败时**回滚内存状态**并返回错误，避免接口返回 201 但数据其实没落盘（这种「假成功」最难排查）。
- `UpdateStatus` / `Delete` 同理：先备份旧值，写盘失败则还原。
- 文件不存在时视为空库，首次启动即用，不报错。

**`internal/api/server.go`**
- 用 Go 1.22+ 的 `ServeMux` 方法路由：`GET /api/health`、`GET /api/issues`、`POST /api/issues`、`PATCH /api/issues/{id}/status`（额外兼容 `POST` 同路径，方便不支持 PATCH 的客户端）、`DELETE /api/issues/{id}`。
- 统一 CORS：`Access-Control-Allow-Origin: *`，允许 `GET,POST,PUT,PATCH,DELETE,OPTIONS`，`OPTIONS` 预检直接 204。React 页面在 5173 端口、后端在 8080，属于跨域，不加这层浏览器会直接拦掉请求。
- 错误响应统一为 `{"error":{"code","message","field"}}`，`message` 是中文人类可读说明，便于 React 直接展示给巡检管理人员。
- 参数校验：`title` trim 后为空 → 400 `invalid_argument` + `field=title`；`priority` 不在枚举 → 400 + `field=priority`；`status` 不在枚举 → 400 + `field=status`；`position` 出现 NaN/Inf → 400。
- 请求体用 `io.LimitReader(1<<20)` 限制 1 MB，避免恶意大 body 打爆内存。
- 非法 JSON → 400 `invalid_json`，空 body 同样 400。

**`cmd/server/main.go`**
- `flag` 支持 `-addr`（默认 `:8080`）与 `-data`（默认 `data/issues.json`）。
- 优雅退出：`signal.Notify` 捕获 SIGINT/SIGTERM → `srv.Shutdown` 5 秒超时。
- 中间件打印 `方法 路径 -> 状态码`，方便 Unity/React 联调时定位是谁发的请求。

### 1.4 验证结果

**单元/集成测试**（`go test ./... -v`）：**25 个用例全部 PASS，0 FAIL**

```
ok  	.../internal/api      1.125s      (13 个用例)
ok  	.../internal/model    0.556s      (4 个用例)
ok  	.../internal/store    0.762s      (8 个用例)
```

**静态检查**：`go vet ./...` 无输出（无告警）；`gofmt -l .` 无输出（格式统一）。
> 期间发现一个自写函数名 `strconv` 与标准库包名撞车，虽然能编译但属于坏味道，已重命名为 `quoteJSON`，避免后人误以为可以 `strconv.Itoa(...)`。

**真实进程冒烟**（`server.exe -addr :18080`）：

| 用例 | 实际响应 | 结论 |
| --- | --- | --- |
| `GET /api/health` | `{"issues":0,"service":"unity-ar-inspection","status":"ok"}` | 200 ✅ |
| `POST /api/issues` 合法体 | `{"issue":{"id":"ISS-1792988466562-SAAEVOZKQGIAC",...,"status":"open","position":{"x":0.42,"y":0.03,"z":1.26},...}}` | 201 ✅ |
| `GET /api/issues` | `{"count":1,"issues":[...]}` | 200 ✅ |
| `POST` 标题 `"   "` | `400 {"error":{"code":"invalid_argument","message":"标题(title)不能为空","field":"title"}}` | 400 ✅ |
| `POST` priority `urgent` | `400 ... "field":"priority"` | 400 ✅ |
| `POST` priority 缺失 | 400 | 400 ✅ |
| `POST` 非法 JSON | 400 | 400 ✅ |
| `PATCH .../status` = `in_progress` | `200 {"issue":{...,"status":"in_progress","updatedAt":"...12:21:12..."}}` | 200 ✅ |
| `PATCH` status=`closed` | `400 ... "field":"status"` | 400 ✅ |
| `PATCH` 未知 ID | 404 | 404 ✅ |
| `OPTIONS` 预检 | `204` + `Access-Control-Allow-Origin: *`、`Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS` | ✅ |

**重启后数据仍在（R7 关键验收）**：写入 1 条问题 → `Ctrl+C` 关闭进程 → **重新启动同一个 exe、同一个数据文件** →

```
GET /api/issues -> {"count":1,"issues":[{"id":"ISS-1792988466562-SAAEVOZKQGIAC",...,"status":"in_progress",...}]}
GET /api/health -> {"issues":1,...}
```

数据与**已被修改的状态 `in_progress`** 都完整保留，证明落盘路径正确、读盘路径正确。

**落盘文件内容**（`server/data/issues.json`）字段与任务书第 4 章一一对应：

```json
{
  "issues": [
    {
      "id": "ISS-1792988466562-SAAEVOZKQGIAC",
      "title": "入口墙面破损",
      "description": "左侧墙体存在裂缝",
      "priority": "high",
      "status": "open",
      "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
      "createdAt": "2026-10-26T12:21:06.5628133+08:00",
      "updatedAt": "2026-10-26T12:21:06.5628133+08:00"
    }
  ]
}
```

### 1.5 提交
- 提交信息：`feat(server): 实现巡检问题 Go 后端、JSON 持久化与参数校验`
- 提交内容：`server/` 全部源码与测试 + `docs/PLAN.md` + `docs/STEP_LOG.md` + `.gitignore`
