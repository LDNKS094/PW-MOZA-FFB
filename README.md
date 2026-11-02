# PW MOZA FFB：起步说明

## 目标与当前结论

探索为 PC 版《Project Wingman》接入 MOZA 飞行底座的飞行力反馈与事件振动，参考本地 `../AC7-MOZA-FFB` 的实现。

**用户已确认 PW → bridge → Cockpit → 杆力链路接通。** bridge 只准备/估算并发送遥测，不计算或下发底座力矩。`payload/DCS.exe` 和 PW 独立预设已安装；预设参数沿用 AC7，当前仍需验证力感标定与停止行为。使用见 [P4 Cockpit 接入](development/P4-COCKPIT.md)，字段和强度差异说明见 [遥测与杆力](development/TELEMETRY-FORCE-NOTES.md)。

实施路线、阶段验收与持续进展统一维护在 [移植计划 PLAN.md](PLAN.md)。本页保留项目背景与静态线索；实施状态以计划文档为准。

优先验证最短链路：

```text
PW 玩家飞机状态 → 游戏内采集器 → 遥测桥接 → MOZA Cockpit → 底座杆力
```

不以游戏原生 FFB 支持为前提，也不要求先还原全部原生震动。

## 本地静态检查依据

检查日期：2026-11-02。

- 游戏目录：`F:/SteamLibrary/steamapps/common/Project Wingman`
- 主程序：`ProjectWingman/Binaries/Win64/ProjectWingman-Win64-Shipping.exe`
- Steam App ID：`895870`；本地 manifest 的 build ID：`16671873`。
- 主程序中发现 `++UE4+Release-4.27` 版本标记。
- 主程序中发现 `PlayerAircraft`、`ThrottleValue`、`Health` 等名称，以及 `GetVelocity`、`ClientPlayForceFeedback`、`ForceFeedbackEffect` 等引擎接口名称。
- 游戏包含 `SDL2.dll`，资源位于 `ProjectWingman/Content/Paks/pakchunk0-WindowsNoEditor.pak`。

这些仅说明存在可探索入口，不能证明字段归属、单位、运行时可读性，或游戏实际调用了震动接口。UE4SS 3.0.1 已通过一次加载、进任务和返回菜单；长期稳定性仍待验证。

## 参考实现与复用边界

参考项目：`../AC7-MOZA-FFB`。

| 位置 | 用途 | PW 适配判断 |
| --- | --- | --- |
| `mod/AC7Telemetry/Scripts/main.lua` | 游戏线程采样，经命名管道发送遥测 | 可参考结构；飞机类、字段、武器状态及单位必须重新确认 |
| `src/bridge/FlightTelemetry.cs` | 遥测转换为 Cockpit 使用的 DCS 格式 | 优先复用计算逻辑，验证坐标系与单位 |
| `src/bridge/Program.cs` | 游戏进程管理与 Cockpit 连接 | 当前查找 `Ace7Game`，需适配 PW 进程及生命周期 |
| `src/bridge/RumbleCarrier.cs`、`Vibration.cs` | 振动输出相关逻辑 | 核对协议与设备后评估复用 |
| `src/rumble/observer.cpp`、`preview.cpp` | 捕获并重建 AC7 原生震动 | 函数地址、对象布局是 AC7 专属，不能直接用于 PW |

现有 AC7 Lua 还依赖 `/Script/Nimbus.PlayerPlane` 等专属类型，不能直接复制运行。共享协议不代表所有字段都可在 PW 中获得，未验证字段不得伪装成真实遥测。

桥接的投影、滤波与字段映射改编自 GPL-3.0 的 AC7 项目，本项目代码按根目录 [LICENSE](LICENSE) 提供。UE4SS 等第三方组件遵守其各自条款；本仓库忽略下载包和用户数据备份。

## 第一阶段：仅证明杆力链路

1. 确认适用于本地 PW 版本的加载方式，验证 UE4SS 是否能启动并获取玩家 Controller / Pawn。
2. 只读采集速度、姿态及时间间隔；记录实际对象类型，核实速度来源、单位与坐标系。
3. 确认菜单、加载、暂停、死亡、换机、退出时如何识别“无有效飞行状态”。
4. 将必要数据接入现有遥测桥接，先检查日志及输出数值，再连接硬件。
5. 使用低力矩预设，确认速度变化和机动能产生方向正确、连续的杆力响应。

### 验收标准

- 能稳定区分玩家飞机与非飞行状态，不误读 AI 或菜单展示机。
- 遥测数值有限、单位明确，姿态跨越 ±180° 时不会产生异常尖峰。
- 暂停、失联、异常、退出时输出及时归零或回到已定义的安全状态。
- 重进任务后可恢复，无须沿用失效对象地址。
- 限力测试时无突发满力、持续残留或明显方向反转。

以上通过后，才认定最小可用架构成立。

## 后续：振动接入

按成本从低到高选择，不要求同时实现：

1. **事件合成**：找到开火、受伤、加力等真实状态，生成对应振动。按钮按下不能直接等同于实际开火。
2. **原生震动捕获**：确认 PW 是否实际使用 UE Force Feedback 或其他输出路径，再选择 Hook 点。
3. **效果精细还原**：仅在原生输出有价值且可稳定识别时，研究波形、强度、持续时间及事件分类。

引擎中存在 Force Feedback 接口名称不代表游戏使用它；手柄震动也不等同于飞行底座杆力。

## 安全与范围

- 开发时先使用日志或模拟输出；首次硬件验证限制力矩，确保可随时断开输出。
- 必须有断流超时、有限数值检查、异常限幅及安全复位。
- 加载器或配置修改前备份原文件，不直接覆写游戏 EXE 或资源包。
- 不复用 AC7 的函数地址或内存偏移，不在未验证布局上尝试 Hook。
- 游戏更新可能破坏反射、签名及字段布局，需要重新验证。

当前不做：完整逆向、按键扩展、安装器、分机型预设、云层/结冰等附加效果，以及与本次目标无关的重构。

## 实施进度

阶段状态、待确认项与下一步统一见 [PLAN.md](PLAN.md)，避免两份清单不同步。

## 官方与社区线索

- MOZA 游戏支持列表：<https://support.mozaracing.com/en/support/solutions/articles/70000679082-game-support>
- OpenFlight PW 说明：<https://github.com/EffortlessMetrics/OpenFlight/blob/main/docs/how-to/wingman-setup.md>

此前检索未找到经验证的 PW 专用 MOZA FFB 插件。此结论并非穷尽性检索，也不代表不能通过本地模组实现。
