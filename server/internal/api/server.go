// Package api 实现给 Unity 客户端和 React 管理端共用的 HTTP 接口。
//
// 路由一览（详见 README）：
//
//	GET    /api/health                服务运行状态检查
//	GET    /api/issues                问题列表
//	POST   /api/issues                上报问题（Unity）
//	PATCH  /api/issues/{id}/status    修改处理状态（React）
//	POST   /api/issues/{id}/status    同上，兼容不支持 PATCH 的客户端
//	DELETE /api/issues/{id}           删除问题（加分项）
package api

import (
	"encoding/json"
	"errors"
	"github.com/gorilla/websocket"
	"io"
	"log"
	"math"
	"net/http"
	"strconv"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/analysis"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/hub"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/model"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/store"
)

// 请求体大小上限（1 MB）。超过时返回 413，而不是静默截断。
const maxBodyBytes = 1 << 20

// 字段长度上限。服务端也做限制，避免有人把整本书粘进标题。
const (
	maxTitleLength       = 200
	maxDescriptionLength = 2000
)

// Server 是 HTTP 处理器。
type Server struct {
	store *store.Store
	hub   *hub.Hub
	mux   *http.ServeMux
}

// New 创建 API 服务。
func New(s *store.Store, h *hub.Hub) *Server {
	if h == nil {
		h = hub.New()
	}
	srv := &Server{store: s, hub: h, mux: http.NewServeMux()}
	srv.routes()
	return srv
}

// ServeHTTP 实现 http.Handler。
func (s *Server) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	// React 管理端跑在 Vite 开发服务器（5173）或静态目录下，
	// 与后端不同源，因此这里统一放开 CORS。
	w.Header().Set("Access-Control-Allow-Origin", "*")
	w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization")
	w.Header().Set("Access-Control-Max-Age", "600")

	if r.Method == http.MethodOptions {
		w.WriteHeader(http.StatusNoContent)
		return
	}
	s.mux.ServeHTTP(w, r)
}

func (s *Server) routes() {
	s.mux.HandleFunc("GET /api/health", s.handleHealth)
	s.mux.HandleFunc("GET /api/issues", s.handleListIssues)
	s.mux.HandleFunc("POST /api/issues", s.handleCreateIssue)
	s.mux.HandleFunc("PATCH /api/issues/{id}/status", s.handleUpdateStatus)
	s.mux.HandleFunc("POST /api/issues/{id}/status", s.handleUpdateStatus)
	s.mux.HandleFunc("DELETE /api/issues/{id}", s.handleDeleteIssue)
	s.mux.HandleFunc("POST /api/analysis/triage", s.handleTriage)
	s.mux.HandleFunc("/ws", s.handleWebSocket)
	s.mux.HandleFunc("/api/ws", s.handleWebSocket)
	s.mux.HandleFunc("/", s.handleNotFound)
}

// ---------- 通用响应工具 ----------

type apiError struct {
	Code    string `json:"code"`
	Message string `json:"message"`
	Field   string `json:"field,omitempty"`
}

func writeJSON(w http.ResponseWriter, code int, payload any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(code)
	if err := json.NewEncoder(w).Encode(payload); err != nil {
		log.Printf("写响应失败: %v", err)
	}
}

func writeError(w http.ResponseWriter, code int, errCode, message, field string) {
	writeJSON(w, code, map[string]any{"error": apiError{Code: errCode, Message: message, Field: field}})
}

// ---------- 请求 DTO ----------

type createIssueRequest struct {
	Title       string          `json:"title"`
	Description string          `json:"description"`
	Priority    string          `json:"priority"`
	Position    *model.Position `json:"position"`
}

type updateStatusRequest struct {
	Status string `json:"status"`
}

// ---------- 处理器 ----------

func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"status":    "ok",
		"service":   "unity-ar-inspection",
		"issues":    s.store.Count(),
		"websocket": "/ws",
	})
}

func (s *Server) handleListIssues(w http.ResponseWriter, r *http.Request) {
	issues := s.store.List()
	if issues == nil {
		issues = []model.Issue{}
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"issues": issues,
		"count":  len(issues),
	})
}

