using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityAR.Core;

namespace UnityAR.UI
{
    /// <summary>一次 HTTP 调用的结果。</summary>
    public class ApiResult
    {
        /// <summary>是否成功（HTTP 2xx）。</summary>
        public bool Success;

        /// <summary>HTTP 状态码。</summary>
        public long StatusCode;

        /// <summary>原始响应体。</summary>
        public string Body;

        /// <summary>给用户看的中文提示。</summary>
        public string Message;

        /// <summary>后端返回的问题 ID（仅创建成功时有值）。</summary>
        public string IssueId;

        /// <summary>是否属于「连不上后端」这类网络问题。</summary>
        public bool IsNetworkError;
    }

    /// <summary>
    /// Go 后端 HTTP 客户端。
    ///
    /// 只用 Unity 内置的 UnityWebRequest，不引入第三方网络库，
    /// 保证 IL2CPP 打包简单、体积可控。
    /// </summary>
    [DisallowMultipleComponent]
    public class ApiClient : MonoBehaviour
    {
        [SerializeField] private BackendSettings backendSettings;

        private void Awake()
        {
            if (backendSettings == null)
            {
                backendSettings = FindObjectOfType<BackendSettings>();
            }
        }

        /// <summary>提交一条问题。回调在主线程执行。</summary>
        public IEnumerator SubmitIssue(IssueDraft draft, Action<ApiResult> onComplete)
        {
            var url = BackendConfig.BuildUrl(backendSettings.BaseUrl, ApiEndpoints.Issues);
            var payload = IssuePayloadBuilder.Build(draft);

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 15;

                yield return request.SendWebRequest();

                var result = Interpret(request);
                if (result.Success)
                {
                    string issueId;
                    if (ApiResponseParser.TryExtractIssueId(result.Body, out issueId))
                    {
                        result.IssueId = issueId;
                    }

                    result.Message = "上报成功，问题编号 " + (result.IssueId ?? "-");
                }

                if (onComplete != null)
                {
                    onComplete(result);
                }
            }
        }

        /// <summary>健康检查，用于在界面上显示后端连通状态。</summary>
        public IEnumerator CheckHealth(Action<ApiResult> onComplete)
        {
            var url = BackendConfig.BuildUrl(backendSettings.BaseUrl, ApiEndpoints.Health);

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();

                var result = Interpret(request);
                if (result.Success && !ApiResponseParser.IsHealthy(result.Body))
                {
                    result.Success = false;
                    result.Message = "后端返回了非预期的健康检查结果";
                }
                else if (result.Success)
                {
                    result.Message = "后端连接正常";
                }

                if (onComplete != null)
                {
                    onComplete(result);
                }
            }
        }

        /// <summary>
        /// 拉取历史问题列表（用于 Unity 端的历史面板）。
        /// </summary>
        public IEnumerator GetIssues(Action<List<IssueSummary>> onComplete)
        {
            var url = BackendConfig.BuildUrl(backendSettings.BaseUrl, ApiEndpoints.Issues);

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();

                var result = Interpret(request);
                if (!result.Success)
                {
                    if (onComplete != null) onComplete(null);
                    yield break;
                }

                var issues = IssueSummary.ParseList(result.Body);
                if (onComplete != null) onComplete(issues);
            }
        }

        /// <summary>修改问题状态。</summary>
        public IEnumerator UpdateStatus(string issueId, string status, Action<ApiResult> onComplete)
        {
            var url = BackendConfig.BuildUrl(backendSettings.BaseUrl, ApiEndpoints.IssueStatus(issueId));
            var payload = "{\"status\":\"" + status + "\"}";

            // UnityWebRequest 没有 kHttpVerbPATCH 常量，用字面量。
            using (var request = new UnityWebRequest(url, "PATCH"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 15;

                yield return request.SendWebRequest();

                var result = Interpret(request);
                if (result.Success)
                {
                    result.Message = "状态已更新";
                }
                if (onComplete != null) onComplete(result);
            }
        }

        /// <summary>删除问题。</summary>
        public IEnumerator DeleteIssue(string issueId, Action<ApiResult> onComplete)
        {
            var url = BackendConfig.BuildUrl(backendSettings.BaseUrl, ApiEndpoints.Issue(issueId));

            using (var request = UnityWebRequest.Delete(url))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();

                var result = Interpret(request);
                if (result.Success)
                {
                    result.Message = "问题已删除";
                }
                if (onComplete != null) onComplete(result);
            }
        }

        /// <summary>
        /// 调用后端的「智能分诊」接口，拿到建议优先级、类别与重复报修风险。
        /// </summary>
        public IEnumerator Triage(string title, string description, Vector3Position position,
            Action<TriageResult> onComplete)
        {
            var url = BackendConfig.BuildUrl(backendSettings.BaseUrl, "/api/analysis/triage");
            var payload = "{\"title\":\"" + JsonEscape(title) + "\",\"description\":\"" +
                          JsonEscape(description) + "\",\"position\":{\"x\":" +
                          position.X.ToString(CultureInfo.InvariantCulture) +
                          ",\"y\":" + position.Y.ToString(CultureInfo.InvariantCulture) +
                          ",\"z\":" + position.Z.ToString(CultureInfo.InvariantCulture) + "}}";

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;

                yield return request.SendWebRequest();

                var result = Interpret(request);
                if (!result.Success)
                {
                    if (onComplete != null) onComplete(null);
                    yield break;
                }

                if (onComplete != null) onComplete(TriageResult.Parse(result.Body));
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>把 UnityWebRequest 的结果翻译成统一结构。</summary>
        private static ApiResult Interpret(UnityWebRequest request)
        {
            var result = new ApiResult
            {
                StatusCode = request.responseCode,
                Body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty
            };

            if (request.result == UnityWebRequest.Result.Success)
            {
                result.Success = true;
                result.Message = "请求成功";

                string message;
                if (ApiResponseParser.TryExtractErrorMessage(result.Body, out message))
                {
                    // 后端用 2xx 返回错误结构的情况极少，但这里做一次兜底。
                    result.Success = false;
                    result.Message = message;
                }

                return result;
            }

            result.IsNetworkError = request.result == UnityWebRequest.Result.ConnectionError
                                   || request.result == UnityWebRequest.Result.DataProcessingError;

            string errorMessage;
            if (ApiResponseParser.TryExtractErrorMessage(result.Body, out errorMessage))
            {
                result.Message = errorMessage;
                return result;
            }

            if (result.IsNetworkError)
            {
                result.Message = "无法连接后端 " + "，请检查后端地址与网络（" + request.error + "）";
                return result;
            }

            result.Message = "请求失败（HTTP " + request.responseCode + "）：" + request.error;
            return result;
        }
    }
}
