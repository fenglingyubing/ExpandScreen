# Release 指南

本文档说明如何生成 Windows 发布包（任务 6.2.1 / REL-001）。

## 目标产物

- Windows 发行版（`dotnet publish` 输出）
- Windows 安装包（Inno Setup，`.exe`）
-（可选）随包附带 `adb`（Google platform-tools）与驱动构建产物

## 前置条件

- Windows 10/11
- .NET 8 SDK
-（可选）Inno Setup（提供 `iscc.exe`）
-（可选）驱动构建环境：Visual Studio + WDK（用于生成 `.sys/.inf/.cat`）

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

