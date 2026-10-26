# 任务规划与执行计划

> 题目：《Unity-AR研发实操题-候选人任务书-简略版》——现场巡检问题标注
> 目标：打通 `Unity AR 放置标记并上报 → Go 后端保存 → React 页面查看 → React 修改处理状态` 的最小闭环。

## 0. 需求拆解

从任务书中提取出的、可验收的最小需求清单：

| 编号 | 端 | 需求 | 验收标准 |
| --- | --- | --- | --- |
| R1 | Go | 服务运行状态检查 | `GET /api/health` 返回 200 |
| R2 | Go | 接收并保存 Unity 上报的问题 | `POST /api/issues` 返回 201 且落库 |
| R3 | Go | 返回已保存的问题列表 | `GET /api/issues` 返回全部问题 |
| R4 | Go | 按 ID 修改处理状态 | `PATCH /api/issues/{id}/status` |
| R5 | Go | 生成唯一 ID、默认状态 open | ID 全局唯一、`status=open` |
| R6 | Go | 标题为空 / 枚举非法 → 明确参数错误 | 400 + `{error:{code,message,field}}` |
| R7 | Go | 服务重启后数据仍存在 | 重启进程后列表不变 |
| R8 | Go | Unity 与 React 共用同一后端与同一份数据 | 两端指向同一 `:8080` |
| R9 | React | 从 Go 拉取问题列表 | 页面渲染标题/优先级/状态 |
| R10 | React | 可改为 open / in_progress / resolved | 状态下拉可改并回写后端 |
| R11 | React | 后端不可用时显示错误提示，不白屏 | 显示错误条 + 重试按钮 |
| R12 | Unity | 真机检测真实水平平面 | AR Foundation Plane Detection |
| R13 | Unity | 点击命中位置放置标记 | 生成基础几何体标记 |
| R14 | Unity | 填写并提交标题/描述/优先级/坐标 | 提交体含 `position{x,y,z}` |
| R15 | Unity | 提交成功/失败有明确提示 | UI Toast / 状态栏 |
| R16 | Unity | 点击按钮/输入框时**不**误放置标记 | UI 事件吞掉射线，命中检测只在 AR 面板 |
| R17 | 交付 | 三端源码 + README + AI_LOG | 仓库可访问、Git 历史完整 |

## 1. 技术选型

| 层 | 选型 | 版本 | 说明 |
| --- | --- | --- | --- |
| 后端 | Go 标准库 `net/http` | Go 1.27 | 零第三方依赖，`go vet` / `go test` 开箱即用 |
| 存储 | 本地 JSON 文件 + 原子写 | — | 任务书允许；实现简单，重启后数据存在 |
| 管理端 | React 18 + TypeScript + Vite | Node 18+/20+ | 任务书指定 React + TypeScript |
| 移动端 | Unity LTS + AR Foundation | Unity 6000.0 LTS | AR Foundation 5.x / UnityEngine.XR.ARFoundation |

### 接口约定（三端字段名严格一致）

```
GET    /api/health              -> 200 {"status":"ok","service":"...","issues":N}
GET    /api/issues              -> 200 {"issues":[Issue...],"count":N}
POST   /api/issues              -> 201 {"issue":Issue}
PATCH  /api/issues/{id}/status  -> 200 {"issue":Issue}
DELETE /api/issues/{id}         -> 200 {"deleted":"ISS-..."}

Issue = {
  "id": "ISS-<ms>-<rand>", "title": "...", "description": "...",
  "priority": "low|medium|high", "status": "open|in_progress|resolved",
  "position": {"x":0,"y":0,"z":0},
  "createdAt": "RFC3339", "updatedAt": "RFC3339"
}

错误 = { "error": { "code": "invalid_argument|invalid_json|not_found|internal_error",
                    "message": "人类可读的中文说明", "field": "title|priority|status|id" } }
```

## 2. 执行步骤（每步都是「先写测试 → 再实现 → 跑通 → 记录 → 提交」）

