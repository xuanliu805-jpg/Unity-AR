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