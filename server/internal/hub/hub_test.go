package hub

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

// testHub 启一个真实的 WebSocket 服务端，验证事件广播真的能到达客户端。
func testHub(t *testing.T) (*Hub, string, func()) {
	t.Helper()

	h := New()

	upgrader := websocket.Upgrader{}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		conn, err := upgrader.Upgrade(w, r, nil)
		if err != nil {
			t.Errorf("upgrade: %v", err)
			return
		}
		h.Serve(conn)
	}))

	return h, "ws" + strings.TrimPrefix(server.URL, "http"), server.Close
}

func dial(t *testing.T, url string) *websocket.Conn {
	t.Helper()
	conn, resp, err := websocket.DefaultDialer.Dial(url, nil)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	_ = resp
	return conn
}

func readEvent(t *testing.T, conn *websocket.Conn) Event {
	t.Helper()
	_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, data, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	var e Event
	if err := json.Unmarshal(data, &e); err != nil {
		t.Fatalf("unmarshal %q: %v", data, err)
	}
	return e
}

func TestHub_BroadcastToAllClients(t *testing.T) {
	h, url, cleanup := testHub(t)
	defer cleanup()

	c1 := dial(t, url)
	defer c1.Close()
	c2 := dial(t, url)
	defer c2.Close()

	// 等两个客户端都注册上
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) && h.ClientCount() < 2 {
		time.Sleep(10 * time.Millisecond)
	}
	if h.ClientCount() != 2 {
		t.Fatalf("client count = %d, want 2", h.ClientCount())
	}

	h.Publish(EventTypeIssueCreated, map[string]string{"id": "ISS-1"})

	for i, conn := range []*websocket.Conn{c1, c2} {
		e := readEvent(t, conn)
		if e.Type != EventTypeIssueCreated {
			t.Fatalf("client %d got type %q, want %q", i, e.Type, EventTypeIssueCreated)
		}
	}
}

func TestHub_HelloSentOnConnect(t *testing.T) {
	_, url, cleanup := testHub(t)
	defer cleanup()

	c := dial(t, url)
	defer c.Close()

	e := readEvent(t, c)
	if e.Type != EventTypeHello {
		t.Fatalf("first event type = %q, want %q", e.Type, EventTypeHello)
	}
}

func TestHub_DisconnectedClientRemoved(t *testing.T) {
	h, url, cleanup := testHub(t)
	defer cleanup()

	c1 := dial(t, url)
	c2 := dial(t, url)

	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) && h.ClientCount() < 2 {
		time.Sleep(10 * time.Millisecond)
	}

	c1.Close()

	deadline = time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) && h.ClientCount() != 1 {
		time.Sleep(20 * time.Millisecond)
	}
	if h.ClientCount() != 1 {
		t.Fatalf("client count after disconnect = %d, want 1", h.ClientCount())
	}

	// 剩下那个客户端仍应收到广播
	// （连接建立时服务端先发一条 hello，先消费掉）
	_ = readEvent(t, c2)
	h.Publish(EventTypeIssueUpdated, map[string]string{"id": "ISS-1"})
	e := readEvent(t, c2)
	if e.Type != EventTypeIssueUpdated {
		t.Fatalf("got %q, want %q", e.Type, EventTypeIssueUpdated)
	}
}

func TestHub_ClientCanPing(t *testing.T) {
	_, url, cleanup := testHub(t)
	defer cleanup()

	c := dial(t, url)
	defer c.Close()

	// 第一条是 hello
	_ = readEvent(t, c)

	if err := c.WriteMessage(websocket.TextMessage, []byte(`{"type":"ping"}`)); err != nil {
		t.Fatalf("write: %v", err)
	}

	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		e := readEvent(t, c)
		if e.Type == EventTypePong {
			return
		}
	}
	t.Fatal("expected a pong event")
}

func TestHub_ConcurrentPublish(t *testing.T) {
	h, url, cleanup := testHub(t)
	defer cleanup()

	c := dial(t, url)
	defer c.Close()
	_ = readEvent(t, c)

	var wg sync.WaitGroup
	for i := 0; i < 20; i++ {
		wg.Add(1)
		go func(n int) {
			defer wg.Done()
			h.Publish(EventTypeIssueCreated, map[string]int{"n": n})
		}(i)
	}
	wg.Wait()

	received := 0
	_ = c.SetReadDeadline(time.Now().Add(3 * time.Second))
	for received < 20 {
		_, _, err := c.ReadMessage()
		if err != nil {
			break
		}
		received++
	}
	if received != 20 {
		t.Fatalf("received %d events, want 20", received)
	}
}
