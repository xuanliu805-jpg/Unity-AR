using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>历史列表与 AI 分诊结果的解析测试。</summary>
    [TestFixture]
    public class IssueSummaryTests
    {
        private const string ListJson = "{\"issues\":[{\"id\":\"ISS-1\",\"title\":\"入口墙面破损\",\"description\":\"左侧墙体存在裂缝\",\"priority\":\"high\",\"status\":\"open\",\"position\":{\"x\":0.42,\"y\":0.03,\"z\":1.26},\"createdAt\":\"2026-10-26T12:21:06+08:00\",\"updatedAt\":\"2026-10-26T12:21:06+08:00\"},{\"id\":\"ISS-2\",\"title\":\"配电箱火花\",\"description\":\"\",\"priority\":\"low\",\"status\":\"resolved\",\"position\":{\"x\":1,\"y\":0,\"z\":2},\"createdAt\":\"2026-10-27T12:21:06+08:00\",\"updatedAt\":\"2026-10-27T12:21:06+08:00\"}],\"count\":2}";

        [Test]
        public void 解析历史问题列表()
        {
            var issues = IssueSummary.ParseList(ListJson);

            Assert.That(issues.Count, Is.EqualTo(2));
            Assert.That(issues[0].ID, Is.EqualTo("ISS-1"));
            Assert.That(issues[0].Title, Is.EqualTo("入口墙面破损"));
            Assert.That(issues[0].Priority, Is.EqualTo("high"));
            Assert.That(issues[0].Status, Is.EqualTo("open"));
            Assert.That(issues[0].Position.X, Is.EqualTo(0.42f).Within(1e-6f));
            Assert.That(issues[0].Position.Z, Is.EqualTo(1.26f).Within(1e-6f));
        }

        [Test]
        public void 第二条的状态与优先级也正确()
        {
            var issues = IssueSummary.ParseList(ListJson);

            Assert.That(issues[1].Priority, Is.EqualTo("low"));
            Assert.That(issues[1].Status, Is.EqualTo("resolved"));
        }

        [Test]
        public void 空响应返回空列表而不是null()
        {
            Assert.That(IssueSummary.ParseList(""), Is.Not.Null);
            Assert.That(IssueSummary.ParseList("").Count, Is.EqualTo(0));
            Assert.That(IssueSummary.ParseList("garbage").Count, Is.EqualTo(0));
            Assert.That(IssueSummary.ParseList("{\"issues\":[]}").Count, Is.EqualTo(0));
        }

        [Test]
        public void 字段缺失时用默认值兜底()
        {
            var issues = IssueSummary.ParseList("{\"issues\":[{\"id\":\"ISS-9\"}]}");

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Priority, Is.EqualTo(IssuePriorities.Medium));
            Assert.That(issues[0].Status, Is.EqualTo(IssueStatuses.Open));
            Assert.That(issues[0].Title, Is.EqualTo(string.Empty));
        }

        [Test]
        public void 解析AI分诊结果()
        {
            const string json = "{\"suggestedPriority\":\"high\",\"suggestedCategory\":\"渗漏\",\"confidence\":0.78,\"duplicateRisk\":\"high\",\"keywords\":[\"外墙\",\"渗水\"],\"reasons\":[\"命中关键词「渗水」\"],\"similarIssues\":[{\"id\":\"ISS-1\",\"title\":\"东侧外墙渗水\",\"similarity\":0.83,\"distance\":2.4,\"ageDays\":3,\"status\":\"open\",\"advice\":\"可能重复\"}]}";

            var r = TriageResult.Parse(json);

            Assert.That(r.SuggestedPriority, Is.EqualTo("high"));
            Assert.That(r.SuggestedCategory, Is.EqualTo("渗漏"));
            Assert.That(r.Confidence, Is.EqualTo(0.78).Within(1e-9));
            Assert.That(r.DuplicateRisk, Is.EqualTo("high"));
            Assert.That(r.HasDuplicateRisk, Is.True);
            Assert.That(r.Keywords.Count, Is.EqualTo(2));
            Assert.That(r.Reasons.Count, Is.EqualTo(1));
            Assert.That(r.SimilarIssues.Count, Is.EqualTo(1));
            Assert.That(r.SimilarIssues[0].Distance, Is.EqualTo(2.4).Within(1e-9));
            Assert.That(r.SimilarIssues[0].AgeDays, Is.EqualTo(3d).Within(1e-9));
        }

        [Test]
        public void 分诊结果解析失败时返回安全默认值()
        {
            var r = TriageResult.Parse("");

            Assert.That(r.SuggestedPriority, Is.EqualTo(IssuePriorities.Medium));
            Assert.That(r.DuplicateRisk, Is.EqualTo("none"));
            Assert.That(r.HasDuplicateRisk, Is.False);
            Assert.That(r.Keywords, Is.Not.Null);
            Assert.That(r.SimilarIssues, Is.Not.Null);
        }
    }
}
