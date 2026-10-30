import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NewIssuePanel from '../NewIssuePanel';
import TriageReport from '../TriageReport';
import type { Issue, TriageResponse } from '../../types';

vi.mock('../../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api')>();
  return {
    ...actual,
    triageIssue: vi.fn(),
    createIssue: vi.fn(),
  };
});

import { createIssue, triageIssue } from '../../api';

const triageIssueMock = vi.mocked(triageIssue);
const createIssueMock = vi.mocked(createIssue);

const highRiskTriage: TriageResponse = {
  suggestedPriority: 'high',
  suggestedCategory: '渗漏',
  confidence: 0.78,
  duplicateRisk: 'high',
  keywords: ['外墙', '渗水', '水渍'],
  reasons: ['命中关键词「渗水/水渍」，历史同类问题 82% 定级为 high'],
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
  triageIssueMock.mockReset();
  createIssueMock.mockReset();
});

describe('TriageReport', () => {
  it('重复报修风险为 high 时显示警告', () => {
    render(
      <TriageReport
        triage={highRiskTriage}
        onAdoptPriority={vi.fn()}
        onLocateIssue={vi.fn()}
      />,
    );

    const warning = screen.getByRole('alert');
    expect(warning).toHaveTextContent('重复报修风险高');
    expect(warning).toHaveTextContent('建议先确认是否重复报修');
  });

  it('重复报修风险为 none 时不显示警告', () => {
    render(
      <TriageReport
        triage={{ ...highRiskTriage, duplicateRisk: 'none' }}
        onAdoptPriority={vi.fn()}
        onLocateIssue={vi.fn()}
      />,
    );

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('展示建议优先级、可信度、关键词和理由', () => {
    render(
      <TriageReport
        triage={highRiskTriage}
        onAdoptPriority={vi.fn()}
        onLocateIssue={vi.fn()}
      />,
    );

    expect(screen.getByTestId('triage-suggested-priority')).toHaveTextContent('高');
    expect(screen.getByTestId('triage-confidence')).toHaveTextContent('78%');
    expect(screen.getByTestId('triage-duplicate-risk')).toHaveTextContent('高');
    expect(screen.getByText('外墙')).toBeInTheDocument();
    expect(screen.getByText('水渍')).toBeInTheDocument();
    expect(
      screen.getByText('命中关键词「渗水/水渍」，历史同类问题 82% 定级为 high'),
    ).toBeInTheDocument();
  });

  it('点击「采纳建议优先级」把建议值交给上层', () => {
    const onAdoptPriority = vi.fn();
    render(
      <TriageReport
        triage={highRiskTriage}
        onAdoptPriority={onAdoptPriority}
        onLocateIssue={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: '采纳建议优先级' }));

    expect(onAdoptPriority).toHaveBeenCalledTimes(1);
    expect(onAdoptPriority).toHaveBeenCalledWith('high');
  });

  it('相似问题的「查看」按钮可以跳转到对应问题', () => {
    const onLocateIssue = vi.fn();
    render(
      <TriageReport
        triage={highRiskTriage}
        onAdoptPriority={vi.fn()}
        onLocateIssue={onLocateIssue}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: '查看相似问题：东侧外墙渗水' }));

    expect(onLocateIssue).toHaveBeenCalledWith('ISS-1792988466562-SAAEVOZKQGIAC');
  });
});

describe('NewIssuePanel 的智能分诊', () => {
  it('点击「智能分诊」后展示分诊单，且不会自动改掉用户填的优先级', async () => {
    triageIssueMock.mockResolvedValue(highRiskTriage);
    render(<NewIssuePanel onCreated={vi.fn()} onLocateIssue={vi.fn()} />);

    fireEvent.change(screen.getByLabelText('标题'), { target: { value: '东侧外墙渗水' } });
    fireEvent.change(screen.getByLabelText('描述'), {
      target: { value: '墙皮发黑，有水渍往下淌' },
    });

    const priority = screen.getByLabelText('优先级') as HTMLSelectElement;
    expect(priority.value).toBe('medium');

    fireEvent.click(screen.getByRole('button', { name: '智能分诊' }));

    expect(await screen.findByTestId('triage-report')).toBeInTheDocument();
    expect(triageIssueMock).toHaveBeenCalledWith({
      title: '东侧外墙渗水',
      description: '墙皮发黑，有水渍往下淌',
      position: { x: 0, y: 0, z: 0 },
    });
    // 建议只是建议：用户填的优先级仍然是 medium
    expect((screen.getByLabelText('优先级') as HTMLSelectElement).value).toBe('medium');
    expect(screen.getByText('建议仅供参考，未自动修改表单')).toBeInTheDocument();
  });

  it('「一键采纳建议优先级」会把表单值改成建议值', async () => {
    triageIssueMock.mockResolvedValue(highRiskTriage);
    render(<NewIssuePanel onCreated={vi.fn()} onLocateIssue={vi.fn()} />);

    fireEvent.change(screen.getByLabelText('标题'), { target: { value: '东侧外墙渗水' } });
    fireEvent.click(screen.getByRole('button', { name: '智能分诊' }));
    await screen.findByTestId('triage-report');

    fireEvent.click(screen.getByRole('button', { name: '采纳建议优先级' }));

    await waitFor(() => {
      expect((screen.getByLabelText('优先级') as HTMLSelectElement).value).toBe('high');
    });
  });

  it('重复风险高时面板里出现警告', async () => {
    triageIssueMock.mockResolvedValue(highRiskTriage);
    render(<NewIssuePanel onCreated={vi.fn()} onLocateIssue={vi.fn()} />);

    fireEvent.change(screen.getByLabelText('标题'), { target: { value: '东侧外墙渗水' } });
    fireEvent.click(screen.getByRole('button', { name: '智能分诊' }));

    expect(await screen.findByText(/重复报修风险高/)).toBeInTheDocument();
  });

  it('标题为空时不做分诊，并给出提示', async () => {
    render(<NewIssuePanel onCreated={vi.fn()} onLocateIssue={vi.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: '智能分诊' }));

    expect(await screen.findByText('请先填写标题再做智能分诊')).toBeInTheDocument();
    expect(triageIssueMock).not.toHaveBeenCalled();
  });

  it('提交成功后把新问题交给上层', async () => {
    const created: Issue = {
      id: 'ISS-NEW',
      title: '东侧外墙渗水',
      description: '墙皮发黑',
      priority: 'medium',
      status: 'open',
      position: { x: 0, y: 0, z: 0 },
      createdAt: '2026-10-30T10:00:00Z',
      updatedAt: '2026-10-30T10:00:00Z',
    };
    createIssueMock.mockResolvedValue(created);
    const onCreated = vi.fn();
    render(<NewIssuePanel onCreated={onCreated} onLocateIssue={vi.fn()} />);

    fireEvent.change(screen.getByLabelText('标题'), { target: { value: '东侧外墙渗水' } });
    fireEvent.click(screen.getByRole('button', { name: '提交问题' }));

    await waitFor(() => {
      expect(onCreated).toHaveBeenCalledWith(created);
    });
    expect(createIssueMock).toHaveBeenCalledWith({
      title: '东侧外墙渗水',
      description: '',
      priority: 'medium',
      position: { x: 0, y: 0, z: 0 },
    });
  });
});