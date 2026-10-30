/**
 * 与 Go 后端（server/internal/model/issue.go）共享的数据结构。
 * 字段名必须与后端 JSON 完全一致，不可自行改名。
 */

export type IssuePriority = 'low' | 'medium' | 'high';

export type IssueStatus = 'open' | 'in_progress' | 'resolved';

/** AR 标记在世界空间中的坐标（米）。 */
export interface Position {
  x: number;
  y: number;
  z: number;
}

export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: IssuePriority;
  status: IssueStatus;
  position: Position;
  /** RFC3339 时间戳 */
  createdAt: string;
  /** RFC3339 时间戳 */
  updatedAt: string;
}

/** GET /api/issues 的响应体。 */
export interface IssuesResponse {
  issues: Issue[];
  count: number;
}

/** PATCH /api/issues/{id}/status 的响应体。 */
export interface UpdateStatusResponse {
  issue: Issue;
}

/** DELETE /api/issues/{id} 的响应体。 */
export interface DeleteIssueResponse {
  deleted: string;
}

/** POST /api/issues 的请求体。 */
export interface CreateIssueRequest {
  title: string;
  description: string;
  priority: IssuePriority;
  position: Position;
}

/** POST /api/issues 的响应体。 */
export interface CreateIssueResponse {
  issue: Issue;
}

/* ---------- AI 智能分诊（POST /api/analysis/triage） ---------- */

/** 问题类别：渗漏 / 破损 / 电气 / 消防 / 结构 / 设备 / 其他。 */
export type IssueCategory =
  | '渗漏'
  | '破损'
  | '电气'
  | '消防'
  | '结构'
  | '设备'
  | '其他';

/** 重复报修风险。 */
export type DuplicateRisk = 'none' | 'low' | 'high';

/** 分诊请求体。 */
export interface TriageRequest {
  title: string;
  description: string;
  position: Position;
}

/** 相似问题（可能是重复报修）。 */
export interface SimilarIssue {
  id: string;
  title: string;
  /** 文本相似度 0~1 */
  similarity: number;
  /** 与新问题坐标的 3D 距离（米） */
  distance: number;
  /** 距今天数 */
  ageDays: number;
  status: IssueStatus;
  advice: string;
}

/** 分诊响应体。 */
export interface TriageResponse {
  /** 建议优先级 —— 只是建议，不会自动改写用户填写的值。 */
  suggestedPriority: IssuePriority;
  suggestedCategory: IssueCategory;
  /** 可信度 0~1 */
  confidence: number;
  duplicateRisk: DuplicateRisk;
  keywords: string[];
  reasons: string[];
  similarIssues: SimilarIssue[];
}

/* ---------- WebSocket 实时通道 ---------- */

/** 实时连接状态。 */
export type RealtimeStatus = 'connecting' | 'open' | 'reconnecting' | 'closed';

/** 后端错误响应：{"error":{"code":...,"message":...,"field":...}} */
export type ApiErrorCode =
  | 'invalid_argument'
  | 'invalid_json'
  | 'not_found'
  | 'internal_error';

export interface ApiErrorBody {
  error: {
    code: ApiErrorCode;
    message: string;
    field?: string;
  };
}

export const PRIORITY_OPTIONS: ReadonlyArray<IssuePriority> = ['high', 'medium', 'low'];

export const STATUS_OPTIONS: ReadonlyArray<IssueStatus> = [
  'open',
  'in_progress',
  'resolved',
];

export const PRIORITY_LABELS: Record<IssuePriority, string> = {
  low: '低',
  medium: '中',
  high: '高',
};

export const STATUS_LABELS: Record<IssueStatus, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
};

export const CATEGORY_OPTIONS: ReadonlyArray<IssueCategory> = [
  '渗漏',
  '破损',
  '电气',
  '消防',
  '结构',
  '设备',
  '其他',
];

export const DUPLICATE_RISK_LABELS: Record<DuplicateRisk, string> = {
  none: '无',
  low: '低',
  high: '高',
};