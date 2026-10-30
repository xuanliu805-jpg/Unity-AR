// Package hub 实现 WebSocket 事件广播中心。
//
// 为什么需要它：Unity 客户端在手机上上报问题后，PC 上的管理端如果只能靠手动刷新才能看到，
// 就完全谈不上「实时协同」。这里把问题的新增/修改/删除广播给所有订阅者。
//
// 设计取舍：
//   - 单进程内存广播，不引入 Redis 之类的外部依赖（原型规模下多实例不是问题）；
//   - 每个连接一个写协程，读写分离，避免并发写同一连接；
//   - 写队列带缓冲，某个客户端消费慢不会阻塞其它客户端。
package hub

import (
	"encoding/json"
	"log"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

// 事件类型。
const (
	EventTypeHello        = "hello"
	EventTypeIssueCreated = "issue.created"
	EventTypeIssueUpdated = "issue.updated"
	EventTypeIssueDeleted = "issue.deleted"
	EventTypePing         = "ping"
	EventTypePong         = "pong"
)

// 其它常量。
const (
	writeWait      = 10 * time.Second
	pongWait       = 60 * time.Second
	pingPeriod     = 25 * time.Second
	maxMessageSize = 4096
	sendBufferSize = 32
)

// Event 是推送给客户端的消息。
type Event struct {
	Type string      `json:"type"`
	Data interface{} `json:"data"`
}

// client 是一个已连接的客户端。
type client struct {
	conn *websocket.Conn
	send chan Event
	hub  *Hub
}

// Hub 是广播中心。
type Hub struct {
	mu      sync.RWMutex
	clients map[*client]struct{}
}

// New 创建一个广播中心。
func New() *Hub {
	return &Hub{clients: make(map[*client]struct{})}
}

// Serve 接管一个已经升级完成的连接并纳入广播。
// 由 HTTP handler 调用。
func (h *Hub) Serve(conn *websocket.Conn) {
	c := &client{conn: conn, send: make(chan Event, sendBufferSize), hub: h}

	h.mu.Lock()
	h.clients[c] = struct{}{}
	h.mu.Unlock()

	c.send <- Event{Type: EventTypeHello, Data: map[string]interface{}{
		"serverTime": time.Now().Format(time.RFC3339),
	}}

	go c.writePump()
	go c.readPump()
}

// Publish 向所有客户端广播一个事件。
func (h *Hub) Publish(eventType string, data interface{}) {
	event := Event{Type: eventType, Data: data}

	h.mu.RLock()
	targets := make([]*client, 0, len(h.clients))
	for c := range h.clients {
		targets = append(targets, c)
	}
	h.mu.RUnlock()

	for _, c := range targets {
		select {
		case c.send <- event:
		default:
			// 客户端消费不过来（网络慢/页面卡住），丢弃这条并断开它，
			// 避免一个卡住的客户端把整个广播拖垮。
			log.Printf("[hub] 客户端发送队列已满，断开连接")
			h.remove(c)
		}
	}
}

// ClientCount 返回当前在线客户端数。
func (h *Hub) ClientCount() int {
	h.mu.RLock()
	defer h.mu.RUnlock()
	return len(h.clients)
}

func (h *Hub) remove(c *client) {
	h.mu.Lock()
	if _, ok := h.clients[c]; ok {
		delete(h.clients, c)
		close(c.send)
	}
	h.mu.Unlock()
	_ = c.conn.Close()
}

// readPump 读取客户端消息（目前只需要处理 ping）。
func (c *client) readPump() {
	defer func() {
		c.hub.remove(c)
	}()

	c.conn.SetReadLimit(maxMessageSize)
	_ = c.conn.SetReadDeadline(time.Now().Add(pongWait))
	c.conn.SetPongHandler(func(string) error {
		return c.conn.SetReadDeadline(time.Now().Add(pongWait))
	})

	for {
		_, data, err := c.conn.ReadMessage()
		if err != nil {
			return
		}

		var in struct {
			Type string `json:"type"`
		}
		if err := json.Unmarshal(data, &in); err != nil {
			continue
		}

		switch in.Type {
		case EventTypePing:
			select {
			case c.send <- Event{Type: EventTypePong, Data: map[string]interface{}{}}:
			default:
			}
		}
	}
}

// writePump 负责把事件写回客户端，并定期发心跳。
func (c *client) writePump() {
	ticker := time.NewTicker(pingPeriod)
	defer func() {
		ticker.Stop()
		_ = c.conn.Close()
	}()

	for {
		select {
		case event, ok := <-c.send:
			_ = c.conn.SetWriteDeadline(time.Now().Add(writeWait))
			if !ok {
				_ = c.conn.WriteMessage(websocket.CloseMessage, []byte{})
				return
			}
			payload, err := json.Marshal(event)
			if err != nil {
				continue
			}
			if err := c.conn.WriteMessage(websocket.TextMessage, payload); err != nil {
				return
			}

		case <-ticker.C:
			_ = c.conn.SetWriteDeadline(time.Now().Add(writeWait))
			if err := c.conn.WriteMessage(websocket.PingMessage, nil); err != nil {
				return
			}
		}
	}
}
