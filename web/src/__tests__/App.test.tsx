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
    expect(screen.getByText('暂无问题')).toBeInTheDocument();
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
});