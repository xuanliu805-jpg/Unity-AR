/**
 * 后端 API 访问层。
 *
 * 契约（Base URL 由 VITE_API_BASE_URL 指定，默认 http://localhost:8080）：
 *   GET    /api/issues                -> { issues: Issue[], count: number }
 *   PATCH  /api/issues/{id}/status    -> { issue: Issue }
 *   DELETE /api/issues/{id}           -> { deleted: "ISS-..." }
 *
 * 错误分三类：
 *   - ApiError：后端正常返回但状态码非 2xx（body 内含 {error:{code,message,field}}）
 *   - NetworkError：fetch 本身失败（后端未启动 / 连接被拒 / 超时）
 *   - CancelledError：调用方主动取消（组件卸载等），不应该当成「后端挂了」
 */

import type {
  ApiErrorBody,
  ApiErrorCode,
  CreateIssueRequest,
  CreateIssueResponse,
  DeleteIssueResponse,
  DuplicateRisk,
  Issue,
  IssueCategory,
  IssuePriority,
  IssueStatus,
  IssuesResponse,
  SimilarIssue,
  TriageRequest,
  TriageResponse,
  UpdateStatusResponse,
} from './types';

import { STATUS_OPTIONS } from './types';

export const DEFAULT_API_BASE_URL = 'http://localhost:8080';

/**
 * 请求超时（毫秒）。
 *
 * 为什么要超时：后端进程被杀掉时，浏览器复用的 keep-alive 连接可能既不报错也不返回，
 * fetch 会一直挂着。没有超时的话，页面会永远停在「加载中」，
 * 用户既看不到错误提示，也没有「重试」按钮可点 —— 属于最糟糕的失败形态。
 */
export const DEFAULT_REQUEST_TIMEOUT_MS = 8000;

/** 读取请求超时时间，可用 VITE_API_TIMEOUT_MS 覆盖。 */
export function getRequestTimeoutMs(): number {
  const raw =
    (typeof import.meta !== 'undefined' && import.meta.env?.VITE_API_TIMEOUT_MS) || '';
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_REQUEST_TIMEOUT_MS;
}

/** 内部使用的超时标记。 */
class RequestTimeoutError extends Error {
  constructor(timeoutMs: number) {
    super(`请求超时（${timeoutMs} ms）`);
    this.name = 'RequestTimeoutError';
  }
}

/** 后端返回的业务错误（HTTP 状态码非 2xx）。 */
export class ApiError extends Error {
  /** 后端给出的错误码；后端新增错误码时这里也会如实透传。 */
  readonly code: string;
  readonly field?: string;
  readonly status: number;

  constructor(message: string, code: ApiErrorCode | string, status: number, field?: string) {
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

/** 请求被调用方主动取消。 */
export class CancelledError extends Error {
  constructor(message = '请求已取消') {
    super(message);
    this.name = 'CancelledError';
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
  const normalized = path.startsWith('/') ? path : '/' + path;
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
  const timeoutMs = getRequestTimeoutMs();
  const controller = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let timedOut = false;

  // 上游 signal（例如组件卸载时取消请求）也要能中止本次请求
  const upstream = init?.signal;
  const onUpstreamAbort = () => controller.abort();
  if (upstream) {
    if (upstream.aborted) {
      controller.abort();
    } else {
      upstream.addEventListener('abort', onUpstreamAbort);
    }
  }

  // 用 Promise.race 而不是只依赖 fetch 自身的 abort：
  // 真实浏览器里 fetch 一般会响应 abort，但某些代理/半开连接下不一定会 settle，
  // race 才是保证「一定会结束」的办法。
  const timeoutPromise = new Promise<never>((_, reject) => {
    timer = setTimeout(() => {
      timedOut = true;
      // 超时后必须中止底层请求，否则会积压一批永不返回的僵尸请求。
      controller.abort();
      reject(new RequestTimeoutError(timeoutMs));
    }, timeoutMs);
  });

  let response: Response;
  try {
    response = await Promise.race([
      fetch(url, { ...init, signal: controller.signal }),
      timeoutPromise,
    ]);
  } catch (error) {
    if (error instanceof RequestTimeoutError || timedOut) {
      throw new NetworkError(
        `请求超时（${Math.round(timeoutMs / 1000)} 秒），后端可能未启动或网络不通`,
        error,
      );
    }
    if (isAbortError(error)) {
      // 调用方主动取消，不该显示成「后端没启动」误导用户。
      throw new CancelledError();
    }
    throw new NetworkError('无法连接后端服务，请确认后端已启动', error);
  } finally {
    if (timer !== undefined) clearTimeout(timer);
    if (upstream) upstream.removeEventListener('abort', onUpstreamAbort);
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

/** 判断是不是「请求被中止」的错误。 */
function isAbortError(error: unknown): boolean {
  if (error instanceof DOMException) {
    return error.name === 'AbortError';
  }
  return (
    typeof error === 'object' &&
    error !== null &&
    (error as { name?: unknown }).name === 'AbortError'
  );
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
  const data = await request<Partial<UpdateStatusResponse>>(
    apiUrl(`/api/issues/${encodeURIComponent(id)}/status`),
    {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status }),
    },
  );

  // 运行时校验：request() 只是类型断言，不会真的检查后端返回了什么。
  // 如果后端（或中间的代理）返回 200 但 body 不符合契约，这里必须挡住，
  // 否则 undefined.id 会在渲染时抛错，把整个页面搞成白屏。
  const issue = data.issue;
  if (!issue || typeof issue.id !== 'string' || typeof issue.status !== 'string') {
    throw new ApiError('后端返回的响应缺少 issue 字段', 'internal_error', 200);
  }
  return issue;
}

/** DELETE /api/issues/{id} */
export async function deleteIssue(id: string): Promise<string> {
  const data = await request<Partial<DeleteIssueResponse>>(
    apiUrl(`/api/issues/${encodeURIComponent(id)}`),
    { method: 'DELETE' },
  );
  if (typeof data.deleted !== 'string' || data.deleted.length === 0) {
    throw new ApiError('后端返回的响应缺少 deleted 字段', 'internal_error', 200);
  }
  return data.deleted;
}

/** POST /api/issues —— 上报一个新问题（管理端也允许手动补录）。 */
export async function createIssue(payload: CreateIssueRequest): Promise<Issue> {
  const data = await request<Partial<CreateIssueResponse>>(
    apiUrl('/api/issues'),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: payload.title,
        description: payload.description,
        priority: payload.priority,
        position: {
          x: payload.position.x,
          y: payload.position.y,
          z: payload.position.z,
        },
      }),
    },
  );

