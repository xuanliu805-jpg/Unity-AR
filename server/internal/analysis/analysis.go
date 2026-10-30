// Package analysis 实现「智能分诊」：根据巡检员输入的问题文本与坐标，
// 给出建议优先级、问题类别、以及**是否存在重复报修**的判断。
//
// 为什么是纯本地算法而不是接大模型：
//  1. 巡检现场常常没有网络，模型不可用就等于功能不可用；
//  2. 重复报修判断需要和历史问题做精确比对，这是检索问题而不是生成问题；
//  3. 纯算法可单元测试、响应在毫秒级、结论可解释（能给出理由）。
//
// 业务价值：
//   - 避免同一处隐患被反复上报，浪费维修资源；
//   - 给经验不足的巡检员一个优先级参考。
package analysis

import (
	"math"
	"sort"
	"strconv"
	"strings"
	"time"
	"unicode"

	"github.com/xuanliu805-jpg/Unity-AR/server/internal/model"
)

// 重复报修风险等级。
const (
	DuplicateRiskNone = "none"
	DuplicateRiskLow  = "low"
	DuplicateRiskHigh = "high"
)

// 判定阈值。
const (
	duplicateSimilarity = 0.55
	duplicateDistance   = 5.0 // 米
	nearDistance        = 3.0 // 米
	similarThreshold    = 0.35
	maxSimilarIssues    = 5
	maxKeywords         = 6
)

// Input 是待分诊的问题。
type Input struct {
	Title       string
	Description string
	Position    model.Position
}

// Candidate 是历史问题（用于比对）。
type Candidate struct {
	ID          string
	Title       string
	Description string
	Priority    model.Priority
	Status      model.Status
	Position    model.Position
	CreatedAt   time.Time
}

// Similar 是一条相似历史问题。
type Similar struct {
	ID         string       `json:"id"`
	Title      string       `json:"title"`
	Similarity float64      `json:"similarity"`
	Distance   float64      `json:"distance"`
	AgeDays    float64      `json:"ageDays"`
	Status     model.Status `json:"status"`
	Advice     string       `json:"advice"`
}

// Result 是分诊结果。
type Result struct {
	SuggestedPriority model.Priority `json:"suggestedPriority"`
	SuggestedCategory string         `json:"suggestedCategory"`
	Confidence        float64        `json:"confidence"`
	DuplicateRisk     string         `json:"duplicateRisk"`
	Keywords          []string       `json:"keywords"`
	Reasons           []string       `json:"reasons"`
	SimilarIssues     []Similar      `json:"similarIssues"`
}

// rule 是一条类别规则：命中关键词 → 类别 + 建议的基础优先级 + 严重度。
type rule struct {
	Category string
	Priority model.Priority
	Severity int
	Keywords []string
}

// severity 把优先级转成可比较的数值。
func severity(priority model.Priority) int {
	switch priority {
	case model.PriorityLow:
		return 1
	case model.PriorityHigh:
		return 3
	default:
		return 2
	}
}

// priorityFor 是 severity 序数（1/2/3）到优先级的映射。
func priorityFor(ord int) model.Priority {
	switch ord {
	case 1:
		return model.PriorityLow
	case 3:
		return model.PriorityHigh
	default:
		return model.PriorityMedium
	}
}

