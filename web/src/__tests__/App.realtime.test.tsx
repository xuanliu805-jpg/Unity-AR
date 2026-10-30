import { act, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { RealtimeClient } from '../api/realtime';
import { MockWebSocket } from '../test-utils/mockWebSocket';
import type { Issue } from '../types';

const existing: Issue = {
  id: 'ISS-1',
  title: '入口墙面破损',
  description: '墙面有裂缝',
  priority: 'high',
  status: 'open',
  position: { x: 1.5, y: 0, z: -2.25 },
  createdAt: '2026-10-26T08:00:00Z',
  updatedAt: '2026-10-26T08:00:00Z',
};

const pushed: Issue = {
  id: 'ISS-2',
  title: '东侧外墙渗水',
  description: '墙皮发黑',
  priority: 'medium',
  status: 'open',
  position: { x: 12.4, y: 0, z: -3.1 },
  createdAt: '2026-10-30T10:00:00Z',
  updatedAt: '2026-10-30T10:00:00Z',
};

function mockResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

const fetchMock = vi.fn();

function createClient(): RealtimeClient {
  return new RealtimeClient({
    url: 'ws://backend.test:8080/ws',
    socketFactory: () => new MockWebSocket('ws://backend.test:8080/ws'),
  });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
  MockWebSocket.reset();
});

describe('App 的实时更新', () => {
  it('首次连接后拉一次全量，收到 issue.created 就地增量插入新问题', async () => {
    fetchMock.mockResolvedValue(mockResponse({ issues: [existing], count: 1 }));
    const client = createClient();

    render(<App realtimeClient={client} />);

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument();
    await waitFor(() => {
      expect(MockWebSocket.instances).toHaveLength(1);
    });

    // 全量只拉一次：事件到达时不重新拉表
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain('/api/issues');

    act(() => {
      MockWebSocket.latest.emitOpen();
      MockWebSocket.latest.emitMessage({ type: 'issue.created', data: pushed });
    });

    expect(
      await screen.findByText('东侧外墙渗水', { selector: '.issue-title' }),
    ).toBeInTheDocument();
    expect(screen.getByText('入口墙面破损')).toBeInTheDocument();
    // 增量更新，不会整表重拉
    expect(fetchMock).toHaveBeenCalledTimes(1);
    // 新问题会给一个轻量提示
    expect(screen.getByText('收到新问题')).toBeInTheDocument();
    expect(screen.getByTestId('total-count')).toHaveTextContent('2');
  });

  it('收到 issue.updated 就地替换那一条', async () => {
    fetchMock.mockResolvedValue(mockResponse({ issues: [existing], count: 1 }));
    const client = createClient();

    render(<App realtimeClient={client} />);
    await screen.findByText('入口墙面破损');
    await waitFor(() => expect(MockWebSocket.instances).toHaveLength(1));

    act(() => {
      MockWebSocket.latest.emitOpen();
      MockWebSocket.latest.emitMessage({
        type: 'issue.updated',
        data: { ...existing, status: 'resolved' },
      });
    });

    await waitFor(() => {
      expect(
        (screen.getByLabelText('入口墙面破损 的处理状态') as HTMLSelectElement).value,
      ).toBe('resolved');
    });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('收到 issue.deleted 把那一条从列表里移除', async () => {
    fetchMock.mockResolvedValue(mockResponse({ issues: [existing], count: 1 }));
    const client = createClient();

    render(<App realtimeClient={client} />);
    await screen.findByText('入口墙面破损');
    await waitFor(() => expect(MockWebSocket.instances).toHaveLength(1));

    act(() => {
      MockWebSocket.latest.emitOpen();
      MockWebSocket.latest.emitMessage({ type: 'issue.deleted', data: { id: 'ISS-1' } });
    });

    await waitFor(() => {
      expect(screen.queryByText('入口墙面破损')).not.toBeInTheDocument();
    });
    expect(screen.getByTestId('total-count')).toHaveTextContent('0');
  });

  it('连接断开后界面显示「重连中」', async () => {
    fetchMock.mockResolvedValue(mockResponse({ issues: [existing], count: 1 }));
    const client = createClient();

    render(<App realtimeClient={client} />);
    await screen.findByText('入口墙面破损');
    await waitFor(() => expect(MockWebSocket.instances).toHaveLength(1));

    act(() => {
      MockWebSocket.latest.emitOpen();
    });
    expect(await screen.findByText('实时已连接')).toBeInTheDocument();

    act(() => {
      MockWebSocket.latest.emitClose();
    });

    expect(await screen.findByText('重连中')).toBeInTheDocument();
  });
});