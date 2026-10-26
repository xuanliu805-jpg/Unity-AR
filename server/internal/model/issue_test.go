package model

import (
	"strings"
	"testing"
	"time"
)

func TestParsePriority(t *testing.T) {
	cases := []struct {
		in    string
		want  Priority
		valid bool
	}{
		{"low", PriorityLow, true},
		{"medium", PriorityMedium, true},
		{"high", PriorityHigh, true},
		{"LOW", "", false},
		{"urgent", "", false},
		{"", "", false},
		{" low ", "", false},
	}
	for _, c := range cases {
		got, ok := ParsePriority(c.in)
		if ok != c.valid {
			t.Fatalf("ParsePriority(%q) valid = %v, want %v", c.in, ok, c.valid)
		}
		if ok && got != c.want {
			t.Fatalf("ParsePriority(%q) = %q, want %q", c.in, got, c.want)
		}
	}
}

func TestParseStatus(t *testing.T) {
	cases := []struct {
		in    string
		want  Status
		valid bool
	}{
		{"open", StatusOpen, true},
		{"in_progress", StatusInProgress, true},
		{"resolved", StatusResolved, true},
		{"closed", "", false},
		{"", "", false},
	}
	for _, c := range cases {
		got, ok := ParseStatus(c.in)
		if ok != c.valid {
			t.Fatalf("ParseStatus(%q) valid = %v, want %v", c.in, ok, c.valid)
		}
		if ok && got != c.want {
			t.Fatalf("ParseStatus(%q) = %q, want %q", c.in, got, c.want)
		}
	}
}

func TestNewIDIsUniqueAndPrefixed(t *testing.T) {
	now := time.Now()
	seen := map[string]bool{}
	for i := 0; i < 2000; i++ {
		id := NewID(now)
		if !strings.HasPrefix(id, "ISS-") {
			t.Fatalf("NewID = %q, want prefix ISS-", id)
		}
		if seen[id] {
			t.Fatalf("NewID produced duplicate id %q", id)
		}
		seen[id] = true
	}
}

func TestNewIDDifferentAcrossMilliseconds(t *testing.T) {
	base := time.Unix(1700000000, 0)
	a := NewID(base)
	b := NewID(base.Add(time.Second))
	if a == b {
		t.Fatalf("expected different ids for different timestamps, got %q", a)
	}
	if !strings.Contains(a, "1700000000000") {
		t.Fatalf("expected id %q to embed timestamp", a)
	}
}