// rules 是巡检场景的类别知识表。
var rules = []rule{
	{
		Category: "消防", Priority: model.PriorityHigh, Severity: 10,
		Keywords: []string{"消防", "灭火器", "消火栓", "火灾", "烟雾", "报警器", "疏散", "安全出口", "防火"},
	},
	{
		Category: "电气", Priority: model.PriorityHigh, Severity: 9,
		Keywords: []string{"漏电", "短路", "起火", "火花", "电缆", "配电箱", "配电", "插座", "电气", "跳闸"},
	},
	{
		Category: "结构", Priority: model.PriorityHigh, Severity: 8,
		Keywords: []string{"塌陷", "承重", "倾倒", "坠落", "结构", "脱落", "变形", "楼板"},
	},
	{
		Category: "渗漏", Priority: model.PriorityMedium, Severity: 6,
		Keywords: []string{"渗水", "渗漏", "漏水", "水渍", "发霉", "受潮", "积水", "滴水", "渗出"},
	},
	{
		Category: "破损", Priority: model.PriorityMedium, Severity: 5,
		Keywords: []string{"破损", "破裂", "碎了", "缺口", "掉漆", "划伤", "损坏", "裂纹", "裂缝"},
	},
	{
		Category: "设备", Priority: model.PriorityMedium, Severity: 5,
		Keywords: []string{"设备", "故障", "停机", "异响", "无法启动", "失灵", "卡住", "不转"},
	},
	{
		Category: "卫生", Priority: model.PriorityLow, Severity: 3,
		Keywords: []string{"垃圾", "杂物", "油污", "卫生", "脏乱", "纸箱"},
	},
}

// stopWords 是分词时过滤掉的虚词。
var stopWords = map[string]bool{
	"的": true, "了": true, "和": true, "是": true, "在": true, "有": true, "个": true,
	"一": true, "不": true, "也": true, "就": true, "都": true, "很": true, "到": true,
	"the": true, "a": true, "an": true, "of": true, "to": true, "is": true, "and": true,
	"in": true, "on": true, "for": true, "with": true, "at": true, "by": true,
}

// tokenize 把中英文混排文本切成词。
// 中文没有空格分词，这里用 2-gram（相邻两字）——简单、无需词典，对「渗水/水渍/配电」这类术语足够准。
// 英文与数字按非字母数字切分并转小写。
func tokenize(text string) []string {
	lower := strings.ToLower(text)

	var tokens []string
	var cjk []rune
	var word []rune

	flushCJK := func() {
		for i := 0; i+1 < len(cjk); i++ {
			bg := string(cjk[i : i+2])
			if !stopWords[bg] {
				tokens = append(tokens, bg)
			}
		}
		cjk = nil
	}
	flushWord := func() {
		if len(word) > 0 {
			w := string(word)
			if len(w) > 1 && !stopWords[w] {
				tokens = append(tokens, w)
			}
		}
		word = nil
	}

	for _, r := range lower {
		switch {
		case unicode.Is(unicode.Han, r):
			flushWord()
			cjk = append(cjk, r)
		case unicode.IsLetter(r), unicode.IsDigit(r):
			flushCJK()
			word = append(word, r)
		default:
			flushCJK()
			flushWord()
		}
	}
	flushCJK()
	flushWord()

	return tokens
}

// termFrequency 统计词频。
func termFrequency(tokens []string) map[string]float64 {
	tf := make(map[string]float64, len(tokens))
	for _, t := range tokens {
		tf[t]++
	}
	return tf
}

// buildIDF 依据语料（历史问题 + 当前问题）计算 IDF，让罕见词更有区分度。
func buildIDF(docs [][]string) map[string]float64 {
	df := make(map[string]float64)
	for _, doc := range docs {
		seen := make(map[string]bool, len(doc))
		for _, t := range doc {
			if !seen[t] {
				seen[t] = true
				df[t]++
			}
		}
	}

	idf := make(map[string]float64, len(df))
	total := float64(len(docs))
	for term, count := range df {
		idf[term] = math.Log((total+1)/(count+1)) + 1
	}
	return idf
}

// cosine 计算两个 TF-IDF 向量的余弦相似度。
func cosine(a, b map[string]float64, idf map[string]float64) float64 {
	var dot, na, nb float64
	for term, va := range a {
		w := idf[term]
		x := va * w
		na += x * x
		if vb, ok := b[term]; ok {
			y := vb * w
			dot += x * y
		}
	}
	for term, vb := range b {
		w := idf[term]
		y := vb * w
		nb += y * y
	}

	if na == 0 || nb == 0 {
		return 0
	}
	return dot / (math.Sqrt(na) * math.Sqrt(nb))
}

