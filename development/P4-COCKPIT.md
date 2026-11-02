# P4：DCS 格式遥测 → MOZA Cockpit

## 范围

```text
PW → 采样/校验/派生量 → DCS 格式遥测 → Cockpit → 底座
```

bridge 只准备并发送遥测，不计算或下发底座力矩，不调用设备 SDK，不生成振动。力矩模型、强度和设备参数由 Cockpit 决定。

## 已准备

- `src/bridge/CockpitOutput.cs`：沿用 AC7 的 `127.0.0.1:3476` TCP 推送协议，仅监听本机。
- 发送周期：有效数据约 16 ms，失效/空闲约 50 ms；帧仍以分号分隔，不另造协议。
- 没有新有效帧超过 500 ms，或收到失效、断线、位置异常等状态，发送中性遥测；不持续发送旧运动数据。
- TCP 写入最多等待 500 ms，允许 Cockpit 断开重连；端口冲突明确报错，不终止其他程序或偷偷换端口。
- 受控关闭尽力发送一帧中性数据后断开；强杀时由 Cockpit 处理断流，不能把协议自检当作底座停止已验证。
- `payload/DCS.exe`：已发布 Windows x64 单文件、依赖 .NET 8 Runtime，进程名与 AC7 参考一致，供 Cockpit 自动识别。
- `PW Default` 预设：`presets/{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset`。

预设复制 AC7 Default，仅替换 `name`、`vehicles`、`uuid`；`device_params` 与 `telemetry_params` 逐项检查完全相同，不擅自调整力矩或效果设置。设备仍为 AB6，游戏仍为 dcs_world，不影响现有 AC7 的 ID/匹配。

## 已安装，用户确认功能接通

用户退出 Cockpit 后已确认进程停止，并完成安装：

- 新增 PW preset、1 条 PresetIndex 记录和 `Base.AB6.dcs_world["PW Default"]` 匹配。
- 索引从 34 项增至 35 项；剔除 PW 新增项后与原 JSON 逐项相同，全部原预设和匹配保留。
- 安装文件与项目 preset 字节相同，设备/遥测参数仍与 AC7 参考一致。
- 未修改输入绑定、游戏文件、存档或其他力矩参数；没有启动正式发送端。

备份和安装清单：`development/cockpit-backups/install-PW-20261102-104600/`。用户随后确认功能与杆力接通，当前重点转为 PW/AC7 力感差异；详见 [TELEMETRY-FORCE-NOTES.md](TELEMETRY-FORCE-NOTES.md)。

## 启动方式

从项目根目录执行：

```powershell
.\payload\DCS.exe
```

随后从 Steam 启动 PW。控制台出现 `[tcp] Cockpit client connected` 表示 TCP 客户端已连接；还要在 Cockpit 实测确认 PW 预设匹配和遥测解析。

注意：

- 不要同时运行 AC7 bridge、真实 DCS 或另一份占用 3476 的桥接；本项目不会自动关闭它们。
- 仍保留旧诊断入口：`dotnet src/bridge/bin/Release/net8.0/PWBridge.dll`，无 TCP 输出。
- 调试时可用 `dotnet .../PWBridge.dll --cockpit` 启用发送，但 dotnet/PWBridge 的进程名可能无法自动被 Cockpit 识别；正式使用 DCS.exe。
- 新版录制固定写到可执行程序目录下的 `captures/session-*/`；正式入口即 `payload/captures/`，与工作目录无关。两种模式均在启动时打印绝对目录，不逐帧刷终端。
- 新版同时记录原始输入与实际 TCP 写入的完整帧/关联/状态；结束用 Ctrl+C 并等待保存完成。详见 [实际发送帧采集](TELEMETRY-CAPTURE.md)。

## 遥测字段与近似值

- 已知运动字段：机体 G、世界速度（DCS 轴映射）、姿态、角速度以及派生攻角/侧滑。
- `tas` 和 `ias` 与参考一样都使用游戏速度作为代理，单位 m/s；没有真实空气密度、风或空速源，不宣称是真实 TAS/IAS。
- IAS 加入中性帧归零，避免失效后保留上一帧空速。
- 未验证的转速、武器、天气、加力、弹药等不伪造；因此这些效果目前不支持。实际 Cockpit 是否还需要其他字段，留待联调判断。
- 滤波和有限值/异常处理只是遥测清理，不是 bridge 在控制电机力矩。

## 验证结果

- Release 编译零警告/错误；PWBridge 与发布所用 DCS 程序集均通过自检。
- TCP 自检仅在随机本机端口运行，不启动 DCS.exe，也不连接正在运行的 Cockpit。
- 自检覆盖：初始中性帧、有效遥测、主动清空、500 ms 过期、断线重连、受控关闭最后中性帧、端口冲突；新增真实测试管道 → 原转换 → 随机 TCP → 后台记录联调，核对源输入关联、快照散列、重复发送和仅输入/未发送的区别，临时记录测试后清除。
- 真实录制回放保持 `ok=823`，新增 IAS 字段没有改变现有速度/G 计算结果。
- PW 预设仅 3 项元数据改变，设备与遥测参数均与 AC7 参考一致。

**用户已确认功能接通。仍需验证：** 输出方向、暂停/退出时实际设备行为，以及同条件下与 AC7 的力感/遥测差异。不能把“有杆力”当作物理估计已完全标定。

## 终端输出

DCS/Cockpit 模式对齐 AC7 的简洁风格，仅保留启动、管道/TCP 连接、断线/超时及错误。不再每秒打印遥测或跟随 `ok`/`stale_position` 等状态逐帧刷屏。默认 PWBridge 诊断模式继续显示详细状态；输入与实际发送帧写文件，开始打印记录路径，退出打印计数，记录丢失/故障会报警。不调整任何遥测数值、滤波或预设参数。

## 构建

发布前确认 DCS.exe 已退出并备份原 `payload/DCS.exe`。先发到临时目录验证，再替换正式程序；不要覆盖运行中的程序。本轮不自动启动真实发送端。

```powershell
dotnet build src/bridge/PWBridge.csproj -c Release
dotnet publish src/bridge/PWBridge.csproj -c Release -r win-x64 --self-contained false -p:AssemblyName=DCS -p:PublishSingleFile=true -o payload
```

发布程序名为 DCS 时无参数启用 TCP；诊断程序名 PWBridge 时保持旧的无参数诊断行为。`--self-test` / `--replay` 均不会启用正式端口。

## 回退

关闭本项目 DCS.exe 即停止发送。恢复预设库时先退出 Cockpit，只撤销本项目新增的 PW 项目，不动原有 AC7 配置；安装前将记录备份和清单。GPL-3.0 代码及许可证在仓库根目录，发布产物附带 LICENSE。
