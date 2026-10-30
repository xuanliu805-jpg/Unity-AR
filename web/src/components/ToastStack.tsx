export interface Toast {
  id: number;
  title: string;
  detail?: string;
  tone?: 'info' | 'warn';
}

export interface ToastStackProps {
  toasts: Toast[];
  onDismiss: (id: number) => void;
}

/**
 * 轻量提示条：实时推送到达时在右上角一闪而过。
 * 不打断用户操作，不遮挡主内容。
 */
export default function ToastStack({ toasts, onDismiss }: ToastStackProps) {
  if (toasts.length === 0) {
    return null;
  }
  return (
    <div className="toast-stack" aria-live="polite">
      {toasts.map((toast) => (
        <div
          className={`toast toast--${toast.tone ?? 'info'}`}
          key={toast.id}
          role="status"
        >
          <span className="toast__title">{toast.title}</span>
          {toast.detail ? <span className="toast__detail">{toast.detail}</span> : null}
          <button
            type="button"
            className="toast__close"
            aria-label={`关闭提示：${toast.title}`}
            onClick={() => onDismiss(toast.id)}
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}