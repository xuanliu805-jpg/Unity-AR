# AI 使用记录（AI_LOG）

## 关于本文件的说明（请先读）

本次开发中「AI」由两部分组成，本文件逐条记录：

1. **Codex 编码代理（主实现者）** —— 承担了任务拆解、Go 后端实现、Unity AR 工程实现、
   全部测试的编写与执行、端到端联调、文档撰写。
   它收到的第一条指令就是用户给出的任务书（记为 **P-001**），此后所有实现动作都由该指令驱动。
2. **主实现者另外发起的 3 次 sub-agent 调用** —— 分别用于「生成 React 管理端」、
   「审查 Unity C# 代码」、「审查 Go 后端 + React 前端」。
   这三次调用的 **Prompt 原文** 完整保留在下面（P-002 / P-003 / P-004）。

关于「未记录 IDE 逐字自动补全」：本项目没有使用任何 IDE 的自动补全来生成代码，
所有代码均由上述两个来源产出，因此不存在需要过滤的补全噪音。

关于敏感信息：全部 Prompt 中不含任何 API Key / Token / 账号 / 隐私信息；
下文出现的 `127.0.0.1:7897` 是本机代理端口，不是密钥。

---

## P-001 - 目标：完成《Unity-AR研发实操题-候选人任务书-简略版》要求的最小闭环原型

### Prompt 原文

