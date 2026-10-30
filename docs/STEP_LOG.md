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

---

## Step 2 — React 管理端

**目标**：完成任务书 3.2（R9–R11），并顺带把加分项里的筛选/删除做掉。

### 2.1 先写测试用例（TDD）

由 AI 助手（sub-agent）按我给出的接口契约与验收标准先产出测试文件，我随后逐个文件复核：

| 测试文件 | 用例 | 对应需求 |
| --- | --- | --- |
| `web/src/api/__tests__/api.test.ts` | base URL 拼接；`fetchIssues` 解析 `{issues,count}`；后端返回 `{error:{...}}` 时抛出带 message 的错误；**fetch reject（后端没启动）时抛出 NetworkError**；`updateStatus` 发 PATCH 且 body 为 `{"status":"..."}`；`deleteIssue` 发 DELETE | R9、R10、R11 |
| `web/src/components/__tests__/IssueList.test.tsx` | 渲染出后端返回的标题/优先级/状态；下拉框选 `resolved` 会调用 `updateStatus` 并更新显示；`updateStatus` 失败时**回滚**并显示错误信息 | R10 |
| `web/src/__tests__/App.test.tsx` | **后端不可用时页面显示错误提示而不是白屏**；空列表显示「暂无问题」空态 | R11 |

### 2.2 测试不通过（红灯）
AI 助手在实现前先跑过一次测试，此时 `src/api.ts`、`src/components/*` 尚不存在，
Vitest 报 `Failed to resolve import` / `Cannot find module`，属于 TDD 的预期红灯。
我接手复核时，测试与实现均已就位，因此本步的红灯证据以 AI 助手的执行记录为准，
绿灯证据由我**亲自重新执行**确认（见 2.4）。

### 2.3 实现（这一阶段具体做了什么）

技术栈：React 18.3 + TypeScript 5.7 + Vite 5.4，测试用 Vitest 2.1 + @testing-library/react 16 + jsdom。
**不引入任何 UI 组件库**，全部手写 CSS，避免版本冲突与体积膨胀。

关键设计（我复核后认可并保留）：
1. **错误分两类建模**：`ApiError`（后端返回了 `{error:{code,message,field}}` → 直接展示后端的中文 message）
   与 `NetworkError`（fetch 本身 reject → "无法连接后端服务，请确认后端已启动"）。
   这样 UI 能区分「参数填错了」和「后端根本没起来」，网络失败也绝不会被吞掉。
2. **base URL 惰性读取**：`getApiBaseUrl()` 每次调用时读 `import.meta.env.VITE_API_BASE_URL`，
   而不是模块加载时缓存成常量 —— 这样测试可以逐用例切换后端地址。
3. **乐观更新 + 失败回滚**：行组件本地持有 `status`，change 时立即切换并调用 `updateStatus`，
   失败则回滚到旧值并弹出错误条，不会出现「界面显示已解决、后端其实没改」的假象。
4. **失败时不清空已有数据**：后端不可用只显示错误条并保留已加载的列表，避免网络抖动把界面清空。
5. **筛选与删除（加分项）**：按优先级/状态下拉筛选；删除带二次确认。

字段名与后端严格一致（`src/types.ts` 里逐字段对齐 Go 的 `model.Issue`）：
`id / title / description / priority / status / position{x,y,z} / createdAt / updatedAt`。

### 2.4 验证结果（我亲自重新执行）

```
> npm run test:run
 ✓ src/api/__tests__/api.test.ts        (11 tests) 14ms
 ✓ src/components/__tests__/IssueList.test.tsx (5 tests) 200ms
 ✓ src/__tests__/App.test.tsx           (4 tests) 211ms
 Test Files  3 passed (3)
      Tests  20 passed (20)
```

```
> npm run typecheck
> tsc -p tsconfig.json --noEmit && tsc -p tsconfig.node.json --noEmit
（0 错误）

> npm run build
✓ 36 modules transformed.
dist/index.html                   0.40 kB │ gzip:  0.32 kB
dist/assets/index-BajjKFB5.css    3.90 kB │ gzip:  1.41 kB
dist/assets/index-CNIt0AWH.js   149.99 kB │ gzip: 48.89 kB
✓ built in 725ms
```

### 2.5 遗留问题（如实记录）
AI 助手做验证时，本机 `go` 不在 PATH 中，因此它是用 `web/scripts/mock-backend.mjs`
（按 Go 契约写的等价 mock）完成的前端联调，**不是真实 Go 后端**。
这一点在 Step 4 里由我改用**真实 Go 进程**重新做了端到端验证。

### 2.6 提交
- 提交信息：`feat(web): 实现 React 管理端问题列表、状态流转与错误处理`
- 提交内容：`web/` 全部源码、测试与工程配置

---

## Step 3 — Unity AR 移动端

**目标**：完成任务书 3.1（R12–R16）。

### 3.0 动手前的关键调研（避免写出编译不过的代码）

本机**没有安装 Unity 编辑器**（只装了 Unity Hub 且没有 Editor），所以 Unity 侧代码无法本地编译验证。
为了不写出「看起来对但编译不过」的代码，我先把 AR Foundation 的 npm 包源码下载下来，逐个核对我要用到的 API 签名：

```
https://packages.unity.com/com.unity.xr.arfoundation/-/com.unity.xr.arfoundation-5.1.5.tgz
```

核对结果（全部以 5.1.5 包内源码为准）：

| 我要用的 API | 5.1.5 源码里的真实签名 | 结论 |
| --- | --- | --- |
| `ARRaycastManager.Raycast` | `public bool Raycast(Vector2 screenPoint, List<ARRaycastHit> hitResults, TrackableType trackableTypes = TrackableType.AllTypes)` | 是**实例方法**；而且 5.x 已经**没有** `RaycastPlane` 参数了（网上大量老教程还在传 `RaycastPlane.PlaneWithinPolygon`，照抄必然编译失败） |
| `ARRaycastHit.pose` | `public Pose pose => m_Transform.TransformPose(sessionRelativePose);` | 是**世界空间**坐标，可以直接用 |
| `ARPlaneManager` | `public PlaneDetectionMode requestedDetectionMode` / `public event Action<ARPlanesChangedEventArgs> planesChanged` | 6.x 之前的 `ARPlaneTrackingMode` 已经不存在，必须用 `PlaneDetectionMode.Horizontal` |
| `ARPlane` | `public PlaneAlignment alignment` | 判断水平面用 `PlaneAlignment.HorizontalUp / HorizontalDown` |
| `ARSession` | `public static ARSessionState state` / `public static event Action<ARSessionStateChangedEventArgs> stateChanged` | **静态**成员，订阅/退订要小心重复订阅 |
| `ARSessionState` | `None / Unsupported / CheckingAvailability / NeedsInstall / Installing / Ready / SessionInitializing / SessionTracking` | 与老教程里的 `Initializing/Moving/Tracking/Paused` **完全不同**，因此代码里只用 `ToString()` 再做字符串映射，不直接引用枚举成员，升级包也不会编译失败 |
| `ARCameraManager` / `ARCameraBackground` | 都存在，`[RequireComponent(typeof(Camera))]` | 加在 Camera 物体上即可 |
| `TrackableCollection<T>` | `struct`，有 `GetEnumerator()` | 可以直接 `foreach` |

