export interface ErrorBannerProps {
  message: string;
  onRetry?: () => void;
  retryLabel?: string;
}

/** 醒目的错误条：后端不可用、操作失败时展示。 */
export default function ErrorBanner({ message, onRetry, retryLabel = '重试' }: ErrorBannerProps) {
  return (
    <div className="error-banner" role="alert">
      <span className="error-banner__icon" aria-hidden="true">
        !
      </span>
      <span className="error-banner__text">{message}</span>
      {onRetry ? (
        <button type="button" className="button button--ghost" onClick={onRetry}>
          {retryLabel}
        </button>
      ) : null}
    </div>
  );
}