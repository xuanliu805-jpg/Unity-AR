# Unity 工程说明

## 打开方式
1. 用 **Unity 2022.3 LTS**（实测 2022.3.62f3c1）打开本目录。
   首次打开时 Unity 会自动拉取 `Packages/manifest.json` 里声明的包（AR Foundation 5.1.5 等），
   需要联网并等待几分钟。
2. 打开后执行一次菜单 **Window → TextMeshPro → Import TMP Essential Resources**（只需一次），
   否则界面上的文字不会显示（TMP 的默认字体资源不在包里，需要单独导入）。
3. 执行菜单 **Tools → AR 巡检 → 生成巡检场景**，会在 `Assets/Scenes/ARInspect.unity` 生成完整场景。
4. 打开生成的场景，点 Play 即可在编辑器里跑（编辑器里没有真实 AR，会提示设备不支持）。
   真机运行请用 **File → Build Settings → Android → Build And Run**。

## 为什么场景是脚本生成的
本项目在编写时没有可用的 Unity 编辑器环境，无法生成并校验 `.unity` 序列化文件。
与其提交一个打开就报错的场景文件，不如把场景结构写成脚本：
打开工程点一下菜单就能得到必然可用的场景，而且结构在 `Assets/Editor/SceneBuilder.cs` 里一目了然。

## 真机运行前的额外配置
- **Project Settings → Player → Other Settings → Active Input Handling**：
  代码同时支持旧输入系统和新输入系统（`UnityAR.Input` 里的 `#if ENABLE_LEGACY_INPUT_MANAGER` /
  `#if ENABLE_INPUT_SYSTEM` 分支），设成 `Both` 最稳。
- **Project Settings → XR Plug-in Management → ARCore → Requirement = Required**（Android）。
- **Project Settings → XR Plug-in Management → ARKit → Enable**（iOS）。
- Android 最低 API 需要 24 以上（ARCore 要求），执行菜单 **Tools → AR 巡检 → 配置 Android AR 设置** 可自动设置。

## 后端地址
真机上的 Go 后端不能写 `localhost`，需要在应用里填开发机的局域网地址，例如
`http://192.168.1.20:8080`。填写入口：场景里的 `App` 物体 → `Backend Settings` 组件 → `Base Url`。
填过一次后会记在 PlayerPrefs 里，下次启动自动带上。

## 单元测试
- Unity 内：菜单 **Window → General → Test Runner → EditMode → Run All**。
  用例位于 `Assets/Tests/EditMode/Core/`，全部是不依赖 UnityEngine 的纯逻辑测试。
- 无 Unity 环境下（CI / 评审机上）：`dotnet test tools/UnityLogicTests/UnityLogicTests.csproj`。
  该工程以链接方式引用同一批 `.cs` 文件，保证「两边跑的永远是同一份代码」。
