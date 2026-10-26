# 测试报告

> 全部测试均在开发机上**实际执行过**，输出为原始结果。
> 最后一次全量执行：2026-10-26。

## 总览

| 层 | 工具 | 用例数 | 结果 |
| --- | --- | --- | --- |
| Go 后端单元 / 集成测试 | `go test ./...` | 29 | ✅ 全部通过 |
| Go 静态检查 | `go vet ./...` / `gofmt -l .` | — | ✅ 无告警、无格式差异 |
| React 单元 / 组件测试 | Vitest + Testing Library | 30 | ✅ 全部通过 |
| React 类型检查 | `tsc --noEmit` | — | ✅ 0 错误 |
| React 生产构建 | `vite build` | — | ✅ 构建成功 |
| Unity 纯逻辑单元测试 | NUnit（`dotnet test`） | 61 | ✅ 全部通过 |
| Unity C# 语法检查 | Roslyn（`tools/SyntaxCheck`） | 28 个文件 | ✅ 0 个语法错误 |
| 三端端到端 | Playwright + 真实 Go 进程 | 25 条断言 | ✅ 全部通过 |
| **合计** | | **145** | ✅ |

---

## 1. Go 后端（29 个用例）

命令：

```bash
cd server
go test ./... -v
go vet ./...
gofmt -l .
```

```
?   	github.com/xuanliu805-jpg/Unity-AR/server/cmd/server	[no test files]
ok  	github.com/xuanliu805-jpg/Unity-AR/server/internal/api	1.219s
ok  	github.com/xuanliu805-jpg/Unity-AR/server/internal/model
ok  	github.com/xuanliu805-jpg/Unity-AR/server/internal/store
```

### `internal/model/issue_test.go`（4 个）
| 用例 | 验证内容 |
| --- | --- |
| `TestParsePriority` | `low/medium/high` 合法；`urgent` / `LOW` / `1` / 空串 / 带空格 全部非法（严格匹配，不折叠大小写） |
| `TestParseStatus` | `open/in_progress/resolved` 合法；`closed` / 空串 非法 |
| `TestNewIDIsUniqueAndPrefixed` | 连续生成 2000 个 ID 互不重复，且都带 `ISS-` 前缀 |
| `TestNewIDDifferentAcrossMilliseconds` | 不同毫秒的 ID 不同，且包含毫秒时间戳 |

### `internal/store/store_test.go`（8 个）
| 用例 | 验证内容 |
| --- | --- |
| `TestNewFileStoreMissingFileStartsEmpty` | 数据文件不存在时视为空库，不报错 |
| `TestCreateAssignsIDStatusAndTimestamps` | 生成 ID、默认 `open`、时间戳非零、输入字段原样保留 |
| `TestListReturnsCreatedIssuesInOrder` | 列表按创建顺序返回 |
| `TestUpdateStatusChangesStatusAndUpdatedAt` | 状态变更、`updatedAt` 变新、`createdAt` 不变 |
| `TestUpdateStatusUnknownID` | 未知 ID 返回 `ErrNotFound` |
| `TestDeleteIssue` | 删除成功 / 删除不存在返回 `ErrNotFound` |
| `TestDataPersistsAcrossRestart` | **新建 Store 重新读盘后数据仍在**（对应任务书 R7） |
| `TestPersistedFileIsValidJSONWithExpectedShape` | 落盘 JSON 含全部 8 个字段，`position` 内含 `x/y/z` |

### `internal/api/server_test.go`（17 个）
| 用例 | 验证内容 |
| --- | --- |
| `TestHealthEndpoint` | `GET /api/health` → 200 + `{"status":"ok"}` |
| `TestCreateIssueSuccess` | 201 + 回显 + 坐标原样 + 默认 `open` + 时间戳存在 |
| `TestCreateIssueEmptyTitleReturns400` | 空串与纯空格都 400，`error.code=invalid_argument`、`field=title` |
| `TestCreateIssueInvalidPriorityReturns400` | `urgent`/`LOW`/`1`/`null` 都 400，`field=priority` |
| `TestCreateIssueMissingPriorityReturns400` | 缺 priority → 400 |
| `TestCreateIssueMalformedJSONReturns400` | 非法 JSON → 400 `invalid_json` |
| `TestCreateIssueTooLongTitleReturns400` | 标题超 200 字符 → 400，`field=title` |
| `TestCreateIssueTooLongDescriptionReturns400` | 描述超 2000 字符 → 400，`field=description` |
| `TestListIssues` | 200 + `issues` 数组 + `count` |
| `TestUpdateStatusSuccess` | 200 + 状态已变 + 列表里也变了（真的落库了） |
| `TestUpdateStatusInvalidValueReturns400` | `closed`/`OPEN`/空串 → 400，`field=status` |
| `TestUpdateStatusUnknownIDReturns404` | 未知 ID → 404 `not_found` |
| `TestDeleteIssueEndpoint` | 删除 → 200，列表里消失 |
| `TestUnknownMethodOnKnownPathReturns405` | `PUT`/`DELETE /api/issues` → 405 `method_not_allowed` |
| `TestUnknownRouteReturns404` | 未知路径 → 404 |
| `TestCORSPreflightAndHeaders` | 预检 204 + `Allow-Origin` + `Allow-Methods` 含 `PATCH` |
| `TestInternalErrorDoesNotLeakServerPaths` | **500 响应不含路径 / 文件名 / 操作系统错误原文** |