### Step 0 — 仓库骨架与文档框架
- 初始化 Git 仓库、`.gitignore`、目录结构。
- 产出：`docs/PLAN.md`、`docs/STEP_LOG.md`、`docs/TEST_REPORT.md`。
- 验收：`git status` 干净，文档可读。

### Step 1 — Go 后端（核心）
- **先写测试**：`internal/model/issue_test.go`、`internal/store/store_test.go`、`internal/api/server_test.go`。
- 实现：`internal/model`、`internal/store`（JSON 持久化）、`internal/api`（路由 + CORS + 参数校验）、`cmd/server/main.go`。
- 验证：`go test ./...` 全绿；`go vet` 无告警；真实起进程 curl 冒烟；**重启进程后数据仍在**。
- 提交：`feat(server): 实现巡检问题 Go 后端与持久化`。

### Step 2 — React 管理端
- **先写测试**（Vitest + Testing Library）：API 客户端解析/错误处理、列表渲染、状态切换、**后端不可用时不白屏**。
- 实现：Vite + React 18 + TS；列表页（标题/优先级/状态）、状态下拉改写、错误条 + 重试、优先级/状态筛选（加分项）。
- 验证：`npm test` 全绿；`npm run build` 通过；对接真实 Go 后端联调。
- 提交：`feat(web): 实现 React 管理端问题列表与状态流转`。

### Step 3 — Unity AR 移动端
- **先写测试**：把与 UnityEngine 无关的纯逻辑（表单校验、坐标转换、请求体组装、优先级枚举）抽到 `Assets/Scripts/Core/`，用 **NUnit（Unity Test Framework）** 写 EditMode 测试；同时用独立的 `tools/UnityLogicTests` .NET 测试工程链接同一批源文件，在没有 Unity 编辑器的机器上**真实跑通**这些用例。
- 实现：`ArSessionController`（AR Session / 平面检测）、`PlacementController`（射线命中 → 放置标记）、`IssueFormController`（输入与校验）、`ApiClient`（提交问题）、`MarkerStyler`（按优先级改标记颜色）。
- **重点覆盖 R16**：UI 事件触发时禁用射线，避免点按钮误放标记。
- 验证：`dotnet test` 全绿（纯逻辑）；Unity 工程用 Unity Test Runner 运行同一批 EditMode 用例（需在装有 Unity 的机器上执行，README 中说明）。
- 提交：`feat(unity): 实现 AR 平面检测、标记放置与问题上报`。

### Step 4 — 三端联调（最小闭环验证）
- 用真实 Go 进程 + 真实 React dev server + 模拟 Unity 上报请求，跑通「上报 → 列表看到 → 改状态 → 重启后仍在」。
- 产出：`docs/STEP_LOG.md` 中的联调记录 + `scripts/smoke_test.ps1` 一键冒烟脚本。
- 提交：`test(e2e): 增加三端联调冒烟脚本与记录`。

### Step 5 — 文档与交付
- `README.md`：版本、启动方式、真机配置后端地址、已完成/未完成/已知问题、接口说明。
- `AI_LOG.md`：本次全部 AI 交互记录（含 Prompt 原文、采纳情况、验证方式）。
- `docs/TEST_REPORT.md`：全部测试清单与结果。
- 提交：`docs: 补齐 README、AI_LOG 与测试报告`。

## 3. 已知限制与应对

| 限制 | 应对 |
| --- | --- |
| 当前机器没有安装 Unity 编辑器（只装了 Unity Hub 且无 Editor） | Unity 侧代码按官方模板手写并保证结构正确；把纯逻辑抽成不依赖 `UnityEngine` 的文件，用 `dotnet test` 真实执行；README 中明确说明真机验证需在装有 Unity 的机器上进行，且该部分未由本人实测 |
| 真机 AR 需要 Android + ARCore 设备 | 代码同时配置 ARCore 与 ARKit 依赖，`#if UNITY_ANDROID` / `#if UNITY_IOS` 分支处理后端地址配置；提供 Inspector 配置项与 `PlayerPrefs` 覆盖 |
| 演示视频需要真机 | 无法提供真实录屏，README 中如实说明「未完成」，并提供可复现的操作步骤脚本 |
