import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
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