func (s *Server) handleCreateIssue(w http.ResponseWriter, r *http.Request) {
	var req createIssueRequest
	if err := decodeJSON(r, &req); err != nil {
		if errors.Is(err, errBodyTooLarge) {
			writeError(w, http.StatusRequestEntityTooLarge, "invalid_argument",
				"请求体过大，请检查提交的内容", "")
			return
		}
		writeError(w, http.StatusBadRequest, "invalid_json", "请求体不是合法的 JSON", "")
		return
	}

	title := strings.TrimSpace(req.Title)
	if title == "" {
		writeError(w, http.StatusBadRequest, "invalid_argument", "标题(title)不能为空", "title")
		return
	}

	// 按「字符数」而不是字节数计算，和 Unity 客户端的口径保持一致：
	// 否则一个 100 字的中文标题（UTF-8 300 字节）会客户端放行、后端 400。
	if utf8.RuneCountInString(title) > maxTitleLength {
		writeError(w, http.StatusBadRequest, "invalid_argument",
			"标题(title)不能超过 "+strconv.Itoa(maxTitleLength)+" 个字符", "title")
		return
	}

	description := strings.TrimSpace(req.Description)
	if utf8.RuneCountInString(description) > maxDescriptionLength {
		writeError(w, http.StatusBadRequest, "invalid_argument",
			"描述(description)不能超过 "+strconv.Itoa(maxDescriptionLength)+" 个字符", "description")
		return
	}

	priority, ok := model.ParsePriority(req.Priority)
	if !ok {
		writeError(w, http.StatusBadRequest, "invalid_argument",
			"优先级(priority)必须是 low、medium 或 high 之一，当前值: "+quoteJSON(req.Priority), "priority")
		return
	}

	pos := model.Position{}
	if req.Position != nil {
		pos = *req.Position
		if !isFinite(pos.X) || !isFinite(pos.Y) || !isFinite(pos.Z) {
			writeError(w, http.StatusBadRequest, "invalid_argument", "位置(position)的 x/y/z 必须是有限数值", "position")
			return
		}
	}

	issue, err := s.store.Create(model.Issue{
		Title:       title,
		Description: description,
		Priority:    priority,
		Position:    pos,
	})
	if err != nil {
		log.Printf("保存问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "internal_error", "保存问题失败，请稍后重试", "")
		return
	}

	writeJSON(w, http.StatusCreated, map[string]any{"issue": issue})

	// 广播给所有订阅者，PC 管理端不用刷新就能看到新问题。
	s.hub.Publish(hub.EventTypeIssueCreated, issue)
}

func (s *Server) handleUpdateStatus(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	if strings.TrimSpace(id) == "" {
		writeError(w, http.StatusBadRequest, "invalid_argument", "问题 ID 不能为空", "id")
		return
	}

	var req updateStatusRequest
	if err := decodeJSON(r, &req); err != nil {
		if errors.Is(err, errBodyTooLarge) {
			writeError(w, http.StatusRequestEntityTooLarge, "invalid_argument",
				"请求体过大，请检查提交的内容", "")
			return
		}
		writeError(w, http.StatusBadRequest, "invalid_json", "请求体不是合法的 JSON", "")
		return
	}

	status, ok := model.ParseStatus(req.Status)
	if !ok {
		writeError(w, http.StatusBadRequest, "invalid_argument",
			"状态(status)必须是 open、in_progress 或 resolved 之一，当前值: "+quoteJSON(req.Status), "status")
		return
	}

	issue, err := s.store.UpdateStatus(id, status)
	if err != nil {
		if errors.Is(err, store.ErrNotFound) {
			writeError(w, http.StatusNotFound, "not_found", "未找到 ID 为 "+id+" 的问题", "id")
			return
		}
		log.Printf("更新状态失败: %v", err)
		writeError(w, http.StatusInternalServerError, "internal_error", "更新状态失败，请稍后重试", "")
		return
	}

	writeJSON(w, http.StatusOK, map[string]any{"issue": issue})

	s.hub.Publish(hub.EventTypeIssueUpdated, issue)
}

func (s *Server) handleDeleteIssue(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	if err := s.store.Delete(id); err != nil {
		if errors.Is(err, store.ErrNotFound) {
			writeError(w, http.StatusNotFound, "not_found", "未找到 ID 为 "+id+" 的问题", "id")
			return
		}
		log.Printf("删除问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "internal_error", "删除问题失败，请稍后重试", "")
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"deleted": id})

	s.hub.Publish(hub.EventTypeIssueDeleted, map[string]string{"id": id})
}

func (s *Server) handleNotFound(w http.ResponseWriter, r *http.Request) {
	// 路径存在、只是方法不对时返回 405，比笼统的 404「未知接口」更准确。
	if isKnownPath(r.URL.Path) {
		writeError(w, http.StatusMethodNotAllowed, "method_not_allowed",
			r.Method+" 方法不支持: "+r.URL.Path, "")
		return
	}
	writeError(w, http.StatusNotFound, "not_found", "未知接口: "+r.URL.Path, "")
}

// isKnownPath 判断路径是否是本服务提供的接口（不考虑方法）。
func isKnownPath(path string) bool {
	switch path {
	case "/api/health", "/api/issues":
		return true
	}
	if !strings.HasPrefix(path, "/api/issues/") {
		return false
	}
	rest := strings.TrimPrefix(path, "/api/issues/")
	if rest == "" {
		return false
	}
	// /api/issues/{id} 或 /api/issues/{id}/status
	if strings.HasSuffix(rest, "/status") {
		rest = strings.TrimSuffix(rest, "/status")
	}
	return rest != "" && !strings.Contains(rest, "/")
}

// handleWebSocket 把连接升级成 WebSocket，并交给广播中心。
func (s *Server) handleWebSocket(w http.ResponseWriter, r *http.Request) {
	upgrader := websocket.Upgrader{
		// 允许任意来源：管理端可能跑在 Vite 开发服务器(5173/5174)或任意静态目录下。
		// WebSocket 握手不受 CORS 限制，这里显式放开是为了让 CheckOrigin 不拦截。
		CheckOrigin: func(r *http.Request) bool { return true },
	}

	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("WebSocket 升级失败: %v", err)
		return
	}

	s.hub.Serve(conn)
}

