# 启动指南（RUN）

> 一份「照着做就能跑起来」的说明。三个部分分别是：**后端**、**管理端网页**、**手机上的 AR App**。
> 前两个 1 分钟内能起来；第三个需要 Unity 编辑器 + 一台支持 ARCore 的安卓手机。

---

## 0. 一次性准备（这台机器已经做完了，换机器才需要）

| 工具 | 版本 | 本机位置 |
| --- | --- | --- |
| Go | 1.27.3 | `E:\tools\go\bin`（已在用户 PATH） |
| Node.js / npm | 24.14.1 / 11.11.0 | `D:\Node.js`（系统自带） |
| .NET SDK | 8.0.427 | `E:\tools\dotnet`（已在用户 PATH） |
| Unity 编辑器 | 2022.3.62f3c1 | `E:\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe` |
| Android SDK/NDK/JDK | 34 / r23b / 11 | `E:\Unity\Editors\...\AndroidPlayer\`（随编辑器装好） |
| adb | 随 SDK | `E:\Unity\Editors\...\AndroidPlayer\SDK\platform-tools\adb.exe` |

检查一下：

```powershell
go version
node -v
npm -v
dotnet --version
"E:\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe" -version
```

---

## 1. 启动 Go 后端（终端 A）

```powershell
cd E:\简历题目\server
go run ./cmd/server
```

看到这一行就成功了：

```
Unity-AR 巡检后端已启动: http://localhost:8080 (数据文件: data/issues.json)
```

**验证**（另开一个终端）：

```powershell
curl http://localhost:8080/api/health
# => {"issues":3,"service":"unity-ar-inspection","status":"ok","websocket":"/ws"}
```

常用参数：

```powershell
go run ./cmd/server -addr :9000              # 换端口
go run ./cmd/server -data ./db/issues.json    # 换数据文件
```

数据存在 `server\data\issues.json`。**想清空重来就删掉这个文件**。

> ⚠️ **Windows Defender 误报**：如果 `go build` 报
> `the file contains a virus or potentially unwanted software`，
> 那是 Go 在 Windows 上的经典误报（`Behavior:Win32/DefenseEvasion.A!ml`，写盘时就被拦，从未执行）。
> 解决办法（管理员 PowerShell）：
> ```powershell
> Set-MpPreference -ExclusionPath "E:\tools\gobuild"
> setx GOTMPDIR E:\tools\gobuild
> ```

---

## 2. 启动 React 管理端（终端 B）

```powershell
cd E:\简历题目\web
npm install          # 第一次跑要装依赖，之后不用
npm run dev
```

打开 <http://127.0.0.1:5173>（端口被占用会自动顺延，看终端提示的实际端口）。

管理端会自动连 `http://localhost:8080` 的后端。如果后端不在 8080：

```powershell
# 方式一：改 .env（记得复制 .env.example 为 .env）
copy .env.example .env

# 方式二：临时指定
$env:VITE_API_BASE_URL="http://localhost:9000"; npm run dev
```

**看到右上角「实时已连接」就说明 WebSocket 也通了**（不只是能看列表，是真能实时收到新问题）。

其他命令：

```powershell
npm run test:run      # 单元测试
npm run typecheck     # 类型检查
npm run build         # 生产构建
npm run mock:api      # 没有 Go 环境时，用本地 mock 后端
```

---

## 3. 手机上的 AR App（Unity）

### 3.1 打开工程

```powershell
"E:\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe" -projectPath "E:\简历题目\unity"
```

- 首次打开要联网拉包（AR Foundation 等），约 5–15 分钟；
- 如果 Unity 提示升级工程版本，点**取消**（工程就是按这个版本建的）；
- 打开后如果提示导入 TMP 资源：**Window → TextMeshPro → Import TMP Essential Resources**（只需一次）。

### 3.2 生成场景（一键）

菜单栏 **Tools → AR 巡检 → 生成巡检场景**

会生成 `Assets/Scenes/ARInspect.unity`（相机 + AR Session + 平面检测 + 射线 + 表单 + 全部组件连线）。

命令行等价写法：

```powershell
"E:\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics `
  -executeMethod UnityAR.EditorTools.ProjectSetup.PrepareAll -projectPath "E:\简历题目\unity"
```

### 3.3 配置后端地址（关键！）

真机上的 `localhost` 是手机自己，**连不到电脑**。有两种做法：

**① 数据线 + adb reverse（推荐，演示最稳）**

```powershell
adb reverse tcp:8080 tcp:8080
```

手机里填 `http://localhost:8080` 即可，不用管 IP 变化和防火墙。

**② 局域网 IP**

```powershell
ipconfig    # 找到 WLAN 的 IPv4，比如 192.168.124.10
```

场景里选 **App** 物体 → `Backend Settings` 组件 → `Base Url` 填 `http://192.168.124.10:8080`。

