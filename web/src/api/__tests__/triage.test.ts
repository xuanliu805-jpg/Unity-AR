import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, triageIssue } from '../../api';

const TEST_BASE = 'http://backend.test:8080';

function mockResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

const triagePayload = {
  suggestedPriority: 'high',
  suggestedCategory: '渗漏',
  confidence: 0.78,
  duplicateRisk: 'high',
  keywords: ['外墙', '渗水', '水渍'],
  reasons: ['命中关键词「渗水/水渍」', '3 天前在相距 2.4 米处已上报过类似问题'],
  similarIssues: [
    {
      id: 'ISS-1792988466562-SAAEVOZKQGIAC',
      title: '东侧外墙渗水',
      similarity: 0.83,
      distance: 2.4,
      ageDays: 3,
      status: 'open',
      advice: '同一位置 3 天前已上报且仍未处理，建议先确认是否重复报修',
    },
  ],
};

beforeEach(() => {
  import.meta.env.VITE_API_BASE_URL = TEST_BASE;
});

afterEach(() => {
  vi.unstubAllGlobals();
  import.meta.env.VITE_API_BASE_URL = 'http://localhost:8080';
});

describe('triageIssue', () => {
  it('向 /api/analysis/triage 发送 POST，body 含标题/描述/坐标', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse(triagePayload));
    vi.stubGlobal('fetch', fetchMock);

    await triageIssue({
      title: '东侧外墙渗水',
      description: '墙皮发黑，有水渍往下淌',
      position: { x: 12.4, y: 0, z: -3.1 },
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${TEST_BASE}/api/analysis/triage`);
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
    expect(JSON.parse(init.body as string)).toEqual({
      title: '东侧外墙渗水',
      description: '墙皮发黑，有水渍往下淌',
      position: { x: 12.4, y: 0, z: -3.1 },
    });
  });

  it('解析建议优先级、可信度、关键词、理由和相似问题', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse(triagePayload)));

    const result = await triageIssue({
      title: '东侧外墙渗水',
      description: '墙皮发黑',
      position: { x: 12.4, y: 0, z: -3.1 },
    });

    expect(result.suggestedPriority).toBe('high');
    expect(result.suggestedCategory).toBe('渗漏');
    expect(result.confidence).toBeCloseTo(0.78);
    expect(result.duplicateRisk).toBe('high');
    expect(result.keywords).toEqual(['外墙', '渗水', '水渍']);
    expect(result.reasons).toHaveLength(2);
    expect(result.similarIssues).toHaveLength(1);
    expect(result.similarIssues[0].id).toBe('ISS-1792988466562-SAAEVOZKQGIAC');
    expect(result.similarIssues[0].similarity).toBeCloseTo(0.83);
    expect(result.similarIssues[0].distance).toBeCloseTo(2.4);
    expect(result.similarIssues[0].ageDays).toBe(3);
    expect(result.similarIssues[0].advice).toContain('重复报修');
  });

  it('字段缺失时归一化成安全默认值，不会把 undefined 传给界面', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse({})));

    const result = await triageIssue({
      title: 'x',
      description: '',
      position: { x: 0, y: 0, z: 0 },
    });

    expect(result.suggestedPriority).toBe('medium');
    expect(result.suggestedCategory).toBe('其他');
    expect(result.confidence).toBe(0);
    expect(result.duplicateRisk).toBe('none');
    expect(result.keywords).toEqual([]);
    expect(result.reasons).toEqual([]);
    expect(result.similarIssues).toEqual([]);
  });

  it('后端返回错误时抛出 ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        mockResponse(
          { error: { code: 'invalid_argument', message: '标题(title)不能为空', field: 'title' } },
          400,
        ),
      ),
    );

    const error = await triageIssue({
      title: '',
      description: '',
      position: { x: 0, y: 0, z: 0 },
    }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).message).toBe('标题(title)不能为空');
    expect((error as ApiError).code).toBe('invalid_argument');
  });
});