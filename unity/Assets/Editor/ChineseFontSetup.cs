using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

namespace UnityAR.EditorTools
{
    /// <summary>
    /// 生成并启用**中文字体**资源。
    ///
    /// 为什么要这一步：TMP Essential Resources 自带的默认字体是 LiberationSans SDF，
    /// **只有拉丁字形、没有中文**。不换字体的话，App 界面上所有中文都会渲染成方框 □□。
    ///
    /// 这个坑只有装到真机上才看得出来 —— 编辑器里如果连 TMP 资源都没导入，
    /// 界面是全空白，反而看不出是「字体缺字形」还是「别的配置问题」。
    ///
    /// 这里用项目里的开源字体（LXGW 霞鹜文楷，SIL OFL 1.1 许可，可自由分发与商用）
    /// 生成一个**动态** TMP 字体资源：动态模式下字形图集在运行时按需生成，
    /// 只有真正用到的字才会进图集，既不撑大包体，也不会缺字。
    /// </summary>
    public static class ChineseFontSetup
    {
        public const string FontSource = "Assets/Fonts/LXGWWenKai-Regular.ttf";
        public const string FontAssetPath = "Assets/Fonts/LXGWWenKai SDF.asset";
        public const string ScenePath = "Assets/Scenes/ARInspect.unity";

        /// <summary>生成中文字体资源、设为 TMP 默认字体、并替换场景里已有的 TMP 文本。</summary>
        public static void Setup()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                fontAsset = CreateFontAsset();
            }

            // 设为 TMP 全局默认字体。
            // TMP_Settings.defaultFontAsset 是只读属性，底层存的是两个私有字段，
            // 所以这里用 SerializedObject 改字段本身：
            //   m_defaultFontAsset      —— 资源引用
            //   m_defaultFontAssetPath  —— 资源路径（重新打开工程时靠它加载）
            var settings = TMP_Settings.instance;
            var so = new SerializedObject(settings);
            var assetProp = so.FindProperty("m_defaultFontAsset");
            var pathProp = so.FindProperty("m_defaultFontAssetPath");
            if (assetProp != null) assetProp.objectReferenceValue = fontAsset;
            if (pathProp != null) pathProp.stringValue = FontAssetPath;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            // 场景里已经建好的 TMP 文本要显式换掉，否则它们还指着旧的拉丁字体。
            if (File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                var texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
                var changed = 0;
                foreach (var text in texts)
                {
                    if (text == null || !text.gameObject.scene.IsValid()) continue;
                    if (text.font == fontAsset) continue;
                    text.font = fontAsset;
                    changed++;
                }

                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                Debug.Log("[项目准备] 已更新 " + changed + " 个 TMP 文本的字体");
            }

            Debug.Log("[项目准备] 中文字体设置完成: " + fontAsset.name);
        }

        /// <summary>由 TTF 源文件生成动态 TMP 字体资源。</summary>
        private static TMP_FontAsset CreateFontAsset()
        {
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(FontSource);
            if (ttf == null)
            {
                throw new Exception("找不到中文字体源文件: " + FontSource);
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(ttf);
            fontAsset.name = "LXGWWenKai SDF";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // 图集贴图与材质必须作为子资源一起保存，否则重新打开工程会丢失引用。
            if (fontAsset.atlasTextures != null)
            {
                foreach (var texture in fontAsset.atlasTextures)
                {
                    if (texture == null) continue;
                    texture.name = fontAsset.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }
            }

            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[项目准备] 已生成中文字体资源: " + FontAssetPath);
            return fontAsset;
        }
    }
}
