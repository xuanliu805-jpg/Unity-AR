import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import IssueList from '../IssueList';
import { ApiError } from '../../api';
import type { Issue } from '../../types';

// 只替换会发起请求的函数，保留真实的 ApiError / toUserMessage
vi.mock('../../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api')>();
  return {
    ...actual,
    updateStatus: vi.fn(),
    deleteIssue: vi.fn(),
  };
});

import { deleteIssue, updateStatus } from '../../api';

const updateStatusMock = vi.mocked(updateStatus);
const deleteIssueMock = vi.mocked(deleteIssue);

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

const formatTime = (iso: string) => iso;

beforeEach(() => {
  updateStatusMock.mockReset();
  deleteIssueMock.mockReset();
});

describe('IssueList', () => {
  it('渲染出后端返回的问题标题/优先级/状态', () => {
    render(<IssueList issues={[issue]} formatTime={formatTime} />);

    expect(screen.getByText('入口墙面破损')).toBeInTheDocument();
    expect(screen.getByText('墙面有裂缝')).toBeInTheDocument();
    expect(screen.getByText('高')).toBeInTheDocument();

    const select = screen.getByLabelText('入口墙面破损 的处理状态') as HTMLSelectElement;
    expect(select.value).toBe('open');
    expect(screen.getByText('待处理')).toBeInTheDocument();

    expect(screen.getByText('x 1.50 · y 0.00 · z -2.25')).toBeInTheDocument();
    expect(screen.getByText('2026-10-26T08:00:00Z')).toBeInTheDocument();
  });

  it('下拉框选择 resolved 会调用 updateStatus 并更新显示', async () => {
    updateStatusMock.mockResolvedValue({ ...issue, status: 'resolved' });
    const onIssueUpdated = vi.fn();
    render(
      <IssueList issues={[issue]} onIssueUpdated={onIssueUpdated} formatTime={formatTime} />,
    );

    const select = screen.getByLabelText('入口墙面破损 的处理状态') as HTMLSelectElement;
    fireEvent.change(select, { target: { value: 'resolved' } });

    await waitFor(() => {
      expect(updateStatusMock).toHaveBeenCalledWith(issue.id, 'resolved');
    });
    await waitFor(() => {
      expect(select.value).toBe('resolved');
    });
    expect(onIssueUpdated).toHaveBeenCalledWith(expect.objectContaining({ status: 'resolved' }));
    expect(screen.getByText('已解决')).toBeInTheDocument();
  });

  it('updateStatus 失败时回滚下拉框并显示错误信息', async () => {
    updateStatusMock.mockRejectedValue(
      new ApiError('未找到 ID 为 ISS-1700000000000-ABCDEFGH 的问题', 'not_found', 404, 'id'),
    );
    render(<IssueList issues={[issue]} formatTime={formatTime} />);

    const select = screen.getByLabelText('入口墙面破损 的处理状态') as HTMLSelectElement;
    fireEvent.change(select, { target: { value: 'resolved' } });

    await waitFor(() => {
      expect(
        screen.getByText('状态更新失败：未找到 ID 为 ISS-1700000000000-ABCDEFGH 的问题'),
      ).toBeInTheDocument();
    });
    expect(select.value).toBe('open');
  });

  it('删除问题需要二次确认，确认后调用 deleteIssue', async () => {
    deleteIssueMock.mockResolvedValue(issue.id);
    const onIssueDeleted = vi.fn();
    const confirmDelete = vi.fn().mockReturnValue(true);
    render(
      <IssueList
        issues={[issue]}
        onIssueDeleted={onIssueDeleted}
        confirmDelete={confirmDelete}
        formatTime={formatTime}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: '删除 入口墙面破损' }));

    await waitFor(() => {
      expect(deleteIssueMock).toHaveBeenCalledWith(issue.id);
    });
    expect(confirmDelete).toHaveBeenCalled();
    await waitFor(() => {
      expect(onIssueDeleted).toHaveBeenCalledWith(issue.id);
    });
  });

  it('空列表显示空态', () => {
    render(<IssueList issues={[]} />);
    expect(screen.getByText('暂无问题')).toBeInTheDocument();
  });
});