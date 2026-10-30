package analysis

import (
	"testing"
	"time"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/model"
)

func candidate(id, title, desc string, priority model.Priority, status model.Status, x, y, z float64, daysAgo int) Candidate {
	return Candidate{
		ID:          id,
		Title:       title,
		Description: desc,
		Priority:    priority,
		Status:      status,
		Position:    model.Position{X: x, Y: y, Z: z},
		CreatedAt:   time.Now().AddDate(0, 0, -daysAgo),
	}
}

func TestTriage_SuggestsHighPriorityForFire(t *testing.T) {
	now := time.Now()
	res := Triage(Input{
		Title:       "消防栓漏水",
		Description: "3 楼消火栓渗水，接口处一直滴水",
		Position:    model.Position{X: 10, Y: 0, Z: 10},
	}, nil, now)

	if res.SuggestedPriority != model.PriorityHigh {
		t.Fatalf("suggestedPriority = %q, want high", res.SuggestedPriority)
	}
	if res.SuggestedCategory != "消防" {
		t.Fatalf("suggestedCategory = %q, want 消防", res.SuggestedCategory)
	}
	if res.Confidence <= 0 {
		t.Fatalf("confidence = %v, want > 0", res.Confidence)
	}
	if len(res.Reasons) == 0 {
		t.Fatal("expected human readable reasons")
	}
}

func TestTriage_SuggestsLowPriorityForMinor(t *testing.T) {
	res := Triage(Input{
		Title:       "地面有少量杂物",
		Description: "走廊放了个纸箱，不影响通行",
		Position:    model.Position{},
	}, nil, time.Now())

	if res.SuggestedPriority != model.PriorityLow {
		t.Fatalf("suggestedPriority = %q, want low", res.SuggestedPriority)
	}
}

func TestTriage_UnknownTextFallsBackToMedium(t *testing.T) {
	res := Triage(Input{
		Title:       "aaa bbb",
		Description: "ccc ddd",
	}, nil, time.Now())

	if res.SuggestedPriority != model.PriorityMedium {
		t.Fatalf("suggestedPriority = %q, want medium", res.SuggestedPriority)
	}
	if res.SuggestedCategory != "其他" {
		t.Fatalf("suggestedCategory = %q, want 其他", res.SuggestedCategory)
	}
}

func TestTriage_FindsDuplicateIssue(t *testing.T) {
	now := time.Now()
	history := []Candidate{
		candidate("ISS-1", "东侧外墙渗水", "墙皮发黑有水渍", model.PriorityMedium, model.StatusOpen, 12.4, 0, -3.1, 3),
	}

	res := Triage(Input{
		Title:       "东侧外墙渗水",
		Description: "墙皮发黑，有水渍往下淌",
		Position:    model.Position{X: 13.0, Y: 0, Z: -3.5},
	}, history, now)

	if res.DuplicateRisk != DuplicateRiskHigh {
		t.Fatalf("duplicateRisk = %q, want high", res.DuplicateRisk)
	}
	if len(res.SimilarIssues) == 0 {
		t.Fatal("expected similar issues")
	}
	top := res.SimilarIssues[0]
	if top.ID != "ISS-1" {
		t.Fatalf("top similar = %q, want ISS-1", top.ID)
	}
	if top.Similarity < 0.5 {
		t.Fatalf("similarity = %v, want >= 0.5", top.Similarity)
	}
	if top.Distance > 5 {
		t.Fatalf("distance = %v, want <= 5", top.Distance)
	}
	if top.AgeDays < 2.9 || top.AgeDays > 3.1 {
		t.Fatalf("ageDays = %v, want ~3", top.AgeDays)
	}
	if top.Advice == "" {
		t.Fatal("expected advice text")
	}
}

func TestTriage_DifferentPlaceIsNotDuplicate(t *testing.T) {
	history := []Candidate{
		candidate("ISS-1", "东侧外墙渗水", "墙皮发黑有水渍", model.PriorityMedium, model.StatusOpen, 12.4, 0, -3.1, 3),
	}

	res := Triage(Input{
		Title:       "东侧外墙渗水",
		Description: "墙皮发黑有水渍",
		Position:    model.Position{X: 120, Y: 0, Z: 80}, // 100 米开外
	}, history, time.Now())

	if res.DuplicateRisk == DuplicateRiskHigh {
		t.Fatalf("duplicateRisk = %q, want not high for 100m away", res.DuplicateRisk)
	}
}

