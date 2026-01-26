# Release 指南

本文档说明如何生成 Windows/Android 发布包（任务 6.2.x / REL-00x）。

## 目标产物

- Windows 发行版（`dotnet publish` 输出）
- Windows 安装包（Inno Setup，`.exe`）
-（可选）随包附带 `adb`（Google platform-tools）与驱动构建产物
- Android Release APK（签名 + R8/资源瘦身 + 多 ABI 输出）

## 前置条件

- Windows 10/11
- .NET 8 SDK
-（可选）Inno Setup（提供 `iscc.exe`）
-（可选）驱动构建环境：Visual Studio + WDK（用于生成 `.sys/.inf/.cat`）
- Android Studio（或 Android SDK + JDK 17）

## 一键构建（推荐）

```powershell
./scripts/release/windows/Build-WindowsRelease.ps1 -Configuration Release -RuntimeIdentifier win-x64
```

输出目录：`artifacts/windows/stage`（用于打包的 staging 目录）。

## 构建安装包（Inno Setup）

安装 Inno Setup 后执行：

```powershell
./scripts/release/windows/Build-WindowsRelease.ps1 -BuildInstaller -AppVersion 0.1.0
```

安装包默认输出：`installer/Output/ExpandScreen-Setup-<version>.exe`

## 包含 ADB（可选）

> 注意：是否允许分发 platform-tools 需自行确认许可/合规要求。

```powershell
./scripts/release/windows/Build-WindowsRelease.ps1 -IncludeAdb
```

将下载并复制到安装目录的 `adb/` 子目录（应用默认会在 `{BaseDirectory}\\adb\\adb.exe` 查找 ADB）。

## 包含驱动构建产物（可选）

在已构建/签名驱动后，把驱动输出目录（包含 `.sys/.inf/.cat` 等）传给脚本：

```powershell
./scripts/release/windows/Build-WindowsRelease.ps1 -DriverArtifactsDir "C:\\path\\to\\driver\\output"
```

安装后驱动文件位于 `{app}\\driver`。

## 代码签名（可选）

本仓库不内置证书与签名步骤。建议在发布流水线中对：

- 主程序（发布目录中的 `.exe/.dll`）
- 驱动（`.sys/.cat`）
- 安装包（`installer/Output/*.exe`）

使用 `signtool.exe` 或企业签名方案进行签名。

---

## Android 发布包（REL-002）

### 1) 配置签名（必需）

方式 A：本地 `keystore.properties`（推荐）

1. 生成 keystore（示例）：

```powershell
keytool -genkeypair -v -keystore expandscreen-release.jks -alias expandscreen -keyalg RSA -keysize 2048 -validity 10000
```

2. 在 `android-client/` 下创建 `keystore.properties`（参考 `android-client/keystore.properties.example`）：

```properties
storeFile=expandscreen-release.jks
storePassword=YOUR_STORE_PASSWORD
keyAlias=expandscreen
keyPassword=YOUR_KEY_PASSWORD
```

> 注意：`keystore.properties` 已加入忽略列表，不要提交到仓库。

方式 B：环境变量（适用于 CI）

- `ANDROID_KEYSTORE_FILE`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`

### 2) 一键构建（推荐）

```powershell
./scripts/release/android/Build-AndroidRelease.ps1
```

默认输出目录：`artifacts/android/apk/<versionName>-<versionCode>/`

### 3) 可选：包含 x86_64 输出

```powershell
./scripts/release/android/Build-AndroidRelease.ps1 -IncludeX86_64
```

### 4) 直接用 Gradle 构建（可选）

```powershell
pushd android-client
./gradlew.bat :app:assembleRelease --no-daemon
popd
```

如需包含 x86_64：`./gradlew.bat -PincludeX86_64=true :app:assembleRelease --no-daemon`
