/**
 * 后端 API 访问层。
 *
 * 契约（Base URL 由 VITE_API_BASE_URL 指定，默认 http://localhost:8080）：
 *   GET    /api/issues                -> { issues: Issue[], count: number }
 *   PATCH  /api/issues/{id}/status    -> { issue: Issue }
 *   DELETE /api/issues/{id}           -> { deleted: "ISS-..." }
 *
 * 错误分两类：
 *   - ApiError：后端正常返回但状态码非 2xx（body 内含 {error:{code,message,field}}）
 *   - NetworkError：fetch 本身失败（后端未启动 / 连接被拒 / 超时）
 */

import type {
  ApiErrorBody,
  ApiErrorCode,
  DeleteIssueResponse,
  Issue,
  IssueStatus,
  IssuesResponse,
  UpdateStatusResponse,
} from './types';

export const DEFAULT_API_BASE_URL = 'http://localhost:8080';

/** 后端返回的业务错误（HTTP 状态码非 2xx）。 */
export class ApiError extends Error {
  readonly code: ApiErrorCode;
  readonly field?: string;
  readonly status: number;

  constructor(message: string, code: ApiErrorCode, status: number, field?: string) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
    this.field = field;
  }
}

/** 网络层失败：后端没启动、连接被拒、超时等。 */
export class NetworkError extends Error {
  readonly cause?: unknown;

  constructor(message: string, cause?: unknown) {
    super(message);
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/** 读取环境变量中的后端地址（去掉结尾多余的斜杠）。 */
export function getApiBaseUrl(): string {
  const raw =
    (typeof import.meta !== 'undefined' && import.meta.env?.VITE_API_BASE_URL) ||
    DEFAULT_API_BASE_URL;
  return raw.trim().replace(/\/+$/, '');
}

/** 把相对路径拼成完整 URL。 */
export function apiUrl(path: string): string {
  const normalized = path.startsWith('/') ? path : `/${path}`;
  return `${getApiBaseUrl()}${normalized}`;
}

function isApiErrorBody(payload: unknown): payload is ApiErrorBody {
  if (typeof payload !== 'object' || payload === null) return false;
  const error = (payload as { error?: unknown }).error;
  if (typeof error !== 'object' || error === null) return false;
  const { code, message } = error as { code?: unknown; message?: unknown };
  return typeof code === 'string' && typeof message === 'string';
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, init);
  } catch (error) {
    throw new NetworkError('无法连接后端服务，请确认后端已启动', error);
  }

  let payload: unknown = null;
  try {
    const text = await response.text();
    if (text.length > 0) {
      payload = JSON.parse(text) as unknown;
    }
  } catch (error) {
    if (error instanceof SyntaxError) {
      // 响应不是合法 JSON：HTTP 状态码正常也要按错误处理，避免把脏数据塞进界面
      if (response.ok) {
        throw new ApiError('后端返回了无法解析的响应', 'internal_error', response.status);
      }
      payload = null;
    } else {
      throw new NetworkError('读取后端响应失败', error);
    }
  }

  if (!response.ok) {
    if (isApiErrorBody(payload)) {
      throw new ApiError(
        payload.error.message,
        payload.error.code,
        response.status,
        payload.error.field,
      );
    }
    throw new ApiError(`请求失败（HTTP ${response.status}）`, 'internal_error', response.status);
  }

  if (payload === null) {
    throw new ApiError('后端返回了空响应', 'internal_error', response.status);
  }
  return payload as T;
}

/** GET /api/issues */
export async function fetchIssues(signal?: AbortSignal): Promise<IssuesResponse> {
  const data = await request<Partial<IssuesResponse> & { issues?: Issue[] }>(
    apiUrl('/api/issues'),
    { method: 'GET', signal },
  );
  const issues = Array.isArray(data.issues) ? data.issues : [];
  const count = typeof data.count === 'number' ? data.count : issues.length;
  return { issues, count };
}

/** PATCH /api/issues/{id}/status */
export async function updateStatus(id: string, status: IssueStatus): Promise<Issue> {
  const data = await request<UpdateStatusResponse>(
    apiUrl(`/api/issues/${encodeURIComponent(id)}/status`),
    {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status }),
    },
  );
  return data.issue;
}

/** DELETE /api/issues/{id} */
export async function deleteIssue(id: string): Promise<string> {
  const data = await request<DeleteIssueResponse>(
    apiUrl(`/api/issues/${encodeURIComponent(id)}`),
    { method: 'DELETE' },
  );
  return data.deleted;
}

/** 把任意异常转成给用户看的中文提示。 */
export function toUserMessage(error: unknown): string {
  if (error instanceof ApiError || error instanceof NetworkError) {
    return error.message;
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return '发生未知错误';
}