import type { IssuePriority, IssueStatus } from '../types';
import { PRIORITY_LABELS, PRIORITY_OPTIONS, STATUS_LABELS, STATUS_OPTIONS } from '../types';

export type PriorityFilter = 'all' | IssuePriority;
export type StatusFilter = 'all' | IssueStatus;

export interface FilterBarProps {
  priority: PriorityFilter;
  status: StatusFilter;
  onPriorityChange: (value: PriorityFilter) => void;
  onStatusChange: (value: StatusFilter) => void;
  /** 当前筛选出来的条数 / 总条数。 */
  visibleCount: number;
  totalCount: number;
  onReset?: () => void;
}

/** 筛选区：两个原生 select（无障碍友好、可键盘操作），样式做成台账标签。 */
export default function FilterBar({
  priority,
  status,
  onPriorityChange,
  onStatusChange,
  visibleCount,
  totalCount,
  onReset,
}: FilterBarProps) {
  const dirty = priority !== 'all' || status !== 'all';

  return (
    <section className="filter-bar" aria-label="筛选条件">
      <label className="filter">
        <span className="filter__label">优先级</span>
        <select
          className="filter__select"
          aria-label="按优先级筛选"
          value={priority}
          onChange={(event) => onPriorityChange(event.target.value as PriorityFilter)}
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
          value={status}
          onChange={(event) => onStatusChange(event.target.value as StatusFilter)}
        >
          <option value="all">全部</option>
          {STATUS_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {STATUS_LABELS[option]}
            </option>
          ))}
        </select>
      </label>

      {onReset ? (
        <button
          type="button"
          className="button button--quiet"
          onClick={onReset}
          disabled={!dirty}
        >
          重置筛选
        </button>
      ) : null}

      <span className="filter-bar__count">
        命中 <strong>{visibleCount}</strong> / {totalCount}
      </span>
    </section>
  );
}