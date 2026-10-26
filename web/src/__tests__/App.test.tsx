import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import type { Issue } from '../types';

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

function mockResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('App', () => {
  it('后端不可用（fetch reject）时显示错误提示而不是崩溃/白屏', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    render(<App />);

    expect(await screen.findByText(/无法连接后端服务/)).toBeInTheDocument();
    // 页面其余部分照常渲染
    expect(screen.getByRole('heading', { name: 'AR 现场巡检 · 问题管理端' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: '标题' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '重试' })).toBeInTheDocument();
    // 加载失败时不应该显示「暂无问题」——那会误导成「后端里真的没有数据」
    expect(screen.getByText(/数据加载失败/)).toBeInTheDocument();
    expect(screen.queryByText('暂无问题')).not.toBeInTheDocument();
  });

  it('空列表显示空态', async () => {
    fetchMock.mockResolvedValue(mockResponse({ issues: [], count: 0 }));

    render(<App />);

    expect(await screen.findByText('暂无问题')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByTestId('total-count')).toHaveTextContent('0');
    });
  });

  it('渲染后端返回的问题并在顶部显示总数', async () => {
    fetchMock.mockResolvedValue(mockResponse({ issues: [issue], count: 1 }));

    render(<App />);

    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument();
    expect(screen.getByTestId('total-count')).toHaveTextContent('1');
    expect(screen.queryByText('暂无问题')).not.toBeInTheDocument();
  });

  it('可以按状态筛选', async () => {
    fetchMock.mockResolvedValue(
      mockResponse({
        issues: [
          issue,
          { ...issue, id: 'ISS-2', title: '配电箱锈蚀', status: 'resolved' },
        ],
        count: 2,
      }),
    );

    render(<App />);
    expect(await screen.findByText('入口墙面破损')).toBeInTheDocument();

    const filter = screen.getByLabelText('按状态筛选') as HTMLSelectElement;
    fireEvent.change(filter, { target: { value: 'resolved' } });

    await waitFor(() => {
      expect(screen.queryByText('入口墙面破损')).not.toBeInTheDocument();
    });
    expect(screen.getByText('配电箱锈蚀')).toBeInTheDocument();
  });

  it('后端返回 200 但响应体不符合契约时不会白屏', async () => {
    // 后端（或中间的代理）返回了合法 JSON，但缺少 issues 字段
    fetchMock.mockResolvedValue(mockResponse({}));

    render(<App />);

    expect(await screen.findByText('暂无问题')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'AR 现场巡检 · 问题管理端' })).toBeInTheDocument();
  });

  it('并发刷新时只有最后一次请求的结果会生效', async () => {
    const resolvers: Array<(value: Response) => void> = [];
    fetchMock.mockImplementation(
      () =>
        new Promise<Response>((resolve) => {
          resolvers.push(resolve);
        }),
    );

    render(<App />);

    // 点两次刷新，制造两个并发请求
    const refresh = await screen.findByRole('button', { name: '刷新' });
    fireEvent.click(refresh);
    fireEvent.click(refresh);

    // 先返回的是「旧」响应（0 条），后返回的是「新」响应（2 条）
    await waitFor(() => {
      expect(resolvers.length).toBeGreaterThanOrEqual(2);
    });
    resolvers[0](mockResponse({ issues: [], count: 0 }));
    resolvers[resolvers.length - 1](mockResponse({ issues: [issue], count: 1 }));

    await waitFor(() => {
      expect(screen.getByTestId('total-count')).toHaveTextContent('1');
    });
    expect(screen.getByText('入口墙面破损')).toBeInTheDocument();
  });
});