> ⚠️ 每次演示前记得查 IP，**DHCP 会变**。
> ⚠️ 防火墙：Windows 默认把「公用网络」的入站连接拦掉。已加过规则（`UnityAR Go Backend 8080`，profile=any）；
> 如果换了网络类型导致连不上，管理员 PowerShell 里执行：
> ```powershell
> netsh advfirewall firewall add rule name="UnityAR Go Backend 8080" dir=in action=allow protocol=TCP localport=8080 profile=any
> ```

### 3.4 打包安装到手机

菜单 **File → Build Settings → Android → Switch Platform → Build And Run**

命令行等价写法（会自动切到 ASCII 目录打包，见下面「已知坑」）：

```powershell
powershell -ExecutionPolicy Bypass -File E:\简历题目\scripts\build_android.ps1
```

装好之后：

```powershell
adb install -r -g E:\简历题目\release\UnityAR-Inspect.apk
```

> 💡 vivo 手机如果 `adb install` 报 `INSTALL_FAILED_ABORTED: User rejected permissions`，
> 说明「开发者选项 → USB 安装」没开。两个办法：
> - 手机 **设置 → 更多设置 → 开发者选项 → 打开「USB 安装」**；
> - 或者绕开 adb 安装（把 APK 传到手机里手动点装）：
>   ```powershell
>   adb push E:\简历题目\release\UnityAR-Inspect.apk /data/local/tmp/UnityAR-Inspect.apk
>   adb shell pm install -r -g /data/local/tmp/UnityAR-Inspect.apk
>   ```

### 3.5 手机上怎么用

1. 打开 App（图标名「园区巡检 AR」），**缓慢平移手机扫描地面**；
2. 顶部提示变成「检测到 N 个水平平面，点击地面放置标记」；
3. **点击地面** → 出现立方体标记，顶部显示「已放置标记 位置: (x, y, z)」，填写面板自动弹出；
4. 填标题（必填）/描述/优先级 → 点「**智能分诊**」看 AI 建议（可选）→ 点「**提交问题**」；
5. 底部提示「上报成功，问题编号 ISS-…」
6. 切到电脑的管理端网页 → 不用刷新，新问题**立刻出现**（WebSocket 推送）；
7. 改状态下拉框 → 后端真的变了；
8. 点右上角「**历史问题**」→ 可以查看/编辑/删除任何一条历史问题。

---

## 4. 一次跑通全部（端到端自动化）

```powershell
cd E:\简历题目
node scripts\e2e.mjs
```

它会自己：编译并启动 Go 后端 → 启动 Vite → 用无头 Chromium 跑完
「上报 → 列表看到 → 改状态 → 杀掉后端不白屏 → 重启后数据仍在」→ 断言 27 条。

---

## 5. 已知坑速查

| 现象 | 原因 | 解决 |
| --- | --- | --- |
| `go: command not found` | Go 没在 PATH | `E:\tools\go\bin` 加入用户 PATH（本机已加） |
| `go build` 报 "contains a virus" | Defender 误报 Go 产物 | 加排除项 `E:\tools\gobuild` + `setx GOTMPDIR E:\tools\gobuild` |
| `go: downloading ... proxy.golang.org` 超时 | 国内直连不通 | `setx GOPROXY https://goproxy.cn,direct` |
| 手机连不上后端 | 防火墙 / IP 变了 / 没开 USB 安装 | 见 3.3、3.4 |
| Unity 打包报 `Invalid project path` | **工程路径含中文** | 用 `scripts\build_android.ps1`（自动复制到纯 ASCII 目录再打包） |
| Unity 打包报 `Vulkan ... not supported by ARCore` | 2022.3 默认启用 Vulkan | 已由 `ArProjectSettings.ConfigureAndroid()` 设为 OpenGLES3 |
| 真机标记是品红色 | Standard shader 没打进包 | 已由 `ConfigureAlwaysIncludedShaders()` 加入 Always Included |
| Unity 界面中文是方框 | TMP 默认字体无中文字形 | 已内置 LXGW 霞鹜文楷动态字体资源 |
| `adb shell input tap` 点不动 App | 切到新输入系统后 adb 的 legacy 注入进不来 | 真机交互需**人工操作**（也是演示视频只能人工录的原因） |
| `git push` 失败 | 本机代理没开 | 把 Clash 之类的代理开起来（本机之前用的是 127.0.0.1:7897） |

---

## 6. 目录速查

```
E:\简历题目\
├── server\      Go 后端（go run ./cmd/server）
├── web\         React 管理端（npm run dev）
├── unity\       Unity AR 工程（Unity.exe -projectPath）
├── release\     装好的 APK（UnityAR-Inspect.apk）
├── build\       打包工作目录（可删）
├── scripts\     e2e.mjs（端到端）、build_android.ps1（打包）
├── tools\       UnityLogicTests（纯逻辑单测）、SyntaxCheck（语法检查）
└── docs\        PLAN / STEP_LOG / TEST_REPORT / API_CONTRACT / screenshots
```
