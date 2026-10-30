import { Component, type ErrorInfo, type ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * 顶层错误边界。
 *
 * 任务书 3.2 明确要求「后端不可用时，页面应显示错误提示，不能直接白屏」。
 * 网络层的错误已经由 api 层转换成 ApiError / NetworkError 并交给 UI 处理，
 * 但万一出现别的未捕获异常（例如某个组件渲染时读到了 undefined），
 * 没有这一层的话 React 会卸载整棵树，页面就变成一片空白。
 * 这里兜住最后一道防线：出错时显示错误卡片 + 刷新按钮，而不是白屏。
 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  override state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // 留一份日志方便排查，但不要把堆栈直接展示给用户。
    console.error('页面渲染出错:', error, info.componentStack);
  }

  override render(): ReactNode {
    const { error } = this.state;
    if (!error) {
      return this.props.children;
    }

    return (
      <div className="error-boundary" role="alert">
        <h1 className="error-boundary__title">页面出错了</h1>
        <p className="error-boundary__message">{error.message || '发生了未知的错误'}</p>
        <button type="button" className="button button--solid" onClick={() => window.location.reload()}>
          刷新页面
        </button>
      </div>
    );
  }
}
