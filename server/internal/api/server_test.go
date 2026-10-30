package api

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/hub"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/store"
)

func newTestServer(t *testing.T) *Server {
	t.Helper()
	path := filepath.Join(t.TempDir(), "issues.json")
	s, err := store.NewFileStore(path)
	if err != nil {
		t.Fatalf("NewFileStore: %v", err)
	}
	return New(s, hub.New())
}

func do(t *testing.T, srv *Server, method, path string, body string) *httptest.ResponseRecorder {
	t.Helper()
	var r *http.Request
	if body == "" {
		r = httptest.NewRequest(method, path, nil)
	} else {
		r = httptest.NewRequest(method, path, bytes.NewBufferString(body))
		r.Header.Set("Content-Type", "application/json")
	}
	w := httptest.NewRecorder()
	srv.ServeHTTP(w, r)
	return w
}

func decode(t *testing.T, w *httptest.ResponseRecorder) map[string]any {
	t.Helper()
	var m map[string]any
	if err := json.Unmarshal(w.Body.Bytes(), &m); err != nil {
		t.Fatalf("decode response %q: %v", w.Body.String(), err)
	}
	return m
}

func TestHealthEndpoint(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodGet, "/api/health", "")
	if w.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", w.Code)
	}
	body := decode(t, w)
	if body["status"] != "ok" {
		t.Fatalf("status field = %v, want ok", body["status"])
	}
}

func TestCreateIssueSuccess(t *testing.T) {
	srv := newTestServer(t)
	payload := `{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}`
	w := do(t, srv, http.MethodPost, "/api/issues", payload)
	if w.Code != http.StatusCreated {
		t.Fatalf("status = %d (%s), want 201", w.Code, w.Body.String())
	}
	body := decode(t, w)
	issue, ok := body["issue"].(map[string]any)
	if !ok {
		t.Fatalf("response has no issue object: %s", w.Body.String())
	}
	if issue["id"] == "" || issue["id"] == nil {
		t.Fatal("expected generated id")
	}
	if issue["status"] != "open" {
		t.Fatalf("status = %v, want open", issue["status"])
	}
	if issue["title"] != "入口墙面破损" {
		t.Fatalf("title = %v", issue["title"])
	}
	if issue["priority"] != "high" {
		t.Fatalf("priority = %v", issue["priority"])
	}
	pos, ok := issue["position"].(map[string]any)
	if !ok {
		t.Fatalf("position missing: %v", issue["position"])
	}
	if pos["x"] != 0.42 || pos["y"] != 0.03 || pos["z"] != 1.26 {
		t.Fatalf("position = %v", pos)
	}
	if issue["createdAt"] == nil || issue["updatedAt"] == nil {
		t.Fatal("expected createdAt/updatedAt")
	}
}

func TestCreateIssueEmptyTitleReturns400(t *testing.T) {
	srv := newTestServer(t)
	for _, payload := range []string{
		`{"title":"","description":"d","priority":"low"}`,
		`{"title":"   ","description":"d","priority":"low"}`,
	} {
		w := do(t, srv, http.MethodPost, "/api/issues", payload)
		if w.Code != http.StatusBadRequest {
			t.Fatalf("status = %d (%s), want 400", w.Code, w.Body.String())
		}
		body := decode(t, w)
		errObj, ok := body["error"].(map[string]any)
		if !ok {
			t.Fatalf("expected error object: %s", w.Body.String())
		}
		if errObj["code"] != "invalid_argument" {
			t.Fatalf("error.code = %v, want invalid_argument", errObj["code"])
		}
		if msg, _ := errObj["message"].(string); msg == "" {
			t.Fatal("expected non-empty error message")
		}
		if errObj["field"] != "title" {
			t.Fatalf("error.field = %v, want title", errObj["field"])
		}
	}
}

func TestCreateIssueInvalidPriorityReturns400(t *testing.T) {
	srv := newTestServer(t)
	for _, priority := range []string{"urgent", "LOW", "1", "null"} {
		w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"t","priority":"`+priority+`"}`)
		if w.Code != http.StatusBadRequest {
			t.Fatalf("priority %q: status = %d (%s), want 400", priority, w.Code, w.Body.String())
		}
		body := decode(t, w)
		errObj := body["error"].(map[string]any)
		if errObj["field"] != "priority" {
			t.Fatalf("error.field = %v, want priority", errObj["field"])
		}
	}
}

func TestCreateIssueMissingPriorityReturns400(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"t"}`)
	if w.Code != http.StatusBadRequest {
		t.Fatalf("status = %d (%s), want 400", w.Code, w.Body.String())
	}
}