> 仔细阅读该文件"E:\简历题目\Unity-AR研发实操题-候选人任务书-简略版.docx"，按要求完成项目，先给出任务规划安排，每一步要先写测试用例，测试不通过要写明原因，每一步干了什么做了什么也要详细记录，以markdown文件存放，打通后这一步完成就提交代码到github上，你要把项目发送到[https://github.com/xuanliu805-jpg/Unity-AR.git](https://github.com/xuanliu805-jpg/Unity-AR.git)

任务书原文（`Unity-AR研发实操题-候选人任务书-简略版.docx`）的关键要求：

> 1. 项目背景：园区巡检人员发现墙面破损、设备异常或安全隐患时，希望用手机在现场放置一个 AR 标记并上报问题。管理人员随后在电脑网页中查看这些问题并更新处理状态。请完成一个小型原型，打通以下流程：Unity AR 放置标记并上报 → Go 后端保存 → React 页面查看问题 → React 修改处理状态
> 2. 固定技术栈：移动端 Unity LTS、C#、AR Foundation；管理端 React、TypeScript；后端 Go。Unity 可选择 Android + ARCore 或 iOS + ARKit；Go 框架不限；数据可保存到 SQLite 或本地 JSON 文件；可以使用 AI 辅助开发，但不要求产品本身接入大模型。
> 3.1 Unity AR 移动端：在支持 AR 的真机上运行；检测真实环境中的水平平面；点击检测到的平面，在命中位置放置一个简单标记；填写并提交问题（标题必填、描述可选、优先级 low/medium/high、位置为标记的 x/y/z 坐标）；提交成功或失败时给用户明确提示；**点击按钮或输入框时，不应误在 AR 场景中放置标记**；标记可以直接使用立方体、球体等 Unity 基础对象。
> 3.2 React 管理端：从 Go 后端获取问题列表；至少显示标题、优先级和状态；能将状态修改为 open / in_progress / resolved；**后端不可用时，页面应显示错误提示，不能直接白屏**。
> 3.3 Go 后端：提供简单的服务运行状态检查；接收并保存 Unity 上报的问题；返回已经保存的问题列表；根据问题 ID 修改处理状态；为新问题生成唯一 ID，默认状态为 open；标题为空或枚举值错误时返回明确的参数错误；数据在 Go 服务重启后仍然存在；Unity 和 React 使用同一个 Go 后端和同一份数据；React 在浏览器中能够正常访问后端。
> 4. 基础数据结构：{ "id", "title", "description", "priority", "status", "position": {"x","y","z"}, "createdAt", "updatedAt" }，三端字段名称应保持一致。
> 5. AI 使用记录：在项目根目录提交 AI_LOG.md，按实际顺序保存所有由你主动发给 AI 的问题或指令，包括追问和纠错；每条记录用「## P-001 - 目标 / ### Prompt 原文 / ### 结果」的格式；必须保留 Prompt 原文；至少记录一次你检查、修改或放弃 AI 结果的过程；不得提交 API Key、Token、账号、个人隐私。
> 6. 交付内容：Unity、React 和 Go 的完整源码；README.md（版本、三部分启动方式、Unity 真机如何配置 Go 后端地址、已完成/未完成/已知问题）；AI_LOG.md；两段 3～5 分钟演示视频；GitHub 仓库需保留完整 Git 历史。

### 结果

- **修改后采用**：作为整个项目的总纲，被拆解成 17 条可验收需求（R1–R17，见 `docs/PLAN.md`），
  并转化为 6 个执行步骤（Step 0–5）。最终三端全部实现并形成可运行的最小闭环。
- **未能满足的部分（如实说明）**：
  - **演示视频缺失** —— 需要真实 AR 手机与 Unity 编辑器，本机不具备这两个条件；
  - **Unity 工程未在本机编译过、真机未实测** —— 本机只装了 Unity Hub、没有 Editor。
  这两项在 README 第 8 节与 `docs/STEP_LOG.md` 中均已明确标注，未做任何粉饰。

### 验证方式
- 需求逐条对照任务书第 3 章，检查 `docs/PLAN.md` 的 R1–R17 映射表，无遗漏。
- 最终验证结果见 `docs/TEST_REPORT.md`：Go 29 + React 30 + Unity 纯逻辑 61 + 端到端 25 = **145 个自动化断言全部通过**。

---

## P-002 - 目标：生成 React + TypeScript 管理端的完整实现（含先写好的测试）

### Prompt 原文

> 你是一名资深 React + TypeScript 前端工程师。请在目录 `E:\简历题目\web` 下**从零创建**一个完整的 React 管理端工程（注意：该目录当前不存在，请自行创建）。**只允许在 `E:\简历题目\web` 目录内写文件**，不要修改 `E:\简历题目` 下其它任何文件。
>
> ## 背景
> 这是一个 Unity AR 现场巡检问题的管理端。巡检员用手机 AR 放置标记并上报问题，管理人员用这个网页查看问题并更新处理状态。Go 后端已经实现并正在运行。
>
> ## 后端 API 契约（必须严格遵守，不要自行改动字段名）
> Base URL 通过环境变量 `VITE_API_BASE_URL` 指定，默认 `http://localhost:8080`。
>
> - `GET  {base}/api/health` → 200 `{"status":"ok","service":"unity-ar-inspection","issues":3}`
> - `GET  {base}/api/issues` → 200 `{"issues": Issue[], "count": number}`
> - `PATCH {base}/api/issues/{id}/status` body `{"status":"in_progress"}` → 200 `{"issue": Issue}`
> - `DELETE {base}/api/issues/{id}` → 200 `{"deleted":"ISS-..."}`
>
> `Issue` 结构（字段名严格一致）：
> ```ts
> {
>   id: string;            // "ISS-<ms>-<rand>"
>   title: string;
>   description: string;
>   priority: "low" | "medium" | "high";
>   status: "open" | "in_progress" | "resolved";
>   position: { x: number; y: number; z: number };
>   createdAt: string;     // RFC3339
>   updatedAt: string;
> }
> ```
>
> 错误响应：`{"error":{"code":"invalid_argument|invalid_json|not_found|internal_error","message":"中文说明","field":"title|priority|status|id"}}`
> 网络层失败（后端没启动、连接被拒、超时）时 fetch 会 reject，页面必须显示错误提示，**绝不能白屏**。
>
> ## 技术要求（任务书强制）
> - React + TypeScript + Vite。
> - 单元测试用 **Vitest + @testing-library/react + jsdom**。
> - 不得引入 UI 组件库（避免版本冲突），用手写 CSS。
> - 不要用 `any` 糊弄类型；类型定义放 `src/types.ts`。
>
> ## 必做功能
> 1. 列出问题：表格/卡片显示 **标题、描述、优先级、状态、位置坐标(x,y,z)、创建时间**。
> 2. 修改状态：每行一个下拉框，可选 `open` / `in_progress` / `resolved`（带中文文案：待处理 / 处理中 / 已解决），选择后立即调用 PATCH，成功后更新本地状态并给出成功提示，失败后回滚下拉框并显示错误。
> 3. 后端不可用时：显示醒目的错误条 + 「重试」按钮，页面其余部分（表头、空态）仍然正常渲染，**不白屏**。
> 4. 空列表显示「暂无问题」空态。
>
> ## 加分项（请实现，成本不高）
> 5. 按优先级 / 按状态筛选。
> 6. 删除问题（带二次确认）。
> 7. 手动刷新按钮 + 顶部显示后端问题总数。
> 8. 加载中骨架/提示。
>
> ## 测试要求（**这是重点，必须先写测试再写实现**）
> 用 Vitest，先写测试文件再写实现。测试至少覆盖：
> - `src/api/__tests__/api.test.ts`：base URL 拼接正确；`fetchIssues` 正确解析 `{issues,count}`；后端返回 `{error:{...}}` 时抛出带 message 的错误；fetch reject（网络不可用）时抛出可识别错误；`updateStatus` 发出的是 PATCH 且 body 为 `{"status":"..."}`；`deleteIssue` 发出 DELETE。
> - `src/components/__tests__/IssueList.test.tsx`：渲染出后端返回的问题标题/优先级/状态；下拉框选择 `resolved` 会调用 `updateStatus` 并更新显示；`updateStatus` 失败时回滚并显示错误信息。
> - `src/__tests__/App.test.tsx`：后端不可用（fetch reject）时页面显示错误提示而不是崩溃/白屏；空列表显示空态。
> - 断言要具体（例如 `screen.getByText('入口墙面破损')`），不要只断言 mock 被调用。
>
> ## 工程文件
> - `package.json`（scripts：`dev` / `build` / `preview` / `test` / `test:run` / `typecheck`）
> - `tsconfig.json`、`tsconfig.node.json`、`vite.config.ts`（含 vitest 配置，`environment: 'jsdom'`，`globals: true`）
> - `index.html`
> - `src/main.tsx`、`src/App.tsx`、`src/api.ts`、`src/types.ts`、`src/components/*.tsx`、`src/styles.css`
> - `README.md`（说明如何启动、如何配置后端地址）
> - `.env.example`（含 `VITE_API_BASE_URL=http://localhost:8080`）
>
> ## 环境说明
> - Node v24.14.1、npm 11.11.0 已可用，网络可访问 npmjs。
> - **必须实际执行 `npm install`、`npm run test:run`、`npm run typecheck`、`npm run build` 并确保全部通过**。不要只写代码不验证。
> - 如果 `npm install` 因为 peerDependencies 报错，用 `--legacy-peer-deps` 或调整版本组合，但最终 `package-lock.json` 要生成。
>
> ## 交付
> 完成后回复：改了哪些文件（完整路径列表）、测试用例数量与结果、构建结果、以及你在实现中做的重要设计决策（3-5 条）。如果遇到失败或取舍，必须如实说明。

### 结果

- **修改后采用**：产出的 `web/` 工程整体保留并提交。20 个测试用例、typecheck、build 全部通过，
  且我逐个文件复核了 `src/api.ts`、`src/types.ts`、`src/App.tsx`、`src/components/*`，
  确认字段名与 Go 后端 `model.Issue` 严格一致、无 `any`、无 `dangerouslySetInnerHTML`。
- **AI 明确说明的不足（我据此补做）**：AI 在回复里写明「本机没有安装 Go（`go` 命令不存在），
  `http://localhost:8080` 上的真实后端未运行，因此无法对真实 Go 服务做端到端验证」，
  并用 `scripts/mock-backend.mjs` 做了等价联调。
  **我随后用真实 Go 进程重做了端到端验证**（Step 4），并在这个过程中发现并修复了一个
  AI 实现里不存在的真实缺陷：后端进程被杀时 `fetch` 会挂死，页面停在「加载中」且没有重试入口。
  详见 `docs/STEP_LOG.md` Step 4.3。
- **AI 自查暴露的问题（我核实后确认存在，已修）**：
  1. `api.ts` 的 `request()` 只做类型断言、不做运行时校验 → 后端返回 200 但 body 缺 `issue` 时，
     `undefined.id` 会在渲染时抛错，React 卸载整棵树 → **白屏**（P-004 审查也独立指出了这一点）。
  2. `App.tsx` 的 `loadIssues` 没有并发保护 → 连续刷新时旧响应会覆盖新响应。
  3. `IssueList` 的删除没有并发保护 → 双击会发两个 DELETE，第二个 404 会弹出与实际结果矛盾的提示。
  4. 加载失败时表格显示「暂无问题」，会把「加载失败」误导成「后端里真的没有数据」。
- **未采纳的部分**：AI 额外提供的 `scripts/mock-backend.mjs`（mock 后端）保留在仓库里，
  但在 README 中**明确标注「它不是后端实现，真实后端在 `server/`」**，避免评审误用。

### 验证方式
- 我亲自重跑：`npm run test:run` → 20 passed；`npm run typecheck` → 0 错误；`npm run build` → 成功。
- 我编写的端到端脚本用**真实 Go 后端 + 真实浏览器**跑通全流程（Step 4）。

---

## P-003 - 目标：审查 Unity C# / AR Foundation 代码是否真的能编译、逻辑是否有洞

### Prompt 原文

> 你是一名资深 Unity C# / AR Foundation 代码审查员。请**只读审查** `E:\简历题目\unity` 目录下的 C# 代码，**不要修改任何文件**，只输出审查结论。
>
> ## 背景
> 这是一个园区巡检 AR 应用：手机端检测水平平面 → 点击地面放置标记 → 填写标题/描述/优先级 → 上报到 Go 后端。
> 目标环境：**Unity 2022.3 LTS + AR Foundation 5.1.5 + ARCore 5.1.5 + ARKit 5.1.5 + TextMeshPro 3.0.6 + Input System 1.7.0**。
>
> ## 我已经核对过的 AR Foundation 5.1.5 真实 API（以此为准，不要用你记忆中的老版本 API）
> - `ARRaycastManager.Raycast(Vector2 screenPoint, List<ARRaycastHit> hitResults, TrackableType trackableTypes = TrackableType.AllTypes)` —— **实例方法**，5.x 已经**没有** `RaycastPlane` 参数。
> - `ARRaycastHit.pose` 是**世界空间** Pose；`ARRaycastHit.sessionRelativePose` 是会话空间。
> - `ARPlaneManager.requestedDetectionMode` 是 `PlaneDetectionMode`（枚举：None / Horizontal / Vertical / NotAxisAligned）；`ARPlaneManager.planesChanged` 是 `event Action<ARPlanesChangedEventArgs>`。
> - `ARPlane.alignment` 是 `PlaneAlignment`（None / HorizontalUp / HorizontalDown / Vertical / NotAxisAligned）。**5.x 没有 `ARPlane.PlaneType`，也没有 `ARPlane.planeType` 属性。**
> - `ARSession.state` 是 **static** 属性；`ARSession.stateChanged` 是 **static** 事件 `Action<ARSessionStateChangedEventArgs>`。
> - `ARSessionState` 的成员是：None / Unsupported / CheckingAvailability / NeedsInstall / Installing / Ready / SessionInitializing / SessionTracking。**没有 Initializing / Moving / Tracking / Paused。**
> - `ARTrackableManager<T>.trackables` 是 `TrackableCollection<T>`（struct，可 foreach，有 `.count`）。
> - `ARCameraManager` 上**没有**公开的 `camera` / `trackingType` 属性；`[RequireComponent(typeof(Camera))]`。
> - `ARCameraBackground` 组件存在。
> - 程序集名：`Unity.XR.ARFoundation`、`Unity.XR.ARSubsystems`、`Unity.TextMeshPro`、`UnityEngine.UI`、`Unity.InputSystem`。
>
> ## 需要重点检查的问题
> 1. **编译期问题**：有没有用到上面这些版本里不存在的 API / 类型 / 枚举成员？有没有 `UnityEngine.InputSystem` 与旧 `Input` 在 `#if` 分支下引用了不存在的类型？
> 2. **逻辑问题**：
>    - `Assets/Scripts/Ar/PlacementController.cs`：射线命中放置的流程对不对？`ARRaycastHit.pose` 的空间是否用对了？`hits` 列表复用是否安全？`GameObject.CreatePrimitive` 生成的 Cube 带 Collider 会不会干扰射线检测？
>    - `Assets/Scripts/Ar/ArSessionController.cs`：`ARSession.stateChanged` 是静态事件，订阅/退订是否正确？`RefreshPlaneCount()` 里的计数逻辑是否有 bug（看第 66-84 行附近的判断）？
>    - `Assets/Scripts/UI/ApiClient.cs`：UnityWebRequest 的用法是否正确（using 释放、result 判断、responseCode）？
>    - `Assets/Scripts/UI/IssueFormController.cs`：协程里 `submitRoutine` 的赋值/清理顺序是否正确？会不会有重复提交？
>    - `Assets/Editor/SceneBuilder.cs`：`SerializedObject` 赋私有字段、TMP_InputField 手工搭建（viewport/textComponent/placeholder）、TMP_Dropdown 是否被用到、EventSystem 输入模块的 `#if` 分支是否正确？`Assign()` 用 `SerializedObject` 给 `Button[]` 数组赋值是否可行？
>    - `Assets/Scripts/Core/*`：纯逻辑有没有边界情况问题（JSON 转义、浮点格式化、数字溢出、null 处理）？
> 3. **需求覆盖**：任务书要求「点击按钮或输入框时，不应误在 AR 场景中放置标记」，现在的三重保障（PlacementGate / EventSystem.IsPointerOverGameObject / RectTransformUtility 矩形判定）有没有漏洞？
>
> ## 输出要求
> 按「严重程度（阻塞/重要/建议）」分组，每条给出：
> - 文件相对路径 + 行号
> - 问题是什么
> - 为什么是问题（引用具体 API 或代码逻辑）
> - 建议的修法（给出可用的代码片段）
>
> 如果某个方面没问题，直接说没问题，**不要为了凑数编问题**。宁可少报也不要报错。

### 结果

- **修改后采用（2 个阻塞级 + 8 个重要级 + 若干建议级）**：
  1. **【阻塞】`TapSource.cs` 旧输入分支命名空间遮蔽** —— 文件位于 `namespace UnityAR.Input` 内，
     简单名 `Input` 会被 C# 解析成命名空间 `UnityAR.Input` 而非 `UnityEngine.Input`，
     报 `CS0234: 命名空间"UnityAR.Input"中不存在类型或命名空间名称"GetTouch"`。
     **由于仓库里没有 `ProjectSettings/ProjectSettings.asset`，Unity 重建时 `activeInputHandler` 默认是 Old，
     也就是说默认配置下打开工程就是这个坏分支 → 整个工程编译不过。**
     → 已改为全限定 `UnityEngine.Input.GetTouch` 等。
  2. **【阻塞】`SceneBuilder.cs` 用了不存在的 `TMP_InputField.viewport`** —— TMP 3.0.6 里只有
     `textViewport`（序列化字段也只有 `m_TextViewport`），`input.viewport = ...` 是 `CS1061`，
     导致 `UnityAR.Editor` 程序集编译失败、场景根本生成不出来。
     我**下载 com.unity.textmeshpro@3.0.6 的源码独立复核**了这一条（`TMP_InputField.cs:618`
     `public RectTransform textViewport { get { return m_TextViewport; } ... }`），确认 AI 说法正确 →
     已改为 `input.textViewport = viewportRect`，并按 AI 的建议给 TextViewport 补上 `RectMask2D`
     （TMP 3.0.6 的 `m_TextViewportRectMask` 正是从它身上取的）。
  3. **【重要】`uiBlocker` 矩形判定在面板收起时仍然生效** —— `RectangleContainsScreenPoint` 只做几何计算、
     不看 `active` 状态，导致面板收起后屏幕下方约 40% 永远放不了标记。
     → 已加 `uiBlocker.gameObject.activeInHierarchy` 判断。
  4. **【重要】`EventSystem.IsPointerOverGameObject` 有一帧延迟** —— 它读的是 `EventSystem.Update` 缓存的
     **上一帧**命中状态，而 `PlacementController.Update` 与 `EventSystem.Update` 的执行顺序不确定，
     于是点顶部状态栏会误放标记（这正是任务书点名要避免的情况）。
     → 已改用 `EventSystem.RaycastAll` 做当前帧射线命中。
  5. **【重要】平面数量事件只在「有/无」翻转时触发** —— `1 → 3` 这类变化会被吞掉，
     状态栏上的「检测到 N 个水平平面」长期失真。→ 已改为按数量变化触发。
  6. **【重要】协程里 NRE 会让「提交问题」按钮永久失效** —— NRE 发生在协程体内 → 协程被 Unity 终止 →
     `submitRoutine = null` 永远不执行 → 按钮永远不可点。→ 已加 null 检查 + `try/finally` 复位。
  7. **【重要】`PlayerSettings` 里注释说放开明文流量、代码却只是重复设置包名** ——
     Android 9+ 默认禁止 http 明文，真机直连 `http://192.168.x.x:8080` 会报
     `CLEARTEXT communication ... not permitted`，联调直接失败。→ 已改为 `PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed`。
  8. **【重要】客户端/服务端长度上限口径不一致** —— Unity 按字符数（100），Go 按字节数（200），
     一个 100 字的中文标题（300 字节）会「客户端放行、后端 400」。→ 已统一：
     Unity 改成 200/2000 **字符**，Go 改成 `utf8.RuneCountInString`（也是字符数），两端一致。
  9. **【建议】优先级配色是死代码** —— `defaultPriority` 永远是 medium，Low/High 颜色永远不生效。
     → 已加 `PlacementController.RestyleLastMarker(priority)`，选完优先级立刻给最近的标记重新上色
     （同时满足任务书加分项「根据优先级或状态改变 AR 标记样式」）。
  10. **【建议】`renderer.material` 会为每个标记实例化一份材质** → 已改用 `MaterialPropertyBlock`。
  11. **【建议】`BackendConfig.NormalizeBaseUrl("http://")` 会被削成 `"http:"`**，拼出非法 URL
      → 已加协议头校验，并**先补了 5 个失败用例再改实现**（`BackendConfigSchemeTests.cs`，61 → 仍为 61 passed）。
  12. **【建议】`Vector3Position.ToString()` 用了当前 culture** → 已改用 `InvariantCulture`。
  13. **【建议】`StatusBanner` 可能拿到 null 的初始状态文案** → 已给 `StatusText` 初始值。
  14. **【建议】`RemoveAllListeners()` 会清掉别人挂的监听** → 已改为保存委托并精确 `RemoveListener`。
- **未采纳（我判断不需要或性价比低，并说明了理由）**：
  - **S1「标记根节点应挂在 AR Origin 下」** —— 采用它需要拿到 `ARSessionOrigin`，
    而 `ARSession` 在 5.1.5 没有公开的 `origin` 属性，改造面大；
    且任务书明确「不要求应用重启后把历史标记恢复到完全相同的真实物理位置」。**保留现状**。
  - **S8「`IssuePriorities.DisplayNames()` 是死代码」** —— AI 说错了：它被 `SceneBuilder` 用来生成三个优先级按钮的文案。
    **只改了注释里「下拉框」的说法**（实际是按钮），代码未动。
  - **「提交 `ProjectSettings/ProjectSettings.asset`」** —— 手写 Unity 序列化资产风险太高，
    改为在 README 里写明「首次打开后需在 Project Settings 里设置 Active Input Handling = Both」。
- **AI 明确说「没有问题的部分」**：AR Foundation 的 API 用法全部正确、没有用到 5.x 已删除的 API
  （无 `RaycastPlane`、无 `ARPlane.PlaneType`、无 `ARSessionState.Initializing/Moving/Tracking/Paused`），
  `ARRaycastHit.pose` 的世界空间用法正确，`hits` 列表复用安全，
  `GameObject.CreatePrimitive` 的 Collider **不会**干扰 AR 射线（AR 射线走的是原生 `XRRaycastSubsystem`，不是 Unity Physics）。
  这一段与我自己对照 5.1.5 包内源码的核对结论一致。

### 验证方式
- 对两条阻塞级结论，我**独立下载了 `com.unity.textmeshpro@3.0.6` 与 `com.unity.xr.arfoundation@5.1.5`
  的源码包逐行复核**，没有只凭 AI 的话改代码。
- 由于本机没有 Unity 编辑器，无法真正编译，我另写了一个**基于 Roslyn 的语法检查工具**
  `tools/SyntaxCheck`：用 `CSharpSyntaxTree.ParseText` 解析全部 28 个 Unity C# 文件，
  结果「检查了 28 个 C# 文件，发现 **0 个语法错误**」——这能挡住括号/分号/语法层面的低级错误，
  但**不能替代 Unity 编译器的类型检查**，这一点在 README 里已如实说明。

---

## P-004 - 目标：审查 Go 后端与 React 前端是否有安全/健壮性/契约问题

### Prompt 原文

> 你是一名资深 Go / React 后端与前端代码审查员。请**只读审查**以下代码，**不要修改任何文件**，只输出审查结论。
>
> - Go 后端：`E:\简历题目\server`（Go 1.27，标准库 net/http，JSON 文件持久化）
> - React 管理端：`E:\简历题目\web\src`（React 18 + TypeScript + Vite + Vitest）
> - 端到端脚本：`E:\简历题目\scripts\e2e.mjs`
>
> ## 后端接口契约（供你理解，勿改）
> ```
> GET    /api/health              -> 200 {"status":"ok","service":"unity-ar-inspection","issues":N}
> GET    /api/issues              -> 200 {"issues":Issue[],"count":N}
> POST   /api/issues              -> 201 {"issue":Issue}
> PATCH  /api/issues/{id}/status  -> 200 {"issue":Issue}
> DELETE /api/issues/{id}         -> 200 {"deleted":"id"}
> Issue = {id,title,description,priority(low|medium|high),status(open|in_progress|resolved),position{x,y,z},createdAt,updatedAt}
> 错误 = {"error":{"code":"invalid_argument|invalid_json|not_found|internal_error","message":"中文","field":"..."}}
> ```
>
> ## 请重点检查
> ### Go
> 1. 并发安全：`store.Store` 的读写锁是否覆盖了所有路径？`Create`/`UpdateStatus`/`Delete` 里写盘失败回滚是否正确？`List()` 返回的是切片副本还是内部切片（外部修改会不会污染内部状态）？
> 2. 持久化：临时文件 + Rename 的原子写是否正确？失败路径会不会留下垃圾临时文件？并发写入会不会互相覆盖？
> 3. HTTP 层：路由匹配、方法限制、状态码语义是否正确？`decodeJSON` 对超大 body、空 body、错误 JSON 的处理是否到位？有没有 panic 风险（例如 `r.PathValue` 取不到）？
> 4. 错误处理：有没有把内部错误细节直接暴露给客户端（可能泄露路径等）？
> 5. ID 生成：`model.NewID` 有没有可能在高并发下碰撞？
> 6. 有没有明显可以简化或存在行为不一致的地方（比如 `POST /api/issues/{id}/status` 这个额外别名）？
>
> ### React
> 1. `src/api.ts`：`request()` 的超时实现（AbortController + Promise.race）有没有竞态/泄漏问题？`finally` 里 `clearTimeout` 与 `removeEventListener` 是否正确？上游 signal 被 abort 时会不会误报成 NetworkError？
> 2. `src/App.tsx` / `src/components/*`：状态管理有没有问题（竞态、闭包过期、key 使用、乐观更新与后端返回不一致）？`useEffect` 依赖是否完整？
> 3. `src/components/IssueRow.tsx`：下拉框在请求进行中被 `disabled`，失败回滚 —— 如果组件在请求返回前被卸载会怎样？
> 4. 错误处理：后端返回 4xx/5xx、返回体不是 JSON、网络断开、后端返回空 body，这几种情况前端的表现是否都合理？有没有可能出现白屏或永久 loading？
> 5. 类型：`src/types.ts` 与后端 `model.Issue` 是否完全一致（字段名、可空性）？有没有用 `any` 蒙混？
> 6. 可访问性 / XSS：有没有直接用 `dangerouslySetInnerHTML` 或把后端字符串拼进 HTML？
>
> ### E2E 脚本
> `scripts/e2e.mjs` 的断言是否真的覆盖了任务书要求的闭环？有没有「看起来在测、其实没测到」的假阳性（例如定位器能匹配到多个元素、断言写得太宽松）？
>
> ## 输出要求
> 按「严重程度（阻塞/重要/建议）」分组，每条给出：文件相对路径 + 行号、问题、为什么是问题、建议修法（可用代码片段）。
> **不要为了凑数编问题**；没问题的方面直接说没问题。

### 结果

- **修改后采用（4 个重要级 + 10 余个建议级）**：
  1. **【重要】500 响应把 `err.Error()` 原样回显，泄露数据文件绝对路径** ——
     实际会返回
     `{"error":{"code":"internal_error","message":"保存问题失败: 替换数据文件失败: rename E:\\...\\issues.json: Access is denied."}}`。
     我**先写了失败用例** `TestInternalErrorDoesNotLeakServerPaths`（用「把数据文件换成同名目录」制造写盘失败），
     红灯复现成功 → 已改为「细节只进服务端日志，客户端拿固定文案」。
  2. **【重要】`updateStatus` 不校验 `data.issue`，可能白屏** —— 后端返回 200 但 body 缺 `issue` 时
     `undefined.id` 抛错，React 卸载整棵树。→ 已加运行时校验 + 顶层 `ErrorBoundary` 兜底。
  3. **【重要】`request()` 的两个缺陷** —— (a) 上游 abort 被误报成「后端没启动」；
     (b) 超时后没有 `controller.abort()`，底层 fetch 泄漏。
     → 已分别改为独立的 `CancelledError`、以及超时时先 `controller.abort()`。
     两条都**先补了失败用例**（`api.test.ts` 从 23 → 28 个用例）。
  4. **【重要】E2E 里三条状态断言是假阳性** —— 状态下拉框渲染了全部 3 个 `<option>`，
     所以「待处理」「已解决」这两个词**无论当前状态是什么都会出现在行文本里**，断言永远不会失败。
     → 已改为断言 `select.inputValue()`（这是真正验证「界面同步」的方式）。
  5. **【建议】方法不匹配返回 404 而非 405** → 已加 `isKnownPath` 判断，返回 405 + `method_not_allowed`。
  6. **【建议】`decodeJSON` 静默截断超大 body**（报 invalid_json 而不是 413）→ 已改为多读 1 字节判断超限并返回 413。
  7. **【建议】标题/描述没有长度上限** → 已加 200/2000 字符上限（与 Unity 端口径一致）。
  8. **【建议】服务端缺 `WriteTimeout` / `IdleTimeout`** → 已补 30s / 60s。
  9. **【建议】进程被强杀会在数据目录留下 `issues-*.tmp` 垃圾** → 已在 `NewFileStore` 启动时清理。
  10. **【建议】`App.tsx` 刷新并发竞态** → 已加请求序号，过期响应丢弃；并补了并发刷新的测试用例。
  11. **【建议】后端不可用时表格显示「暂无问题」有误导性** → 已改为「数据加载失败，请点击右上角「刷新」重试」。
  12. **【建议】删除没有并发保护** → 已加 `deletingIds` 集合，删除中按钮禁用。
  13. **【建议】`IssueRow` 回滚用陈旧闭包值** → 已改用 `useRef` 保存「最后一次已知的服务端状态」。
  14. **【建议】`catch {}` 吞掉异常** → 已补 `console.error`。
- **未采纳（附理由）**：
  - **「删掉 `POST /api/issues/{id}/status` 别名」** —— 它是给不支持 PATCH 的客户端用的兼容入口，
    同一个 handler、行为一致，保留并在 README 接口表里标注为「兼容入口」。
  - **「`DisallowUnknownFields` 拒绝未知字段」** —— Unity 端多传字段会直接失败，
    比起严格的字段校验，对原型阶段更危险的是「客户端稍微改一下就上报不了」。**保持现状**。
  - **「换成 SQLite 解决写放大」** —— 任务书允许 JSON 文件；当前规模（原型）下全量重写完全够用，
    换 SQLite 引入 CGO/依赖反而增加风险。**已在 README「已知问题」里记录**。
  - **「rename 后 fsync 父目录」** —— Windows 上无对应语义，收益极低，**不做**。
  - **「`store.Get()` 是死代码」** —— 保留，它是有用的查询接口且被测试覆盖。
  - **「`ApiError.code` 类型联合过窄」** —— 已把类上的 `code` 放宽为 `string`，
    但 `types.ts` 的 `ApiErrorCode` 联合类型保留作为文档。
- **AI 明确说「经核实没有问题的部分」（我复核后同意）**：
  Go 侧并发安全（锁覆盖完整、无数据竞争）、`List()` 返回的是**深拷贝**（`model.Issue` 全是值类型）、
  `Delete` 里的三索引切片 `s.issues[:i:i]` 写法正确（不会污染 backup）、
  原子写主流程正确、路由无 panic 风险、ID 生成碰撞概率可忽略、
  React 与 Go 的类型完全一致、全仓无 `any`、无 XSS 风险、`IssueRow` 在请求期间被卸载是安全 no-op。

### 验证方式
- **每一条「修改后采用」都先补了失败用例**，再改实现：
  - Go：`TestCreateIssueTooLongTitleReturns400`、`TestCreateIssueTooLongDescriptionReturns400`、
    `TestUnknownMethodOnKnownPathReturns405`、`TestInternalErrorDoesNotLeakServerPaths`
    → 红灯 4 failed → 改实现 → 全绿（29 个用例）。
  - React：`api.test.ts` 新增「响应体契约校验」3 个 + 「请求取消」2 个 → 红灯 5 failed → 全绿（30 个用例）；
    `App.test.tsx` 新增「并发刷新」用例 → 全绿。
  - Unity：`BackendConfigSchemeTests.cs` 新增 5 个用例 → 红灯 → 全绿（61 个用例）。
- 端到端脚本重跑：25 条断言全部通过，其中 4 条是这次新加的
  （非法 priority 的 400 + 错误契约结构、空标题的 400 + field、未知 ID 的 404、DELETE 接口）。

---

## 汇总：AI 参与方式与人工把关

| 环节 | 谁做的 | AI 产出是否被采纳 | 人工验证方式 |
| --- | --- | --- | --- |
| 任务拆解与计划 | Codex 编码代理 | — | 对照任务书逐条映射 R1–R17 |
| Go 后端 | Codex 编码代理 | — | 29 个单测 + 真实进程冒烟 + 重启持久化验证 |
| Unity AR 工程 | Codex 编码代理 | — | 61 个纯逻辑用例（dotnet test）+ Roslyn 语法检查 0 错 + 逐个核对 AR Foundation 5.1.5 源码 |
| React 管理端 | **sub-agent（P-002）** | 修改后采用 | 我复核全部源码；补 10 个用例；真实后端 E2E |
| Unity 代码审查 | **sub-agent（P-003）** | 采纳 14 条、驳回 3 条 | 独立下载 TMP/ARF 包源码复核阻塞级结论；补失败用例后改实现 |
| Go + React 代码审查 | **sub-agent（P-004）** | 采纳 14 条、驳回 6 条 | 每条都先补失败用例再改实现；E2E 重跑 |
| 演示视频 | — | **未完成**（需人工操作手机录制） | README 第 7 节给出可复现脚本，管理端已有自动化截图 |
| Unity 编译 / 打包 / 真机验证 | Codex 编码代理 | — | 真实 Unity 编译 0 错、Test Runner 61/61、APK 装机跑通闭环 |

---

## P-005 - 目标：把 Unity 从「源码级核对」推进到「真编译 + 真机跑通」

### Prompt 原文

> 我激活完了

> 现在电脑连上手机了，你告诉我给该做什么

### 结果

- **修改后采用（全部完成）**：这一阶段 AI 自主完成了以下工作，并把每一步的产物与验证都落到了仓库里：
  1. **装环境**：Unity Hub 3.3.7 → Unity 2022.3.62f3c1（中国区 CDN，3.6 GB / 1.7 分钟）
     + Android Build Support + OpenJDK 11 + Android SDK 34 + NDK r23b + adb；
  2. **真编译**：`Unity.exe -batchmode -quit -projectPath unity` → **0 error / 0 warning**，4 个程序集全部生成；
  3. **真测试**：`Unity.exe -batchmode -runTests -testPlatform EditMode` → **61 / 61 通过**；
  4. **真打包**：→ **30 MB APK**，IL2CPP + arm64-v8a，`uses-feature: android.hardware.camera.ar` 已就位；
  5. **真机验证**：装机 → 相机出图、AR 平面检测（「检测到 1 个水平平面」）、
     点界面控件被正确忽略（「点击了界面控件，已忽略该操作」）、手机上提交的问题被 Go 后端收到、
     React 网页上能看到并能改状态。

- **AI 自主发现并修复的 8 个问题**（每个都写了现象 → 根因 → 修法，见 `docs/STEP_LOG.md` Step 6）：
  1. `manifest.json` 的 `testables` 段导致包解析失败；
  2. **工程路径含中文**导致 Android 工具链直接拒绝打包；
  3. **ARCore 不支持 Vulkan**，Unity 2022.3 默认启用了 Vulkan；
  4. NDK 目录层级不对导致 `Android NDK not found`；
  5. 活动构建平台不是 Android 导致 ARCore 的 Gradle 检查失败；
  6. **TMP 默认字体没有中文字形**，真机上所有中文显示成方框 □□ → 引入 OFL 授权的开源中文字体并生成动态 TMP 字体资源；
  7. **UI 完全收不到点击**（按钮无反应、输入框不聚焦）→ `activeInputHandler` 从 0（旧）改为 2（Both），
     EventSystem 改挂 `InputSystemUIInputModule` 并显式 `AssignDefaultActions()`；
  8. **ARCore 的 XR Loader 没为 Android 启用** → 脚本里调 `XRPackageMetadataStore.AssignLoader` 自动启用。

- **AI 自主写的工具**（都进了仓库）：
  - `scripts/build_android.ps1` —— 自动处理「复制到 ASCII 路径 → 打包 → APK 拷回」；
  - `unity/Assets/Editor/ProjectSetup.cs` —— 一键准备（TMP 资源 / 中文字体 / 生成场景 / Android 设置 / ARCore Loader）+ 一键打包；
  - `unity/Assets/Editor/ChineseFontSetup.cs` —— 中文字体资源生成；
  - `unity/Assets/Editor/EventSystemDiagnostics.cs` —— 输入系统与 EventSystem 配置诊断。

- **AI 明确没有做到的（如实说明）**：
  - **真机放置标记的视觉确认**：手机一直放在桌上对着墙，ARCore 检不到地面平面，
    没法真的点地面放下立方体。**但放置的判定逻辑有 9 个单元测试覆盖**，
    且真机上「点界面控件会被正确忽略」这一条已实测通过。
  - **演示视频**：需要人工操作手机录制，AI 无法代替。
  - **真机输入中文**：`adb shell input text` 注入的 ASCII 被这台机器的中文输入法吞掉。
    人工在真机上手动输入并提交是成功的（后端收到了 `ISS-1793287849089-G6AGFCNXLPMR4`）。

### 验证方式
- 编译、测试、打包都用真实 Unity 命令行，退出码与日志逐条核对；
- APK 用 `aapt2 dump badging` 核对包名 / 权限 / `uses-feature` / native-code，
  并用 `unzip` 确认 `libUnityARCore.so`、`libarcore_sdk_c.so`、`libarcore_sdk_jni.so`、`libil2cpp.so` 都在包里；
- 真机用 `adb install` / `am start` / `screencap` / `logcat` 驱动与取证；
- 闭环用真实 Go 后端 + 真实浏览器（Playwright）验证，截图存在 `docs/screenshots/`。
