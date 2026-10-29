using System;
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEditor.XR.ARCore;
using TMPro;
using UnityEditor.Build.Reporting;

namespace UnityAR.EditorTools
{
    /// <summary>
    /// Android 真机打包的完整前置配置。
    ///
    /// 这些步骤在 Unity GUI 里都是「Project Settings 里点一下」，
    /// 但要支持无人值守打包 / 评审机一键复现，就得能用脚本调。
    ///
    /// 命令行用法：
    ///   Unity.exe -batchmode -quit -executeMethod UnityAR.EditorTools.ProjectSetup.PrepareAll -projectPath &lt;工程路径&gt;
    ///   Unity.exe -batchmode -quit -executeMethod UnityAR.EditorTools.ProjectSetup.BuildApk -projectPath &lt;工程路径&gt;
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>按顺序执行全部准备工作。</summary>
        public static void PrepareAll()
        {
            Debug.Log("[项目准备] 开始");

            ImportTmpEssentials();
            BuildScene();
            // 必须在 BuildScene 之后：生成场景会新建 TMP 文本，
            // 换字体要放在后面，否则新场景里的文本又用回默认拉丁字体。
            ChineseFontSetup.Setup();
            ArProjectSettings.ConfigureAndroid();
            ConfigureAndroidToolPaths();
            EnableArCoreLoader();
            SetArCoreRequirementRequired();

            Debug.Log("[项目准备] 全部完成");
        }

        /// <summary>
        /// 导入 TextMeshPro 的 Essential Resources。
        ///
        /// TMP 的默认字体资源（LibreSans SDF 等）不在包里，必须单独导入，
        /// 否则场景里的 TMP 文本没有字体，界面上会一个字都不显示。
        /// 这里调的是 TMP 官方提供的导入器，第三个参数 interactive=false 表示不弹窗。
        /// </summary>
        public static void ImportTmpEssentials()
        {
            try
            {
                TMP_PackageResourceImporter.ImportResources(true, false, false);
                Debug.Log("[项目准备] TMP Essential Resources 导入完成");
            }
            catch (Exception e)
            {
                // 重复导入时 TMP 会抛异常，这不算失败。
                Debug.LogWarning("[项目准备] TMP 资源导入跳过（可能已导入）: " + e.Message);
            }
        }

        /// <summary>生成巡检场景。</summary>
        public static void BuildScene()
        {
            SceneBuilder.BuildScene();
        }

        /// <summary>
        /// 告诉 Unity 去哪里找 Android SDK / NDK / JDK。
        ///
        /// 这三个路径是**本机**的设置，存放在 EditorPrefs 里（不进版本库）。
        /// 这里按 Unity 2022.3 的默认安装布局推断，如果本机装在别处，
        /// 可以通过环境变量 UNITY_ANDROID_SDK_ROOT 等覆盖。
        /// </summary>
        public static void ConfigureAndroidToolPaths()
        {
            // Application.dataPath 是工程目录（Assets 的父目录），不是编辑器安装目录。
            // 编辑器根目录要从 EditorApplication.applicationPath 反推：
            //   applicationPath = <编辑器根>\Editor\Unity.exe
            var editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath);
            var player = Path.Combine(editorRoot, "Data", "PlaybackEngines", "AndroidPlayer");

            var sdk = EnvOr("UNITY_ANDROID_SDK_ROOT", Path.Combine(player, "SDK"));
            var ndk = EnvOr("UNITY_ANDROID_NDK_ROOT", FindNdkRoot(Path.Combine(player, "NDK")));
            var jdk = EnvOr("UNITY_JDK_ROOT", Path.Combine(player, "OpenJDK"));

            EditorPrefs.SetString("AndroidSdkRoot", sdk);
            EditorPrefs.SetString("AndroidNdkRoot", ndk);
            EditorPrefs.SetString("JdkPath", jdk);
            EditorPrefs.SetString("AndroidJdkRoot", jdk);

