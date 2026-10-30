package api

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

// TestLoggingMiddlewareAllowsWebSocketUpgrade 是一条回归测试：
//
// 日志中间件用 statusRecorder 包装了 ResponseWriter，而 statusRecorder
// 一开始没有实现 http.Hijacker，导致 WebSocket 握手直接 500
// （"response does not implement http.Hijacker"）。
//
// 这个坑之所以漏掉，是因为原来的单元测试直接拿 api.Server 当 handler 用，没套中间件；
// 只有真正跑起进程才会暴露。所以这里特意用「套了中间件的真实 HTTP 服务」来测。
func TestLoggingMiddlewareAllowsWebSocketUpgrade(t *testing.T) {
	api, _ := newTestServerWithHub(t)

	srv := httptest.NewServer(LoggingMiddleware(api))
	defer srv.Close()

	u := "ws" + strings.TrimPrefix(srv.URL, "http") + "/ws"
	conn, resp, err := websocket.DefaultDialer.Dial(u, nil)
	if err != nil {
		t.Fatalf("WebSocket 握手失败: %v (HTTP %v)", err, resp)
	}
	defer conn.Close()

	_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, _, err = conn.ReadMessage()
	if err != nil {
		t.Fatalf("读取 hello 事件失败: %v", err)
	}
}

// TestLoggingMiddlewareStillRecordsStatus 确认中间件没有把状态码统计搞坏。
func TestLoggingMiddlewareStillRecordsStatus(t *testing.T) {
	api, _ := newTestServerWithHub(t)

	handler := LoggingMiddleware(api)
	req := httptest.NewRequest(http.MethodGet, "/api/health", nil)
	rec := httptest.NewRecorder()
	handler.ServeHTTP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", rec.Code)
	}
}