// triageRequest 是 AI 智能分诊的请求体。
type triageRequest struct {
	Title       string          `json:"title"`
	Description string          `json:"description"`
	Position    *model.Position `json:"position"`
}

// handleTriage 做「智能分诊」：给建议优先级、问题类别、以及重复报修风险。
//
// 这是纯本地算法（关键词 + TF-IDF 相似度 + 3D 距离 + 规则表），
// 不依赖任何外部大模型 API —— 巡检现场常常没有网络。
func (s *Server) handleTriage(w http.ResponseWriter, r *http.Request) {
	var req triageRequest
	if err := decodeJSON(r, &req); err != nil {
		if errors.Is(err, errBodyTooLarge) {
			writeError(w, http.StatusRequestEntityTooLarge, "invalid_argument", "请求体过大，请检查提交的内容", "")
			return
		}
		writeError(w, http.StatusBadRequest, "invalid_json", "请求体不是合法的 JSON", "")
		return
	}

	title := strings.TrimSpace(req.Title)
	if title == "" {
		writeError(w, http.StatusBadRequest, "invalid_argument", "标题(title)不能为空", "title")
		return
	}

	pos := model.Position{}
	if req.Position != nil {
		pos = *req.Position
	}

	history := make([]analysis.Candidate, 0, s.store.Count())
	for _, issue := range s.store.List() {
		history = append(history, analysis.Candidate{
			ID:          issue.ID,
			Title:       issue.Title,
			Description: issue.Description,
			Priority:    issue.Priority,
			Status:      issue.Status,
			Position:    issue.Position,
			CreatedAt:   issue.CreatedAt,
		})
	}

	result := analysis.Triage(analysis.Input{
		Title:       title,
		Description: strings.TrimSpace(req.Description),
		Position:    pos,
	}, history, time.Now())

	writeJSON(w, http.StatusOK, result)
}

// ---------- 辅助函数 ----------

// errBodyTooLarge 表示请求体超过了 maxBodyBytes。
var errBodyTooLarge = errors.New("请求体过大")

func decodeJSON(r *http.Request, dst any) error {
	// 多读 1 个字节用于判断是否超限：直接用 LimitReader 截断会把超大 body
	// 变成一段非法 JSON，报「invalid_json」，掩盖了真正的原因。
	body, err := io.ReadAll(io.LimitReader(r.Body, maxBodyBytes+1))
	if err != nil {
		return err
	}
	if len(body) > maxBodyBytes {
		return errBodyTooLarge
	}
	if len(strings.TrimSpace(string(body))) == 0 {
		return errors.New("请求体为空")
	}
	return json.Unmarshal(body, dst)
}

func isFinite(f float64) bool {
	return !math.IsNaN(f) && !math.IsInf(f, 0)
}

// quoteJSON 避免直接把任意输入回显到响应里时破坏 JSON 结构。
func quoteJSON(s string) string {
	b, err := json.Marshal(s)
	if err != nil {
		return `""`
	}
	return string(b)
}