func TestTriage_ResolvedSimilarIssueStillAdvises(t *testing.T) {
	history := []Candidate{
		candidate("ISS-1", "外墙渗水", "有水渍", model.PriorityMedium, model.StatusResolved, 1, 0, 1, 10),
	}
	res := Triage(Input{
		Title:       "外墙渗水",
		Description: "又有水渍",
		Position:    model.Position{X: 1, Y: 0, Z: 1},
	}, history, time.Now())

	if len(res.SimilarIssues) == 0 {
		t.Fatal("expected similar issues even when resolved")
	}
}

func TestTriage_KeywordsAreExtracted(t *testing.T) {
	res := Triage(Input{
		Title:       "配电箱有火花",
		Description: "配电箱内有火花，疑似漏电",
	}, nil, time.Now())

	if len(res.Keywords) == 0 {
		t.Fatal("expected keywords")
	}
	found := false
	for _, k := range res.Keywords {
		if k == "火花" || k == "漏电" || k == "配电" {
			found = true
		}
	}
	if !found {
		t.Fatalf("keywords = %v, expected to contain 火花/漏电/配电", res.Keywords)
	}
}

func TestTriage_EmptyInputDoesNotPanic(t *testing.T) {
	res := Triage(Input{}, nil, time.Now())
	if res.SuggestedPriority == "" {
		t.Fatal("expected a fallback priority")
	}
	if res.SimilarIssues == nil {
		t.Fatal("similarIssues should be an empty slice, not nil, so JSON renders []")
	}
	if res.Keywords == nil {
		t.Fatal("keywords should be an empty slice, not nil")
	}
}

func TestTriage_PriorityBlendedWithHistory(t *testing.T) {
	now := time.Now()
	history := []Candidate{
		candidate("ISS-1", "渗水", "墙上有水", model.PriorityHigh, model.StatusOpen, 1, 0, 1, 1),
		candidate("ISS-2", "渗水", "墙上有水", model.PriorityHigh, model.StatusOpen, 1, 0, 1, 1),
		candidate("ISS-3", "渗水", "墙上有水", model.PriorityMedium, model.StatusOpen, 1, 0, 1, 1),
	}
	res := Triage(Input{
		Title:       "墙上有水",
		Description: "渗水",
		Position:    model.Position{X: 1, Y: 0, Z: 1},
	}, history, now)

	if res.SuggestedPriority != model.PriorityHigh {
		t.Fatalf("suggestedPriority = %q, want high (history mostly high)", res.SuggestedPriority)
	}
}

func TestTriage_SimilarIssuesSortedBySimilarityDesc(t *testing.T) {
	history := []Candidate{
		candidate("ISS-weak", "东墙渗水", "墙面有水", model.PriorityMedium, model.StatusOpen, 1, 0, 1, 1),
		candidate("ISS-strong", "外墙渗水", "有水渍", model.PriorityMedium, model.StatusOpen, 1, 0, 1, 1),
	}
	res := Triage(Input{
		Title:       "外墙渗水",
		Description: "有水渍",
		Position:    model.Position{X: 1, Y: 0, Z: 1},
	}, history, time.Now())

	if len(res.SimilarIssues) < 2 {
		t.Fatalf("expected 2 similar issues, got %d", len(res.SimilarIssues))
	}
	if res.SimilarIssues[0].ID != "ISS-strong" {
		t.Fatalf("top = %q, want ISS-strong", res.SimilarIssues[0].ID)
	}
	if res.SimilarIssues[0].Similarity < res.SimilarIssues[1].Similarity {
		t.Fatalf("not sorted desc: %v < %v", res.SimilarIssues[0].Similarity, res.SimilarIssues[1].Similarity)
	}
}

func TestTriage_UnrelatedIssueIsFilteredOut(t *testing.T) {
	history := []Candidate{
		candidate("ISS-other", "空调不制冷", "机房温度高", model.PriorityLow, model.StatusOpen, 5, 0, 5, 1),
	}
	res := Triage(Input{
		Title:       "外墙渗水",
		Description: "有水渍",
		Position:    model.Position{X: 1, Y: 0, Z: 1},
	}, history, time.Now())

	if len(res.SimilarIssues) != 0 {
		t.Fatalf("expected unrelated issue to be filtered out, got %+v", res.SimilarIssues)
	}
	if res.DuplicateRisk != DuplicateRiskNone {
		t.Fatalf("duplicateRisk = %q, want none", res.DuplicateRisk)
	}
}
