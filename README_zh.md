# PW MOZA Flight Feedback

[English](README.md) | 中文

通过 MOZA Cockpit 和 AB6 底座，为 Project Wingman 提供飞行杆力与震动。

安装方式可选**脚本安装**或**手动安装**。

## 工作原理

- **飞行力反馈**：采集本机飞行遥测，转换为 DCS 格式，由 Cockpit App 计算杆力。
- **震动**：转接游戏原生震动，并补充本机机炮、受损和销毁事件。

桥接程序在游戏运行时加载 `payload/PWVibration.dll`，游戏 EXE 和资源文件保持原样。

## 安装

### 前置依赖

| 依赖 | 用途 |
| --- | --- |
| [UE4SS 3.0.1](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/v3.0.1)（x64） | 加载遥测脚本 |
| [.NET 8 Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)（Windows x64） | 运行桥接程序 |
| [MOZA Cockpit](https://www.mozaracing.com/downloads/) 与 AB6 | 计算杆力并驱动底座 |

依赖需自行安装配置。

安装后 PW 的标准目录结构应为：

```text
Project Wingman/ProjectWingman/Binaries/Win64/
  ProjectWingman-Win64-Shipping.exe
  dwmapi.dll
  UE4SS.dll
  Mods/
```

UE4SS 也可以位于 `Win64/ue4ss/`，脚本会识别这两种布局。

### 脚本安装

1. 运行一次 Cockpit，生成预设库后退出。
2. 退出 PW 和本项目桥接。
3. 运行 `Install.cmd`。

脚本自动定位 Steam 游戏目录，安装 PWTelemetry，合并模组列表，并将 PW Default 写入 Cockpit 预设库。重复安装会覆盖本项目脚本和预设。

需要指定游戏目录时：

```powershell
.\Install.cmd -GameDirectory 'D:\Games\Project Wingman'
```

### 脚本卸载

退出 PW、Cockpit 和桥接后运行 `Uninstall.cmd`。脚本移除 PWTelemetry、对应模组列表条目，以及本项目的 Cockpit 预设和匹配项。

```powershell
.\Uninstall.cmd -GameDirectory 'D:\Games\Project Wingman'
```

### 手动安装

1. 运行一次 Cockpit，以生成预设库，然后退出。
2. 将 `mod/PWTelemetry/` 复制到当前 UE4SS 的 `Mods/` 目录。
3. 在 `Mods/mods.txt` 中添加：

   ```text
   PWTelemetry : 1
   ```

4. 在 `Mods/mods.json` 的数组中添加以下对象，保留已有条目：

   ```json
   { "mod_name": "PWTelemetry", "mod_enabled": true }
   ```

5. 将 `presets/{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset` 复制到 %USERPROFILE%\Documents\MOZA Cockpit\PresetLibrary\。
6. 在 `PresetIndex.index` 的 `presets` 数组中添加以下记录，`file_path` 改为实际完整路径：

   ```json
   {
     "date": "1791976000",
     "device_type": "Base",
     "devices": ["AB6"],
     "file_path": "C:/Users/YOUR_USER_NAME/Documents/MOZA Cockpit/PresetLibrary/{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset",
     "games": ["dcs_world"],
     "name": "PW Default",
     "product_type": "AS23",
     "uuid": "{5a70e8ad-f59c-4b37-b598-2d8083691f5f}",
     "vehicles": "PW Default",
     "version": "1.0.1"
   }
   ```

7. 在 `OptimalMatch.vconfig` 的 `Base.AB6.dcs_world` 中添加：

   ```json
   "PW Default": "{5a70e8ad-f59c-4b37-b598-2d8083691f5f}"
   ```

### 手动卸载

按照手动安装说明反向操作。

仍被其他模组使用的公共依赖无需删除。

## 使用

1. 打开 Cockpit。
2. 运行 `DCS.exe`，Cockpit 使用 PW Default 匹配。
3. 启动 PW。

## 按键绑定

在 PW 的控制设置中绑定外设轴与按钮。Cockpit 预设负责杆力，游戏绑定决定按键动作。

## 已知限制

所有飞机共用一套默认预设，没有做分机型预设。

仅测试 AB6 + MHG + MTQ，未测试其他设备组合。

## 从源码构建

环境：.NET 8 SDK、x64 MSVC 和 CMake。

在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

编译产物：

```text
DCS.exe
payload/
  PWVibration.dll
```

`DCS.exe` 从 `payload/PWVibration.dll` 加载原生震动模块。构建中间文件和发布暂存集中在 `development/`。编译后即可按照常规安装方式使用。

## 许可证

本项目以 [GPL-3.0](LICENSE) 发布。随包的第三方组件保留各自的许可证。