同时对比了 **AR Foundation 6.6.2**（Unity 6000.0）的 API，发现 6.x 对
`ARSessionState`、`ARCameraManager`（不再有公开的 `camera`/`trackingType`）改动更大。
因此最终选择 **Unity 2022.3 LTS + AR Foundation 5.1.5** 这条更稳、更常见的组合。

### 3.1 先写测试用例（TDD）

关键手法：**把与 UnityEngine 无关的纯逻辑抽到 `unity/Assets/Scripts/Core/`**，
再用一个独立的 .NET 测试工程 `tools/UnityLogicTests` 以**链接方式**（`Compile Include`）引用同一批 `.cs` 文件。
这样「Unity Test Runner 里跑的用例」和「命令行跑的用例」永远是同一份源文件，不会出现两套实现跑偏。

| 测试文件 | 用例 | 对应需求 |
| --- | --- | --- |
| `IssueValidatorTests.cs` | 空标题/纯空格标题/null 标题 → 失败且 `field=title`；标题超长；描述为空仍通过；三种合法优先级通过；非法优先级 → `field=priority`；描述超长；坐标 NaN/Inf → `field=position`；空草稿不抛异常 | R14 |
| `IssuePayloadBuilderTests.cs` | 请求体含 title/description/priority/position{x,y,z}；**不含** id/status/createdAt/updatedAt（都由后端生成）；首尾空白裁剪；双引号/反斜杠/换行正确转义；零坐标不多余精度；负坐标正常 | R14 |
| `IssueEnumsTests.cs` | 优先级集合 = {low,medium,high}；状态集合 = {open,in_progress,resolved}；非法值被拒；中文显示名一一对应 | R14、R10 |
| `ApiEndpointsTests.cs` | 路径拼接；ID 特殊字符转义；后端地址去尾斜杠/去空白/空值回落默认；局域网地址拼接 | 真机配置后端地址 |
| `PlacementGateTests.cs` | **触点落在 UI 上 → 不放置**；面板打开（全局禁用）→ 不放置；没检测到平面 → 不放置；不是点击手势 → 不放置；判定优先级（禁用 > UI > 无平面 > 非点击）；每种原因都有中文提示 | **R16** |
| `ApiResponseParserTests.cs` | 从响应里取问题 ID；取错误 message / field；转义引号还原；健康检查识别 | R15 |

### 3.2 测试不通过（红灯）—— 原因分析

第一次 `dotnet test`（实现文件尚未写）：

```
E:\简历题目\unity\Assets\Tests\EditMode\Core\PlacementGateTests.cs(16,24):
error CS0246: 当前上下文中不存在名称"PlacementContext"
```

**原因**：测试先行，`PlacementContext` / `PlacementGate` 还没实现，编译失败 —— 预期的红灯。

第二处红灯（响应解析）：

```
error CS0103: 当前上下文中不存在名称"ApiResponseParser"
```

**原因**：同上，`ApiResponseParser` 尚未实现。

另外做了一次**「移除实现验证测试有效性」**的实验：把 `unity/Assets/Scripts/Core` 整个目录临时移走后重跑，
56 个用例全部编译失败（`CS0234: 命名空间"UnityAR"中不存在类型或命名空间名"Core"`），
说明这些用例确实在测实现，而不是空跑。

### 3.3 实现（这一阶段具体做了什么）

**目录结构**

```
unity/
├─ Assets/
│  ├─ Scripts/
│  │  ├─ Core/        ← 不依赖 UnityEngine，可被 .NET 测试工程链接编译
│  │  │   IssuePriorities / IssueStatuses / Vector3Position / IssueDraft
│  │  │   ValidationResult / IssueValidator / IssuePayloadBuilder
│  │  │   ApiEndpoints / BackendConfig / ApiResponseParser / PlacementGate
│  │  ├─ Ar/          ← ARSessionController / PlacementController / MarkerStyler
│  │  ├─ UI/          ← ApiClient / IssueFormController / StatusBanner / BackendSettings
│  │  └─ Input/       ← TapSource（新老输入系统双实现）
│  ├─ Editor/         ← SceneBuilder（一键生成场景）/ ArProjectSettings
│  └─ Tests/EditMode/Core/   ← NUnit 用例
├─ Packages/manifest.json     ← AR Foundation 5.1.5 / ARCore / ARKit / TMP / Input System
└─ ProjectSettings/ProjectVersion.txt
```

**几个关键实现点**

1. **UnityEditor.SceneBuilder**：用代码生成完整场景（相机 + ARCameraManager + ARCameraBackground、
   ARSession + ARPlaneManager + ARRaycastManager、Canvas + 表单、EventSystem、全部组件连线）。
   **为什么不用 .unity 文件**：手写 Unity 序列化 YAML 极易出错，而本机没有 Unity 编辑器可以校验；
   与其提交一个「打开就报错」的场景文件，不如让评审点一下菜单就得到必然可用的场景，
   而且场景结构在代码里一目了然。私有 `[SerializeField]` 字段用 `SerializedObject` 赋值。
2. **放置判定三重保障**（对应 R16）：
   - `PlacementGate`（纯逻辑，已单测）：判定顺序 = 全局禁用 > 触点在 UI 上 > 无平面 > 非点击；
   - `EventSystem.current.IsPointerOverGameObject(fingerId)`：覆盖按钮/输入框/下拉框；
   - `RectTransformUtility.RectangleContainsScreenPoint(uiBlocker, ...)`：填写面板矩形内一律不放标记
     （防止某些 UI 模块配置下 EventSystem 不更新触摸命中状态）。
   另外填写面板打开时直接把 `placement.PlacementEnabled = false`。
