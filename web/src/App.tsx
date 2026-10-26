import { useCallback, useEffect, useMemo, useState } from 'react';
import { fetchIssues, toUserMessage } from './api';
import ErrorBanner from './components/ErrorBanner';
import IssueList from './components/IssueList';
import type { Issue, IssuePriority, IssueStatus } from './types';
import { PRIORITY_LABELS, PRIORITY_OPTIONS, STATUS_LABELS, STATUS_OPTIONS } from './types';

type PriorityFilter = 'all' | IssuePriority;
type StatusFilter = 'all' | IssueStatus;

export default function App() {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState<boolean>(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [priorityFilter, setPriorityFilter] = useState<PriorityFilter>('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');

  const loadIssues = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const data = await fetchIssues();
      setIssues(data.issues);
      setTotal(data.count);
    } catch (error) {
      // 后端不可用：显示错误条，页面其余部分照常渲染，不白屏
      setLoadError(toUserMessage(error));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadIssues();
  }, [loadIssues]);

  const visibleIssues = useMemo(
    () =>
      issues.filter(
        (issue) =>
          (priorityFilter === 'all' || issue.priority === priorityFilter) &&
          (statusFilter === 'all' || issue.status === statusFilter),
      ),
    [issues, priorityFilter, statusFilter],
  );

  const handleIssueUpdated = useCallback((updated: Issue) => {
    setIssues((current) => current.map((item) => (item.id === updated.id ? updated : item)));
  }, []);

  const handleIssueDeleted = useCallback((id: string) => {
    setIssues((current) => current.filter((item) => item.id !== id));
    setTotal((current) => Math.max(0, current - 1));
  }, []);

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header__left">
          <h1 className="app-title">AR 现场巡检 · 问题管理端</h1>
          <p className="app-subtitle">
            共 <strong data-testid="total-count">{total}</strong> 个问题
            {visibleIssues.length !== issues.length ? `（当前筛选出 ${visibleIssues.length} 个）` : ''}
          </p>
        </div>
        <div className="app-header__right">
          <button
            type="button"
            className="button button--primary"
            onClick={() => {
              void loadIssues();
            }}
          >
            刷新
          </button>
        </div>
      </header>

      {loadError ? (
        <ErrorBanner
          message={loadError}
          onRetry={() => {
            void loadIssues();
          }}
        />
      ) : null}

      <section className="toolbar">
        <label className="filter">
          <span className="filter__label">优先级</span>
          <select
            className="filter__select"
            aria-label="按优先级筛选"
            value={priorityFilter}
            onChange={(event) => setPriorityFilter(event.target.value as PriorityFilter)}
          >
            <option value="all">全部</option>
            {PRIORITY_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {PRIORITY_LABELS[option]}
              </option>
            ))}
          </select>
        </label>

        <label className="filter">
          <span className="filter__label">状态</span>
          <select
            className="filter__select"
            aria-label="按状态筛选"
            value={statusFilter}
            onChange={(event) => setStatusFilter(event.target.value as StatusFilter)}
          >
            <option value="all">全部</option>
            {STATUS_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {STATUS_LABELS[option]}
              </option>
            ))}
          </select>
        </label>
      </section>

      <IssueList
        issues={visibleIssues}
        loading={loading}
        onIssueUpdated={handleIssueUpdated}
        onIssueDeleted={handleIssueDeleted}
      />
    </div>
  );
}