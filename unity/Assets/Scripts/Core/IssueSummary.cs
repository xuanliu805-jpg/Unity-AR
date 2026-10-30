using System.Collections.Generic;

namespace UnityAR.Core
{
    /// <summary>
    /// 一条历史问题的摘要（Unity 端历史列表用）。
    /// 解析放在 Core 里，因此可以被单元测试覆盖。
    /// </summary>
    public class IssueSummary
    {
        public string ID = string.Empty;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public string Priority = IssuePriorities.Medium;
        public string Status = IssueStatuses.Open;
        public Vector3Position Position = Vector3Position.Zero;
        public string CreatedAt = string.Empty;
        public string UpdatedAt = string.Empty;

        /// <summary>解析 GET /api/issues 的响应体。</summary>
        public static List<IssueSummary> ParseList(string json)
        {
            var result = new List<IssueSummary>();
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            var root = JsonValue.Parse(json);
            if (root == null)
            {
                return result;
            }

            var issues = root["issues"];
            if (issues == null || issues.Kind != JsonKind.Array)
            {
                return result;
            }

            foreach (var item in issues.Items)
            {
                var issue = ParseOne(item);
                if (issue != null)
                {
                    result.Add(issue);
                }
            }

            return result;
        }

        /// <summary>解析单条问题对象。</summary>
        public static IssueSummary ParseOne(JsonValue item)
        {
            if (item == null || item.Kind != JsonKind.Object)
            {
                return null;
            }

            var issue = new IssueSummary
            {
                ID = Str(item["id"]),
                Title = Str(item["title"]),
                Description = Str(item["description"]),
                Priority = Str(item["priority"]),
                Status = Str(item["status"]),
                CreatedAt = Str(item["createdAt"]),
                UpdatedAt = Str(item["updatedAt"])
            };

            if (string.IsNullOrEmpty(issue.Priority)) issue.Priority = IssuePriorities.Medium;
            if (string.IsNullOrEmpty(issue.Status)) issue.Status = IssueStatuses.Open;

            var pos = item["position"];
            if (pos != null && pos.Kind == JsonKind.Object)
            {
                issue.Position = new Vector3Position(
                    (float)pos["x"].AsDouble,
                    (float)pos["y"].AsDouble,
                    (float)pos["z"].AsDouble);
            }

            return issue;
        }

        private static string Str(JsonValue v)
        {
            return v == null || v.IsNull ? string.Empty : (v.AsString ?? string.Empty);
        }
    }

    /// <summary>
    /// AI 智能分诊的结果。
    /// </summary>
    public class TriageResult
    {
        public string SuggestedPriority = IssuePriorities.Medium;
        public string SuggestedCategory = "其他";
        public double Confidence;
        public string DuplicateRisk = "none";
        public List<string> Keywords = new List<string>();
        public List<string> Reasons = new List<string>();
        public List<TriageSimilar> SimilarIssues = new List<TriageSimilar>();

        /// <summary>是否提示存在重复报修风险。</summary>
        public bool HasDuplicateRisk
        {
            get { return DuplicateRisk == "high"; }
        }

        /// <summary>解析 POST /api/analysis/triage 的响应体。</summary>
        public static TriageResult Parse(string json)
        {
            var result = new TriageResult();
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            var root = JsonValue.Parse(json);
            if (root == null || root.Kind != JsonKind.Object)
            {
                return result;
            }

            result.SuggestedPriority = Str(root["suggestedPriority"]);
            if (string.IsNullOrEmpty(result.SuggestedPriority)) result.SuggestedPriority = IssuePriorities.Medium;
            result.SuggestedCategory = Str(root["suggestedCategory"]);
            if (string.IsNullOrEmpty(result.SuggestedCategory)) result.SuggestedCategory = "其他";
            result.Confidence = root["confidence"].AsDouble;
            result.DuplicateRisk = Str(root["duplicateRisk"]);
            if (string.IsNullOrEmpty(result.DuplicateRisk)) result.DuplicateRisk = "none";

            var keywords = root["keywords"];
            if (keywords != null && keywords.Kind == JsonKind.Array)
            {
                foreach (var k in keywords.Items)
                {
                    var s = k.AsString;
                    if (!string.IsNullOrEmpty(s)) result.Keywords.Add(s);
                }
            }

            var reasons = root["reasons"];
            if (reasons != null && reasons.Kind == JsonKind.Array)
            {
                foreach (var r in reasons.Items)
                {
                    var s = r.AsString;
                    if (!string.IsNullOrEmpty(s)) result.Reasons.Add(s);
                }
            }

            var similar = root["similarIssues"];
            if (similar != null && similar.Kind == JsonKind.Array)
            {
                foreach (var s in similar.Items)
                {
                    result.SimilarIssues.Add(new TriageSimilar
                    {
                        ID = Str(s["id"]),
                        Title = Str(s["title"]),
                        Similarity = s["similarity"].AsDouble,
                        Distance = s["distance"].AsDouble,
                        AgeDays = s["ageDays"].AsDouble,
                        Status = Str(s["status"]),
                        Advice = Str(s["advice"])
                    });
                }
            }

            return result;
        }

        private static string Str(JsonValue v)
        {
            return v == null || v.IsNull ? string.Empty : (v.AsString ?? string.Empty);
        }
    }

    /// <summary>一条相似历史问题（分诊结果里的）。</summary>
    public class TriageSimilar
    {
        public string ID = string.Empty;
        public string Title = string.Empty;
        public double Similarity;
        public double Distance;
        public double AgeDays;
        public string Status = string.Empty;
        public string Advice = string.Empty;
    }
}