func TestCreateIssueMalformedJSONReturns400(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":`)
	if w.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400", w.Code)
	}
}

func TestListIssues(t *testing.T) {
	srv := newTestServer(t)
	do(t, srv, http.MethodPost, "/api/issues", `{"title":"A","priority":"low"}`)
	do(t, srv, http.MethodPost, "/api/issues", `{"title":"B","priority":"high"}`)

	w := do(t, srv, http.MethodGet, "/api/issues", "")
	if w.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", w.Code)
	}
	body := decode(t, w)
	issues, ok := body["issues"].([]any)
	if !ok {
		t.Fatalf("expected issues array: %s", w.Body.String())
	}
	if len(issues) != 2 {
		t.Fatalf("len = %d, want 2", len(issues))
	}
	if body["count"] != float64(2) {
		t.Fatalf("count = %v, want 2", body["count"])
	}
}

func TestUpdateStatusSuccess(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"A","priority":"low"}`)
	created := decode(t, w)["issue"].(map[string]any)
	id := created["id"].(string)

	w = do(t, srv, http.MethodPatch, "/api/issues/"+id+"/status", `{"status":"in_progress"}`)
	if w.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", w.Code, w.Body.String())
	}
	body := decode(t, w)
	issue := body["issue"].(map[string]any)
	if issue["status"] != "in_progress" {
		t.Fatalf("status = %v, want in_progress", issue["status"])
	}
	if issue["id"] != id {
		t.Fatalf("id changed: %v vs %v", issue["id"], id)
	}

	// 再查列表确认状态已经持久化
	w = do(t, srv, http.MethodGet, "/api/issues", "")
	issues := decode(t, w)["issues"].([]any)
	first := issues[0].(map[string]any)
	if first["status"] != "in_progress" {
		t.Fatalf("persisted status = %v", first["status"])
	}
}

func TestUpdateStatusInvalidValueReturns400(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"A","priority":"low"}`)
	id := decode(t, w)["issue"].(map[string]any)["id"].(string)

	for _, st := range []string{"closed", "OPEN", ""} {
		w = do(t, srv, http.MethodPatch, "/api/issues/"+id+"/status", `{"status":"`+st+`"}`)
		if w.Code != http.StatusBadRequest {
			t.Fatalf("status %q: code = %d, want 400", st, w.Code)
		}
	}
}

func TestUpdateStatusUnknownIDReturns404(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodPatch, "/api/issues/ISS-does-not-exist/status", `{"status":"open"}`)
	if w.Code != http.StatusNotFound {
		t.Fatalf("status = %d (%s), want 404", w.Code, w.Body.String())
	}
}

func TestDeleteIssueEndpoint(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"A","priority":"low"}`)
	id := decode(t, w)["issue"].(map[string]any)["id"].(string)

	w = do(t, srv, http.MethodDelete, "/api/issues/"+id, "")
	if w.Code != http.StatusOK && w.Code != http.StatusNoContent {
		t.Fatalf("status = %d, want 200/204", w.Code)
	}
	w = do(t, srv, http.MethodGet, "/api/issues", "")
	if len(decode(t, w)["issues"].([]any)) != 0 {
		t.Fatal("expected issue to be deleted")
	}
}

func TestCORSPreflightAndHeaders(t *testing.T) {
	srv := newTestServer(t)
	r := httptest.NewRequest(http.MethodOptions, "/api/issues", nil)
	r.Header.Set("Origin", "http://localhost:5173")
	r.Header.Set("Access-Control-Request-Method", "POST")
	w := httptest.NewRecorder()
	srv.ServeHTTP(w, r)
	if w.Code != http.StatusNoContent && w.Code != http.StatusOK {
		t.Fatalf("preflight status = %d", w.Code)
	}
	if got := w.Header().Get("Access-Control-Allow-Origin"); got == "" {
		t.Fatal("missing Access-Control-Allow-Origin header")
	}
	if !strings.Contains(strings.ToLower(w.Header().Get("Access-Control-Allow-Methods")), "patch") {
		t.Fatalf("Access-Control-Allow-Methods = %q", w.Header().Get("Access-Control-Allow-Methods"))
	}
}

func TestUnknownRouteReturns404(t *testing.T) {
	srv := newTestServer(t)
	w := do(t, srv, http.MethodGet, "/api/nope", "")
	if w.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want 404", w.Code)
	}
}

func TestCreateIssueTooLongTitleReturns400(t *testing.T) {
	srv := newTestServer(t)
	long := strings.Repeat("测", 201)

	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"`+long+`","priority":"low"}`)

	if w.Code != http.StatusBadRequest {
		t.Fatalf("status = %d (%s), want 400", w.Code, w.Body.String())
	}
	body := decode(t, w)
	errObj := body["error"].(map[string]any)
	if errObj["field"] != "title" {
		t.Fatalf("error.field = %v, want title", errObj["field"])
	}
}

