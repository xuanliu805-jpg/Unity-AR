using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAR.EditorTools
{
    /// <summary>
    /// 一键配置 Android / iOS 的 AR 相关 Player Settings。
    ///
    /// 只做「有公开 API、且容易漏」的几项；ARCore 的 Requirement=Required、
    /// ARKit 的 Enable 开关在 XR Plug-in Management 里，需要在 Project Settings 手动勾选。
    /// </summary>
    public static class ArProjectSettings
    {
        [MenuItem("Tools/AR 巡检/配置 Android AR 设置", false, 20)]
        public static void ConfigureAndroid()
        {
            // ARCore 要求 Android 7.0(API 24) 以上
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // ARM64 在 Android 上必须用 IL2CPP（Unity 2022.3 里 Mono 只支持 ARMv7）。
            // 新建的工程默认 scriptingBackend 是空表（= 全 Mono），这里显式指定。
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.example.unityar.inspect");
            PlayerSettings.companyName = "AR Inspection Demo";
            PlayerSettings.productName = "园区巡检 AR";

            // 允许明文 HTTP：Android 9+ 默认禁止 http:// 明文流量，
            // 真机直连开发机的 http://192.168.x.x:8080 必须打开这个开关，
            // 否则会报 "CLEARTEXT communication to ... not permitted"。
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            // ARCore 只支持 OpenGLES3，不支持 Vulkan。
            // Unity 2022.3 的 Android 默认图形 API 列表里含 Vulkan，
            // 不改的话打包会直接失败：
            //   BuildFailedException: You have enabled the Vulkan graphics API, which is not supported by ARCore.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            Debug.Log("[AR 巡检] 图形 API 已设为 OpenGLES3（ARCore 不支持 Vulkan）");

            AssetDatabase.SaveAssets();
            Debug.Log("[AR 巡检] 已配置 Android：minSdk 24 / ARM64 / 包名 com.example.unityar.inspect");
            Debug.Log("[AR 巡检] 请手动完成：Project Settings → XR Plug-in Management → ARCore → Requirement = Required");
        }

        [MenuItem("Tools/AR 巡检/配置 iOS AR 设置", false, 21)]
        public static void ConfigureIos()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.example.unityar.inspect");
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.companyName = "AR Inspection Demo";
            PlayerSettings.productName = "园区巡检 AR";
            AssetDatabase.SaveAssets();
            Debug.Log("[AR 巡检] 已配置 iOS：包名 com.example.unityar.inspect / 最低 iOS 13.0");
            Debug.Log("[AR 巡检] 请手动完成：Project Settings → XR Plug-in Management → ARKit → Enable，并填写 Camera Usage Description");
        }

        /// <summary>
        /// 把标记要用的 Standard shader 加进「始终包含的着色器」。
        ///
        /// 背景：标记是用 <c>GameObject.CreatePrimitive</c> 运行时创建的，
        /// 用的是内置的 Default-Material，它引用 Standard shader。
        /// 而 Standard 默认**不在** Always Included Shaders 里，
        /// 新建工程又没有任何材质引用它 —— 于是打包时它被剔除，
        /// 真机上标记会渲染成 Unity 的「shader 缺失」品红色（255,0,255）。
        /// 这个坑只有在真机上才看得见。
        /// </summary>
        public static void ConfigureAlwaysIncludedShaders()
        {
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null)
            {
                Debug.LogWarning("[AR 巡检] 读不到 GraphicsSettings.asset，跳过着色器配置");
                return;
            }

            var standard = Shader.Find("Standard");
            if (standard == null)
            {
                Debug.LogWarning("[AR 巡检] 找不到 Standard shader");
                return;
            }

            var so = new SerializedObject(graphicsSettings);
            var array = so.FindProperty("m_AlwaysIncludedShaders");
            if (array == null)
            {
                return;
            }

            for (var i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (element != null && element.name == standard.name)
                {
                    Debug.Log("[AR 巡检] Standard shader 已在始终包含列表中");
                    return;
                }
            }

            array.InsertArrayElementAtIndex(0);
            array.GetArrayElementAtIndex(0).objectReferenceValue = standard;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(graphicsSettings);
            AssetDatabase.SaveAssets();
            Debug.Log("[AR 巡检] 已把 Standard shader 加入始终包含的着色器（否则真机上的标记会是品红色）");
        }
    }
}