3. **输入系统兼容**：`ITapSource` 接口 + `#if ENABLE_INPUT_SYSTEM` / `#if ENABLE_LEGACY_INPUT_MANAGER`
   两套实现 + `TapSourceFactory`。Unity 的「Active Input Handling」有 Old/New/Both 三种，
   直接写 `Input.GetTouch` 或 `Touchscreen.current` 都可能编译不过，这样写两边都能编。
4. **JSON 手写序列化**：请求体不依赖 `JsonUtility`/Newtonsoft，`IssuePayloadBuilder` 自己拼 JSON 并做转义，
   因此可以被 .NET 单测逐字符断言；数字用 `InvariantCulture` 格式化，避免中文 Windows 上把小数点写成逗号。
5. **响应解析**：`ApiResponseParser` 定向查找 `"issue"`/`"error"` 并还原转义，
   避免为「取一个 ID」引入整个 JSON 库（IL2CPP 打包体积友好）。
6. **真机后端地址**：`BackendSettings` 组件把地址写在 Inspector 里，运行时也能改，改完存 PlayerPrefs；
   `BackendConfig.NormalizeBaseUrl` 负责去尾斜杠/空值回落。
7. **优先级用三个按钮而不是下拉框**：TMP 下拉框需要复杂的模板 GameObject，用代码搭模板极易出错；
   三个大按钮在手机上反而更不容易误触，也省掉一整段易错的模板构建代码。

### 3.4 验证结果

```
> dotnet test tools/UnityLogicTests/UnityLogicTests.csproj
已通过! - 失败: 0，通过: 56，已跳过: 0，总计: 56
```

56 个用例全部通过（覆盖校验、序列化、枚举、路径拼接、放置判定、响应解析）。

### 3.5 未完成 / 已知问题（如实记录，重要）
- **没有 Unity 编辑器，Unity 工程未在本机编译过**，真机 AR 也**未实测**。
  凡是依赖 UnityEngine / AR Foundation 的代码（`Ar/`、`UI/`、`Input/`、`Editor/`）只做了**源码级 API 核对**（对照 5.1.5 包内源码逐个确认签名），
  没有经过 Unity 编译器与真机验证。README 中已明确标注这一点。
- **没有演示视频**：任务书要求两段 3–5 分钟的手机/电脑录屏，需要真实设备与 Unity 编辑器，本机无法产出。
  README 中给出可复现的演示脚本（Demo 脚本），但视频本身**缺失**。
- 场景文件由脚本生成而非提交 `.unity`（原因见 3.3.1）。

### 3.6 提交
- 提交信息：`feat(unity): 实现 AR 平面检测、标记放置、问题上报与纯逻辑单元测试`

---

## Step 4 — 三端联调（真实后端 + 真实前端 + 无头浏览器）

**目标**：用**真实的 Go 进程**和**真实的 React 页面**跑通任务书第 1 章的闭环，
并验证「后端不可用时页面不能白屏」。

### 4.1 思路
Step 2 里 AI 助手因为本机没有 Go，用一个 mock 后端做的前端联调，**不算真实验证**。
所以这一版我自己写了一个端到端脚本 `scripts/e2e.mjs`：
它自己编译并启动 Go 后端 → 启动 Vite 开发服务器 → 用 Playwright 驱动无头 Chromium 走完整流程 → 关掉所有进程。

### 4.2 先写测试用例（E2E 断言清单）

| # | 断言 | 对应需求 |
| --- | --- | --- |
| 1 | 模拟 Unity 上报返回 201 | R2 |
| 2 | 后端生成了问题 ID | R5 |
| 3 | 新建问题默认状态为 `open` | R5 |
| 4 | React 列表里看得到 Unity 上报的标题「入口墙面破损」 | R9 |
| 5 | 顶部显示问题总数 1 | R9 |
| 6 | 列表里显示优先级「高」 | R9 |
| 7 | 列表里显示状态「待处理」 | R9 |
| 8 | 列表里显示位置坐标 x/y/z | R9 |
| 9 | 下拉框选 `resolved` 后，**后端**状态真的变成 `resolved` | R10 |
| 10 | 界面上的状态同步变成「已解决」 | R10 |
| 11 | 杀掉后端后，页面显示错误提示 | **R11** |
| 12 | 杀掉后端后，表头仍然渲染（没有白屏） | **R11** |
| 13 | 杀掉后端后，错误条上有「重试」按钮 | **R11** |
| 14 | Go 重启后数据仍然存在 | **R7** |
| 15 | Go 重启后状态修改也保留（仍是已解决） | **R7** |
| 16 | 浏览器控制台没有 JS 报错 | 质量 |
| 17 | 页面没有未捕获的异常 | 质量 |

### 4.3 测试不通过（红灯）—— 发现并修复了一个真实缺陷 ⚠️

**第一次跑 E2E 的结果：**

```
✔ Unity 上报返回 201
✔ 后端生成了问题 ID
✔ 默认状态为 open
✔ React 列表里看得到 Unity 上报的标题
✔ 顶部显示问题总数 1
✔ 列表里显示了优先级「高」
✔ 列表里显示了状态「待处理」
✔ 列表里显示了位置坐标
✔ React 改状态后后端真的变成 resolved
✔ 界面上的状态同步变成「已解决」
✘ 后端不可用时页面显示错误提示          ← 失败
✔ 后端不可用时表头仍然渲染（没有白屏）
✘ 后端不可用时仍能看到错误条上的重试按钮  ← 失败
```

**定位过程**：Go 后端进程日志显示被杀之后没有任何请求记录，说明请求**根本没发出去**。
再往后看日志发现，重启后端的那一刻，浏览器挂起的那次请求居然返回了 200 ——
说明这次 fetch 是**挂死**了，既没有成功也没有失败。

**原因**：浏览器复用了之前那条 keep-alive 长连接。后端进程被 `taskkill` 杀掉后，
这条连接进入「半开」状态：客户端发出去的请求石沉大海，服务端也不会再发 RST。
于是 `fetch` 既不 resolve 也不 reject，React 停在「加载中」，
**既没有错误提示，也没有「重试」按钮**。这恰恰是任务书 3.2 里最不想看到的失败形态
（「后端不可用时，页面应显示错误提示，不能直接白屏」——虽然没白屏，但也没任何提示）。

**修复**（先写失败用例再改实现，TDD）：

1. 先在 `web/src/api/__tests__/api.test.ts` 里加 3 个用例：
   - 「后端一直不响应时按时抛出 NetworkError（而不是永远转圈）」——用 `vi.fn().mockImplementation(() => new Promise(() => {}))` 模拟永不 resolve 的 fetch；
   - 「默认超时时间是 8 秒」；
   - 「可以通过环境变量调整超时时间」。
   跑测试 → **3 failed**（`getRequestTimeoutMs is not a function`），红灯确认。
