// Package model 定义三端共享的数据结构（问题 / 优先级 / 状态 / 位置）。
//
// 字段命名与任务书《4. 基础数据结构》保持一致：
//
//	{ "id", "title", "description", "priority", "status",
//	  "position": { "x", "y", "z" },
//	  "createdAt", "updatedAt" }
package model

import (
	"crypto/rand"
	"encoding/base32"
	"fmt"
	"strings"
	"time"
)

// Priority 表示问题优先级，取值范围固定为 low / medium / high。
type Priority string

const (
	PriorityLow    Priority = "low"
	PriorityMedium Priority = "medium"
	PriorityHigh   Priority = "high"
)

// Status 表示问题处理状态，取值范围固定为 open / in_progress / resolved。
type Status string

const (
	StatusOpen       Status = "open"
	StatusInProgress Status = "in_progress"
	StatusResolved   Status = "resolved"
)

// Position 是 AR 标记在世界空间中的 x/y/z 坐标（米）。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Issue 是一条巡检问题记录。
type Issue struct {
	ID          string    `json:"id"`
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    Priority  `json:"priority"`
	Status      Status    `json:"status"`
	Position    Position  `json:"position"`
	CreatedAt   time.Time `json:"createdAt"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

// AllPriorities 返回所有合法优先级，供接口提示信息使用。
func AllPriorities() []Priority {
	return []Priority{PriorityLow, PriorityMedium, PriorityHigh}
}

// AllStatuses 返回所有合法状态，供接口提示信息使用。
func AllStatuses() []Status {
	return []Status{StatusOpen, StatusInProgress, StatusResolved}
}

// ParsePriority 解析优先级字符串，不合法时返回 false。
// 严格匹配，不做大小写折叠、不做空白裁剪，避免 "HIGH" 这类歧义输入被静默接受。
func ParsePriority(s string) (Priority, bool) {
	switch Priority(s) {
	case PriorityLow, PriorityMedium, PriorityHigh:
		return Priority(s), true
	default:
		return "", false
	}
}

// ParseStatus 解析状态字符串，不合法时返回 false。
func ParseStatus(s string) (Status, bool) {
	switch Status(s) {
	case StatusOpen, StatusInProgress, StatusResolved:
		return Status(s), true
	default:
		return "", false
	}
}

// NewID 生成全局唯一的问题 ID。
// 形如 ISS-<unix毫秒>-<随机串>：前半部分保证大致按时间有序，
// 后半部分由 crypto/rand 提供，避免同一毫秒内并发提交产生重复 ID。
func NewID(now time.Time) string {
	buf := make([]byte, 8)
	if _, err := rand.Read(buf); err != nil {
		// crypto/rand 失败属于极端情况，退化为时间纳秒后缀，
		// 宁可牺牲一点随机性也不能让服务因为一条数据无法入库而挂掉。
		return fmt.Sprintf("ISS-%d-%d", now.UnixMilli(), now.UnixNano())
	}
	token := strings.TrimRight(base32.StdEncoding.EncodeToString(buf), "=")
	return fmt.Sprintf("ISS-%d-%s", now.UnixMilli(), strings.ToUpper(token))
}
