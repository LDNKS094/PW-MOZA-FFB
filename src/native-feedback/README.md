# PW 震动适配

用户已认可 DirectInput 震动基础版。本轮补充实际机炮发射、轻伤/重创和销毁冲击，尚待实机确认；MTQ 按用户决定搁置。完整计数与测试表见 [覆盖清单](../../development/VIBRATION-COVERAGE.md)。

## 效果与预期

- 全局增益 1.5，最终幅度限制在 DirectInput 0–10000。
- 原生四通道取最强幅度：大马达占优或非零平局为 70 ms 周期，小马达占优为 60 ms。保留游戏包络；通道没有效果 ID，不能据此确认每段事件身份。
- 机炮：直接捕获本机 `Cannon_GP_C`、`Cannon_Gau_C`、`CANNON_BASE_C` 的弹体生成与完成生成事件，产生 28 ms 周期、80 ms 衰减的短震，连续出弹时刷新。基础峰值 0.45。不读取弹药数量，不要求 Actor 指针变化；空扣扳机、NPC、归属不符或生成失败不触发。静态路径与隔离调用检查通过，真实游戏命中仍待本轮测试。
- 受损：单次损血不足前值 20% 为轻伤（35 ms 周期、200 ms 衰减）；至少 20% 为重创，沿用坠毁的 80 ms 周期和 800 ms 包络（保持峰值 200 ms，再用 600 ms 衰减），峰值强度仍随相对损血增加。不按伤害武器分类；首次采样、回血、断线及换任务不触发。
- 销毁：本机 g_IsDestroyed 或生命值耗尽发送一次独立事件，80 ms 周期，前 200 ms 满幅保持，再用 600 ms 衰减，总长 800 ms。上轮 350 ms 版本已实测输出满幅但体感太短，本轮按用户决定加长，覆盖坠海、撞地或被击毁。菜单、暂停和断线不作为销毁；重复事件不重触发，也不截断已确认的有限冲击。
- 原生、机炮、受损和销毁按当时最强幅度选择，不相加。自定义事件同样遵守游戏反馈开关、倍率、暂停、前台和平台门控。

## 单程序与本轮联合测试

正式入口只有 `payload/DCS.exe`，原来的独立启动器和 Enable/Stop 脚本已退役。

本轮打开 Cockpit，沿用已有 PW Default 预设，退出旧桥接与 PW，在项目根目录的 PowerShell 中运行：

```powershell
& .\payload\DCS.exe
```

确认 `[tcp] Cockpit client connected` 后正常启动 PW。`[vib] hook ready` 只代表挂钩就绪，设备是否打开请看随后打印的原生日志路径。无需另开震动程序。

一次飞行检查杆力随飞行变化，开炮/穿云/导弹时能叠加震动，震动结束后杆力仍正常；任选一次坠海或撞地检查约 0.8 秒的连续冲击。暂停、切窗及 Ctrl+C 应停止。发生缺失或共存问题保留该次日志即可，完整预期见覆盖清单。

日志首次命中打印 `gun_fire_confirmed type=...`，每两秒汇总 `gun_begin`（三条炮弹路径的生成调用）、`gun_local_begin`（本机归属及非空生成）、`gun_finish`（完成生成调用）、`gun_shots`（本机匹配成功）和 `gun_updates`（机炮赢得混合并请求更新输出）。`gun_gp/gun_gau/gun_base` 区分三种弹体。它们用于区分来源未命中、归属门控或输出未选中；不会逐发刷日志。

无参数即同时运行既有 Cockpit 杆力桥接与震动，本轮正式验收两路在 AB6 上的共存。仅震动模式 `--vibration-only` 仍保留，供定位共存问题时使用。遥测算法和用户预设没有调整。

## 连续性与停止

最多两个 DirectInput 执行轴，双轴 Cartesian 方向 [7000,7000]，单轴 [1]。普通幅度更新用 NoRestart；每约 50 ms 按当前波形相位续播，保留用户认可的 **150 ms 硬件到期保护**。

独立输出线程约每 10 ms 检查快照。无有效效果、暂停、切窗、平台禁止或来源超过 150 ms 均停止自身效果。父程序退出、停止命令或心跳超过 1 秒会清理输出。不调用 StopAll、全局增益或回中，不自动重连失败设备。DLL/跳板留到游戏退出；停止后重新启用需重启 PW。

## 构建

需要 Windows x64、VS 2022 C++、CMake、.NET 8：

```powershell
cmake -S src/native-feedback -B development/artifacts/pw-native-build/build -G "Visual Studio 17 2022" -A x64
cmake --build development/artifacts/pw-native-build/build --config Release
$feedbackDll = (Resolve-Path development/artifacts/pw-native-build/build/Release/PWNativeFeedback.dll).Path
dotnet publish src/bridge/PWBridge.csproj -c Release -r win-x64 --self-contained false -p:AssemblyName=DCS -p:PublishSingleFile=true "-p:NativeFeedbackDll=$feedbackDll" -o development/artifacts/integrated-feedback/stage
```

原生 DLL 嵌入 DCS.exe，启动时校验并提取到程序目录的 `runtime/<sha256>/`，无需独立部署 DLL 或开发目录。CMake 只构建原生模块；不再构建启动器。

仅支持 PW EXE SHA-256 `092e84225624a4de9c05d2404ff55269bd4a2aa2ff0548f2a183c6bf36abc85a`，托管端与 DLL 双重核对，另检查挂钩入口签名。仅匹配一个 `MOZA AB6 FFB Base` 和 controller 0。

MinHook 与 DirectInput 来源及许可见 [THIRD-PARTY.md](THIRD-PARTY.md)。AC7 参考副本只读，来源边界见 [对照记录](../../development/AC7-VIBRATION-ALIGNMENT.md)。
