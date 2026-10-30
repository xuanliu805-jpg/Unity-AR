# 接口契约（Go 后端 ↔ React 管理端 ↔ Unity 客户端）

> 三端共用。所有路径都挂在 `Base URL` 之下（默认 `http://localhost:8080`）。

## 1. 现有接口（不变）

| 方法 | 路径 | 请求 | 响应 |
| --- | --- | --- | --- |
| GET | `/api/health` | — | `{"status":"ok","service":"unity-ar-inspection","issues":3}` |
| GET | `/api/issues` | — | `{"issues":Issue[],"count":3}` |
| POST | `/api/issues` | `{"title","description","priority","position":{"x","y","z"}}` | `201 {"issue":Issue}` |
| PATCH | `/api/issues/{id}/status` | `{"status":"open\|in_progress\|resolved"}` | `200 {"issue":Issue}` |
| POST | `/api/issues/{id}/status` | 同上 | 同上（兼容入口） |
| DELETE | `/api/issues/{id}` | — | `200 {"deleted":"ISS-..."}` |

`Issue`：

```json
{
  "id": "ISS-1792988466562-SAAEVOZKQGIAC",
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "low|medium|high",
  "status": "open|in_progress|resolved",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
  "createdAt": "2026-10-26T12:21:06.5628133+08:00",
  "updatedAt": "2026-10-26T12:21:12.032014+08:00"
}
```

错误：`{"error":{"code":"invalid_argument|invalid_json|not_found|method_not_allowed|internal_error","message":"中文说明","field":"title"}}`

## 2. WebSocket 实时推送（新增）

**端点**：`GET /ws`（等价别名 `GET /api/ws`）。标准 WebSocket 握手（RFC 6455）。

**服务端 → 客户端** 消息统一是 `{"type": "...", "data": {...}}`：

| type | data | 触发时机 |
| --- | --- | --- |
| `hello` | `{"serverTime":"2026-10-30T10:00:00+08:00","issues":3}` | 连接建立后立刻发一次 |
| `issue.created` | `Issue` | 有新问题上报（Unity 或 React 都会触发） |
| `issue.updated` | `Issue` | 问题字段或状态发生变化 |
| `issue.deleted` | `{"id":"ISS-..."}` | 问题被删除 |
| `ping` | `{}` | 服务端每 25 秒心跳一次 |

**客户端 → 服务端**：

| type | 含义 |
| --- | --- |
| `{"type":"ping"}` | 客户端心跳，服务端回 `{"type":"pong","data":{}}` |

**前端使用约定**：

1. 连上后先 `GET /api/issues` 拉一次全量，再靠事件增量更新（避免连接瞬间丢事件）。
2. 断线自动重连，**指数退避**（1s → 2s → 4s → 8s，上限 15s）。
3. 收到 `issue.created` / `issue.updated` / `issue.deleted` 时**就地更新本地列表**（不要整表重拉，避免列表闪烁）。

## 3. AI 智能分诊（新增）

**端点**：`POST /api/analysis/triage`

请求：

```json
{
  "title": "东侧外墙渗水",
  "description": "墙皮发黑，有水渍往下淌",
  "position": { "x": 12.4, "y": 0.0, "z": -3.1 }
}
```

响应：

```json
{
  "suggestedPriority": "high",
  "suggestedCategory": "渗漏",
  "confidence": 0.78,
  "duplicateRisk": "high",
  "keywords": ["外墙", "渗水", "水渍"],
  "reasons": [
    "命中关键词「渗水/水渍」，历史同类问题 82% 定级为 high",
    "3 天前在相距 2.4 米处已上报过类似问题，可能重复"
  ],
  "similarIssues": [
    {
      "id": "ISS-1792988466562-SAAEVOZKQGIAC",
      "title": "东侧外墙渗水",
      "similarity": 0.83,
      "distance": 2.4,
      "ageDays": 3,
      "status": "open",
      "advice": "同一位置 3 天前已上报且仍未处理，建议先确认是否重复报修"
    }
  ]
}
```

字段说明：

| 字段 | 含义 |
| --- | --- |
| `suggestedPriority` | 建议优先级 `low/medium/high`，**只是建议，不自动改用户填的值** |
| `suggestedCategory` | 问题类别：`渗漏/破损/电气/消防/结构/设备/其他` |
| `confidence` | 0~1，建议的可信度 |
| `duplicateRisk` | `none/low/high`，重复报修风险 |
| `keywords` | 抽取出的关键词 |
| `reasons` | 人类可读的理由（管理端与 Unity 端都直接展示） |
| `similarIssues[].similarity` | 0~1 文本相似度 |
| `similarIssues[].distance` | 与新问题坐标的 3D 距离（米） |
| `similarIssues[].ageDays` | 距今天数 |
| `similarIssues[].advice` | 给巡检员的建议 |

**说明**：这套分析是**纯本地算法**（中文 2-gram 关键词提取 + TF-IDF 余弦相似度 + Jaccard + 3D 距离 + 规则表），
**不调用任何外部大模型 API**，因此离线可用、可单元测试、响应在毫秒级。
它的业务价值是**避免重复报修**和**给巡检员一个优先级建议**。
