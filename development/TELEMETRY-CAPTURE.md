# 实际发送帧采集（schema 1）

## 状态与边界

采集集成在现有 bridge 中，继续使用 `payload/DCS.exe`，没有另造测试 bridge。转换仍按每个新输入计算，TCP 仍发送最新缓存：有效约 16 ms、失效约 50 ms，500 ms 过期和写入期限不变。`FlightTelemetry.cs`、Lua 与力反馈预设不改。

记录使用有界队列与后台文件写入，不在遥测循环等待磁盘。仪表代码仍有少量时间/分配开销，不能宣称对调度零影响。队列满或写盘失败会明确标记记录不完整，不能拿缺失记录证明没有尖峰。

**已取得新版实际飞行的 TCP 发送记录；设备释放与最终记录完整性尚未验收。** 本轮有限检查未见值得继续追查的明显异常，按用户意愿暂停强度差异排查，不再要求重复测试；结果见 [TELEMETRY-FORCE-NOTES.md](TELEMETRY-FORCE-NOTES.md)。 自动联调使用独立管道和随机本机 TCP 端口，不连接真实 Cockpit，不发送设备命令。单文件与 DCS 程序集自检均通过，旧程序已备份、新 `payload/DCS.exe` 已替换并校验；清单在 `development/deployments/capture-20261102-053917-142/manifest.json`。当时另一个 AC7 DCS.exe 仍在运行，未动它，也未启动 PW 正式进程。

## 唯一正式入口与位置

```powershell
.\payload\DCS.exe
```

新记录固定在 **可执行程序目录**下，不再依赖启动时的工作目录：

```text
payload/captures/session-<UTC日期时间>-<随机后缀>/
  metadata.json       版本、时钟原点、编译时源码 SHA-256、程序/快照散列
  raw.jsonl           每条已读完整输入行、input_id、接收单调时间、管道连接号、解析结果
  frames.jsonl        每次 TCP 写入尝试的完整 DCS 帧、输入关联、开始/完成时间、状态
  events.jsonl        管道连接/断开/超时；游戏 PID、安装路径及 Lua 文件散列（可读时）
  capture.txt         原始行与断线 none，供现有 --replay 使用
  summary.json        正常退出并排空队列后生成：完整性、计数、丢失与错误
  provenance/        实际程序的 .bin 副本；可用的运行配置与磁盘上 PW preset 快照
```

每次启动打印 `[capture] session: <绝对目录>`。程序快照使用 `.bin` 后缀避免误启动；不要直接运行采集目录中的程序。磁盘 preset 快照不能证明 Cockpit 当时实际选中了它；Lua 散列是连接时磁盘文件散列，不是进程内脚本镜像校验。无法读取的可选信息记为空/错误，不改用户配置。

## 时间、关联与写入含义

- `raw.jsonl.received_s` 与 `frames.jsonl.write_started_s/write_completed_s` 使用同一个 bridge 单调时钟，单位秒。UTC 原点只是近似对齐，不用于求导。
- `frames.input_id` 关联 `raw.input_id`；源会话、序号、`sample_game_t/sample_real_t` 同时保存。不要把每次发送都当成新的采样：同一输入可能发送多次，也可能在发送前被更新输入覆盖。
- `active` 是该次发送快照的有效/新鲜状态，`reason` 区分正常、预热、过期、断线及最后中立帧。中立帧也照实录制，不能只分析 active 数据而忽略切换。
- `frame` 是交给 ASCII TCP 写入的完整字符串，不是事后重新计算的字段汇总。
- `write_status=succeeded`：`NetworkStream.WriteAsync` 已完成；**不证明 Cockpit 已解析，更不等于电机实测力矩**。
- `write_status=failed_or_partial`：写入异常/取消，可能有部分字节已发出，不能把整条帧记作成功或断言完全没有发送。
- 这不是逐字节网络抓包；超长/截断输入由原有有界读取拒绝，并以 I/O 断开事件记录，不当作有效完整输入。

旧的 `payload/development/artifacts/p3-*/capture.txt` 只有输入，按当前公式得到的 G 是重建，不能追认成旧版实际发包。游戏 `UE4SS.log` 的约 1 Hz 心跳同样不能替代短时起始峰值数据。

## 测试前后确认

先关闭 AC7/其他 DCS bridge（不能共用 3476），再启动 PW 这一份。确认启动窗口有以下三类事件，缺一不要做机动测试：

1. `[capture] session: ...`
2. `[pipe] game mod connected`（进入飞行后）
3. `[tcp] Cockpit client connected`，且 Cockpit 中确认 PW 预设/遥测接通

下一轮只需约一分钟：约 800 km/h、低坡度平飞 5 秒；轻拉约 1 秒后回中，等 5 秒；重复三次。不滚转，不故意大幅变速，不需要视频。拉杆幅度是人的描述，本版没有记录物理杆位，不能据此量化“5% 输入增益”或精确输入延迟。若反馈过大立即停止，不要硬顶。

结束后用 **Ctrl+C** 关闭 bridge，等 `[capture] saved: ...` 再关窗口。有效分析至少要求：

- `summary.json` 存在、`complete=true`、`dropped=0`、`faulted=false`；
- `valid_inputs>0`、`valid_sent>0`、`has_active_tcp_output=true`；
- 对应帧使用 `write_status=succeeded`；断线/失败帧单独看。

`complete` 仅表示记录器完整排空而未报告丢失，不表示做过完整飞行、每次网络写入都成功或设备安全已验收。`valid_sent=0` 会单独报警，避免把只有游戏输入的记录误当成实际发包。

随后按姿态/轨迹变化估计机动起点，检查最初 0.2～0.5 秒与持续段的 G、速度、方向变化及中立切换。没有物理杆位，所以这个起点不是精确的操纵杆起动时间；不先改强度或缩放。

## 自动验证与清理

`--self-test` 保留回归：转换/拒绝异常、命名管道读写、TCP 重连/端口冲突、500 ms 新鲜度、退出中立帧、实际输入到发送关联、重复发送、时钟/程序快照、输入但无 TCP 客户端的识别、拒绝覆盖既有记录。

测试录制与源码验证用临时发布目录在使用后删除；真实飞行、旧程序备份不删除。单文件验证使用临时 `PWBridge.exe --self-test`，不会启动正式 3476 端口，也不运行正式名称的 DCS.exe。
