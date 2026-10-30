import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  RECONNECT_BACKOFF_SEQUENCE,
  RealtimeClient,
  backoffDelay,
  getWebSocketUrl,
} from '../realtime';
import { MockWebSocket } from '../../test-utils/mockWebSocket';
import type { Issue } from '../../types';

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

function createClient() {
  return new RealtimeClient({
    url: 'ws://backend.test:8080/ws',
    socketFactory: () => new MockWebSocket('ws://backend.test:8080/ws'),
  });
}

beforeEach(() => {
  MockWebSocket.reset();
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('getWebSocketUrl', () => {
  it('把 http 基地址换成 ws 并拼上 /ws', () => {
    expect(getWebSocketUrl('http://localhost:8080')).toBe('ws://localhost:8080/ws');
  });

  it('https 基地址换成 wss', () => {
    expect(getWebSocketUrl('https://inspection.example.com')).toBe(
      'wss://inspection.example.com/ws',
    );
  });

  it('基地址结尾多余的斜杠不会产生双斜杠', () => {
    expect(getWebSocketUrl('http://localhost:8080///')).toBe('ws://localhost:8080/ws');
  });
});

describe('backoffDelay', () => {
  it('退避序列是 1s -> 2s -> 4s -> 8s -> 15s，之后封顶', () => {
    const delays = [1, 2, 3, 4, 5, 6, 7].map(backoffDelay);
    expect(delays).toEqual([1000, 2000, 4000, 8000, 15000, 15000, 15000]);
    expect(RECONNECT_BACKOFF_SEQUENCE).toEqual([1000, 2000, 4000, 8000, 15000]);
  });
});

describe('RealtimeClient', () => {
  it('连接成功后状态变为 open', () => {
    const statuses: string[] = [];
    const client = createClient();
    client.setHandlers({
      onStatus: (status) => {
        statuses.push(status);
      },
    });

    client.connect();

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(MockWebSocket.latest.url).toBe('ws://backend.test:8080/ws');
    expect(statuses).toEqual(['connecting']);

    MockWebSocket.latest.emitOpen();

    expect(statuses).toEqual(['connecting', 'open']);
    expect(client.status).toBe('open');
  });

  it('收到 issue.created 时回调新问题', () => {
    const created: Issue[] = [];
    const client = createClient();
    client.setHandlers({
      onIssueCreated: (payload) => {
        created.push(payload);
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();
    MockWebSocket.latest.emitMessage({ type: 'issue.created', data: issue });

    expect(created).toHaveLength(1);
    expect(created[0].id).toBe('ISS-1700000000000-ABCDEFGH');
    expect(created[0].title).toBe('入口墙面破损');
    expect(created[0].priority).toBe('high');
  });

  it('收到 issue.updated 时回调更新后的问题', () => {
    const updated: Issue[] = [];
    const client = createClient();
    client.setHandlers({
      onIssueUpdated: (payload) => {
        updated.push(payload);
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();
    MockWebSocket.latest.emitMessage({
      type: 'issue.updated',
      data: { ...issue, status: 'resolved' },
    });

    expect(updated).toHaveLength(1);
    expect(updated[0].status).toBe('resolved');
  });

  it('收到 issue.deleted 时回调被删除的 id', () => {
    const deleted: string[] = [];
    const client = createClient();
    client.setHandlers({
      onIssueDeleted: (id) => {
        deleted.push(id);
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();
    MockWebSocket.latest.emitMessage({ type: 'issue.deleted', data: { id: issue.id } });

    expect(deleted).toEqual(['ISS-1700000000000-ABCDEFGH']);
  });

  it('hello 消息里带上服务端的问题总数', () => {
    let count: number | undefined;
    const client = createClient();
    client.setHandlers({
      onHello: (data) => {
        count = data.issues;
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();
    MockWebSocket.latest.emitMessage({
      type: 'hello',
      data: { serverTime: '2026-10-30T10:00:00+08:00', issues: 7 },
    });

    expect(count).toBe(7);
  });

  it('断线后会按退避时间重新建立连接', () => {
    const client = createClient();
    client.connect();
    MockWebSocket.latest.emitOpen();
    expect(MockWebSocket.instances).toHaveLength(1);

    MockWebSocket.latest.emitClose();

    // 还没到退避时间，不应该重连
    vi.advanceTimersByTime(999);
    expect(MockWebSocket.instances).toHaveLength(1);

    vi.advanceTimersByTime(1);
    expect(MockWebSocket.instances).toHaveLength(2);
    expect(client.status).toBe('reconnecting');
  });

  it('连续断线时的重连间隔是 1s/2s/4s/8s/15s', () => {
    const delays: number[] = [];
    const client = createClient();
    client.setHandlers({
      onStatus: (_status, info) => {
        if (info?.delayMs !== undefined) {
          delays.push(info.delayMs);
        }
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();

    for (let round = 0; round < 5; round += 1) {
      MockWebSocket.latest.emitClose();
      vi.advanceTimersByTime(15000);
    }

    expect(delays).toEqual([1000, 2000, 4000, 8000, 15000]);
    expect(MockWebSocket.instances).toHaveLength(6);
  });

  it('重连成功后 attempt 归零，下次断线重新从 1s 开始', () => {
    const delays: number[] = [];
    const client = createClient();
    client.setHandlers({
      onStatus: (_status, info) => {
        if (info?.delayMs !== undefined) {
          delays.push(info.delayMs);
        }
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();
    MockWebSocket.latest.emitClose();
    vi.advanceTimersByTime(1000);
    expect(client.retries).toBe(1);

    MockWebSocket.latest.emitOpen();
    expect(client.retries).toBe(0);

    MockWebSocket.latest.emitClose();
    vi.advanceTimersByTime(1000);
    expect(delays).toEqual([1000, 1000]);
  });

  it('主动 close 之后不再重连', () => {
    const client = createClient();
    client.connect();
    MockWebSocket.latest.emitOpen();

    client.close();
    vi.advanceTimersByTime(60000);

    expect(MockWebSocket.instances).toHaveLength(1);
    expect(client.status).toBe('closed');
  });

  it('收到非法 JSON 时不会抛异常，只报告错误', () => {
    const errors: Error[] = [];
    const client = createClient();
    client.setHandlers({
      onError: (error) => {
        errors.push(error);
      },
    });

    client.connect();
    MockWebSocket.latest.emitOpen();
    MockWebSocket.latest.onmessage?.({ data: 'not-json' });

    expect(errors).toHaveLength(1);
    expect(errors[0].message).toContain('无法解析');
  });
});