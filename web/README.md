# AR 现场巡检 · 问题管理端（React + TypeScript）

Unity AR 现场巡检的管理端网页：查看巡检员上报的问题、修改处理状态、筛选与删除。
前端使用 React 18 + TypeScript + Vite，单元测试使用 Vitest + @testing-library/react + jsdom，
UI 全部手写 CSS（不引入任何 UI 组件库）。

## 1. 环境要求

- Node.js >= 18（开发机实测 v24.14.1）
- npm >= 9（开发机实测 11.11.0）
- Go 后端已启动在 `http://localhost:8080`（可与 Unity 客户端共用）

## 2. 快速开始

```bash
cd web
npm install          # 如遇 peerDependencies 冲突，可改用 npm install --legacy-peer-deps
npm run dev          # 开发服务器 http://127.0.0.1:5173
```

浏览器打开 <http://127.0.0.1:5173> 即可看到问题列表。

## 3. 配置后端地址

后端地址通过环境变量 `VITE_API_BASE_URL` 指定，**默认 `http://localhost:8080`**。

```bash
# 方式一：复制 .env.example 为 .env 后按需修改
cp .env.example .env

# 方式二：临时指定
VITE_API_BASE_URL=http://192.168.1.20:8080 npm run dev
```

`.env.example` 内容：

```
VITE_API_BASE_URL=http://localhost:8080
VITE_API_TIMEOUT_MS=8000
```

> 注意：Vite 只在**启动/构建时**注入 `VITE_` 前缀变量，修改 `.env` 后必须重启 `npm run dev`
> 或重新 `npm run build`。地址末尾多余的 `/` 会被自动去掉，不会产生 `//api/issues`。

## 4. 可用脚本

| 命令 | 说明 |
| --- | --- |
| `npm run dev` | 启动开发服务器（默认 5173 端口） |
| `npm run build` | 类型检查 + 生产构建到 `dist/` |
| `npm run preview` | 本地预览 `dist/` 构建产物 |
| `npm run test` | Vitest 监听模式 |
| `npm run test:run` | Vitest 单次运行（CI 用） |
| `npm run typecheck` | `tsc --noEmit` 检查 `src/` 与 `vite.config.ts` |
| `npm run mock:api` | 启动本地 mock 后端（见下文，无需 Go 环境） |

## 5. 没有 Go 环境时怎么跑起来

仓库自带一个按 Go 后端契约实现的 mock 服务（`scripts/mock-backend.mjs`），端口默认 8080：

```bash
npm run mock:api      # 终端 A
npm run dev           # 终端 B
```

它实现了 `/api/health`、`/api/issues`、`PATCH /api/issues/{id}/status`、`DELETE /api/issues/{id}`
以及与 Go 后端完全一致的错误响应格式 `{"error":{"code","message","field"}}`。

### 请求超时

`VITE_API_TIMEOUT_MS`（毫秒，默认 8000）控制单次请求的最长等待时间。

这是 E2E 联调时发现并修掉的一个真实问题：后端进程被杀掉后，浏览器会复用之前的
keep-alive 连接，这个请求既不报错也不返回，`fetch` 会一直挂着 —— 页面既不报错也没有
「重试」按钮可点。加了超时之后，超时会转成 `NetworkError`，页面显示
「请求超时（N 秒），后端可能未启动或网络不通」并给出重试按钮。
## 6. 后端 API 契约

Base URL：`${VITE_API_BASE_URL}`（默认 `http://localhost:8080`）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/health` | `{"status":"ok","service":"unity-ar-inspection","issues":3}` |
| GET | `/api/issues` | `{"issues": Issue[], "count": number}` |
| PATCH | `/api/issues/{id}/status` | body `{"status":"in_progress"}` → `{"issue": Issue}` |
| DELETE | `/api/issues/{id}` | `{"deleted":"ISS-..."}` |

```ts
interface Issue {
  id: string;
  title: string;
  description: string;
  priority: 'low' | 'medium' | 'high';
  status: 'open' | 'in_progress' | 'resolved';
  position: { x: number; y: number; z: number };
  createdAt: string; // RFC3339
  updatedAt: string; // RFC3339
}
```

错误响应：`{"error":{"code":"invalid_argument|invalid_json|not_found|internal_error","message":"中文说明","field":"title|priority|status|id"}}`

## 7. 功能说明

- **问题列表**：标题、描述、优先级、状态、位置坐标 (x, y, z)、创建时间。
- **修改状态**：每行一个下拉框（待处理 / 处理中 / 已解决），选择后立即 `PATCH`；
  成功后用后端返回的记录更新本地状态，失败则回滚下拉框并显示错误条。
- **后端不可用**：顶部醒目错误条 + 「重试」按钮，表头、空态、筛选器照常渲染，绝不白屏。
- **空列表**：显示「暂无问题」空态。
- **筛选**：按优先级、按状态筛选（加分项）。
- **删除**：带二次确认（`window.confirm`），删除成功后从列表移除（加分项）。
- **刷新 / 总数**：右上角「刷新」按钮，顶部显示后端问题总数（加分项）。
- **加载中**：列表为空时显示「加载中……」占位行。

## 8. 目录结构

```
web/
├── index.html
├── package.json / package-lock.json
├── tsconfig.json / tsconfig.node.json
├── vite.config.ts            # 含 vitest 配置（jsdom + globals）
├── .env.example
├── scripts/mock-backend.mjs  # 本地 mock 后端
└── src/
    ├── main.tsx
    ├── App.tsx               # 页面容器：加载、筛选、总数、错误条
    ├── api.ts                # API 访问层（ApiError / NetworkError）
    ├── types.ts              # 与后端一致的类型定义
    ├── styles.css            # 手写样式
    ├── setupTests.ts
    ├── api/__tests__/api.test.ts
    ├── components/
    │   ├── ErrorBanner.tsx
    │   ├── IssueList.tsx
    │   ├── IssueRow.tsx
    │   └── __tests__/IssueList.test.tsx
    └── __tests__/App.test.tsx
```

## 9. 测试

```bash
npm run test:run
```

覆盖内容：

- `src/api/__tests__/api.test.ts`：base URL 拼接（默认值、结尾斜杠、缺少前导斜杠）、
  `fetchIssues` 解析 `{issues,count}`、后端 `{error:{...}}` 抛出带 message 的 `ApiError`、
  fetch reject 抛出 `NetworkError`、`updateStatus` 发出 PATCH 且 body 为 `{"status":"..."}`、
  `deleteIssue` 发出 DELETE。
- `src/components/__tests__/IssueList.test.tsx`：渲染标题/优先级/状态、
  下拉框选 `resolved` 会调用 `updateStatus` 并更新显示、失败时回滚并显示错误、删除二次确认、空态。
- `src/__tests__/App.test.tsx`：后端不可用时显示错误提示而不是崩溃/白屏、空列表空态、
  渲染问题并显示总数、按状态筛选。