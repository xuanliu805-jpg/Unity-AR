using UnityEngine;
using UnityAR.Core;

namespace UnityAR.UI
{
    /// <summary>
    /// 后端地址设置。
    ///
    /// 真机上 Go 后端通常跑在开发机的局域网 IP（http://192.168.x.x:8080），
    /// 写死 localhost 是连不上的，所以：
    ///   1. Inspector 上可以直接改；
    ///   2. 运行时也可以在界面上的输入框里改；
    ///   3. 改完写进 PlayerPrefs，下次启动自动带上。
    /// </summary>
    [DisallowMultipleComponent]
    public class BackendSettings : MonoBehaviour
    {
        public const string PlayerPrefsKey = "unity-ar.backend-base-url";

        [Tooltip("Go 后端地址。真机请填开发机的局域网 IP，例如 http://192.168.1.20:8080")]
        [SerializeField] private string baseUrl = BackendConfig.DefaultBaseUrl;

        /// <summary>规范化之后的后端地址（去掉末尾斜杠）。</summary>
        public string BaseUrl
        {
            get { return BackendConfig.NormalizeBaseUrl(baseUrl); }
            set
            {
                baseUrl = BackendConfig.NormalizeBaseUrl(value);
                PlayerPrefs.SetString(PlayerPrefsKey, baseUrl);
                PlayerPrefs.Save();
            }
        }

        private void Awake()
        {
            var saved = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(saved))
            {
                baseUrl = BackendConfig.NormalizeBaseUrl(saved);
            }
            else
            {
                baseUrl = BackendConfig.NormalizeBaseUrl(baseUrl);
            }
        }
    }
}
