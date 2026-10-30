import type { Issue } from '../types';

export interface StatStripProps {
  issues: Issue[];
}

interface Metric {
  key: string;
  label: string;
  value: number;
  tone: 'ink' | 'open' | 'busy' | 'done' | 'flag';
}

/**
 * 顶部计量条：把「一共多少条、还剩多少没处理」做成测绘仪表读数的样子，
 * 而不是四张圆角卡片。数字用等宽字体，标签用小号大写字距。
 */
export default function StatStrip({ issues }: StatStripProps) {
  const open = issues.filter((issue) => issue.status === 'open').length;
  const busy = issues.filter((issue) => issue.status === 'in_progress').length;
  const done = issues.filter((issue) => issue.status === 'resolved').length;
  const flag = issues.filter((issue) => issue.priority === 'high').length;
  const total = issues.length;

  const metrics: Metric[] = [
    { key: 'total', label: '全部问题', value: total, tone: 'ink' },
    { key: 'open', label: '待处理', value: open, tone: 'open' },
    { key: 'busy', label: '处理中', value: busy, tone: 'busy' },
    { key: 'done', label: '已解决', value: done, tone: 'done' },
    { key: 'flag', label: '高优先级', value: flag, tone: 'flag' },
  ];

  const ratio = total === 0 ? 0 : Math.round((done / total) * 100);

  return (
    <section className="stat-strip" aria-label="问题统计">
      <div className="stat-strip__metrics">
        {metrics.map((metric) => (
          <div className={`stat stat--${metric.tone}`} key={metric.key}>
            <span className="stat__value">{metric.value}</span>
            <span className="stat__label">{metric.label}</span>
          </div>
        ))}
      </div>
      <div className="stat-strip__progress">
        <div className="meter" role="img" aria-label={`已解决 ${ratio}%`}>
          <span className="meter__fill" style={{ width: `${ratio}%` }} />
        </div>
        <span className="stat-strip__ratio">处理进度 {ratio}%</span>
      </div>
    </section>
  );
}