### 真实进程冒烟（`server.exe`，非自动化断言，人工核对）
| 用例 | 实际响应 |
| --- | --- |
| `GET /api/health` | `{"issues":0,"service":"unity-ar-inspection","status":"ok"}` |
| `POST /api/issues` 合法体 | 201，`{"issue":{"id":"ISS-1792988466562-SAAEVOZKQGIAC",...,"status":"open","position":{"x":0.42,"y":0.03,"z":1.26}}}` |
| `POST` 标题 `"   "` | 400 `{"error":{"code":"invalid_argument","message":"标题(title)不能为空","field":"title"}}` |
| `POST` priority `urgent` | 400，`field=priority` |
| `POST` 非法 JSON | 400 `invalid_json` |
| `PATCH .../status` = `in_progress` | 200，`updatedAt` 已更新 |
| `PATCH` status=`closed` | 400，`field=status` |
| `PATCH` 未知 ID | 404 |
| `OPTIONS` 预检 | 204 + `Access-Control-Allow-Origin: *` |
| **重启进程后再查** | `{"count":1,"issues":[{...,"status":"in_progress",...}]}` —— **数据与状态修改都还在** |

---

## 2. React 管理端（30 个用例）

命令：

```bash
cd web
npm run test:run
npm run typecheck
npm run build
```

```
✓ src/api/__tests__/api.test.ts        (19 tests) 141ms
✓ src/components/__tests__/IssueList.test.tsx (5 tests) 167ms
✓ src/__tests__/App.test.tsx           (6 tests) 285ms

Test Files  3 passed (3)
     Tests  30 passed (30)
```

```
> npm run typecheck
> tsc -p tsconfig.json --noEmit && tsc -p tsconfig.node.json --noEmit
（0 错误）

> npm run build
✓ 37 modules transformed.
dist/index.html                   0.40 kB │ gzip:  0.32 kB
dist/assets/index-BA_HmY-e.css    4.27 kB │ gzip:  1.52 kB
dist/assets/index-BM56p9TS.js   151.98 kB │ gzip: 49.75 kB
✓ built in 717ms
```

### `src/api/__tests__/api.test.ts`（19 个）
- **URL 拼接**：`getApiBaseUrl` / `apiUrl` 的默认值、结尾斜杠、缺少前导斜杠。
- **`fetchIssues`**：解析 `{issues,count}`；缺 `count` 时用 `issues.length` 兜底；
  后端返回 `{error:{...}}` 时抛 `ApiError`（带 message / code / field / status）；
  fetch reject 时抛 `NetworkError`。
- **`updateStatus`**：发的是 PATCH、body 为 `{"status":"..."}`、Content-Type 正确；后端报错抛 `ApiError`；
  **返回 200 但 body 缺 `issue` 或 `issue.id` 类型不对时抛 `ApiError`**（防止 `undefined.id` 导致白屏）。
- **`deleteIssue`**：发 DELETE、返回被删的 ID；**返回 200 但缺 `deleted` 时抛 `ApiError`**。
- **超时**：默认 8000 ms、可被 `VITE_API_TIMEOUT_MS` 覆盖；
  **后端永不响应时按时抛 `NetworkError`（不是永远转圈）**；**超时后会中止底层 fetch**。
- **取消**：上游 signal 被 abort 时抛 `CancelledError`，**不会**被误报成「后端没启动」。

### `src/components/__tests__/IssueList.test.tsx`（5 个）
渲染标题/优先级/状态；下拉框选 `resolved` 会调用 `updateStatus` 并更新显示；
`updateStatus` 失败时**回滚**并显示错误信息；删除二次确认；空态。

### `src/__tests__/App.test.tsx`（6 个）
- **后端不可用（fetch reject）时显示错误提示而不是崩溃/白屏**，且此时**不显示「暂无问题」**
  （显示「数据加载失败，请点击右上角「刷新」重试」），表头与「重试」按钮照常存在。
- 空列表显示「暂无问题」空态。
- 渲染后端返回的问题并在顶部显示总数。
- 可以按状态筛选。
- 后端返回 200 但响应体不符合契约时不会白屏。
- **并发刷新时只有最后一次请求的结果生效**（旧响应不会覆盖新响应）。

---

## 3. Unity 纯逻辑（61 个用例）

命令：

