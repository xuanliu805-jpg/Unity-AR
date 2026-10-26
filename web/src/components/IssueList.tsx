import { useState } from 'react';
import { deleteIssue, toUserMessage, updateStatus } from '../api';
import type { Issue, IssueStatus } from '../types';
import ErrorBanner from './ErrorBanner';
import IssueRow from './IssueRow';

export interface IssueListProps {
  issues: Issue[];
  loading?: boolean;
  /** 列表数据是否因为后端不可用而没能加载成功。用于区分「确实没有数据」和「加载失败」。 */
  loadFailed?: boolean;
  /** 状态更新成功后把后端返回的最新记录回传给上层。 */
  onIssueUpdated?: (issue: Issue) => void;
  /** 删除成功后通知上层移除该条。 */
  onIssueDeleted?: (id: string) => void;
  /** 二次确认弹窗，方便测试时替换。 */
  confirmDelete?: (message: string) => boolean;
  formatTime?: (iso: string) => string;
}

const defaultFormatTime = (iso: string): string => {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
  );
};

export default function IssueList({
  issues,
  loading = false,
  loadFailed = false,
  onIssueUpdated,
  onIssueDeleted,
  confirmDelete = (message) => window.confirm(message),
  formatTime = defaultFormatTime,
}: IssueListProps) {
  const [actionError, setActionError] = useState<string | null>(null);
  // 正在删除的 id：避免双击删除按钮发出两个 DELETE，
  // 第二个会返回 404，弹出「删除失败：未找到…」这种与实际结果矛盾的提示。
  const [deletingIds, setDeletingIds] = useState<string[]>([]);

  const handleUpdateStatus = async (id: string, status: IssueStatus): Promise<void> => {
    try {
      const updated = await updateStatus(id, status);
      onIssueUpdated?.(updated);
    } catch (error) {
      setActionError(`状态更新失败：${toUserMessage(error)}`);
      throw error; // 交给 IssueRow 回滚下拉框
    }
  };

  const handleDelete = async (id: string): Promise<void> => {
    const issue = issues.find((item) => item.id === id);
    const title = issue ? issue.title : id;
    if (!confirmDelete(`确认删除问题「${title}」吗？该操作不可撤销。`)) {
      return;
    }

    setDeletingIds((current) => (current.includes(id) ? current : [...current, id]));
    try {
      await deleteIssue(id);
      onIssueDeleted?.(id);
    } catch (error) {
      setActionError(`删除失败：${toUserMessage(error)}`);
    } finally {
      setDeletingIds((current) => current.filter((item) => item !== id));
    }
  };

  return (
    <section className="issue-list">
      {actionError ? (
        <ErrorBanner
          message={actionError}
          onRetry={() => setActionError(null)}
          retryLabel="关闭"
        />
      ) : null}

      <div className="table-wrapper">
        <table className="issue-table">
          <thead>
            <tr>
              <th scope="col">标题</th>
              <th scope="col">描述</th>
              <th scope="col">优先级</th>
              <th scope="col">状态</th>
              <th scope="col">位置坐标</th>
              <th scope="col">创建时间</th>
              <th scope="col">操作</th>
            </tr>
          </thead>
          <tbody>
            {issues.map((issue) => (
              <IssueRow
                key={issue.id}
                issue={issue}
                deleting={deletingIds.includes(issue.id)}
                onUpdateStatus={handleUpdateStatus}
                onDelete={(id) => {
                  void handleDelete(id);
                }}
                formatTime={formatTime}
              />
            ))}
            {loading && issues.length === 0 ? (
              <tr className="issue-row issue-row--skeleton">
                <td colSpan={7}>加载中……</td>
              </tr>
            ) : null}
            {!loading && issues.length === 0 ? (
              <tr className="issue-row issue-row--empty">
                <td colSpan={7}>
                  {loadFailed ? '数据加载失败，请点击右上角「刷新」重试' : '暂无问题'}
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </section>
  );
}
