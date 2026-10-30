using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 迷你 JSON 解析器的单元测试。
    /// Unity 端要解析后端的问题列表和 AI 分诊结果，解析器一旦出错，
    /// 表现会是「列表空着」这种很难定位的现象，所以必须测扎实。
    /// </summary>
    [TestFixture]
    public class JsonTests
    {
        [Test]
        public void 解析对象()
        {
            var v = JsonValue.Parse("{\"a\":1,\"b\":\"two\"}");

            Assert.That(v, Is.Not.Null);
            Assert.That(v.Kind, Is.EqualTo(JsonKind.Object));
            Assert.That(v["a"].AsInt, Is.EqualTo(1));
            Assert.That(v["b"].AsString, Is.EqualTo("two"));
        }

        [Test]
        public void 解析顶层数组()
        {
            var v = JsonValue.Parse("[{\"id\":\"A\"},{\"id\":\"B\"}]");

            Assert.That(v.Kind, Is.EqualTo(JsonKind.Array));
            Assert.That(v.Count, Is.EqualTo(2));
            Assert.That(v.Items[0]["id"].AsString, Is.EqualTo("A"));
            Assert.That(v.Items[1]["id"].AsString, Is.EqualTo("B"));
        }

        [Test]
        public void 解析嵌套结构()
        {
            var v = JsonValue.Parse("{\"issue\":{\"position\":{\"x\":0.42,\"y\":0.03,\"z\":1.26}}}");

            Assert.That(v["issue"]["position"]["x"].AsDouble, Is.EqualTo(0.42).Within(1e-9));
            Assert.That(v["issue"]["position"]["z"].AsDouble, Is.EqualTo(1.26).Within(1e-9));
        }

        [Test]
        public void 解析布尔与null()
        {
            var v = JsonValue.Parse("{\"ok\":true,\"bad\":false,\"none\":null}");

            Assert.That(v["ok"].AsBool, Is.True);
            Assert.That(v["bad"].AsBool, Is.False);
            Assert.That(v["none"].IsNull, Is.True);
        }

        [Test]
        public void 解析负数与科学计数()
        {
            var v = JsonValue.Parse("{\"a\":-1.5,\"b\":2e3}");

            Assert.That(v["a"].AsDouble, Is.EqualTo(-1.5).Within(1e-9));
            Assert.That(v["b"].AsDouble, Is.EqualTo(2000d).Within(1e-9));
        }

        [Test]
        public void 解析转义字符()
        {
            var v = JsonValue.Parse("{\"t\":\"a\\\"b\\nc\"}");

            Assert.That(v["t"].AsString, Is.EqualTo("a\"b\nc"));
        }

        [Test]
        public void 解析中文()
        {
            var v = JsonValue.Parse("{\"title\":\"入口墙面破损\"}");

            Assert.That(v["title"].AsString, Is.EqualTo("入口墙面破损"));
        }

        [Test]
        public void 缺失的字段返回null而不是抛异常()
        {
            var v = JsonValue.Parse("{\"a\":1}");

            Assert.That(v["missing"], Is.Null);
        }

        [Test]
        public void 非法JSON返回null而不是抛异常()
        {
            Assert.That(JsonValue.Parse("{not json"), Is.Null);
            Assert.That(JsonValue.Parse(""), Is.Null);
            Assert.That(JsonValue.Parse(null), Is.Null);
            Assert.That(JsonValue.Parse("{\"a\":1"), Is.Null);
        }

        [Test]
        public void 空对象与空数组()
        {
            var o = JsonValue.Parse("{}");
            var a = JsonValue.Parse("[]");

            Assert.That(o.Count, Is.EqualTo(0));
            Assert.That(a.Count, Is.EqualTo(0));
        }

        [Test]
        public void 忽略多余空白()
        {
            var v = JsonValue.Parse("  {  \"a\" : 1 , \"b\" : [ 1 , 2 ] }  ");

            Assert.That(v["a"].AsInt, Is.EqualTo(1));
            Assert.That(v["b"].Count, Is.EqualTo(2));
        }

        [Test]
        public void 能解析后端真实的问题列表结构()
        {
            const string json = "{\"issues\":[{\"id\":\"ISS-1\",\"title\":\"入口墙面破损\",\"description\":\"左侧墙体存在裂缝\",\"priority\":\"high\",\"status\":\"open\",\"position\":{\"x\":0.42,\"y\":0.03,\"z\":1.26},\"createdAt\":\"2026-10-26T12:21:06.5628133+08:00\",\"updatedAt\":\"2026-10-26T12:21:06.5628133+08:00\"}],\"count\":1}";

            var v = JsonValue.Parse(json);

            Assert.That(v["count"].AsInt, Is.EqualTo(1));
            var issue = v["issues"].Items[0];
            Assert.That(issue["id"].AsString, Is.EqualTo("ISS-1"));
            Assert.That(issue["priority"].AsString, Is.EqualTo("high"));
            Assert.That(issue["position"]["x"].AsDouble, Is.EqualTo(0.42).Within(1e-9));
        }

        [Test]
        public void 能解析AI分诊结果结构()
        {
            const string json = "{\"suggestedPriority\":\"high\",\"suggestedCategory\":\"渗漏\",\"confidence\":0.78,\"duplicateRisk\":\"high\",\"keywords\":[\"外墙\",\"渗水\"],\"reasons\":[\"命中关键词\"],\"similarIssues\":[{\"id\":\"ISS-1\",\"title\":\"东侧外墙渗水\",\"similarity\":0.83,\"distance\":2.4,\"ageDays\":3,\"status\":\"open\",\"advice\":\"可能重复\"}]}";

            var v = JsonValue.Parse(json);

            Assert.That(v["suggestedPriority"].AsString, Is.EqualTo("high"));
            Assert.That(v["duplicateRisk"].AsString, Is.EqualTo("high"));
            Assert.That(v["keywords"].Count, Is.EqualTo(2));
            Assert.That(v["similarIssues"].Items[0]["distance"].AsDouble, Is.EqualTo(2.4).Within(1e-9));
        }
    }
}
