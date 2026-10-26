// Command server 启动巡检问题后端服务。
//
// 用法：
//
//	go run ./cmd/server                 # 默认监听 :8080，数据写入 ./data/issues.json
//	go run ./cmd/server -addr :9000     # 自定义端口
//	go run ./cmd/server -data ./db.json  # 自定义数据文件
package main

import (
	"context"
	"errors"
	"flag"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/api"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/store"
)

func main() {
	addr := flag.String("addr", ":8080", "HTTP 监听地址")
	dataPath := flag.String("data", "data/issues.json", "JSON 数据文件路径")
	flag.Parse()

	st, err := store.NewFileStore(*dataPath)
	if err != nil {
		log.Fatalf("初始化数据存储失败: %v", err)
	}

	srv := &http.Server{
		Addr:              *addr,
		Handler:           logging(api.New(st)),
		ReadHeaderTimeout: 10 * time.Second,
		// 慢速客户端不能长期占用连接
		WriteTimeout: 30 * time.Second,
		IdleTimeout:  60 * time.Second,
	}

	go func() {
		log.Printf("Unity-AR 巡检后端已启动: http://localhost%s (数据文件: %s)", *addr, *dataPath)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("服务异常退出: %v", err)
		}
	}()

	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit

	log.Println("正在关闭服务...")
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := srv.Shutdown(ctx); err != nil {
		log.Printf("关闭超时: %v", err)
	}
	log.Println("已退出")
}

// logging 打印每条请求的方法、路径与状态码，方便排查 Unity / React 的联调问题。
func logging(next http.Handler) http.Handler {
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
