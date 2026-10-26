package store

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/model"
)

func newTempStore(t *testing.T) (*Store, string) {
	t.Helper()
	dir := t.TempDir()
	path := filepath.Join(dir, "issues.json")
	s, err := NewFileStore(path)
	if err != nil {
		t.Fatalf("NewFileStore: %v", err)
	}
	return s, path
}

func TestNewFileStoreMissingFileStartsEmpty(t *testing.T) {
	s, _ := newTempStore(t)
	if got := s.Count(); got != 0 {
		t.Fatalf("Count = %d, want 0", got)
	}
}

func TestCreateAssignsIDStatusAndTimestamps(t *testing.T) {
	s, _ := newTempStore(t)
	in := model.Issue{
		Title:       "入口墙面破损",
		Description: "左侧墙体存在裂缝",
		Priority:    model.PriorityHigh,
		Position:    model.Position{X: 0.42, Y: 0.03, Z: 1.26},
	}
	got, err := s.Create(in)
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if got.ID == "" {
		t.Fatal("expected generated id")
	}
	if got.Status != model.StatusOpen {
		t.Fatalf("Status = %q, want open", got.Status)
	}
	if got.CreatedAt.IsZero() || got.UpdatedAt.IsZero() {
		t.Fatal("expected timestamps to be set")
	}
	if got.Title != in.Title || got.Priority != in.Priority || got.Position != in.Position {
		t.Fatalf("Create did not preserve input: %+v", got)
	}
}

func TestListReturnsCreatedIssuesInOrder(t *testing.T) {
	s, _ := newTempStore(t)
	for _, title := range []string{"A", "B", "C"} {
		if _, err := s.Create(model.Issue{Title: title, Priority: model.PriorityLow}); err != nil {
			t.Fatalf("Create: %v", err)
		}
	}
	list := s.List()
	if len(list) != 3 {
		t.Fatalf("len(List) = %d, want 3", len(list))
	}
	for i, want := range []string{"A", "B", "C"} {
		if list[i].Title != want {
			t.Fatalf("List[%d].Title = %q, want %q", i, list[i].Title, want)
		}
	}
}

func TestUpdateStatusChangesStatusAndUpdatedAt(t *testing.T) {
	s, _ := newTempStore(t)
	created, _ := s.Create(model.Issue{Title: "T", Priority: model.PriorityMedium})

	time.Sleep(2 * time.Millisecond)
	updated, err := s.UpdateStatus(created.ID, model.StatusResolved)
	if err != nil {
		t.Fatalf("UpdateStatus: %v", err)
	}
	if updated.Status != model.StatusResolved {
		t.Fatalf("Status = %q, want resolved", updated.Status)
	}
	if !updated.UpdatedAt.After(created.UpdatedAt) {
		t.Fatalf("UpdatedAt %v not after %v", updated.UpdatedAt, created.UpdatedAt)
	}
	if !updated.CreatedAt.Equal(created.CreatedAt) {
		t.Fatalf("CreatedAt changed: %v vs %v", updated.CreatedAt, created.CreatedAt)
	}
	stored, err := s.Get(created.ID)
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if stored.Status != model.StatusResolved {
		t.Fatalf("persisted status = %q, want resolved", stored.Status)
	}
}

func TestUpdateStatusUnknownID(t *testing.T) {
	s, _ := newTempStore(t)
	_, err := s.UpdateStatus("ISS-not-exist", model.StatusOpen)
	if !errors.Is(err, ErrNotFound) {
		t.Fatalf("err = %v, want ErrNotFound", err)
	}
}

func TestDeleteIssue(t *testing.T) {
	s, _ := newTempStore(t)
	created, _ := s.Create(model.Issue{Title: "T", Priority: model.PriorityLow})
	if err := s.Delete(created.ID); err != nil {
		t.Fatalf("Delete: %v", err)
	}
	if s.Count() != 0 {
		t.Fatalf("Count = %d, want 0", s.Count())
	}
	if err := s.Delete("ISS-missing"); !errors.Is(err, ErrNotFound) {
		t.Fatalf("err = %v, want ErrNotFound", err)
	}
}

func TestDataPersistsAcrossRestart(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "issues.json")

	s1, err := NewFileStore(path)
	if err != nil {
		t.Fatalf("NewFileStore: %v", err)
	}
	created, err := s1.Create(model.Issue{
		Title:    "入口墙面破损",
		Priority: model.PriorityHigh,
		Position: model.Position{X: 0.42, Y: 0.03, Z: 1.26},
	})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}

	// 模拟进程重启：重新从磁盘加载
	s2, err := NewFileStore(path)
	if err != nil {
		t.Fatalf("reload NewFileStore: %v", err)
	}
	if s2.Count() != 1 {
		t.Fatalf("Count after restart = %d, want 1", s2.Count())
	}
	loaded, err := s2.Get(created.ID)
	if err != nil {
		t.Fatalf("Get after restart: %v", err)
	}
	if loaded.Title != "入口墙面破损" || loaded.Priority != model.PriorityHigh {
		t.Fatalf("loaded issue mismatch: %+v", loaded)
	}
	if loaded.Position != created.Position {
		t.Fatalf("position mismatch: %+v vs %+v", loaded.Position, created.Position)
	}
}

func TestPersistedFileIsValidJSONWithExpectedShape(t *testing.T) {
	s, path := newTempStore(t)
	_, _ = s.Create(model.Issue{Title: "标题", Priority: model.PriorityLow})

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("ReadFile: %v", err)
	}
	var payload struct {
		Issues []map[string]any `json:"issues"`
	}
	if err := json.Unmarshal(raw, &payload); err != nil {
		t.Fatalf("persisted file is not valid JSON: %v", err)
	}
	if len(payload.Issues) != 1 {
		t.Fatalf("persisted issues = %d, want 1", len(payload.Issues))
	}
	issue := payload.Issues[0]
	for _, key := range []string{"id", "title", "description", "priority", "status", "position", "createdAt", "updatedAt"} {
		if _, ok := issue[key]; !ok {
			t.Fatalf("persisted issue missing field %q", key)
		}
	}
	pos, ok := issue["position"].(map[string]any)
	if !ok {
		t.Fatalf("position is not an object: %#v", issue["position"])
	}
	for _, key := range []string{"x", "y", "z"} {
		if _, ok := pos[key]; !ok {
			t.Fatalf("position missing field %q", key)
		}
	}
}
