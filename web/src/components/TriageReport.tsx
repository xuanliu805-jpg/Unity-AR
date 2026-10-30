import type { IssuePriority, TriageResponse } from '../types';
import { DUPLICATE_RISK_LABELS, PRIORITY_LABELS } from '../types';

export interface TriageReportProps {
  triage: TriageResponse;
  /** 表单里当前填的优先级，用于提示「建议与当前值不同」。 */
  currentPriority?: IssuePriority;
  /** 一键采纳建议优先级。 */
  onAdoptPriority: (priority: IssuePriority) => void;
  /** 跳转到相似问题（可能是重复报修）。 */
  onLocateIssue: (id: string) => void;
}

const RISK_TEXT: Record<string, string> = {
  high: '重复报修风险高',
  low: '重复报修风险低',
  none: '未发现重复报修',
};

/**
 * 分诊单：AI 建议只是一张「建议单」，
 * 优先级必须由用户点「采纳」才会写进表单，绝不自动改写。
 */
export default function TriageReport({
  triage,
  currentPriority,
  onAdoptPriority,
  onLocateIssue,
}: TriageReportProps) {
  const percent = Math.round(triage.confidence * 100);
  const sameAsCurrent = currentPriority === triage.suggestedPriority;

  return (
    <div className="triage" data-testid="triage-report">
      <div className="triage__head">
        <span className="triage__stamp">AI 分诊单</span>
        <span className="triage__category">{triage.suggestedCategory}</span>
      </div>

      <div className="triage__grid">
        <div className="triage__cell">
          <span className="triage__key">建议优先级</span>
          <span
            className={`triage__value triage__value--${triage.suggestedPriority}`}
            data-testid="triage-suggested-priority"
          >
            {PRIORITY_LABELS[triage.suggestedPriority]}
          </span>
          <button
            type="button"
            className="button button--adopt"
            onClick={() => onAdoptPriority(triage.suggestedPriority)}
          >
            采纳建议优先级
          </button>
          {sameAsCurrent ? (
            <span className="triage__note">与当前填写一致</span>
          ) : (
            <span className="triage__note">建议仅供参考，未自动修改表单</span>
          )}
        </div>

        <div className="triage__cell">
          <span className="triage__key">可信度</span>
          <span className="triage__value triage__value--number" data-testid="triage-confidence">
            {percent}%
          </span>
          <span className="meter meter--inline" role="img" aria-label={`可信度 ${percent}%`}>
            <span className="meter__fill" style={{ width: `${percent}%` }} />
          </span>
        </div>

        <div className="triage__cell">
          <span className="triage__key">重复报修风险</span>
          <span
            className={`triage__value triage__value--risk-${triage.duplicateRisk}`}
            data-testid="triage-duplicate-risk"
          >
            {DUPLICATE_RISK_LABELS[triage.duplicateRisk]}
          </span>
        </div>
      </div>

      {triage.duplicateRisk === 'high' ? (
        <div className="triage__warning" role="alert">
          <span className="triage__warning-mark" aria-hidden="true">
            !
          </span>
          <span>
            {RISK_TEXT.high}：下方存在相似问题，建议先确认是否重复报修，再决定是否派单。
          </span>
        </div>
      ) : null}

      {triage.keywords.length > 0 ? (
        <div className="triage__keywords">
          <span className="triage__key">关键词</span>
          {triage.keywords.map((keyword) => (
            <span className="chip" key={keyword}>
              {keyword}
            </span>
          ))}
        </div>
      ) : null}

      {triage.reasons.length > 0 ? (
        <ul className="triage__reasons">
          {triage.reasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      ) : null}

      {triage.similarIssues.length > 0 ? (
        <div className="triage__similar">
          <span className="triage__key">相似问题</span>
          {triage.similarIssues.map((similar) => (
            <div className="similar" key={similar.id}>
              <div className="similar__main">
                <span className="similar__title">{similar.title}</span>
                <span className="similar__metrics">
                  相似度 {Math.round(similar.similarity * 100)}% · 距离{' '}
                  {similar.distance.toFixed(1)} m · {similar.ageDays} 天前
                </span>
              </div>
              <button
                type="button"
                className="button button--outline"
                aria-label={`查看相似问题：${similar.title}`}
                onClick={() => onLocateIssue(similar.id)}
              >
                查看
              </button>
              {similar.advice ? (
                <p className="similar__advice">{similar.advice}</p>
              ) : null}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}