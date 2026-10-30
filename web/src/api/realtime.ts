/**
 * WebSocket 实时通道客户端。
 *
 * 契约（docs/API_CONTRACT.md 第 2 节）：
 *   - 端点：GET /ws（等价别名 /api/ws），普通 WebSocket 握手
 *   - 服务端 -> 客户端消息统一形如 {"type":"...","data":{...}}
 *       hello        {"serverTime":"...","issues":3}
 *       issue.created   Issue
 *       issue.updated   Issue
 *       issue.deleted   {"id":"ISS-..."}
 *       ping         {}
 *   - 客户端 -> 服务端：{"type":"ping"}，服务端回 {"type":"pong","data":{}}
 *
 * 设计要点：
 *   1. 断线指数退避重连：1s -> 2s -> 4s -> 8s -> 15s（上限 15s）。
 *   2. 重连成功后由调用方决定是否重新拉全量（App 会在重连后重新 GET /api/issues，
 *      避免断线期间丢事件；正常收事件时是就地增量更新，不会整表重拉）。
 *   3. 状态（connecting / open / reconnecting / closed）对外暴露，
 *      UI 用它画「实时连接」指示灯。
 */

import { getApiBaseUrl } from '../api';
import type { Issue, RealtimeStatus } from '../types';

/** 重连退避序列（毫秒），超出后保持最后一个值。 */
export const RECONNECT_BACKOFF_SEQUENCE: ReadonlyArray<number> = [
  1000, 2000, 4000, 8000, 15000,
];

export const RECONNECT_BASE_DELAY_MS = 1000;
export const RECONNECT_MAX_DELAY_MS = 15000;

/**
 * 计算第 attempt 次重连应该等待多久（attempt 从 1 开始）。
 * 1 -> 1000, 2 -> 2000, 3 -> 4000, 4 -> 8000, 5+ -> 15000。
 */
export function backoffDelay(attempt: number): number {
  if (!Number.isFinite(attempt) || attempt < 1) {
    return RECONNECT_BASE_DELAY_MS;
  }
  const index = Math.min(
    Math.floor(attempt) - 1,
    RECONNECT_BACKOFF_SEQUENCE.length - 1,
  );
  return RECONNECT_BACKOFF_SEQUENCE[index];
}

/**
 * 由 API 基地址推导 WebSocket 地址。
 * http://localhost:8080 -> ws://localhost:8080/ws
 * https://example.com    -> wss://example.com/ws
 */
export function getWebSocketUrl(baseUrl?: string): string {
  const base = (baseUrl ?? getApiBaseUrl()).trim().replace(/\/+$/, '');
  const wsBase = base.replace(/^http:/i, 'ws:').replace(/^https:/i, 'wss:');
  return `${wsBase}/ws`;
}

/** 客户端用到的 WebSocket 最小接口（测试里可以用假实现替换）。 */
export interface SocketLike {
  readyState: number;
  close(): void;
  send(data: string): void;
  /* eslint-disable @typescript-eslint/no-explicit-any */
  onopen: ((event: any) => void) | null;
  onclose: ((event: any) => void) | null;
  onerror: ((event: any) => void) | null;
  onmessage: ((event: any) => void) | null;
  /* eslint-enable @typescript-eslint/no-explicit-any */
}

export type SocketFactory = (url: string) => SocketLike;

/** 状态变化时附带的细节。 */
export interface RealtimeStatusInfo {
  /** 已经重试了几次（首次连接为 0）。 */
  attempt: number;
  /** 本次重连的等待时长（毫秒）；非重连时为 undefined。 */
  delayMs?: number;
}

export interface RealtimeHandlers {
  onStatus?: (status: RealtimeStatus, info?: RealtimeStatusInfo) => void;
  onIssueCreated?: (issue: Issue) => void;
  onIssueUpdated?: (issue: Issue) => void;
  onIssueDeleted?: (id: string) => void;
  onHello?: (data: { serverTime?: string; issues?: number }) => void;
  onError?: (error: Error) => void;
}

export interface RealtimeClientOptions {
  url: string;
  handlers?: RealtimeHandlers;
  socketFactory?: SocketFactory;
  /** 客户端心跳间隔（毫秒）。默认 20s，服务端每 25s ping 一次。 */
  pingIntervalMs?: number;
}

const DEFAULT_PING_INTERVAL_MS = 20000;

const defaultSocketFactory: SocketFactory = (url) =>
  new WebSocket(url) as unknown as SocketLike;

export class RealtimeClient {
  private readonly url: string;

  private readonly socketFactory: SocketFactory;

  private readonly pingIntervalMs: number;

  private handlers: RealtimeHandlers;

  private socket: SocketLike | null = null;

  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  private pingTimer: ReturnType<typeof setInterval> | null = null;

  /** 已经重试的次数，连接成功后归零。 */
  private attempt = 0;

  private closedByUser = false;