2. 在 `web/src/api.ts` 里实现：
   - 新增 `DEFAULT_REQUEST_TIMEOUT_MS = 8000` 与 `getRequestTimeoutMs()`（读 `VITE_API_TIMEOUT_MS`）；
   - `request()` 内部用 `AbortController` + `Promise.race([fetch(...), timeoutPromise])`：
     **不能只依赖 `fetch` 自身的 abort**，因为半开连接下浏览器可能根本不响应 abort，
     `Promise.race` 才是保证「一定会结束」的办法；
   - 超时转成 `NetworkError('请求超时（N 秒），后端可能未启动或网络不通')`。
3. 再跑测试 → **23 passed**。

**修完之后的 E2E 结果：**

```
✔ 后端不可用时页面显示错误提示
✔ 后端不可用时表头仍然渲染（没有白屏）
✔ 后端不可用时仍能看到错误条上的重试按钮
```

页面实际显示（见 `docs/screenshots/03-后端不可用时显示错误提示.png`）：
红色错误条「请求超时（2 秒），后端可能未启动或网络不通」+ 「重试」按钮，
表头、筛选器、已有数据行全部照常渲染。

### 4.4 联调过程中另外修掉的两个脚本自身问题
1. **端口残留**：Windows 下 `shell:true` 启动的进程，`child.kill()` 只会杀掉 `cmd.exe` 壳，
   真正的 Go/Vite 进程还活着 → 下一次启动报 `Only one usage of each socket address`，
   而且会串到上一轮的数据，**让测试结果失真**。改成用 `spawnSync('taskkill', ['/PID', pid, '/T', '/F'])` 同步杀进程树。
2. **Playwright 解析**：ESM 的 `import()` 不认 `NODE_PATH`，改成用 `createRequire` 在若干候选目录里解析，
   找不到再回退到标准 `import('playwright')`。

### 4.5 最终 E2E 结果

```
[e2e] 4/6 模拟 Unity 上报问题
  ✔ Unity 上报返回 201
  ✔ 后端生成了问题 ID
  ✔ 默认状态为 open
[e2e] 5/6 打开 React 管理端并验证
  ✔ React 列表里看得到 Unity 上报的标题
  ✔ 顶部显示问题总数 1
  ✔ 列表里显示了优先级「高」
  ✔ 列表里显示了状态「待处理」
  ✔ 列表里显示了位置坐标
  ✔ React 改状态后后端真的变成 resolved
  ✔ 界面上的状态同步变成「已解决」
[e2e] 6/6 停掉后端，验证页面不白屏
  ✔ 后端不可用时页面显示错误提示
  ✔ 后端不可用时表头仍然渲染（没有白屏）
  ✔ 后端不可用时仍能看到错误条上的重试按钮
   重启后端，验证数据持久化
  ✔ Go 重启后数据仍然存在
  ✔ Go 重启后状态修改也保留（仍是已解决）
  ✔ 浏览器控制台没有 JS 报错
  ✔ 页面没有未捕获的异常

[e2e] 全部通过 ✅
```

**17 项断言全部通过**，任务书第 1 章的闭环已在**真实后端 + 真实前端 + 真实浏览器**上验证。

### 4.6 截图（`docs/screenshots/`）
| 文件 | 内容 |
| --- | --- |
| `01-列表显示Unity上报的问题.png` | React 页面显示刚上报的问题：标题/描述/优先级「高」/状态「待处理」/坐标 x 0.42 · y 0.03 · z 1.26 |
| `02-React修改状态为已解决.png` | 下拉框改成「已解决」，后端同步 |
| `03-后端不可用时显示错误提示.png` | 后端被杀掉后的错误条 + 重试按钮，表头与已有数据仍在 |
| `04-重启后数据仍然存在.png` | Go 重启后问题与状态都还在 |

### 4.7 提交
- 提交信息：`test(e2e): 增加三端联调冒烟脚本，并修复请求挂死导致页面无提示的问题`

---

## Step 5 — 代码评审、缺陷修复与文档交付

**目标**：在交付前做一轮独立评审，把能自动化的缺陷全部修掉，并补齐 README / AI_LOG / 测试报告。

### 5.1 做了什么

1. **发起两次独立代码评审**（AI sub-agent，只读审查，不允许改文件）：
   - P-003：Unity C# / AR Foundation 代码审查（Prompt 原文见 `AI_LOG.md`）；
   - P-004：Go 后端 + React 前端 + E2E 脚本审查（Prompt 原文见 `AI_LOG.md`）。
2. **逐条复核并分类处理**：每一条「重要 / 阻塞」结论我都先写**失败用例**再改实现，
   驳回的建议在 `AI_LOG.md` 里逐条写明理由。
3. **补齐文档**：`README.md`、`AI_LOG.md`、`docs/TEST_REPORT.md`，并更新本文件。

### 5.2 评审发现并修复的问题（重点）

#### A. 两个会让 Unity 工程**编译不过**的问题（阻塞级）

| 问题 | 原因 | 修法 |
| --- | --- | --- |
| `Assets/Scripts/Input/TapSource.cs` 旧输入分支 | 文件在 `namespace UnityAR.Input` 内，简单名 `Input` 被 C# 解析成**命名空间** `UnityAR.Input` 而不是 `UnityEngine.Input`，报 `CS0234: 命名空间"UnityAR.Input"中不存在类型或命名空间名称"GetTouch"`。**因为仓库里没有 `ProjectSettings.asset`，Unity 重建时 `activeInputHandler` 默认是 Old，也就是说默认配置下打开工程正好命中这个坏分支** | 全部改成全限定 `UnityEngine.Input.GetTouch` / `GetMouseButtonDown` / `mousePosition` |
| `Assets/Editor/SceneBuilder.cs` 的 TMP 输入框 | TMP 3.0.6 的 `TMP_InputField` 只有 `textViewport`（序列化字段也只有 `m_TextViewport`），**没有** `viewport` → `CS1061`，`UnityAR.Editor` 程序集编译失败、场景根本生成不出来 | 改为 `input.textViewport = viewportRect;`，并给 TextViewport 补 `RectMask2D`（TMP 3.0.6 的 `m_TextViewportRectMask` 正是从它身上取的） |

> 对这两条我**没有只凭 AI 的话改代码**：单独下载了 `com.unity.textmeshpro@3.0.6` 的源码包，
> 核对到 `TMP_InputField.cs:618` 明确写着
> `public RectTransform textViewport { get { return m_TextViewport; } set { ... } }`，确认 AI 结论正确后才动手。

