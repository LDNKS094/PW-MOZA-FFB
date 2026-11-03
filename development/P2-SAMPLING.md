# P2：低频原始数据验证

本页保留低频验证经过；当前已升级高频采样和独立诊断管道，正式入口为 `payload/DCS.exe`，运行与记录步骤见 [P4-COCKPIT.md](P4-COCKPIT.md) 和 [TELEMETRY-CAPTURE.md](TELEMETRY-CAPTURE.md)。下节为回到主线后的生命周期补充，后续低频描述保留为历史。

## P2/P4 补充：失效原因日志（已部署，实机待验）

采集与力感排查已提交 `97a440b`，强度差异暂挂。继续原计划，先增强死亡/暂停/失控的可观测性，不直接跳到振动输出。

Lua 在状态变化时向 `UE4SS.log` 写一行：

```text
[PWProbe] state reason=destroyed session=... last_seq=... game_t=... real_t=... health_raw=... destroyed_raw=true
```

- 原因：`awaiting_tick`、`active`、`paused`、`destroyed`、`health_depleted`、`not_local`、`not_flight`、`controller_unknown`、`sample_failed`、`snapshot_failed`。
- 死亡分支保留实际读到的健康/销毁标志，以及该回调的游戏/真实时间和最后有效序号。尚未读到的值写 `unknown`，不拿上次值冒充本次观测。
- 相同状态不逐 tick 刷日志；不同会话与从失效恢复时重新记录。错误详情仍按既有次数限流。
- 协议维持原有 `none` 与 v1 数值帧，采样、G 计算、500 ms 超时、preset 和设备职责不变；没有添加新对象轮询、猜测 hook 或设备 API。
- 若暂停/死亡先停止 ReceiveTick，Lua 无法观察标志变化；仍由 bridge 超时进入中立，不能标成“已确认死亡原因”或精确死亡时间。

游戏退出后只替换已备份的 `main.lua`；SHA-256 与源码一致。旧 Lua 与上一轮真实日志保存在 `development/deployments/lifecycle-reasons-20261102-063653-926/`。Lua 模拟回归已覆盖状态/时间日志、重复失效不刷屏、健康耗尽/销毁、暂停恢复、丢失本机控制、异常限流与原有管道重连。未新增一次性测试源码或遗留合成日志；持续回归脚本保留。

**下一步仅一次生命周期检查，不再比杆力强弱：** 选可放弃的自由飞行/普通任务，避免征服模式或铁人存档。关闭其他 DCS 桥接，启动 `payload/DCS.exe` 与 PW；进入飞行 → 暂停约 3 秒 → 恢复 → 死亡一次 → 重试恢复 → 返回菜单。只观察失效后动态杆力是否消失、恢复后是否重新接通（预设自身回中力不一定消失），异常立即断开，勿硬顶。结束用 Ctrl+C 等待保存，不需要视频。

随后联合 UE4SS 状态日志、原始 none/超时与实际 TCP 中立帧确认可观测范围。实际硬件释放只能由用户反馈确认，软件时序不替代电机测量。

### 实机结果：基本生命周期通过

用户已按流程测试，报告没有感觉到明显问题。记录：`payload/captures/session-20261102-064151-053-9c42a61b/`；当次游戏日志已保存为同目录 `UE4SS.lifecycle.log`。连接记录中的 Lua SHA-256 与上述部署一致。

- session 10：三段中的前两次暂停均观察到 `paused → active`；序号 229/320 后的原始 none 均关联到实际成功发送的中立帧。
- 两次死亡确实读到 `g_IsDestroyed=true`，而健康仍为 100：session 10 / seq 590，game_t=103.6154、real_t=105.3197；session 12 / seq 1214，game_t=131.0444、real_t=133.8994。不是仅凭断流推测死亡，也说明不能只靠健康归零判死。
- 两次死亡后的原始 none，分别在约 15.27 ms / 0.02 ms 后开始写入对应中立帧；重试后 session 12、14 恢复有效输出。
- session 14 再次暂停后恢复，随后菜单切为 not_flight；最后 none 在约 41.03 ms 后开始写入对应中立帧。
- 六次暂停/死亡/菜单失效输入均按同一 `input_id` 找到成功发送的中立帧；运动字段核对为速度 0、法向 1 G、其余动态量 0。接收 none → 开始写中立帧约 0.02～41.03 ms，**不是死亡发生到电机释放的总延迟**。
- 检查时已读 1432 条输入、1434 条有效成功发送，未见记录中的 TCP 写失败。检查时仍没有 summary.json，因此不声称 dropped=0、完整排空或 Ctrl+C 最后一帧已验证。

P2 最小遥测的本轮生命周期证据补齐；P4 的暂停/死亡/重试/菜单基本联调通过，用户无明显异常，但强杀、精确电机释放延迟和完整受控退出仍未验收。不再为力感标定重复飞行；接下来按用户要求优先只读调查 P5 原生手柄震动请求链路，不先做事件合成，尚不启用振动输出。

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
