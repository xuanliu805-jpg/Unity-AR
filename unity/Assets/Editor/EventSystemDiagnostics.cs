using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace UnityAR.EditorTools
{
    /// <summary>
    /// 诊断 EventSystem 的输入模块配置。
    ///
    /// 背景：真机上「点按钮没反应、输入框不聚焦」，但 AR 放置判定却能正确识别出
    /// 「点击了界面控件」。说明问题不在场景结构，而在**事件系统的输入模块**
    /// 与项目实际启用的输入系统不匹配 —— UI 那一半收不到输入。
    /// 这个方法把相关事实一次性打印出来，避免靠猜。
    /// </summary>
    public static class EventSystemDiagnostics
    {

        /// <summary>
        /// 从 ProjectSettings.asset 里读 activeInputHandler。
        /// PlayerSettings.activeInputHandler 是 internal API，读不到，只能从配置文件里取。
        /// </summary>
        private static string ReadActiveInputHandler()
        {
            var path = System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName,
                "ProjectSettings", "ProjectSettings.asset");

            if (!System.IO.File.Exists(path)) return "(读不到 ProjectSettings.asset)";

            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (line.StartsWith("  activeInputHandler:"))
                {
                    return line.Split(':')[1].Trim() + "  (0=旧 1=新 2=Both)";
                }
            }

            return "(ProjectSettings.asset 里没有 activeInputHandler 字段)";
        }
        public static void Diagnose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== EventSystem / 输入系统诊断 ==========");

#if ENABLE_INPUT_SYSTEM
            sb.AppendLine("编译期宏 ENABLE_INPUT_SYSTEM      : 已定义（新输入系统）");
#else
            sb.AppendLine("编译期宏 ENABLE_INPUT_SYSTEM      : 未定义");
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            sb.AppendLine("编译期宏 ENABLE_LEGACY_INPUT_MANAGER: 已定义（旧输入系统）");
#else
            sb.AppendLine("编译期宏 ENABLE_LEGACY_INPUT_MANAGER: 未定义");
#endif

            sb.AppendLine("运行时 activeInputHandler            : " + ReadActiveInputHandler() +
                          "  (0=旧 1=新 2=Both)");
            sb.AppendLine("运行时 InputSystem.settings           : " + InputSystem.settings.updateMode);

            var scenePath = "Assets/Scenes/ARInspect.unity";
            if (System.IO.File.Exists(scenePath))
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

                var es = UnityEngine.Object.FindObjectOfType<EventSystem>();
                if (es == null)
                {
                    sb.AppendLine("场景里没有 EventSystem！");
                }
                else
                {
                    sb.AppendLine("场景里的 EventSystem                : " + es.gameObject.name);
                    var modules = es.GetComponents<BaseInputModule>();
                    sb.AppendLine("  挂载的输入模块数量               : " + modules.Length);
                    foreach (var m in modules)
                    {
                        sb.AppendLine("    - " + m.GetType().FullName + "  enabled=" + m.enabled);
                        var uiModule = m as InputSystemUIInputModule;
                        if (uiModule != null)
                        {

                            sb.AppendLine("      actionsAsset                 : " +
                                          (uiModule.actionsAsset != null ? uiModule.actionsAsset.name : "(null)"));
                        }
                    }
                }

                // 顺便确认 UI 元素是否真的可被射线命中
                var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
                if (canvas != null)
                {
                    var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                    sb.AppendLine("Canvas 的 GraphicRaycaster          : " + (raycaster != null ? "有" : "❌ 没有"));
                }
            }

            sb.AppendLine("===========================================");
            Debug.Log(sb.ToString());
        }
    }
}