            Debug.Log("[项目准备] Android SDK  : " + sdk + (Directory.Exists(sdk) ? "" : "  ❌ 不存在"));
            Debug.Log("[项目准备] Android NDK  : " + ndk + (Directory.Exists(ndk) ? "" : "  ❌ 不存在"));
            Debug.Log("[项目准备] OpenJDK      : " + jdk + (Directory.Exists(jdk) ? "" : "  ❌ 不存在"));
        }

        /// <summary>
        /// 找到「直接包含 ndk-build.cmd」的 NDK 根目录。
        /// 先看自己，再看一层子目录（Unity 的 NDK 解压后通常是 NDK/android-ndk-r23b/）。
        /// </summary>
        private static string FindNdkRoot(string ndkDir)
        {
            if (File.Exists(Path.Combine(ndkDir, "ndk-build.cmd")))
            {
                return ndkDir;
            }

            if (Directory.Exists(ndkDir))
            {
                foreach (var sub in Directory.GetDirectories(ndkDir))
                {
                    if (File.Exists(Path.Combine(sub, "ndk-build.cmd")))
                    {
                        return sub;
                    }
                }
            }

            return ndkDir;
        }
        private static string EnvOr(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        /// <summary>
        /// 为 Android 启用 ARCore 的 XR Loader。
        ///
        /// 这一步对应 GUI 里的 Project Settings → XR Plug-in Management → Android → ARCore → 勾选。
        /// 不开的话打出来的包里没有 ARCore，App 能装上但不会有任何 AR 能力。
        /// </summary>
        public static void EnableArCoreLoader()
        {
            // 注意命名空间是 UnityEngine.XR.ARCore（虽然程序集名是 Unity.XR.ARCore）。
            const string loaderTypeName = "UnityEngine.XR.ARCore.ARCoreLoader";

            if (XRPackageMetadataStore.IsLoaderAssigned(loaderTypeName, BuildTargetGroup.Android))
            {
                Debug.Log("[项目准备] ARCore Loader 已启用，跳过");
                return;
            }

            var perTarget = GetOrCreatePerBuildTargetSettings();

            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            var managerSettings = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var ok = XRPackageMetadataStore.AssignLoader(managerSettings, loaderTypeName, BuildTargetGroup.Android);
            AssetDatabase.SaveAssets();

            Debug.Log("[项目准备] ARCore Loader 启用：" + (ok ? "成功" : "失败"));
        }

        /// <summary>
        /// 把 ARCore 的 Requirement 设为 Required。
        ///
        /// 对应 GUI 里的 XR Plug-in Management → ARCore → Requirement。
        /// Required 的含义是「只允许装在支持 ARCore 的设备上」，同时也决定了
        /// AndroidManifest 里 uses-feature 是否标记为 required。
        /// </summary>
        public static void SetArCoreRequirementRequired()
        {
            var settings = ARCoreSettings.GetOrCreateSettings();
            settings.requirement = ARCoreSettings.Requirement.Required;
            settings.depth = ARCoreSettings.Requirement.Optional;
            AssetDatabase.SaveAssets();
            Debug.Log("[项目准备] ARCore Requirement = Required");
        }

        /// <summary>
        /// 拿到（必要时创建）XRGeneralSettingsPerBuildTarget。
        ///
        /// 这个对象在 Unity GUI 里是「Project Settings → XR Plug-in Management」打开时自动创建的，
        /// 它的 GetOrCreate() 是 internal。这里用反射调用它，保证脚本化配置和 GUI 操作
        /// 走的是同一条路径、产生同样的资产。
        /// </summary>
        private static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTargetSettings()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (method != null)
            {
                return (XRGeneralSettingsPerBuildTarget)method.Invoke(null, null);
            }

            // 反射拿不到时的兜底：手动创建并注册为配置对象。
            var created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
            return created;
        }
        /// <summary>
        /// 打包 Android APK。
        ///
        /// 命令行：
        ///   Unity.exe -batchmode -quit -executeMethod UnityAR.EditorTools.ProjectSetup.BuildApk -projectPath &lt;工程路径&gt;
        /// </summary>
        public static void BuildApk()
        {
            // 打包前先把 Player Settings 补齐（图形 API / 明文 HTTP / ABI / 包名 等）。
            // 不调用的话，这些设置可能还停留在上一次 PrepareAll 的状态。
            ArProjectSettings.ConfigureAndroid();
            ConfigureAndroidToolPaths();

            // 必须先把活动构建平台切到 Android：
            //   1) ARCore 的预处理要读 Android 的 Gradle 配置，平台不对会直接抛
            //      "Cannot get path to the Gradle launcher unless the active build platform is Android"；
            //   2) 图形 API / 目标架构等设置也是按 BuildTarget 存的。
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[项目准备] 切换活动平台到 Android（首次会导入 Android 平台资源，需要几分钟）...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    throw new Exception("切换到 Android 构建平台失败");
                }
            }

            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "build");
            Directory.CreateDirectory(outDir);
            var apkPath = Path.Combine(outDir, "UnityAR-Inspect.apk");

            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0 || !scenes[0].enabled)
            {
                throw new Exception("Build Settings 里没有启用的场景，无法打包");
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenes[0].path },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            Debug.Log("[项目准备] 开始打包 APK → " + apkPath);
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log("[项目准备] 打包结果: " + summary.result +
                      "  大小=" + (summary.totalSize / (1024 * 1024)) + "MB" +
                      "  耗时=" + summary.totalTime +
                      "  错误=" + summary.totalErrors);

            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception("APK 打包失败: " + summary.result);
            }
        }
    }
}