#### B. 「点按钮会误放标记」这条需求的两处真实漏洞（重要级）

| 问题 | 原因 | 修法 |
| --- | --- | --- |
| 顶部状态栏仍会误放标记 | `EventSystem.IsPointerOverGameObject` 读的是 `EventSystem.Update` 里缓存的**上一帧**命中状态，而 `PlacementController.Update` 与 `EventSystem.Update` 的执行顺序不确定 → 点 UI 时判定成「不在 UI 上」 | 改用 `EventSystem.RaycastAll` 做**当前帧**射线命中 |
| 屏幕下方约 40% 永远放不了标记 | `uiBlocker` 绑的是填写面板，`RectangleContainsScreenPoint` 只做几何计算、**不看对象是否 active**，面板收起后它依然占着屏幕下半部分 | 加上 `uiBlocker.gameObject.activeInHierarchy` 判断 |

#### C. 后端：500 响应泄露服务器路径（重要级）

红灯复现（`TestInternalErrorDoesNotLeakServerPaths`，用「把数据文件换成同名目录」制造写盘失败）：

```
{"error":{"code":"internal_error","message":"保存问题失败: 替换数据文件失败: rename E:\\...\\sub\\issues.json: Access is denied."}}
```

→ 细节只进服务端日志，客户端拿固定文案「保存问题失败，请稍后重试」。

#### D. 前端：可能白屏 / 永久 loading / 竞态（重要级）

| 问题 | 修法 |
| --- | --- |
| 后端返回 200 但 body 缺 `issue` → `undefined.id` 抛错 → React 卸载整棵树 → **白屏** | `updateStatus` / `deleteIssue` 加运行时校验；顶层加 `ErrorBoundary` 兜底 |
| 上游 signal 被 abort 被误报成「后端没启动」 | 新增 `CancelledError`，与 `NetworkError` 区分 |
| 超时后底层 fetch 没被中止，积压僵尸请求 | 超时回调里先 `controller.abort()` |
| 连续刷新时旧响应覆盖新响应 | 加请求序号，过期响应丢弃 |
| 删除没有并发保护，双击会发两个 DELETE | `deletingIds` 集合，删除中按钮禁用 |
| 加载失败时表格显示「暂无问题」，误导成「真的没数据」 | 改为「数据加载失败，请点击右上角「刷新」重试」 |

#### E. E2E 脚本里的**假阳性断言**（重要级）

原来三条断言是 `rowText.includes('待处理')` / `includes('已解决')` ——
但状态下拉框渲染了全部 3 个 `<option>`，这两个词**无论当前状态是什么都会出现在行文本里**，断言永远不会失败。
已改成断言 `select.inputValue()`（这才是真正验证「界面同步」的方式）。

#### F. 其它已修的中小问题

- 后端：方法不匹配返回 405（`method_not_allowed`）而不是笼统的 404；超大 body 返回 413 而不是被静默截断成 `invalid_json`；
  标题/描述长度上限 200/2000 **字符**（改成 `utf8.RuneCountInString`，与 Unity 客户端口径一致 —— 原来客户端按字符、后端按字节，
  一个 100 字的中文标题会「客户端放行、后端 400」）；补 `WriteTimeout` / `IdleTimeout`；
  启动时清理进程被强杀留下的 `issues-*.tmp`。
- Unity：`PlayerSettings.insecureHttpOption = AlwaysAllowed`（Android 9+ 默认禁止 http 明文，真机联调必须放开）；
  `BackendConfig.NormalizeBaseUrl("http://")` 会被削成 `"http:"` 的 bug（加了协议头校验）；
  平面数量事件只在「有/无」翻转时触发导致状态栏数字失真；协程里 NRE 会让「提交问题」按钮**永久失效**（加 null 检查 + `try/finally`）；
  优先级配色是死代码（加 `RestyleLastMarker`，选完优先级立刻给最近的标记上色，顺带满足加分项）；
  `renderer.material` 材质泄漏 → 改用 `MaterialPropertyBlock`；`Vector3Position.ToString()` 的 culture 问题；
  `StatusText` 初始 null；`RemoveAllListeners()` 误删他人监听。

### 5.3 新增的验证手段：Unity C# 语法检查

因为本机没有 Unity 编辑器，我写了一个基于 Roslyn 的语法检查工具 `tools/SyntaxCheck`：
用 `CSharpSyntaxTree.ParseText` 解析全部 Unity C# 文件，只报语法诊断。

```
> dotnet run --project tools/SyntaxCheck -- unity/Assets
检查了 28 个 C# 文件，发现 0 个语法错误。
```

它**不能替代** Unity 编译器的类型检查（那需要 UnityEngine / AR Foundation 的程序集），
但能挡住括号不配对、少分号这类低级错误。README 里已如实说明这个边界。

### 5.4 测试不通过（红灯）记录汇总（本步）

所有改动都遵循「先写失败用例，再改实现」：

| 层 | 新增用例 | 红灯结果 | 改完后的绿灯 |
| --- | --- | --- | --- |
| Go | 超长标题 / 超长描述 / 405 方法不允许 / 500 不泄露路径（4 个） | `FAIL: TestCreateIssueTooLongTitleReturns400` 等 **4 failed** | 29 passed |
| React | 响应体契约校验 3 个 + 请求取消 2 个 | **5 failed**（`getRequestTimeoutMs is not a function` 等） | 30 passed |
| React | 并发刷新只认最后一次响应 | 新用例先写 | 30 passed |
| Unity | 后端地址协议头 5 个 | **1 failed**（无协议头地址没有被拒绝） | 61 passed |

### 5.5 最终测试结果

| 层 | 用例数 | 结果 |
| --- | --- | --- |
| Go 后端 | 29 | ✅ |
| React | 30 | ✅ |
| Unity 纯逻辑 | 61 | ✅ |
| Unity C# 语法检查 | 28 个文件 | ✅ 0 错 |
| 三端端到端 | 25 条断言 | ✅ |
| **合计** | **145 个自动化断言** | ✅ 全绿 |

### 5.6 提交
- 提交信息：`fix: 修复代码评审发现的缺陷（Unity 编译阻塞、误放标记、路径泄露、白屏、挂死），并补齐 README/AI_LOG/测试报告`

---

## Step 6 — Unity 真实编译、真机打包与真机验证

**背景**：Step 5 结束时 Unity 代码只做到了「源码级 API 核对」（对照 AR Foundation 5.1.5 包内源码逐个确认签名），
因为当时机器上没装 Unity 编辑器。本步把环境补齐，**把「核对」升级成「真编译 + 真机跑」**。

