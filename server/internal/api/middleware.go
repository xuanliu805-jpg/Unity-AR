package api

import (
	"bufio"
	"fmt"
	"log"
	"net"
	"net/http"
)

// LoggingMiddleware 给每个请求打印「方法 路径 -> 状态码」，方便排查 Unity / React 的联调问题。
//
// 注意：这个包装器**必须**实现 http.Hijacker，
// 否则 WebSocket 升级会失败并报 "response does not implement http.Hijacker"。
// 这是实测踩过的坑：最初中间件写在 cmd/server 里，单元测试直接用 api 没套中间件，
// 于是测试全绿、真机上 WebSocket 握手却 500。
func LoggingMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		rec := &statusRecorder{ResponseWriter: w, status: http.StatusOK}
		next.ServeHTTP(rec, r)
		log.Printf("%s %s -> %d", r.Method, r.URL.Path, rec.status)
	})
}

type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (r *statusRecorder) WriteHeader(code int) {
	r.status = code
	r.ResponseWriter.WriteHeader(code)
}

// Hijack 把底层连接交出去，供 WebSocket 升级使用。
func (r *statusRecorder) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	hijacker, ok := r.ResponseWriter.(http.Hijacker)
	if !ok {
		return nil, nil, fmt.Errorf("底层 ResponseWriter 不支持 http.Hijacker")
	}
	return hijacker.Hijack()
}