  private currentStatus: RealtimeStatus = 'closed';

  constructor(options: RealtimeClientOptions) {
    this.url = options.url;
    this.handlers = options.handlers ?? {};
    this.socketFactory = options.socketFactory ?? defaultSocketFactory;
    this.pingIntervalMs = options.pingIntervalMs ?? DEFAULT_PING_INTERVAL_MS;
  }

  get status(): RealtimeStatus {
    return this.currentStatus;
  }

  /** 当前已经重试的次数（测试与 UI 展示用）。 */
  get retries(): number {
    return this.attempt;
  }

  setHandlers(handlers: RealtimeHandlers): void {
    this.handlers = handlers;
  }

  /** 建立连接。重复调用是安全的。 */
  connect(): void {
    this.closedByUser = false;
    if (this.socket && (this.socket.readyState === 0 || this.socket.readyState === 1)) {
      return;
    }
    this.openSocket();
  }

  /** 主动断开（组件卸载时调用），不再重连。 */
  close(): void {
    this.closedByUser = true;
    this.clearReconnectTimer();
    this.stopPing();
    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onopen = null;
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      try {
        socket.close();
      } catch {
        // 已经关闭的 socket 再 close 可能抛错，忽略即可。
      }
    }
    this.setStatus('closed');
  }

  private openSocket(): void {
    this.setStatus(this.attempt === 0 ? 'connecting' : 'reconnecting');

    let socket: SocketLike;
    try {
      socket = this.socketFactory(this.url);
    } catch (error) {
      this.handlers.onError?.(
        error instanceof Error ? error : new Error(String(error)),
      );
      this.scheduleReconnect();
      return;
    }

    this.socket = socket;

    socket.onopen = () => {
      this.attempt = 0;
      this.setStatus('open');
      this.startPing();
    };

    socket.onmessage = (event: { data?: unknown }) => {
      this.handleMessage(event?.data);
    };

    socket.onerror = () => {
      this.handlers.onError?.(new Error('WebSocket 连接出错'));
      // 有的实现只触发 error 不触发 close，这里先排一次重连；
      // 真正的 close 事件会走同一套退避逻辑（定时器已存在时不会重复排）。
      this.scheduleReconnect();
    };

    socket.onclose = () => {
      this.stopPing();
      this.socket = null;
      if (this.closedByUser) {
        this.setStatus('closed');
        return;
      }
      this.scheduleReconnect();
    };
  }

  private scheduleReconnect(): void {
    if (this.closedByUser) {
      return;
    }
    if (this.reconnectTimer !== null) {
      return; // 已经排过一次重连，忽略重复的 close/error
    }
    this.attempt += 1;
    const delayMs = backoffDelay(this.attempt);
    this.setStatus('reconnecting', { attempt: this.attempt, delayMs });
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.openSocket();
    }, delayMs);
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  private startPing(): void {
    this.stopPing();
    this.pingTimer = setInterval(() => {
      const socket = this.socket;
      if (!socket || socket.readyState !== 1) {
        return;
      }
      try {
        socket.send(JSON.stringify({ type: 'ping' }));
      } catch {
        // 心跳失败不致命，交给 onerror / onclose 处理重连。
      }
    }, this.pingIntervalMs);
  }

  private stopPing(): void {
    if (this.pingTimer !== null) {
      clearInterval(this.pingTimer);
      this.pingTimer = null;
    }
  }

  private handleMessage(raw: unknown): void {
    let payload: unknown = raw;
    if (typeof raw === 'string') {
      try {
        payload = JSON.parse(raw) as unknown;
      } catch {
        this.handlers.onError?.(new Error('收到无法解析的 WebSocket 消息'));
        return;
      }
    }
    if (typeof payload !== 'object' || payload === null) {
      return;
    }

    const { type, data } = payload as { type?: unknown; data?: unknown };
    if (typeof type !== 'string') {
      return;
    }

    switch (type) {
      case 'hello':
        this.handlers.onHello?.((data ?? {}) as { serverTime?: string; issues?: number });
        break;
      case 'issue.created':
        if (isIssue(data)) {
          this.handlers.onIssueCreated?.(data);
        }
        break;
      case 'issue.updated':
        if (isIssue(data)) {
          this.handlers.onIssueUpdated?.(data);
        }
        break;
      case 'issue.deleted': {
        const id = (data as { id?: unknown } | undefined)?.id;
        if (typeof id === 'string') {
          this.handlers.onIssueDeleted?.(id);
        }
        break;
      }
      case 'ping':
      case 'pong':
        break;
      default:
        break;
    }
  }

  private setStatus(status: RealtimeStatus, info?: RealtimeStatusInfo): void {
    this.currentStatus = status;
    this.handlers.onStatus?.(status, info);
  }
}

function isIssue(value: unknown): value is Issue {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as Issue).id === 'string' &&
    typeof (value as Issue).title === 'string'
  );
}