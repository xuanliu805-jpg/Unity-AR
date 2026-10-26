import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  CancelledError,
  DEFAULT_REQUEST_TIMEOUT_MS,
  getRequestTimeoutMs,
  DEFAULT_API_BASE_URL,
  NetworkError,
  apiUrl,
  deleteIssue,
  fetchIssues,
  getApiBaseUrl,
  updateStatus,
} from '../../api';
import type { Issue } from '../../types';

const TEST_BASE = 'http://backend.test:8080';

const issue: Issue = {
  id: 'ISS-1700000000000-ABCDEFGH',
  title: '入口墙面破损',
  description: '墙面有裂缝',
  priority: 'high',
  status: 'open',
  position: { x: 1.5, y: 0, z: -2.25 },
  createdAt: '2026-10-26T08:00:00Z',
  updatedAt: '2026-10-26T08:00:00Z',
};

/** 造一个最小可用的 Response 替身（jsdom 下没有 fetch 实现）。 */
function mockResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

beforeEach(() => {
  import.meta.env.VITE_API_BASE_URL = TEST_BASE;
});

afterEach(() => {
  vi.unstubAllGlobals();
  import.meta.env.VITE_API_BASE_URL = DEFAULT_API_BASE_URL;
});

describe('apiUrl / getApiBaseUrl', () => {
  it('把相对路径拼到 base URL 后面', () => {
    expect(getApiBaseUrl()).toBe(TEST_BASE);
    expect(apiUrl('/api/issues')).toBe(`${TEST_BASE}/api/issues`);
    expect(apiUrl('/api/health')).toBe(`${TEST_BASE}/api/health`);
  });

  it('路径缺少前导斜杠时也能正确拼接', () => {
    expect(apiUrl('api/issues')).toBe(`${TEST_BASE}/api/issues`);
  });

  it('base URL 结尾多余的斜杠不会产生双斜杠', () => {
    import.meta.env.VITE_API_BASE_URL = `${TEST_BASE}///`;
    expect(apiUrl('/api/issues')).toBe(`${TEST_BASE}/api/issues`);
  });

  it('未配置 VITE_API_BASE_URL 时使用默认地址', () => {
    import.meta.env.VITE_API_BASE_URL = '';
    expect(getApiBaseUrl()).toBe(DEFAULT_API_BASE_URL);
    expect(apiUrl('/api/issues')).toBe('http://localhost:8080/api/issues');
  });
});

describe('fetchIssues', () => {
  it('正确解析 {issues,count}', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse({ issues: [issue], count: 1 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await fetchIssues();

    expect(result.issues).toHaveLength(1);
    expect(result.issues[0].title).toBe('入口墙面破损');
    expect(result.issues[0].priority).toBe('high');
    expect(result.count).toBe(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe(`${TEST_BASE}/api/issues`);
  });

  it('缺少 count 时用 issues 长度兜底', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse({ issues: [issue] })));

    const result = await fetchIssues();

    expect(result.count).toBe(1);
  });

  it('后端返回 {error:{...}} 时抛出带 message 的错误', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        mockResponse(
          { error: { code: 'invalid_argument', message: '标题(title)不能为空', field: 'title' } },
          400,
        ),
      ),
    );

    const error = await fetchIssues().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).message).toBe('标题(title)不能为空');
    expect((error as ApiError).code).toBe('invalid_argument');
    expect((error as ApiError).field).toBe('title');
    expect((error as ApiError).status).toBe(400);
  });

  it('fetch reject（后端没启动）时抛出可识别的网络错误', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await fetchIssues().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(NetworkError);
    expect((error as NetworkError).name).toBe('NetworkError');
    expect((error as NetworkError).message).toContain('无法连接后端服务');
  });
});

describe('updateStatus', () => {
  it('发出 PATCH 且 body 为 {"status":"..."}', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(mockResponse({ issue: { ...issue, status: 'resolved' } }));
    vi.stubGlobal('fetch', fetchMock);

    const updated = await updateStatus('ISS-1', 'resolved');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${TEST_BASE}/api/issues/ISS-1/status`);
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(init.body as string)).toEqual({ status: 'resolved' });
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
    expect(updated.status).toBe('resolved');
  });

  it('后端报错时抛出 ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        mockResponse({ error: { code: 'not_found', message: '未找到 ID 为 ISS-1 的问题', field: 'id' } }, 404),
      ),
    );

    const error = await updateStatus('ISS-1', 'resolved').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('not_found');
  });
});

describe('deleteIssue', () => {
  it('发出 DELETE 并返回被删除的 ID', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse({ deleted: 'ISS-1' }));
    vi.stubGlobal('fetch', fetchMock);

    const deleted = await deleteIssue('ISS-1');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${TEST_BASE}/api/issues/ISS-1`);
    expect(init.method).toBe('DELETE');
    expect(deleted).toBe('ISS-1');
  });
});
describe('请求超时', () => {
  it('后端一直不响应时按时抛出 NetworkError（而不是永远转圈）', async () => {
    import.meta.env.VITE_API_TIMEOUT_MS = '50';
    // fetch 永远不 resolve，模拟「后端进程被杀掉、连接半开」的挂死场景
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise(() => {})));

    const error = await fetchIssues().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(NetworkError);
    expect((error as NetworkError).message).toContain('超时');
  });

  it('默认超时时间是 8 秒', () => {
    import.meta.env.VITE_API_TIMEOUT_MS = '';
    expect(getRequestTimeoutMs()).toBe(DEFAULT_REQUEST_TIMEOUT_MS);
    expect(DEFAULT_REQUEST_TIMEOUT_MS).toBe(8000);
  });

  it('可以通过环境变量调整超时时间', () => {
    import.meta.env.VITE_API_TIMEOUT_MS = '3000';
    expect(getRequestTimeoutMs()).toBe(3000);
  });
});
describe('响应体契约校验', () => {
  it('updateStatus 在后端返回 200 但缺少 issue 时抛出 ApiError，而不是把 undefined 传下去', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse({})));
    expect(await updateStatus('ISS-1', 'resolved').catch((e: unknown) => e)).toBeInstanceOf(ApiError);
  });

  it('updateStatus 在 issue 字段类型不对时抛出 ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse({ issue: { id: 42 } })));
    expect(await updateStatus('ISS-1', 'resolved').catch((e: unknown) => e)).toBeInstanceOf(ApiError);
  });

  it('deleteIssue 在后端返回 200 但缺少 deleted 时抛出 ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse({})));
    expect(await deleteIssue('ISS-1').catch((e: unknown) => e)).toBeInstanceOf(ApiError);
  });
});

describe('请求取消', () => {
  it('上游 signal 被 abort 时抛出 CancelledError，而不是误报「后端没启动」', async () => {
    const controller = new AbortController();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
        return new Promise((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => {
            const error = new Error('aborted');
            error.name = 'AbortError';
            reject(error);
          });
        });
      }),
    );

    const pending = fetchIssues(controller.signal).catch((e: unknown) => e);
    controller.abort();
    const error = await pending;

    expect(error).toBeInstanceOf(CancelledError);
    expect(error).not.toBeInstanceOf(NetworkError);
  });

  it('超时会中止底层 fetch，不留下悬挂请求', async () => {
    import.meta.env.VITE_API_TIMEOUT_MS = '50';
    let capturedSignal: AbortSignal | null | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
        capturedSignal = init?.signal ?? undefined;
        return new Promise(() => {});
      }),
    );

    await fetchIssues().catch(() => undefined);

    expect(capturedSignal?.aborted).toBe(true);
  });
});
