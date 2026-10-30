import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { fetchIssues, toUserMessage } from './api';
import { RealtimeClient, getWebSocketUrl } from './api/realtime';
import ErrorBanner from './components/ErrorBanner';
import FilterBar, { type PriorityFilter, type StatusFilter } from './components/FilterBar';
import IssueList from './components/IssueList';
import LiveIndicator from './components/LiveIndicator';
import NewIssuePanel from './components/NewIssuePanel';
import StatStrip from './components/StatStrip';
import ToastStack, { type Toast } from './components/ToastStack';
import type { Issue, RealtimeStatus } from './types';

export interface AppProps {
  /**
   * 注入实时客户端（测试用）。
   * - 传 null：完全关闭实时通道
   * - 传实例：使用该实例（测试里可以塞一个假 WebSocket）
   * - 不传：按环境变量新建一条真实的 WebSocket 连接
   */
  realtimeClient?: RealtimeClient | null;
}

export default function App({ realtimeClient }: AppProps = {}) {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [priorityFilter, setPriorityFilter] = useState<PriorityFilter>('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [realtimeStatus, setRealtimeStatus] = useState<RealtimeStatus>('connecting');
  const [realtimeAttempt, setRealtimeAttempt] = useState(0);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [freshId, setFreshId] = useState<string | null>(null);
  const [locatedId, setLocatedId] = useState<string | null>(null);

  // 请求序号：并发刷新时只有最后一次的结果才允许写进 state。
  // 否则「先发后到」的旧响应会覆盖新响应，界面显示过期数据。
  const loadSeq = useRef(0);
  const toastSeq = useRef(0);
  const issuesRef = useRef<Issue[]>([]);
  issuesRef.current = issues;

  const pushToast = useCallback((title: string, detail?: string, tone?: Toast['tone']) => {
    toastSeq.current += 1;
    const id = toastSeq.current;
    setToasts((current) => [...current, { id, title, detail, tone }]);
    setTimeout(() => {
      setToasts((current) => current.filter((toast) => toast.id !== id));
    }, 5000);
  }, []);

  const loadIssues = useCallback(async () => {
    const seq = ++loadSeq.current;
    setLoading(true);
    setLoadError(null);
    try {
      const data = await fetchIssues();
      if (seq !== loadSeq.current) {
        return; // 已经有更新的请求在飞，丢弃这次过期的响应
      }
      setIssues(data.issues);
    } catch (error) {
      if (seq !== loadSeq.current) {
        return;
      }
      // 后端不可用：显示错误条，页面其余部分照常渲染，不白屏
      setLoadError(toUserMessage(error));
    } finally {
      if (seq === loadSeq.current) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void loadIssues();
  }, [loadIssues]);

  /* ---------- 实时通道 ---------- */

  const client = useMemo(() => {
    if (realtimeClient !== undefined) {
      return realtimeClient;
    }
    // 测试环境里不要真的去连后端的 WebSocket。
    if (import.meta.env.MODE === 'test') {
      return null;
    }
    return new RealtimeClient({ url: getWebSocketUrl() });
  }, [realtimeClient]);

  useEffect(() => {
    if (!client) {
      setRealtimeStatus('closed');
      return;
    }

    client.setHandlers({
      onStatus: (status, info) => {
        setRealtimeStatus(status);
        setRealtimeAttempt(info?.attempt ?? 0);
        // 断线重连成功后重新拉一次全量，补齐断线期间丢掉的事件。
        if (status === 'open' && (info?.attempt ?? 0) > 0) {
          void loadIssues();
        }
      },
      onIssueCreated: (issue) => {
        setIssues((current) => {
          const index = current.findIndex((item) => item.id === issue.id);
          if (index === -1) {
            return [issue, ...current];
          }
          const next = current.slice();
          next[index] = issue;
          return next;
        });
        setFreshId(issue.id);
        setTimeout(() => {
          setFreshId((current) => (current === issue.id ? null : current));
        }, 4000);
        pushToast('收到新问题', issue.title, 'info');
      },
      onIssueUpdated: (issue) => {
        setIssues((current) =>
          current.map((item) => (item.id === issue.id ? issue : item)),
        );
      },
      onIssueDeleted: (id) => {
        setIssues((current) => current.filter((item) => item.id !== id));
        pushToast('问题已被删除', id, 'warn');
      },
    });

    client.connect();

    return () => {
      client.close();
    };
  }, [client, loadIssues, pushToast]);

  // 被「定位」的问题：滚动到可视区域并短暂描边。
  useEffect(() => {
    if (!locatedId) {
      return;
    }
    const element = document.getElementById(`issue-row-${locatedId}`);
    element?.scrollIntoView?.({ behavior: 'smooth', block: 'center' });
    const timer = setTimeout(() => {
      setLocatedId((current) => (current === locatedId ? null : current));
    }, 3000);
    return () => clearTimeout(timer);
  }, [locatedId]);

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
  }, []);

  /** 相似问题跳转：定位到台账里的那一条并高亮。 */
  const handleLocateIssue = useCallback(
    (id: string) => {
      const exists = issuesRef.current.some((item) => item.id === id);
      if (!exists) {
        pushToast('找不到这条相似问题', '它可能已被删除', 'warn');
        return;
      }
      const hiddenByFilter = !visibleIssues.some((item) => item.id === id);
      if (hiddenByFilter) {
        setPriorityFilter('all');
        setStatusFilter('all');
      }
      setLocatedId(id);
    },
    [pushToast, visibleIssues],
  );

  const total = issues.length;

  return (
    <div className="shell">
      <header className="masthead">
        <div className="masthead__id">
          <span className="masthead__eyebrow">Unity AR · 现场巡检台账</span>
          <h1 className="masthead__title">AR 现场巡检 · 问题管理端</h1>
          <p className="masthead__lede">
            巡检员在手机上用 AR 上报，管理人员在这里定级、派单、销项。
            当前台账共 <strong data-testid="total-count">{total}</strong> 个问题
            {visibleIssues.length !== issues.length
              ? `（当前筛选出 ${visibleIssues.length} 个）`
              : ''}
          </p>
        </div>
        <div className="masthead__meta">
          <LiveIndicator
            status={realtimeStatus}
            attempt={realtimeAttempt}
            off={!client}
            onReconnect={() => client?.connect()}
          />
          <button
            type="button"
            className="button button--solid"
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

      <div className="shell__body">
        <main className="ledger">
          <StatStrip issues={issues} />
          <FilterBar
            priority={priorityFilter}
            status={statusFilter}
            onPriorityChange={setPriorityFilter}
            onStatusChange={setStatusFilter}
            visibleCount={visibleIssues.length}
            totalCount={total}
            onReset={() => {
              setPriorityFilter('all');
              setStatusFilter('all');
            }}
          />
          <IssueList
            issues={visibleIssues}
            loading={loading}
            loadFailed={Boolean(loadError)}
            onIssueUpdated={handleIssueUpdated}
            onIssueDeleted={handleIssueDeleted}
            freshId={freshId}
            locatedId={locatedId}
          />
        </main>

        <aside className="rail">
          <NewIssuePanel
            onCreated={(issue) => {
              setIssues((current) =>
                current.some((item) => item.id === issue.id)
                  ? current
                  : [issue, ...current],
              );
              pushToast('已补录问题', issue.title, 'info');
            }}
            onLocateIssue={handleLocateIssue}
          />
        </aside>
      </div>

      <ToastStack
        toasts={toasts}
        onDismiss={(id) => {
          setToasts((current) => current.filter((toast) => toast.id !== id));
        }}
      />
    </div>
  );
}