### 6.1 环境准备（全部实测通过）

| 项 | 版本 / 路径 |
| --- | --- |
| Unity Hub | 3.3.7.0（`C:\Program Files\Unity Hub`） |
| Unity 编辑器 | **2022.3.62f3c1**（中国区 LTS 发布版，`E:\Unity\Editors`） |
| Android Build Support | 已装（`UnitySetup-Android-Support-for-Editor`） |
| OpenJDK | 11.0.14.1+1 |
| Android SDK | platform-tools 32 / build-tools 34 / platforms android-34 / cmdline-tools 6.0 |
| Android NDK | r23b（23.1.7779620） |
| Go | 1.27.3（写入用户 PATH） |
| .NET SDK | 8.0.427（写入用户 PATH） |
| 手机 | vivo V2302A，Android 16 (API 36)，arm64-v8a，已装 `com.google.ar.core` |

安装源用的是 Unity 中国区 CDN（`download.unitychina.cn`，实测 37 MB/s，3.6 GB 编辑器 1.7 分钟下完）。

### 6.2 真实编译（第一次就通过）

```powershell
Unity.exe -batchmode -quit -nographics -projectPath E:\简历题目\unity -logFile compile.log
```

结果：**退出码 0，0 个 `error CS`，0 个 `warning CS`**，生成 4 个程序集：
`UnityAR.Core.dll` / `UnityAR.Runtime.dll` / `UnityAR.Editor.dll` / `UnityAR.Tests.EditMode.dll`。

这一步同时验证了 Step 5 里 AI 评审指出的**两个编译阻塞问题确实修对了**：
- `namespace UnityAR.Input` 遮蔽 `UnityEngine.Input`（CS0234）
- `TMP_InputField.viewport` 在 TMP 3.0.6 中应为 `textViewport`（CS1061）

### 6.3 Unity Test Runner 真实执行（61 个用例）

```powershell
Unity.exe -batchmode -runTests -testPlatform EditMode -projectPath E:\简历题目\unity -testResults results.xml
```

```
total : 61   passed : 61   failed : 0   skipped : 0   result : Passed
```

### 6.4 打包 APK 过程中踩到的 5 个坑（每一个都写清了现象与原因）

| # | 现象 | 根因 | 修法 |
| --- | --- | --- | --- |
| 1 | `An error occurred while resolving packages: com.unity.test-framework.tests cannot be found` | `manifest.json` 里多写了 `"testables": ["com.unity.test-framework"]`，它会连带解析一个不存在的相关包 | 删掉 `testables` 段 |
| 2 | `No valid Unity Editor license found` | 没激活 | 用 `Unity.Licensing.Client` 走 Unity 账号激活（`.ulf`） |
| 3 | `Invalid project path ... contains non-ASCII characters` | **工程路径 `E:\简历题目\unity` 含中文**，Android 构建工具链（Gradle/aapt/NDK）不支持非 ASCII 路径 | 复制到纯 ASCII 目录 `E:\UnityBuild\UnityAR` 再打包，`scripts/build_android.ps1` 已封装 |
| 4 | `BuildFailedException: You have enabled the Vulkan graphics API, which is not supported by ARCore` | Unity 2022.3 的 Android 默认图形 API 含 Vulkan | 显式设为 `GraphicsDeviceType.OpenGLES3` |
| 5 | `Android NDK not found` | Unity 期望 NDK 根目录**直接**含 `ndk-build.cmd`，而解压出来是 `NDK/android-ndk-r23b/` | 把内容上提一层；脚本里加了 `FindNdkRoot()` 自动探测 |
| 6 | `ARCore requires Gradle 5.6.4 or later ... unless the active build platform is Android` | 当前活动构建平台不是 Android | 打包前 `EditorUserBuildSettings.SwitchActiveBuildTarget(Android)` |

最终 APK：**30 MB，IL2CPP + arm64-v8a**，`aapt2 dump badging` 验证：

```
package: name='com.example.unityar.inspect' versionCode='1' versionName='1.0'
sdkVersion:'24'   targetSdkVersion:'34'
uses-permission: name='android.permission.INTERNET'
uses-permission: name='android.permission.CAMERA'
uses-feature: name='android.hardware.camera.ar'        ← ARCore 已设为必需
native-code: 'arm64-v8a'
lib/arm64-v8a/libUnityARCore.so / libarcore_sdk_c.so / libarcore_sdk_jni.so / libil2cpp.so
```

### 6.5 真机上发现的 2 个问题（只有真机才暴露）

#### 问题 A：界面中文全部显示成方框 □□

**现象**：APK 装到手机后，相机画面正常、平面检测正常（顶部显示「检测到 1 个水平平面」），
但**所有中文都是方框**。

**根因**：TMP Essential Resources 自带的默认字体是 LiberationSans SDF，**只有拉丁字形，没有中文**。
编辑器里如果连 TMP 资源都没导入，界面是全空白，反而看不出是「缺字形」。

**修法**：
1. 引入开源中文字体 **LXGW 霞鹜文楷**（SIL OFL 1.1，可自由分发商用）到 `unity/Assets/Fonts/`；
2. 新增 `Assets/Editor/ChineseFontSetup.cs`，用 `TMP_FontAsset.CreateFontAsset(ttf)` 生成**动态**字体资源
   （动态模式下字形图集运行时按需生成，只有用到的字进图集，不撑包体也不会缺字）；
3. 通过 `SerializedObject` 改 `TMP_Settings` 的私有字段 `m_defaultFontAsset` / `m_defaultFontAssetPath`
   （`defaultFontAsset` 属性是只读的）；
4. 遍历场景里所有 `TMP_Text` 显式替换字体。

**注意执行顺序**：必须在 `BuildScene()` **之后**做 —— 生成场景会新建 TMP 文本，先换字体会被覆盖。

#### 问题 B：UI 完全收不到点击（按钮无反应、输入框不聚焦）

**现象**：点按钮无反应、点输入框键盘不弹；但 AR 放置判定却能正确提示「点击了界面控件，已忽略该操作」。

**定位过程**：写了一个诊断方法 `EventSystemDiagnostics.Diagnose()`，在真 Unity 里打印事实：

```
编译期宏 ENABLE_INPUT_SYSTEM      : 未定义
编译期宏 ENABLE_LEGACY_INPUT_MANAGER: 已定义（旧输入系统）
运行时 activeInputHandler            : 0  (0=旧 1=新 2=Both)
场景里的 EventSystem                : EventSystem
  挂载的输入模块数量               : 1
    - UnityEngine.EventSystems.StandaloneInputModule  enabled=True
Canvas 的 GraphicRaycaster          : 有
```

