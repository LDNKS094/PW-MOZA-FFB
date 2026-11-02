# P2：低频原始数据验证

本页保留低频验证经过；当前已升级高频采样和独立诊断管道，运行步骤以 [P3-DIAGNOSTICS.md](P3-DIAGNOSTICS.md) 为准。

## P1 首轮结果

保存的日志：`development/artifacts/p1-first-run/UE4SS.log`。

- 引擎识别为 4.27，`ClientRestart` post-hook 注册成功。
- 所有 12 次 Pawn 切换事件的 Controller 均报告 `local=true`。
- 顺序包含 LoadPawn → 过场 → 主菜单 → 任务选择 → 机库 → 飞行 → 主菜单；菜单和飞行状态可通过完整类名区分。
- 飞行 Pawn 类：`/Game/ProjectWingman/Blueprints/PlayerActor/Player/FlyingPawn.FlyingPawn_C`。
- 原日志时间 `23:52:22` 进入飞行，`23:56:41` 切回加载，`23:56:44` 回到主菜单；日志时间按原文记录，不推断时区。
- 没有 `PWProbe` 回调或注册失败。`UWorld::bKismetScriptError = 0x10D` 是偏移扫描输出，不是报错。
- 这是一次基本加载和生命周期验证，不覆盖死亡、重生、长时间稳定性或即时暂停识别。

## 当前部署

首轮已取得 105 条样本并恢复第二次任务采样，但发现 GetVelocity 全零和周期性位置跳变，详见 [P2-FIRST-RESULT.md](P2-FIRST-RESULT.md)。随后原点验证采得 70 条新样本，`position + origin` 已消除 6 次原点改变的尖峰；用户确认 HUD 单位可调且已改为 km/h。后续内部转为 m / m/s，与 HUD 设置分离。最新修订备份在 `development/deployments/p2-origin-2026-11-02T00-12-02-762Z/`。

沿用 UE4SS 3.0.1，只更新 `Mods/PWProbe/Scripts/main.lua`，没有改 EXE、加载器设置、用户输入或存档。

P1 脚本和日志备份，以及本轮哈希：

```text
development/deployments/p2-2026-11-01T23-59-57-311Z/
  manifest.json
  previous-main.lua
  P1-UE4SS.log
```

源码仍在 `mod/PWProbe/Scripts/main.lua`。

## 本轮采样行为

- 本地 `ClientRestart` 确认该飞行类后，延迟到游戏线程注册其 `ReceiveTick` hook。
- 依据官方 API，`/Game/` Blueprint 函数在第 2 个回调参数接收 post-hook，不照抄社区脚本的第 3 个参数写法。
- 只保留当前 Pawn 全名和会话序号等普通值，不跨切图保留 UObject 引用。tick 中同时核对全名与 `IsLocallyControlled()`，不采其他飞机。
- 每个墙钟秒最多记录 1 条原始样本；这只是诊断频率，不是最终遥测频率。
- 读取位置、世界原点、`GetVelocity()`、姿态、游戏时间、引擎真实时间、暂停值。GetVelocity 本轮已证实全零，只保留作诊断，不用于遥测。
- 尝试读取社区线索字段 `p_Current forward speed`、`g_Health`、`g_IsDestroyed`、`a_AfterburnerFloat`，标记为 `*_raw`；不可得显示 `unknown`，不填零冒充。
- 必需数值缺失或为 NaN/Infinity 时拒绝该样本并记录有限数量错误。没有转换单位、推算 G 值或发送外部遥测。
- 没有主动暂停检测：如果暂停会停止 Pawn tick，只有样本空档，不保证出现 `paused=true`。这项必须在后续采样生命周期/断流处理完善。
- 当前每进程注册一次飞行 tick；首轮第二次任务采样已恢复，更多切图/更新仍需回归，不能假定 Blueprint UFunction 在所有情况下仍有效。

## 当前短测试

已完成，不再要求重复：70 条样本已验证世界原点修正。下一步先实现高频速度重建与离线桥接，再安排必要的机动、暂停/死亡安全回归。完整结果见 `P2-FIRST-RESULT.md`。

## 已完成的首轮用户测试

1. 从 Steam 启动，进入任务或自由飞行，平飞约 10 秒。
2. 改变油门、加减速；记下或截图一个相对稳定时刻的 HUD 速度和单位，便于对照原始速度。
3. 分别抬头、低头、左/右滚转，观察是否有异常卡顿。
4. 暂停约 5 秒，再恢复飞行约 10 秒。
5. 返回菜单后再进一次任务，飞行约 10 秒，正常退出。

完成后读取 Win64 下 `UE4SS.log` 的 `[PWProbe] sample`：对比位移/时间与速度向量，确认实际轴向、速度来源、姿态方向、会话重置和重进任务采样。若没有样本，先排查 hook 和方法可用性，不把缺失解释成飞机静止。

**本轮仍无力反馈输出，不启动任何 DCS 桥接。** 如果失败，停止重试并保留当次日志；游戏退出后可用 `previous-main.lua` 恢复 P1 探针。完整加载器移除见 `P1-PROBE.md`。

## 自检

`development/test-probe.lua` 通过临时 Fengari 解释器运行，覆盖本地/远端和失效对象、Blueprint 回调参数位置、延迟任务跨会话失效、日志频率、未知字段、NaN 和有限错误输出。

模拟测试不覆盖 UE4SS 原生反射、UFunction 生命周期、线程安全或真实数据单位；P2 运行结果未验证前不接 Cockpit。
