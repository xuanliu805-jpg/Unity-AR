package api

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"net/url"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/hub"
	"github.com/xuanliu805-jpg/Unity-AR/server/internal/store"
)

func newTestServerWithHub(t *testing.T) (*Server, *hub.Hub) {
	t.Helper()
	path := filepath.Join(t.TempDir(), "issues.json")
	st, err := store.NewFileStore(path)
	if err != nil {
		t.Fatalf("NewFileStore: %v", err)
	}
	h := hub.New()
	return New(st, h), h
}

// dialWS 连上测试服务器的 /ws。
func dialWS(t *testing.T, srv *httptest.Server) *websocket.Conn {
	t.Helper()
	u := "ws" + strings.TrimPrefix(srv.URL, "http") + "/ws"
	conn, _, err := websocket.DefaultDialer.Dial(u, nil)
	if err != nil {
		t.Fatalf("dial %s: %v", u, err)
	}
	return conn
}

func readWS(t *testing.T, conn *websocket.Conn) hub.Event {
	t.Helper()
	_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, data, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	var e hub.Event
	if err := json.Unmarshal(data, &e); err != nil {
		t.Fatalf("unmarshal %q: %v", data, err)
	}
	return e
}

func TestWebSocketEndpointExists(t *testing.T) {
	api, _ := newTestServerWithHub(t)
	srv := httptest.NewServer(api)
	defer srv.Close()

	conn := dialWS(t, srv)
	defer conn.Close()

	e := readWS(t, conn)
	if e.Type != hub.EventTypeHello {
		t.Fatalf("first event = %q, want hello", e.Type)
	}
}

func TestCreateIssuePublishesIssueCreated(t *testing.T) {
	api, _ := newTestServerWithHub(t)
	srv := httptest.NewServer(api)
	defer srv.Close()

	conn := dialWS(t, srv)
	defer conn.Close()
	_ = readWS(t, conn) // hello

	w := do(t, api, http.MethodPost, "/api/issues",
		`{"title":"入口墙面破损","priority":"high","position":{"x":1,"y":0,"z":2}}`)
	if w.Code != http.StatusCreated {
		t.Fatalf("create status = %d", w.Code)
	}

	e := readWS(t, conn)
	if e.Type != hub.EventTypeIssueCreated {
		t.Fatalf("event = %q, want issue.created", e.Type)
	}
	payload, _ := e.Data.(map[string]interface{})
	if payload["id"] == nil || payload["id"] == "" {
		t.Fatalf("event data missing id: %+v", e.Data)
	}
}

func TestUpdateStatusPublishesIssueUpdated(t *testing.T) {
	api, _ := newTestServerWithHub(t)
	srv := httptest.NewServer(api)
	defer srv.Close()

	conn := dialWS(t, srv)
	defer conn.Close()
	_ = readWS(t, conn)

	w := do(t, api, http.MethodPost, "/api/issues", `{"title":"t","priority":"low"}`)
	created := decode(t, w)["issue"].(map[string]interface{})
	id := created["id"].(string)

	// 创建本身也会广播 issue.created，先消费掉
	_ = readWS(t, conn)

	w = do(t, api, http.MethodPatch, "/api/issues/"+url.PathEscape(id)+"/status", `{"status":"resolved"}`)
	if w.Code != http.StatusOK {
		t.Fatalf("patch status = %d", w.Code)
	}

	e := readWS(t, conn)
	if e.Type != hub.EventTypeIssueUpdated {
		t.Fatalf("event = %q, want issue.updated", e.Type)
	}
}

func TestDeletePublishesIssueDeleted(t *testing.T) {
	api, _ := newTestServerWithHub(t)
	srv := httptest.NewServer(api)
	defer srv.Close()

	conn := dialWS(t, srv)
	defer conn.Close()
	_ = readWS(t, conn)

	w := do(t, api, http.MethodPost, "/api/issues", `{"title":"t","priority":"low"}`)
	created := decode(t, w)["issue"].(map[string]interface{})
	id := created["id"].(string)

	// 创建本身也会广播 issue.created，先消费掉
	_ = readWS(t, conn)

	w = do(t, api, http.MethodDelete, "/api/issues/"+url.PathEscape(id), "")
	if w.Code != http.StatusOK {
		t.Fatalf("delete status = %d", w.Code)
	}

	e := readWS(t, conn)
	if e.Type != hub.EventTypeIssueDeleted {
		t.Fatalf("event = %q, want issue.deleted", e.Type)
	}
}