配置本身是自洽的（都是旧输入系统），但**工程装了 Input System 包却只启用旧输入系统**，
这套组合在 Android 上 UI 收不到输入。注意我的 `RaycastAll` 判定是**当场重算**的，
所以它能正确识别 UI 命中 —— 这也反过来证明「场景结构没问题，问题在事件系统的输入模块」。

**修法**：
1. `ProjectSettings.asset` 里 `activeInputHandler: 0` → `2`（Both），两个宏都定义；
2. `SceneBuilder` 里**先移除已有的 `BaseInputModule`**，再挂 `InputSystemUIInputModule`，
   并**显式调用 `AssignDefaultActions()`**（`OnEnable` 里虽然会调，但脚本生成场景这条路径要确保万无一失）；
3. 改完后重新诊断确认：

```
编译期宏 ENABLE_INPUT_SYSTEM      : 已定义（新输入系统）
运行时 activeInputHandler            : 2  (0=旧 1=新 2=Both)
    - UnityEngine.InputSystem.UI.InputSystemUIInputModule  enabled=True
      actionsAsset                 : DefaultInputActions
```

修完重新打包装机，**输入框能正常聚焦、软键盘弹出**。

### 6.6 真机上的完整闭环验证 ✅

用 `adb reverse tcp:8080 tcp:8080` 把手机的 `localhost:8080` 隧道到电脑的 Go 后端
（这样不受 WiFi、IP 变化、防火墙影响），然后：

| 步骤 | 验证结果 | 截图 |
| --- | --- | --- |
| App 启动，相机画面正常 | ✅ AR 相机出图 | `docs/screenshots/07-真机中文界面.png` |
| AR 平面检测 | ✅ 顶部提示「检测到 1 个水平平面，点击地面放置标记」 | 同上 |
| 点击界面控件 | ✅ 提示「点击了界面控件，已忽略该操作」——**任务书要求的「点按钮不误放标记」在真机上被验证** | `docs/screenshots/08-真机放置标记.png` |
| 手机提交问题 | ✅ 提示「上报成功，问题编号 ISS-1793287849089-G6AGFCNXLPMR4」，**Go 后端真的收到了** | 同上 |
| React 网页看到该条 | ✅ 列表第 4 行就是它 | `docs/screenshots/09-React看到真机上报的数据.png` |
| React 改状态 | ✅ 下拉框改成「已解决」，后端确认 `status: resolved`、`updatedAt` 已更新 | `docs/screenshots/10-React改状态为已解决.png` |

> 备注：截图里手机端那条的标题是「1」、描述是「2」，
> 是**人工在真机上敲的**（当时正在手机上操作授权），不是 App 的问题；
> 我用 `adb shell input text` 注入的文本会被这台机器的中文输入法吞掉，属于 adb/IME 的限制。

### 6.7 交付物更新

- 新增 `scripts/build_android.ps1`：自动处理「复制到 ASCII 路径 → 打包 → APK 拷回」的流程；
- 新增 `unity/Assets/Editor/ProjectSetup.cs`：一键准备（TMP 资源 / 中文字体 / 生成场景 / Android 设置 / ARCore Loader）+ 一键打包；
- 新增 `unity/Assets/Editor/ChineseFontSetup.cs`：中文字体资源生成；
- 新增 `unity/Assets/Editor/EventSystemDiagnostics.cs`：输入系统与 EventSystem 配置的诊断工具；
- `unity/Assets/Scenes/ARInspect.unity`、`unity/Assets/XR/**`、`unity/Assets/TextMesh Pro/**` 已纳入版本库；
- `unity/ProjectSettings/` 现在是 Unity 真实生成的完整配置（`activeInputHandler: 2`）。

---

## Step 7 — WebSocket 实时推送、AI 智能分诊、前端重设计、Unity 历史面板

**目标**：把上一轮评审提的加分项做掉，并把「手机上放不了标记」这个核心问题修好。

### 7.1 AI 智能分诊（后端，纯本地算法）

**业务价值定位**：巡检现场最大的浪费是**同一处隐患被反复上报**，
其次是经验不足的巡检员定级不准。所以这个 AI 功能不是「接个聊天机器人」，
而是解决两个具体的业务问题：

1. **重复报修识别** —— 新问题与历史问题在语义和空间上都接近时，提示「这可能重复报修了」；
2. **优先级建议** —— 结合关键词规则和历史同类问题的定级分布，给一个建议值。

**为什么不用大模型 API**：
- 巡检现场经常没有网络，模型不可用就等于功能不可用；
- 重复报修判断本质是**检索**问题（与历史库比对），不是生成问题；
- 纯算法可单元测试、毫秒级响应、结论可解释（能给出理由）。

**实现**（`server/internal/analysis/`）：
- 中文用 **2-gram** 分词（中文没有空格分词，bigram 对「渗水/水渍/配电」这类术语足够准），
  英文数字按非字母数字切分并过滤停用词；
- 相似度 = `0.65 × TF-IDF 余弦 + 0.35 × Jaccard`；
- 重复判定：相似度 ≥ 0.55 **且** 3D 距离 ≤ 5 米 → `high`；
- 优先级建议 = 关键词规则命中项 与 历史相似问题分布 取更严重的一档；
- 类别识别：消防/电气/结构/渗漏/破损/设备/卫生 七类规则表。

**13 个单元测试**（先写测试再实现）。红灯时暴露了一个真 bug：
**规则的严重度权重（3~10）与优先级的序数（1~3）混用了同一把尺子**，
导致「消防（权重 10）」永远压过「high（序数 3）」，历史分布永远影响不了结果。
修正为统一到序数再比较。

**新接口**：`POST /api/analysis/triage`（8 个接口测试）。

### 7.2 WebSocket 实时推送

**问题**：Unity 在手机上上报问题后，PC 管理端只能手动刷新才看得到，谈不上实时协同。

**实现**：
- `server/internal/hub`：广播中心，每连接一个写协程，读写分离，发送队列带缓冲；
- `GET /ws`（别名 `/api/ws`）：连接即发 `hello`，之后推 `issue.created` / `issue.updated` / `issue.deleted`；
- 服务端 25 秒心跳一次，客户端可发 `ping` 换 `pong`；
- 前端 `web/src/api/realtime.ts`：断线**指数退避**重连（1s→2s→4s→8s，上限 15s），
  收到事件**就地更新本地列表**（不整表重拉，避免列表闪烁），并有「实时已连接 / 重连中」状态指示。
