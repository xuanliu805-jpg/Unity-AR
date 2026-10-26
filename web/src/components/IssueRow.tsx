import { useEffect, useRef, useState } from 'react';
import type { Issue, IssueStatus } from '../types';
import { STATUS_LABELS, STATUS_OPTIONS } from '../types';

export interface IssueRowProps {
  issue: Issue;
  /** 返回 Promise，失败时抛出异常；本组件负责回滚下拉框。 */
  onUpdateStatus: (id: string, status: IssueStatus) => Promise<void>;
  onDelete: (id: string) => void;
  formatTime: (iso: string) => string;
  /** 删除请求进行中：禁用删除按钮，避免重复提交。 */
  deleting?: boolean;
}

export default function IssueRow({
  issue,
  onUpdateStatus,
  onDelete,
  formatTime,
  deleting = false,
}: IssueRowProps) {
  const [status, setStatus] = useState<IssueStatus>(issue.status);
  const [busy, setBusy] = useState(false);

  // 保存「最近一次已知的服务端状态」。
  // 失败回滚要用它，而不是渲染闭包里的旧值：
  // 请求期间父组件可能刷新了列表并带来新的 issue.status，
  // 用陈旧的 previous 回滚会把新状态覆盖掉。
  const serverStatus = useRef<IssueStatus>(issue.status);

  // 服务端状态变化（例如刷新后）同步回下拉框
  useEffect(() => {
    serverStatus.current = issue.status;
    setStatus(issue.status);
  }, [issue.status]);

  const handleChange = async (next: IssueStatus) => {
    const previous = serverStatus.current;
    setStatus(next);
    setBusy(true);
    try {
      await onUpdateStatus(issue.id, next);
      serverStatus.current = next;
    } catch (error) {
      // 失败：回滚到最后一次已知的服务端状态
      setStatus(previous);
      console.error('更新问题状态失败:', error);
    } finally {
      setBusy(false);
    }
  };

  const { x, y, z } = issue.position;

  return (
    <tr className="issue-row" data-status={issue.status}>
      <td className="cell cell--title">
        <div className="issue-title">{issue.title}</div>
        <div className="issue-id">{issue.id}</div>
      </td>
      <td className="cell cell--description">{issue.description || '—'}</td>
      <td className="cell">
        <span className={`priority priority--${issue.priority}`}>
          {issue.priority === 'high' ? '高' : issue.priority === 'medium' ? '中' : '低'}
        </span>
      </td>
      <td className="cell">
        <select
          className={`status-select status-select--${status}`}
          aria-label={`${issue.title} 的处理状态`}
          value={status}
          disabled={busy}
          onChange={(event) => {
            void handleChange(event.target.value as IssueStatus);
          }}
        >
          {STATUS_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {STATUS_LABELS[option]}
            </option>
          ))}
        </select>
      </td>
      <td className="cell cell--position">
        <span className="position">
          x {x.toFixed(2)} · y {y.toFixed(2)} · z {z.toFixed(2)}
        </span>
      </td>
      <td className="cell cell--time">{formatTime(issue.createdAt)}</td>
      <td className="cell cell--actions">
        <button
          type="button"
          className="button button--danger"
          aria-label={`删除 ${issue.title}`}
          disabled={deleting}
          onClick={() => onDelete(issue.id)}
        >
          {deleting ? '删除中…' : '删除'}
        </button>
      </td>
    </tr>
  );
}
