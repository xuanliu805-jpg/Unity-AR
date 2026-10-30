import type { SocketLike } from '../api/realtime';

/**
 * 测试用的假 WebSocket。
 *
 * 记录所有被创建的实例，并提供 emitOpen / emitMessage / emitClose / emitError
 * 让测试可以手动驱动「连接成功」「服务端推送」「连接断开」这些时刻。
 */
export class MockWebSocket implements SocketLike {
  static instances: MockWebSocket[] = [];

  static reset(): void {
    MockWebSocket.instances = [];
  }

  /** 最近创建的实例。 */
  static get latest(): MockWebSocket {
    const instance = MockWebSocket.instances[MockWebSocket.instances.length - 1];
    if (!instance) {
      throw new Error('还没有创建过 WebSocket 实例');
    }
    return instance;
  }

  readyState = 0;

  sent: string[] = [];

  closed = false;

  /* eslint-disable @typescript-eslint/no-explicit-any */
  onopen: ((event: any) => void) | null = null;
  onclose: ((event: any) => void) | null = null;
  onerror: ((event: any) => void) | null = null;
  onmessage: ((event: any) => void) | null = null;
  /* eslint-enable @typescript-eslint/no-explicit-any */

  constructor(public readonly url: string) {
    MockWebSocket.instances.push(this);
  }

  send(data: string): void {
    this.sent.push(data);
  }

  close(): void {
    this.closed = true;
    this.readyState = 3;
  }

  emitOpen(): void {
    this.readyState = 1;
    this.onopen?.({ type: 'open' });
  }

  emitMessage(payload: unknown): void {
    this.onmessage?.({ data: JSON.stringify(payload) });
  }

  emitClose(code = 1006): void {
    this.readyState = 3;
    this.onclose?.({ code });
  }

  emitError(): void {
    this.onerror?.({ type: 'error' });
  }
}