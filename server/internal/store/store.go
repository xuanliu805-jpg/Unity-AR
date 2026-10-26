// Package store 提供问题的持久化。
//
// 当前实现使用本地 JSON 文件（任务书允许「SQLite 或本地 JSON 文件」），
// 写入采用「临时文件 + 原子 rename」，避免进程在写盘中途崩溃导致文件半截损坏。
package store

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"time"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/model"
)

// ErrNotFound 表示目标问题不存在。
var ErrNotFound = errors.New("issue not found")

// Store 是并发安全的问题仓库。
type Store struct {
	mu     sync.RWMutex
	path   string
	issues []model.Issue
	now    func() time.Time
}

// filePayload 是落盘文件的结构。
type filePayload struct {
	Issues []model.Issue `json:"issues"`
}

// NewFileStore 打开（或创建）指定路径的 JSON 数据文件。
// 文件不存在时视为空数据，不会报错，保证首次启动即可用。
func NewFileStore(path string) (*Store, error) {
	s := &Store{path: path, now: time.Now}
	if err := s.load(); err != nil {
		return nil, err
	}
	return s, nil
}

func (s *Store) load() error {
	raw, err := os.ReadFile(s.path)
	if err != nil {
		if os.IsNotExist(err) {
			s.issues = nil
			return nil
		}
		return fmt.Errorf("读取数据文件失败: %w", err)
	}
	if len(raw) == 0 {
		s.issues = nil
		return nil
	}
	var payload filePayload
	if err := json.Unmarshal(raw, &payload); err != nil {
		return fmt.Errorf("数据文件不是合法 JSON: %w", err)
	}
	s.issues = payload.Issues
	return nil
}

// save 把当前内存数据原子地写回磁盘。调用方必须持有写锁。
func (s *Store) save() error {
	payload := filePayload{Issues: s.issues}
	raw, err := json.MarshalIndent(payload, "", "  ")
	if err != nil {
		return fmt.Errorf("序列化失败: %w", err)
	}
	dir := filepath.Dir(s.path)
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return fmt.Errorf("创建数据目录失败: %w", err)
	}
	tmp, err := os.CreateTemp(dir, "issues-*.tmp")
	if err != nil {
		return fmt.Errorf("创建临时文件失败: %w", err)
	}
	tmpName := tmp.Name()
	defer func() {
		tmp.Close()
		os.Remove(tmpName) // rename 成功后这里是 no-op
	}()
	if _, err := tmp.Write(raw); err != nil {
		return fmt.Errorf("写入临时文件失败: %w", err)
	}
	if err := tmp.Sync(); err != nil {
		return fmt.Errorf("刷盘失败: %w", err)
	}
	if err := tmp.Close(); err != nil {
		return fmt.Errorf("关闭临时文件失败: %w", err)
	}
	if err := os.Rename(tmpName, s.path); err != nil {
		return fmt.Errorf("替换数据文件失败: %w", err)
	}
	return nil
}

// List 返回全部问题（按创建顺序）。
func (s *Store) List() []model.Issue {
	s.mu.RLock()
	defer s.mu.RUnlock()
	out := make([]model.Issue, len(s.issues))
	copy(out, s.issues)
	return out
}

// Count 返回问题总数。
func (s *Store) Count() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.issues)
}

// Get 按 ID 查询问题。
func (s *Store) Get(id string) (model.Issue, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	for _, it := range s.issues {
		if it.ID == id {
			return it, nil
		}
	}
	return model.Issue{}, ErrNotFound
}

// Create 保存一条新问题：生成唯一 ID、默认状态 open、写入创建/更新时间。
func (s *Store) Create(in model.Issue) (model.Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	now := s.now()
	in.ID = model.NewID(now)
	in.Status = model.StatusOpen
	in.CreatedAt = now
	in.UpdatedAt = now

	s.issues = append(s.issues, in)
	if err := s.save(); err != nil {
		// 写盘失败时回滚内存状态，避免出现「接口返回成功但数据没落盘」。
		s.issues = s.issues[:len(s.issues)-1]
		return model.Issue{}, err
	}
	return in, nil
}

// UpdateStatus 根据 ID 修改处理状态。
func (s *Store) UpdateStatus(id string, status model.Status) (model.Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	for i := range s.issues {
		if s.issues[i].ID != id {
			continue
		}
		backup := s.issues[i]
		s.issues[i].Status = status
		s.issues[i].UpdatedAt = s.now()
		if err := s.save(); err != nil {
			s.issues[i] = backup
			return model.Issue{}, err
		}
		return s.issues[i], nil
	}
	return model.Issue{}, ErrNotFound
}

// Delete 删除一条问题。
func (s *Store) Delete(id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	for i := range s.issues {
		if s.issues[i].ID != id {
			continue
		}
		backup := make([]model.Issue, len(s.issues))
		copy(backup, s.issues)
		s.issues = append(s.issues[:i:i], s.issues[i+1:]...)
		if err := s.save(); err != nil {
			s.issues = backup
			return err
		}
		return nil
	}
	return ErrNotFound
}
