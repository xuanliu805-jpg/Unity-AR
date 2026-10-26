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
	"io"
	"log"
	"math"
	"net/http"
	"strings"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/model"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/store"
)

// Server 是 HTTP 处理器。
type Server struct {
	store *store.Store
	mux   *http.ServeMux
}

// New 创建 API 服务。
func New(s *store.Store) *Server {
	srv := &Server{store: s, mux: http.NewServeMux()}
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
		"status":  "ok",
		"service": "unity-ar-inspection",
		"issues":  s.store.Count(),
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
		writeError(w, http.StatusBadRequest, "invalid_json", "请求体不是合法的 JSON: "+err.Error(), "")
		return
	}

	title := strings.TrimSpace(req.Title)
	if title == "" {
		writeError(w, http.StatusBadRequest, "invalid_argument", "标题(title)不能为空", "title")
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
		Description: strings.TrimSpace(req.Description),
		Priority:    priority,
		Position:    pos,
	})
	if err != nil {
		log.Printf("保存问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "internal_error", "保存问题失败: "+err.Error(), "")
		return
	}

	writeJSON(w, http.StatusCreated, map[string]any{"issue": issue})
}

func (s *Server) handleUpdateStatus(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	if strings.TrimSpace(id) == "" {
		writeError(w, http.StatusBadRequest, "invalid_argument", "问题 ID 不能为空", "id")
		return
	}

	var req updateStatusRequest
	if err := decodeJSON(r, &req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid_json", "请求体不是合法的 JSON: "+err.Error(), "")
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
		writeError(w, http.StatusInternalServerError, "internal_error", "更新状态失败: "+err.Error(), "")
		return
	}

	writeJSON(w, http.StatusOK, map[string]any{"issue": issue})
}

func (s *Server) handleDeleteIssue(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	if err := s.store.Delete(id); err != nil {
		if errors.Is(err, store.ErrNotFound) {
			writeError(w, http.StatusNotFound, "not_found", "未找到 ID 为 "+id+" 的问题", "id")
			return
		}
		log.Printf("删除问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "internal_error", "删除问题失败: "+err.Error(), "")
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"deleted": id})
}

func (s *Server) handleNotFound(w http.ResponseWriter, r *http.Request) {
	writeError(w, http.StatusNotFound, "not_found", "未知接口: "+r.URL.Path, "")
}

// ---------- 辅助函数 ----------

func decodeJSON(r *http.Request, dst any) error {
	body, err := io.ReadAll(io.LimitReader(r.Body, 1<<20))
	if err != nil {
		return err
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