  const issue = data.issue;
  if (!issue || typeof issue.id !== 'string' || typeof issue.title !== 'string') {
    throw new ApiError('后端返回的响应缺少 issue 字段', 'internal_error', 200);
  }
  return issue;
}

/* ---------- AI 智能分诊 ---------- */

const PRIORITY_VALUES: ReadonlyArray<IssuePriority> = ['high', 'medium', 'low'];
const CATEGORY_VALUES: ReadonlyArray<IssueCategory> = [
  '渗漏',
  '破损',
  '电气',
  '消防',
  '结构',
  '设备',
  '其他',
];
const RISK_VALUES: ReadonlyArray<DuplicateRisk> = ['none', 'low', 'high'];

function clamp01(value: unknown): number {
  const n = typeof value === 'number' && Number.isFinite(value) ? value : 0;
  return Math.min(1, Math.max(0, n));
}

/**
 * POST /api/analysis/triage —— 本地算法分诊（不调用任何外部大模型）。
 *
 * 返回值一律做运行时归一化：后端字段缺失/类型不对时退回到安全默认值，
 * 绝不让 undefined 流进渲染层把页面搞崩。
 */
export async function triageIssue(payload: TriageRequest): Promise<TriageResponse> {
  const data = await request<Partial<TriageResponse>>(
    apiUrl('/api/analysis/triage'),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: payload.title,
        description: payload.description,
        position: {
          x: payload.position.x,
          y: payload.position.y,
          z: payload.position.z,
        },
      }),
    },
  );

  if (!data || typeof data !== 'object') {
    throw new ApiError('分诊服务返回了无法解析的响应', 'internal_error', 200);
  }

  const suggestedPriority = PRIORITY_VALUES.includes(
    data.suggestedPriority as IssuePriority,
  )
    ? (data.suggestedPriority as IssuePriority)
    : 'medium';

  const suggestedCategory = CATEGORY_VALUES.includes(
    data.suggestedCategory as IssueCategory,
  )
    ? (data.suggestedCategory as IssueCategory)
    : '其他';

  const duplicateRisk = RISK_VALUES.includes(data.duplicateRisk as DuplicateRisk)
    ? (data.duplicateRisk as DuplicateRisk)
    : 'none';

  const keywords = Array.isArray(data.keywords)
    ? data.keywords.filter((item): item is string => typeof item === 'string')
    : [];

  const reasons = Array.isArray(data.reasons)
    ? data.reasons.filter((item): item is string => typeof item === 'string')
    : [];

  const similarIssues = Array.isArray(data.similarIssues)
    ? data.similarIssues
        .filter(
          (item): item is SimilarIssue =>
            typeof item === 'object' &&
            item !== null &&
            typeof (item as SimilarIssue).id === 'string',
        )
        .map((item) => ({
          id: item.id,
          title: typeof item.title === 'string' ? item.title : item.id,
          similarity: clamp01(item.similarity),
          distance: typeof item.distance === 'number' ? item.distance : 0,
          ageDays: typeof item.ageDays === 'number' ? item.ageDays : 0,
          status: STATUS_OPTIONS.includes(item.status) ? item.status : 'open',
          advice: typeof item.advice === 'string' ? item.advice : '',
        }))
    : [];

  return {
    suggestedPriority,
    suggestedCategory,
    confidence: clamp01(data.confidence),
    duplicateRisk,
    keywords,
    reasons,
    similarIssues,
  };
}
/** 把任意异常转成给用户看的中文提示。 */
export function toUserMessage(error: unknown): string {
  if (error instanceof ApiError || error instanceof NetworkError || error instanceof CancelledError) {
    return error.message;
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return '发生未知错误';
}