func TestCreateIssueTooLongDescriptionReturns400(t *testing.T) {
	srv := newTestServer(t)
	long := strings.Repeat("描", 2001)

	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"t","description":"`+long+`","priority":"low"}`)

	if w.Code != http.StatusBadRequest {
		t.Fatalf("status = %d (%s), want 400", w.Code, w.Body.String())
	}
	body := decode(t, w)
	errObj := body["error"].(map[string]any)
	if errObj["field"] != "description" {
		t.Fatalf("error.field = %v, want description", errObj["field"])
	}
}

func TestUnknownMethodOnKnownPathReturns405(t *testing.T) {
	srv := newTestServer(t)

	for _, method := range []string{http.MethodPut, http.MethodDelete} {
		w := do(t, srv, method, "/api/issues", "")
		if w.Code != http.StatusMethodNotAllowed {
			t.Fatalf("%s /api/issues: status = %d, want 405", method, w.Code)
		}
		body := decode(t, w)
		errObj, ok := body["error"].(map[string]any)
		if !ok {
			t.Fatalf("%s: expected error object: %s", method, w.Body.String())
		}
		if errObj["code"] != "method_not_allowed" {
			t.Fatalf("%s: error.code = %v, want method_not_allowed", method, errObj["code"])
		}
	}
}

// TestInternalErrorDoesNotLeakServerPaths 验证服务端内部错误（这里用「数据文件被目录占位」模拟写盘失败）
// 不会把部署路径、盘符、操作系统错误原文回给客户端。细节只应出现在服务端日志里。
func TestInternalErrorDoesNotLeakServerPaths(t *testing.T) {
	dir := t.TempDir()
	dataPath := filepath.Join(dir, "sub", "issues.json")
	if err := os.MkdirAll(filepath.Dir(dataPath), 0o755); err != nil {
		t.Fatalf("MkdirAll: %v", err)
	}
	if err := os.WriteFile(dataPath, []byte(`{"issues":[]}`), 0o644); err != nil {
		t.Fatalf("WriteFile: %v", err)
	}

	st, err := store.NewFileStore(dataPath)
	if err != nil {
		t.Fatalf("NewFileStore: %v", err)
	}

	// 把数据文件换成一个同名的目录：之后的 Rename 覆盖会失败，从而触发 500 分支。
	if err := os.Remove(dataPath); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	if err := os.Mkdir(dataPath, 0o755); err != nil {
		t.Fatalf("Mkdir: %v", err)
	}

	srv := New(st, hub.New())
	w := do(t, srv, http.MethodPost, "/api/issues", `{"title":"t","priority":"low"}`)
	if w.Code != http.StatusInternalServerError {
		t.Fatalf("status = %d (%s), want 500", w.Code, w.Body.String())
	}

	body := w.Body.String()
	for _, leak := range []string{dir, "issues.json", "rename", "Access is denied", "os."} {
		if strings.Contains(body, leak) {
			t.Fatalf("500 响应泄露了内部信息 %q: %s", leak, body)
		}
	}

	errObj := decode(t, w)["error"].(map[string]any)
	if errObj["code"] != "internal_error" {
		t.Fatalf("error.code = %v, want internal_error", errObj["code"])
	}
	if msg, _ := errObj["message"].(string); msg == "" {
		t.Fatal("expected non-empty error message")
	}
}