func TestTriageEndpointReturnsSuggestion(t *testing.T) {
	api, _ := newTestServerWithHub(t)

	// 先造一条历史问题用于重复报修判断
	w := do(t, api, http.MethodPost, "/api/issues",
		`{"title":"东侧外墙渗水","description":"墙皮发黑有水渍","priority":"medium","position":{"x":12.4,"y":0,"z":-3.1}}`)
	if w.Code != http.StatusCreated {
		t.Fatalf("seed status = %d", w.Code)
	}

	w = do(t, api, http.MethodPost, "/api/analysis/triage",
		`{"title":"东侧外墙渗水","description":"墙皮发黑有水渍","position":{"x":13.0,"y":0,"z":-3.5}}`)
	if w.Code != http.StatusOK {
		t.Fatalf("triage status = %d (%s)", w.Code, w.Body.String())
	}

	body := decode(t, w)
	if body["suggestedPriority"] == "" {
		t.Fatalf("missing suggestedPriority: %+v", body)
	}
	if body["duplicateRisk"] != "high" {
		t.Fatalf("duplicateRisk = %v, want high", body["duplicateRisk"])
	}
	similar, ok := body["similarIssues"].([]any)
	if !ok || len(similar) == 0 {
		t.Fatalf("expected similarIssues, got %+v", body["similarIssues"])
	}
	reasons, ok := body["reasons"].([]any)
	if !ok || len(reasons) == 0 {
		t.Fatalf("expected reasons, got %+v", body["reasons"])
	}
}

func TestTriageEndpointValidatesTitle(t *testing.T) {
	api, _ := newTestServerWithHub(t)

	w := do(t, api, http.MethodPost, "/api/analysis/triage", `{"title":"","description":"x"}`)
	if w.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400", w.Code)
	}
	body := decode(t, w)
	errObj := body["error"].(map[string]interface{})
	if errObj["field"] != "title" {
		t.Fatalf("field = %v, want title", errObj["field"])
	}
}

func TestTriageEndpointRejectsBadJSON(t *testing.T) {
	api, _ := newTestServerWithHub(t)

	w := do(t, api, http.MethodPost, "/api/analysis/triage", `{"title":`)
	if w.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400", w.Code)
	}
}

func TestTriageEndpointSuggestsPriorityForFire(t *testing.T) {
	api, _ := newTestServerWithHub(t)

	payload, _ := json.Marshal(map[string]interface{}{
		"title":       "消防栓漏水",
		"description": "3 楼消火栓接口一直滴水",
	})
	w := do(t, api, http.MethodPost, "/api/analysis/triage", string(payload))
	if w.Code != http.StatusOK {
		t.Fatalf("status = %d (%s)", w.Code, w.Body.String())
	}
	body := decode(t, w)
	if body["suggestedPriority"] != "high" {
		t.Fatalf("suggestedPriority = %v, want high", body["suggestedPriority"])
	}
	if body["suggestedCategory"] != "消防" {
		t.Fatalf("suggestedCategory = %v, want 消防", body["suggestedCategory"])
	}
}

func TestHealthEndpointIncludesWSHint(t *testing.T) {
	api, _ := newTestServerWithHub(t)
	w := do(t, api, http.MethodGet, "/api/health", "")
	if w.Code != http.StatusOK {
		t.Fatalf("status = %d", w.Code)
	}
	body := decode(t, w)
	if body["websocket"] != "/ws" {
		t.Fatalf("health should advertise the websocket path, got %+v", body)
	}
}

func TestCORSAllowsUpgradeHeaders(t *testing.T) {
	api, _ := newTestServerWithHub(t)
	r := httptest.NewRequest(http.MethodOptions, "/api/issues", nil)
	r.Header.Set("Origin", "http://localhost:5173")
	r.Header.Set("Access-Control-Request-Method", "GET")
	w := httptest.NewRecorder()
	api.ServeHTTP(w, r)
	if w.Code == http.StatusForbidden {
		t.Fatalf("preflight rejected: %d", w.Code)
	}
}
