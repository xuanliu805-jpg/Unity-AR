<#
.SYNOPSIS
    在 Windows 上打包 Android APK。

.DESCRIPTION
    这个脚本解决一个 Unity + Android 的硬性限制：

    **Unity 工程的路径不能包含非 ASCII（中文）字符。**
    Android 的构建工具链（Gradle / aapt / NDK）处理中文路径会直接失败：

        UnityException: Invalid project path
        Project path 'E:\简历题目\unity' contains non-ASCII characters at position 3,
        Android Tools don't work properly with non-ASCII paths.

    所以脚本先把工程复制到一个纯 ASCII 的临时目录再打包，打完把 APK 拷回来。

.PARAMETER UnityPath
    Unity 编辑器可执行文件路径。默认取 E:\Unity\Editors 下的 2022.3.62f3c1。

.PARAMETER StageDir
    中转用的纯 ASCII 目录，默认 E:\UnityBuild\UnityAR。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build_android.ps1
#>
param(
    [string]$UnityPath = "E:\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe",
    [string]$StageDir = "E:\UnityBuild\UnityAR"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcProject = Join-Path $repoRoot "unity"

Write-Host "=== 1/4 检查环境 ===" -ForegroundColor Cyan
if (-not (Test-Path $UnityPath)) { throw "找不到 Unity 编辑器: $UnityPath" }
Write-Host "  Unity: $UnityPath"

# 纯 ASCII 检查（临时目录也要检查）
foreach ($p in @($StageDir, $repoRoot)) {
    if ($p -match '[^\x00-\x7F]') {
        Write-Warning "路径含非 ASCII 字符: $p"
    }
}

Write-Host "=== 2/4 复制工程到纯 ASCII 目录 ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
foreach ($dir in @("Assets", "Packages", "ProjectSettings")) {
    $target = Join-Path $StageDir $dir
    if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    Copy-Item -Path (Join-Path $srcProject $dir) -Destination $target -Recurse -Force
    Write-Host "  已复制 $dir"
}

Write-Host "=== 3/4 打包 ===" -ForegroundColor Cyan
$log = Join-Path $env:TEMP "unity-android-build.log"
& $UnityPath -batchmode -quit -nographics `
    -executeMethod UnityAR.EditorTools.ProjectSetup.BuildApk `
    -projectPath $StageDir -logFile $log
$code = $LASTEXITCODE

Get-Content $log -ErrorAction SilentlyContinue |
    Select-String -Pattern "项目准备|BuildFailed|Exception" |
    ForEach-Object { "  " + $_.Line }

if ($code -ne 0) {
    Write-Host "打包失败，完整日志: $log" -ForegroundColor Red
    exit $code
}

Write-Host "=== 4/4 把 APK 拷回工程目录 ===" -ForegroundColor Cyan
$apk = Get-ChildItem (Join-Path $StageDir "build") -Filter "*.apk" | Select-Object -First 1
if (-not $apk) { throw "没有找到 APK" }
$dest = Join-Path $repoRoot "build"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $apk.FullName (Join-Path $dest $apk.Name) -Force
Write-Host "  APK: $(Join-Path $dest $apk.Name)  ($([math]::Round($apk.Length/1MB,1)) MB)" -ForegroundColor Green

Write-Host ""
Write-Host "下一步：adb install -r -g `"$(Join-Path $dest $apk.Name)`"" -ForegroundColor Green
Write-Host "真机连电脑时先执行 adb reverse tcp:8080 tcp:8080，App 里后端地址填 http://localhost:8080"