// jaccard 计算两个词集合的交并比。
func jaccard(a, b map[string]bool) float64 {
	if len(a) == 0 || len(b) == 0 {
		return 0
	}
	inter := 0
	for t := range a {
		if b[t] {
			inter++
		}
	}
	union := len(a) + len(b) - inter
	if union == 0 {
		return 0
	}
	return float64(inter) / float64(union)
}

// distance 计算两个坐标的 3D 距离（米）。
func distance(a, b model.Position) float64 {
	dx := a.X - b.X
	dy := a.Y - b.Y
	dz := a.Z - b.Z
	return math.Sqrt(dx*dx + dy*dy + dz*dz)
}

// matchRules 返回命中的规则与关键词。
func matchRules(text string) (rule, []string) {
	best := rule{Category: "其他", Priority: model.PriorityMedium, Severity: 2}
	var hits []string

	for _, r := range rules {
		for _, kw := range r.Keywords {
			if strings.Contains(text, kw) {
				hits = append(hits, kw)
				if r.Severity > best.Severity {
					best = r
				}
				break // 同一规则只记一次命中
			}
		}
	}

	return best, hits
}

// match 是「一条历史问题」的中间结果。
type match struct {
	cand  Candidate
	score float64
	dist  float64
}

// Triage 对输入的问题做智能分诊。
func Triage(input Input, history []Candidate, now time.Time) Result {
	text := input.Title + " " + input.Description

	result := Result{
		SuggestedPriority: model.PriorityMedium,
		SuggestedCategory: "其他",
		DuplicateRisk:     DuplicateRiskNone,
		Keywords:          []string{},
		Reasons:           []string{},
		SimilarIssues:     []Similar{},
	}

	matched, hits := matchRules(text)
	result.SuggestedCategory = matched.Category

	inputTokens := tokenize(text)

	docs := make([][]string, 0, len(history)+1)
	docs = append(docs, inputTokens)
	for _, c := range history {
		docs = append(docs, tokenize(c.Title+" "+c.Description))
	}
	idf := buildIDF(docs)

	inputTF := termFrequency(inputTokens)
	inputSet := make(map[string]bool, len(inputTokens))
	for _, t := range inputTokens {
		inputSet[t] = true
	}

	// --- 相似历史问题 ---
	var matches []match
	for _, c := range history {
		candTokens := tokenize(c.Title + " " + c.Description)
		candTF := termFrequency(candTokens)
		candSet := make(map[string]bool, len(candTokens))
		for _, t := range candTokens {
			candSet[t] = true
		}

		sim := 0.65*cosine(inputTF, candTF, idf) + 0.35*jaccard(inputSet, candSet)
		if sim < similarThreshold {
			continue
		}
		matches = append(matches, match{cand: c, score: sim, dist: distance(input.Position, c.Position)})
	}

	sort.SliceStable(matches, func(i, j int) bool {
		if matches[i].score != matches[j].score {
			return matches[i].score > matches[j].score
		}
		return matches[i].dist < matches[j].dist
	})

	// --- 重复报修风险 ---
	risk := DuplicateRiskNone
	for _, m := range matches {
		if m.score >= duplicateSimilarity && m.dist <= duplicateDistance {
			risk = DuplicateRiskHigh
			break
		}
	}
	if risk != DuplicateRiskHigh {
		for _, m := range matches {
			if m.score >= duplicateSimilarity || m.dist <= nearDistance {
				risk = DuplicateRiskLow
				break
			}
		}
	}
	result.DuplicateRisk = risk

	// --- 相似问题列表 ---
	for i, m := range matches {
		if i >= maxSimilarIssues {
			break
		}
		ageDays := math.Round(now.Sub(m.cand.CreatedAt).Hours()/24*10) / 10
		result.SimilarIssues = append(result.SimilarIssues, Similar{
			ID:         m.cand.ID,
			Title:      m.cand.Title,
			Similarity: math.Round(m.score*100) / 100,
			Distance:   math.Round(m.dist*100) / 100,
			AgeDays:    ageDays,
			Status:     m.cand.Status,
			Advice:     adviceFor(m.cand, ageDays),
		})
	}

	// --- 优先级建议：规则判定 与 历史同类问题分布 取更严重的一档 ---
	//
	// 注意两种「严重度」不是同一把尺子：
	//   - rule.Severity 是 3~10 的内部排序权重（消防 10 > 电气 9 > 结构 8 > 渗漏 6 …）；
	//   - severity(priority) 是 low/medium/high 的 1/2/3 序数。
	// 比较时必须先统一到序数，否则「消防的 10」永远大于「high 的 3」，规则会压过历史分布。
	ruleOrdinal := severity(matched.Priority)

	weighted := map[model.Priority]float64{}
	for _, m := range matches {
		weighted[m.cand.Priority] += m.score
	}
	var bestPriority model.Priority
	bestWeight := 0.0
	for p, w := range weighted {
		if w > bestWeight {
			bestWeight = w
			bestPriority = p
		}
	}

	finalOrdinal := ruleOrdinal
	if bestPriority != "" && severity(bestPriority) > finalOrdinal {
		finalOrdinal = severity(bestPriority)
	}
	result.SuggestedPriority = priorityFor(finalOrdinal)

	// --- 关键词 ---
	seen := make(map[string]bool)
	for _, kw := range hits {
		if !seen[kw] {
			seen[kw] = true
			result.Keywords = append(result.Keywords, kw)
		}
	}

	counts := map[string]int{}
	for _, t := range inputTokens {
		counts[t]++
	}
	type kv struct {
		term  string
		count int
	}
	var rest []kv
	for term, count := range counts {
		if !seen[term] {
			rest = append(rest, kv{term, count})
		}
	}
	sort.SliceStable(rest, func(i, j int) bool {
		if rest[i].count != rest[j].count {
			return rest[i].count > rest[j].count
		}
		return rest[i].term < rest[j].term
	})
	for _, r := range rest {
		if len(result.Keywords) >= maxKeywords {
			break
		}
		result.Keywords = append(result.Keywords, r.term)
	}

	// --- 理由 ---
	if len(hits) > 0 {
		result.Reasons = append(result.Reasons,
			"命中「"+strings.Join(dedupeStrings(hits), "/")+"」等关键词，归类为"+matched.Category)
	}
	if bestPriority != "" && severity(bestPriority) > ruleOrdinal {
		result.Reasons = append(result.Reasons,
			"历史相似问题多数定为"+string(bestPriority)+"，据此上调建议优先级")
	}
	if len(result.SimilarIssues) > 0 && risk == DuplicateRiskHigh {
		top := result.SimilarIssues[0]
		result.Reasons = append(result.Reasons,
			formatAge(top.AgeDays)+"在相距 "+formatFloat(top.Distance)+" 米处已上报过类似问题，可能重复")
	}

	// --- 可信度 ---
	result.Confidence = math.Round(math.Min(0.95, 0.35+0.1*float64(len(hits))+0.2*bestWeight)*100) / 100

	return result
}

// adviceFor 针对一条相似历史问题给出建议。
func adviceFor(c Candidate, ageDays float64) string {
	switch c.Status {
	case model.StatusResolved:
		return "同一位置 " + formatAge(ageDays) + "前的问题已标记为已解决，本次可能是复发"
	case model.StatusInProgress:
		return "同一位置 " + formatAge(ageDays) + "前已上报且正在处理中，建议先确认是否重复报修"
	default:
		return "同一位置 " + formatAge(ageDays) + "前已上报且仍未处理，建议先确认是否重复报修"
	}
}

func dedupeStrings(in []string) []string {
	seen := map[string]bool{}
	out := []string{}
	for _, s := range in {
		if !seen[s] {
			seen[s] = true
			out = append(out, s)
		}
	}
	return out
}

func formatAge(days float64) string {
	d := int(math.Round(days))
	if d <= 0 {
		return "今天"
	}
	return strconv.Itoa(d) + " 天"
}

func formatFloat(f float64) string {
	return strconv.Itoa(int(math.Round(f)))
}