- 前端 14 个 WebSocket 单测（用 mock 的 WebSocket 类测连接、事件分发、断线重连、退避序列）。

**实测（真实 Go 后端 + 真实浏览器）**：
```
从手机侧上报 → 页面不刷新，列表里立刻出现新问题
远端改状态    → 页面上该行的下拉框同步变成 resolved
```

**抓到一个真 bug**：日志中间件用 `statusRecorder` 包装了 `ResponseWriter`，
而它**没有实现 `http.Hijacker`**，WebSocket 握手直接 500
（`websocket: response does not implement http.Hijacker`）。
单元测试没覆盖到，因为测试直接用 `api.Server` 当 handler，**没套中间件**；
只有真正把进程跑起来才会暴露。
→ 把中间件挪进 `internal/api` 并让 `statusRecorder` 实现 `Hijack()`，同时补了一条
「套了中间件的真实 HTTP 服务也能完成 WS 握手」的回归测试。

### 7.3 前端重设计（按 `frontend-design` skill）

原版是深色 + 表格，用户反馈「有点丑、偏明亮风格一点」。按 skill 的方法论重做：
- **设计主张**：把管理端做成「**现场巡检台账**」——牛皮纸/米白底 + 网格纹理 + 编辑式排版，
  数字用等宽字体的表格化排布，像一本真的巡检台账而不是一个后台模板；
- **字体**：标题用有性格的西文 display 字体（中文回落 PingFang SC / 微软雅黑），
  数字与 ID 用等宽字体，强化「台账」的工业感；
- **色彩**：暖白纸面 + 墨黑文字 + 一个朱红作为唯一强调色（高优先级、进行中进度条都用它）；
- **交互**：进场 stagger、状态切换过渡、实时连接指示、Toast 提示。

实测：1450×1000 视口下无控制台报错，「实时已连接」指示正常。

同时修了一个布局问题：**描述列被挤成竖排**（表格列宽被标题/坐标列抢走），
调整了 `min-width` 与描述列的最小宽度。

### 7.4 Unity 端：历史问题列表 + 编辑、AI 分诊入口、标记样式

| 能力 | 实现 |
| --- | --- |
| **历史问题列表与编辑** | `IssueHistoryPanel`：运行时自建 UI 的全屏面板，列出全部历史问题（标题/优先级/状态/时间/坐标），每条可直接改成 待处理/处理中/已解决，或删除 |
| **AI 分诊入口** | 表单里加「智能分诊」按钮，调用后端分诊接口，展示建议优先级/类别/可信度/重复风险/关键词/理由/相似问题；**建议只是建议**，要另点「采纳建议优先级」才改表单 |
| **标记样式按优先级+状态** | 优先级决定色相（低绿/中黄/高红），状态决定形态（待处理实心 / 处理中放大 1.25× / 已解决半透明 0.35 且缩小 0.75×） |

**Unity 侧新增纯逻辑（都可单测）**：
- `Core/Json.cs` —— 极简 JSON 解析器（`JsonUtility` 不支持顶层数组、字段缺失会抛异常）；
- `Core/IssueSummary.cs` —— 历史列表与分诊结果的解析；
- `Core/RayPlaneIntersection.cs` —— 射线 × 平面求交。

### 7.5 修「手机上放不了标记」

**定位过程**（这次没有靠猜）：
1. 真机上确实放不出标记；
2. 加运行时日志，打印每次点击的坐标、`EventSystem` 是否存在、UI 命中数、判定结果；
3. 日志显示 `uiHits=0` —— 说明**点击没有被识别成 UI 点击**，于是走到了「放置标记」分支；
4. 但同时发现 `finger=1..6` 全是真实手指 id，**`adb input tap` 注入的 legacy 事件根本进不了 App**
   （切到新输入系统后 adb 注入走的是 legacy 通道）。
   → 「按钮点了没反应」其实是我用 adb 驱动的**误判**，按钮本身是好的。

**两个真实缺陷**：
1. **`EventSystem.RaycastAll` 在有嵌套 Canvas 时拿不到画布上的 GraphicRaycaster**，`uiHits` 恒为 0
   → 「点按钮不误放标记」这条要求实际上**没被满足**（点按钮会同时尝试放置标记）。
   → 改为**直接问画布上的 GraphicRaycaster**，并把它序列化进 `PlacementController`。
2. **`IssueHistoryPanel` 自建嵌套 Canvas**（带 CanvasScaler + 自己的 GraphicRaycaster），
   既干扰画布缩放，又让上面那个全局 raycaster 列表更不可靠。
   → 改为挂在**已有 UI 画布**下（不新建 Canvas），并 `SetAsLastSibling()` 保证画在最上层。

**另外一个真机上才暴露的坑**：标记渲染成 **品红色 (255,0,255)**（Unity 的「shader 缺失」色）。
根因：标记是 `GameObject.CreatePrimitive` 运行时创建的，用的内置 Default-Material，
它引用的 **Standard shader 默认不在「始终包含的着色器」里**，而新建工程没有任何材质引用它
→ 打包时被剔除 → 真机上没 shader 可用。
→ 新增 `ArProjectSettings.ConfigureAlwaysIncludedShaders()` 把 Standard 加进去，
同时 `PlacementController` 给标记显式建一份 Standard 材质（双保险）。

**放置逻辑本身的加固**：原来只依赖 `ARRaycastManager.Raycast`，
而 AR Foundation 的 `TrackableType.Planes` 里含 ARCore 原生层**不支持**的
`PlaneWithinInfinity` 标志位，掩码是直接透传给原生层的，部分机型会返回空结果
—— 这正是「明明检测到地面平面却怎么点都放不下」的原因。
→ 改成两条路径：① 只请求 ARCore 明确支持的 `PlaneWithinPolygon`；
② 失败则用**已检测到的水平平面自己求交**（`Core/RayPlaneIntersection`，10 个单元测试覆盖）。

### 7.6 本步的测试与验证

| 层 | 用例数 | 结果 |
| --- | --- | --- |
| Go 后端 | 55（新增 analysis 13 + hub 5 + 接口 8） | ✅ |
| React | 62（新增 realtime 14 + triage 4 + 分诊面板 10 + 实时 App 4） | ✅ |
| Unity 纯逻辑（dotnet） | 89 | ✅ |
| **Unity Test Runner（真 Unity）** | **89** | ✅ |
| 三端端到端 | 25 | ✅ |
| **合计** | **231** | ✅ |
