using UnityEditor;
using UnityEngine;

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
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.example.unityar.inspect");
            PlayerSettings.companyName = "AR Inspection Demo";
            PlayerSettings.productName = "园区巡检 AR";

            // 允许明文 HTTP：Android 9+ 默认禁止 http:// 明文流量，
            // 真机直连开发机的 http://192.168.x.x:8080 必须打开这个开关，
            // 否则会报 "CLEARTEXT communication to ... not permitted"。
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            AssetDatabase.SaveAssets();
            Debug.Log("[AR 巡检] 已配置 Android：minSdk 24 / ARM64 / 包名 com.example.unityar.inspect");
            Debug.Log("[AR 巡检] 请手动完成：Project Settings → XR Plug-in Management → ARCore → Requirement = Required");
            Debug.Log("[AR 巡检] 真机访问 http 明文地址时，还需在 AndroidManifest 中允许 cleartext 流量（或把后端部署为 https）。");
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
    }
}