```bash
dotnet test tools/UnityLogicTests/UnityLogicTests.csproj
```

```
已通过! - 失败: 0，通过: 61，已跳过: 0，总计: 61
```

这些用例同时可以在装有 Unity 的机器上通过 **Window → General → Test Runner → EditMode → Run All** 运行
（源文件是同一份，见 `tools/UnityLogicTests/UnityLogicTests.csproj` 的 `Compile Include`）。

| 测试文件 | 用例数 | 覆盖内容 |
| --- | --- | --- |
| `IssueValidatorTests.cs` | 12 | 空标题 / 纯空格 / null 标题 / 超长标题 / 描述可选 / 超长描述 / 三种合法优先级 / 非法优先级 / 坐标 NaN / 坐标 Inf / null 草稿 |
| `IssuePayloadBuilderTests.cs` | 9 | 四个字段齐全、不含后端生成的字段、首尾空白裁剪、双引号转义、反斜杠转义、换行转义、零坐标、负坐标、null 参数抛异常 |
| `IssueEnumsTests.cs` | 7 | 优先级/状态集合与后端一致、非法值被拒、中文显示名一一对应 |
| `ApiEndpointsTests.cs` | 12 | 路径拼接、ID 转义、后端地址去尾斜杠/去空白/空值回落、局域网地址拼接 |
| `BackendConfigSchemeTests.cs` | 5 | 无协议头回落默认、`http://` 不被削成 `http:`、`http`/`https` 完整保留、拼出的 URL 合法 |
| `PlacementGateTests.cs` | 9 | 触点在 UI 上 / 面板打开 / 无平面 / 非点击 → 四种拒绝原因 + 判定优先级 + 每种原因都有中文提示 + null 上下文 |
| `ApiResponseParserTests.cs` | 8 | 取问题 ID / 取错误 message / 取错误 field / 转义还原 / 健康检查识别 |

---

## 4. 三端端到端（25 条断言）

命令：

```bash
node scripts/e2e.mjs
```

脚本自己会：编译并启动 Go 后端 → 启动 Vite → 用无头 Chromium 跑完整流程 → 关掉所有进程。

```
[e2e] 4/7 校验后端参数校验与错误契约
  ✔ 非法 priority 返回 400
  ✔ 错误响应结构符合契约 {error:{code,message,field}}
  ✔ 空标题返回 400
  ✔ 空标题的 field 是 title
  ✔ 未知问题 ID 返回 404
[e2e] 5/7 模拟 Unity 上报问题
  ✔ Unity 上报返回 201
  ✔ 后端生成了问题 ID
  ✔ 默认状态为 open
  ✔ 坐标原样保存
[e2e] 6/7 打开 React 管理端并验证
  ✔ React 列表里看得到 Unity 上报的标题
  ✔ 顶部显示问题总数 1
  ✔ 列表里显示了优先级「高」
  ✔ 状态下拉框的当前值是 open（不是「文本里出现过 open」）
  ✔ 列表里显示了位置坐标
  ✔ React 改状态后后端真的变成 resolved
  ✔ 界面上的下拉框同步变成 resolved
[e2e] 7/7 停掉后端，验证页面不白屏
  ✔ 后端不可用时页面显示错误提示
  ✔ 后端不可用时表头仍然渲染（没有白屏）
  ✔ 后端不可用时仍能看到错误条上的重试按钮
   重启后端，验证数据持久化
  ✔ Go 重启后数据仍然存在
  ✔ Go 重启后状态修改也保留（仍是 resolved）
  ✔ DELETE 接口返回 200
  ✔ 删除后列表里没有这条问题了
  ✔ 浏览器控制台没有 JS 报错
  ✔ 页面没有未捕获的异常

[e2e] 全部通过 ✅
```

截图见 `docs/screenshots/`。

---

## 5. 未覆盖的部分（如实说明）

| 项 | 原因 |
| --- | --- |
| Unity 编辑器内编译 | 本机没有安装 Unity Editor，无法运行 Unity 编译器 |
| Unity EditMode 用例在 Unity 内运行 | 同上；已用 `dotnet test` 在同一批源文件上真实执行 |
| 真机 AR（平面检测 / 放置 / 提交） | 本机没有 AR 手机 |
| Android / iOS 打包 | 同上 |
| 演示视频 | 同上 |
| Go 的并发压力测试 | 原型规模下用写锁 + 原子写已经足够，未做专门压测 |

替代措施：
- Unity 侧用 **Roslyn 语法检查**（`tools/SyntaxCheck`）覆盖语法层面的错误：28 个文件、0 错误；
- Unity 侧用**逐条对照 AR Foundation 5.1.5 / TextMeshPro 3.0.6 包内源码**核对 API 签名；
- Unity 侧的**业务逻辑**（校验、序列化、放置判定、响应解析、路径拼接）全部抽成不依赖 `UnityEngine` 的纯 C#，
  已经过 61 个自动化用例。
