# AC7 震动对照与当前实现

依据用户提供的 `reference/AC7-MOZA-FFB/` 只读副本；没有修改参考文件。源码与候选文档不能等同于 AC7 现场二进制。详细查证和参考散列保存在 `artifacts/ac7-vibration-comparison/`。

| 项目 | AC7 参考源码 | PW 当前实现 |
|---|---|---|
| 输出 | DirectInput GUID_Sine，最多双轴 | 同一输出类型，双轴 [7000,7000] |
| 原生周期 | 依分类采用 28–110 ms 等周期 | 最终大/小马达族 70/60 ms，通道族不等于事件 ID |
| 补充事件 | 分类曲线/事件 | 本机炮弹完成生成：28 ms；轻伤：35 ms；重创/销毁：80 ms |
| 包络 | 原生事件求值 | 原生四通道不重建；机炮/轻伤分别 80/200 ms 衰减；重创和销毁均保持峰值 200 ms 加 600 ms 衰减，为本项目设计 |
| 混合 | 最强事件 winner | 原生、机炮、受损、销毁最强 winner，不叠加幅度 |
| 增益 | RumbleSettings.ini 为 1.5 | 基础版用户认可后，按用户指定改为 1.5，满幅封顶 |
| 连续性 | NoRestart、无限效果 | NoRestart，约 50 ms 按当前相位续播 |
| 时限 | 无限效果 | 保留用户认可的 150 ms 硬件保护 |

用户已反馈 PW 基础版震动“好很多，可以接受”。第一版机炮实测无反馈，日志 `gun_shots=0`；原来的弹药加单一 Actor 字段判据已移除。现直接匹配 FireGun 三条炮弹的 deferred spawn/finish 调用点、本机 context/owner、同线程生成对象及成功返回。类 getter 已解析为 Cannon_GP_C、Cannon_Gau_C、CANNON_BASE_C，无需弹药变化或不同 Actor 地址。新版另有轻伤/重创和销毁独立事件，实际触发待测。销毁不进入 Cockpit 杆力计算，也不凭位置猜测坠海或撞地；损血不区分子弹/导弹。

统一程序 `payload/DCS.exe` 嵌入原生模块；无参数负责 Cockpit 和震动，`--vibration-only` 保留作定位模式。Cockpit 原有遥测算法和预设不改；用户已决定本轮联合验收两路共存。上轮机炮 CANNON_BASE 122 发已命中，销毁两次输出满幅约 328/344 ms；本轮按反馈加长到 800 ms。

本轮软件检查覆盖三类炮弹的真实调用 ABI、归属与完成匹配、轻伤/重创/销毁包络、终端事件单次发送与重连、IPC v3、嵌入模块和错误 EXE 拒绝；发布自检通过。结果位于 `artifacts/gun-fire-event/`，此前集成证据仍在 `artifacts/integrated-feedback/`。独立子代理精简净删 666 行，移除旧曲线实验、Lua P5追踪、退役观察模式与重复检查；保留来源和停止保护。临时验证程序用毕删除，保留结果。分类口径见 [覆盖清单](VIBRATION-COVERAGE.md)。

MTQ 没有发现可直接确认的适配接口，按用户最新决定搁置，没有调用通用 SDK 马达接口。

具体测试预期与构建方式见 [震动适配说明](../src/native-feedback/README.md)。
