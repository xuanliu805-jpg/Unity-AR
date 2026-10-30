import type { RealtimeStatus } from '../types';

export interface LiveIndicatorProps {
  status: RealtimeStatus;
  /** 已经重试的次数，用于在重连时告诉用户「第几次」。 */
  attempt?: number;
  /** 点击后立刻重连。 */
  onReconnect?: () => void;
  /** 实时通道整体关闭（例如在测试环境）。 */
  off?: boolean;
}

const LABELS: Record<RealtimeStatus, string> = {
  connecting: '正在连接',
  open: '实时已连接',
  reconnecting: '重连中',
  closed: '已断开',
};

/** 实时通道状态指示灯：连接 / 重连 / 断开 一眼可辨。 */
export default function LiveIndicator({
  status,
  attempt = 0,
  onReconnect,
  off = false,
}: LiveIndicatorProps) {
  const label = off ? '实时未启用' : LABELS[status];

  return (
    <div
      className={`live live--${off ? 'off' : status}`}
      role="status"
      aria-live="polite"
    >
      <span className="live__lamp" aria-hidden="true" />
      <span className="live__text">{label}</span>
      {!off && status === 'reconnecting' && attempt > 0 ? (
        <span className="live__detail">第 {attempt} 次</span>
      ) : null}
      {!off && onReconnect && (status === 'closed' || status === 'reconnecting') ? (
        <button type="button" className="button button--quiet" onClick={onReconnect}>
          立即重连
        </button>
      ) : null}
    </div>
  );
}