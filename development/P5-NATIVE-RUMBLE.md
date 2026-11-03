# P5：原生手柄震动链路调查

## 目标与当前状态

**当前路线以 [../PLAN.md](../PLAN.md) 为准：原生直用 → 必要接口适配 → 曲线重放。** 不默认需要捕获所有曲线、建立独立数据协议或新增振动后端。

已确认四个 UE 观察点可注册，原生 MissileLaunch 请求及其通道、五键曲线、时长和参数可读取；包内注册的同类资产仅此一项。**用户最新决策：此前计算/Cockpit 杆力方案与离线模型仅保留代码，当前先分别盘点事件震动和持续飞行杆力，再决定原生适配范围。** 完整实机已确认 AB6 能执行启动正弦、飞行请求未进入被观察的输出接口；已定位空转发槽，并继续追动态震动来源。2026-11-03 的完整证据及最新盘点见本文末尾。

AC7 参考在游戏内部捕获 Notify/Stop/Scale/Pause/Tick，读取其原生曲线和独立输入，经共享内存传给 NativeRawFlight/Vibration。PW 不复制 AC7 的 RVA、对象布局或效果编号。最终两路马达混合值可用于定位，但不冒充混合前的效果身份。

## 当前选择：先验证能否原生直用

- XInput 通常面向 Xbox/XInput 兼容手柄，ScePad 面向 PlayStation 手柄；SDL Haptic 面向通用触觉/力反馈设备，包括但不限于摇杆。三者是设备输出接口，不是三种效果，更不表示都能驱动 MOZA。
- 先查询现有设备的能力并原样观测游戏实际调用：记录设备/句柄归属、效果类型、原始参数和返回结果，必要时保留调用来源。只做能力查询和透传观察，不主动创建/播放测试效果、不伪造手柄、不擅自改绑定或震动开关。
- 若 PW 可直接正确驱动 MOZA，且自然结束/失效停止可靠、与 Cockpit 不冲突，则直接使用原生功能。无需为了复用已写的代码而额外加一层捕获/重放。
- 若已有原生输出只是设备或协议不兼容，优先做必要的接口适配；只有两条路线确实不可行并记录原因，才推进已有曲线模型与额外接收/输出链路。
- SDL Haptic 可能发送持续力、弹簧、阻尼或周期力等，不等于独立马达震动。若效果也写 AB6 力反馈，必须先明确游戏与 Cockpit 的所有权、效果管理与停止策略；不能让两个未经协调的输出叠加。
- 设备实际输出测试须先完成低力矩/限幅、立即停止和恢复准备；安排集中验收，不在调查中隐式发送效果。未知调用条件不等于必须换手柄，缺记录也不能直接判定没有原生震动。

下文记录已做的定位、采集和模型研究；其中旧的“下一步”是当时路线，不覆盖当前直用优先决策。曲线配置与时序分离的原则在需要适配/重放时仍成立，但不是新增系统的理由。

## 历史路线与证据：直接定位曲线集合，事件只负责播放时序

用户指出 AC7 开发时是直接找到目标曲线配置，而非逐个触发录制。核对参考 `preview.cpp`：配置按 object+0x60+type*32 索引，支持 46 个类型；Notify 读取被激活类型的配置。这证明曲线来源是已定位、可枚举的配置结构，不必以事件出现顺序发现全集；其具体偏移和数量仍不能搬到 PW。

当前 PW catalogueNativeEffect 仅在播放回调中读取资产，因此只是请求样本/字段验证，并非完整资源目录。后续以资源索引、配置引用和可验证的批量枚举定位效果集合，再与原生播放/更新/停止请求对接；不要求用户逐效果触发。仅枚举已加载对象也不能宣称全库，未触发资产不等于不支持。

已只读完成本机 Pak v11 的完整目录与 AssetRegistry 资产数组解析：25,661 个文件项、11,828 条资产记录，按类筛出的 ForceFeedbackEffect 只有 MissileLaunch 一项；直接读取其包内导出并与实际五键曲线核对，散列通过。不是只查文件名或已加载对象；也不能据此排除动态、原生代码内嵌或其他接口来源。完整范围、校验和引用候选见 [P5-RESOURCE-INVENTORY.md](P5-RESOURCE-INVENTORY.md)。不再要求用户逐种触发来建立目录，回到已定位资源集合的端到端接入。

以下逐次探测经过作为历史证据保留，不再作为逐个采曲线或默认建立重放链路的待办。当前优先级是上述原生直用判定与 PLAN.md。

## 静态证据（不是运行证明）

检查本机 PW build `16671873`，主 EXE SHA-256：

```text
092e84225624a4de9c05d2404ff55269bd4a2aa2ff0548f2a183c6bf36abc85a
```

只读解析 PE 普通/延迟导入、DLL 导出与字符串，未修改或运行这些二进制：

| 路径 | 实际静态证据 | 限制 |
| --- | --- | --- |
| XInput | EXE 普通导入 `XINPUT1_3.dll` ordinal 2、3；用本机 System32 DLL 导出核对分别为 **XInputGetState、XInputSetState** | 游戏不含 SetState 名称字符串是因为按序号导入；存在导入仍不能证明飞行中调用 |
| SDL | 延迟导入 `SDL_HapticNewEffect/UpdateEffect/RunEffect/StopEffect/StopAll/Pause/Unpause` 等 | 这是通用 haptic 接口，不一定是手柄双马达，也可能是其他设备的力反馈或未使用代码 |
| ScePad | 延迟导入 `scePadSetVibration`、`scePadSetVibrationMode` 等 | 与当前实际输入设备/Steam Input 路径有关，不能因为 DLL 存在就认定在用 |
| UE 上游 | 完整名称为 K2_ClientPlayForceFeedback、ClientPlayForceFeedback_Internal、ClientStopForceFeedback、PlayDynamicForceFeedback | 最初子串搜索误把 ClientPlayForceFeedback 当成准确名称，已修正；仍需运行验证，直接 C++ 调用可能绕过反射 |

XInput SetState 的本版 IAT RVA 为 `0x7b903d8`，仅作静态证据，没有写入 hook 代码，不能跨版本当固定地址使用。

SDL2.dll SHA-256：`5526bdb59fc7bced273ee285314f4259a354e1caf6ad59590f89c2b25d3458d2`；libScePad.dll：`a5717fbc64b687ee76031304f3461af15a14593aa89e1551d2ac00b99022cf76`。SDL 导出也含 Joystick/GameControllerRumble，但本次 EXE 的延迟导入使用上述 Haptic 系列，不能混为同一种证据。

一次性 PE 分析在工具沙箱中执行，带 PE64/边界断言；没有新增分析依赖、落地临时解析脚本或修改系统 DLL。

## 第一步观测：UE 原生 UFunction 请求

在现有 `mod/PWProbe/Scripts/main.lua` 中观测本版四个原生 UFunction；每个先用 StaticFindObject 检查存在、有效且全名为目标 Function，再注册 post-hook：

```text
/Script/Engine.PlayerController:K2_ClientPlayForceFeedback
/Script/Engine.PlayerController:ClientPlayForceFeedback_Internal
/Script/Engine.PlayerController:ClientStopForceFeedback
/Script/Engine.PlayerController:PlayDynamicForceFeedback
```

- 仅记录有效且 IsLocalController 为真的调用者。原始调用先执行，回调不返回覆盖值。
- 保存动作名、会话/最后采样序号、游戏/真实时间；最多八个参数。安全解包的原始标量直接记录，UObject 参数只记录有效性及全名；未知结构/类型标为 opaque，不猜结构布局或曲线偏移。
- 不从输入按钮、a_AfterburnerFloat 或 g_Health 构造震动，不改变请求的强度、时长、返回值或暂停行为。
- 每个函数每进程最多打印 64 条原始调用，随后输出一次限流提示；新的效果资产仍可继续进入通用目录（总计最多 64 个）。这是定位探针，不是无损波形记录；不能用限流后的缺失行断言没有新效果。
- 单个 hook 不可用或日志解包失败独立报错，不影响 ClientRestart、飞行遥测或其他观测点。不跨回调/异步线程保留 UObject。
- 日志在游戏 Win64 下的 `UE4SS.log`，前缀 `[PWProbe] native_rumble`。注册成功只代表观测点可安装，不代表游戏实际调用它。

部署时确认 PW 未运行，备份旧 Lua 和前一轮真实日志；只替换 main.lua，没有更换 DCS.exe、游戏 EXE、加载器设置、绑定、preset 或存档。清单：

```text
development/deployments/native-rumble-names-fix-20261102-091544-576/manifest.json
```

Lua 模拟回归已通过：只记录本机调用、原始参数与时间、回调无返回覆盖、不改变遥测/发送、未知参数不崩溃、日志限流、单 hook 不可用时遥测仍正常。模拟回归不证明 UE4SS 实机注册成功或已经找到实际震动来源。

API 依据：[UE4SS 3.0.1 RegisterHook](https://raw.githubusercontent.com/UE4SS-RE/RE-UE4SS/v3.0.1/docs/lua-api/global-functions/registerhook.md)：只 hook 已存在于内存的 UFunction，/Script 原生函数第 3 个回调为 post-hook。使用通用参数观测，不臆造不同引擎版本的固定参数布局。

## 首轮实测与名称修正

用户已完成不启动 DCS.exe、使用现有 MOZA 的测试。当次日志与安装 Lua 保存在 `development/artifacts/p5-native-first-run/`，末尾时间为 2026-11-02 09:07:18（按原日志，不推断为当地时区）。

- ClientStopForceFeedback、PlayDynamicForceFeedback 注册成功，未记录到调用；ClientPlayForceFeedback 入口注册失败，错误仅呈现 function 值。
- 遥测观察到 session 10、3601 个源序号及销毁/菜单状态，说明脚本确实加载并覆盖了飞行过程；但没有关键播放入口覆盖，不能据此证明没有原生震动或必须连接手柄。
- 完整二进制字符串确认本版准确名称是 K2_ClientPlayForceFeedback 与 ClientPlayForceFeedback_Internal。错误来自探针把子串命中当作完整 UFunction 名称，不是用户操作。
- 已改为两种实际播放入口，加上停止/动态入口；先检查目标是否为有效且准确的 UFunction，再挂钩。Lua 模拟回归新增不存在名称、缺函数、错误 UObject 类型及 internal 入口；均通过。游戏停止时备份部署并核对散列，尚未重新实机验证。

暂不要求用户换手柄或立刻重测；下一次自然运行先检查四个注册结果，再判断是否需要继续末端观测。即便四个全注册、没有调用，也不能排除设备/震动设置条件或绕过反射的原生调用。

## 修正后实测：确认原生导弹发射请求

用户使用现有 MOZA、未启动 DCS.exe 重新射击测试。日志与当次 Lua 已保存在 `development/artifacts/p5-native-missile-confirmed/`。

- 09:20:34：K2_ClientPlayForceFeedback、ClientPlayForceFeedback_Internal、ClientStopForceFeedback、PlayDynamicForceFeedback 四个观察点均注册成功。
- 09:21:47 / 48 / 51 / 53：ClientPlayForceFeedback_Internal 共收到四次本机原生请求，游戏时间分别 64.3934 / 65.5631 / 68.5628 / 70.2281 s，源序号 338 / 384 / 500 / 565。
- 原生参数 1 为 `ForceFeedbackEffect /Game/ProjectWingman/Blueprints/PlayerActor/MissileLaunch.MissileLaunch`；参数 2 为 `ScriptStruct /Script/Engine.ForceFeedbackParameters`，其具体字段尚未解码。
- 因此确实找到游戏自己的导弹发射效果入口，不是用按钮/遥测合成，也不需要先接实体手柄才能捕获此上游请求。
- 本轮其余三个入口未记录调用；不能据此宣称机炮、加力或停止不支持，也不能把自然结束当成显式 Stop 请求。

下一步围绕这个已确认原生效果读取原始通道、曲线、时长与播放参数，再实现相应更新/停止和安全门控；不临时造一个导弹振动脉冲，也不需要为定位入口再做重复射击测试。当前仅证明请求和资产身份，尚未证明最终马达值、实际曲线重放或设备输出。

## 通用原生效果目录（已部署，实机字段读取待验）

不按 MissileLaunch 名称写特例，所有捕获到的原生播放请求都以完整资产路径建立 ID。一次性记录的都是回调内复制出的原始标量/字符串，不跨回调保存 UObject/结构体/数组包装器。

日志行：

- `native_asset_begin`：资产 ID、完整路径、通道数。
- `native_key`：通道/关键帧序号、原始时间/幅度、插值模式、切线模式/权重模式、到达/离开切线及权重；使用 17 位数值格式，不缩放或重建波形。
- `native_channel`：四个原始马达标志，顺序 LL/LS/RL/RS；内联/外部曲线身份、可读的 DefaultValue/PreInfinityExtrap/PostInfinityExtrap。
- `native_asset_end`：关键帧数、最大键时间、可读的 Duration 属性。最大键时间单独标注，不冒充 GetDuration() 实测值；所有记录保持 `playback_ready=false`。
- `native_params`：Internal 请求中原始 Tag、bLooping、bIgnoreTimeDilation、bPlayWhilePaused，按 action/event_count 与 native_rumble 关联；其他入口暂保留原始参数，不猜参数顺序。
- `native_rumble` 同时携带 asset_id。目录与参数读取错误以 `state=unsupported` 记录，不影响遥测或原始请求。

使用 `TArray:GetArrayNum/ForEach`、`element:get()` 与反射结构体成员访问；外部曲线有效时优先读外部 FloatCurve，不触碰不生效的内联曲线。API 依据：[TArray](https://raw.githubusercontent.com/UE4SS-RE/RE-UE4SS/v3.0.1/docs/lua-api/classes/tarray.md)、[UScriptStruct](https://raw.githubusercontent.com/UE4SS-RE/RE-UE4SS/v3.0.1/docs/lua-api/classes/uscriptstruct.md)、[UE4.27 Force Feedback](https://dev.epicgames.com/documentation/en-us/unreal-engine/force-feedback?application_version=4.27)。公开字段定义不能代替本机运行验证；字段缺失必须明确报告。

每资产上限 8 通道、总计 256 键、64 KiB 文本，每进程 64 个资产；超限或必需字段缺失时不输出正常的完整 begin/end 数据块。可选默认值/外推/Duration 不可读时明确 unknown，不填零。因此即便出现 asset_end，也不代表所有播放语义已经可重放；解析还应检查 unknown 与 playback_ready 标志。按路径仅缓存首个快照，运行中同一路径曲线变更不追踪，接生产回放前须处理此限制。

Lua 模拟回归通过：通用不同资产、原始键/切线/马达标记、外部曲线优先且不读内联、原始播放标志、缓存重复效果、超过原始请求日志上限仍发现新资产、键数量超限、缺插值字段不补值，以及原有遥测/生命周期/观察点隔离。无新依赖、一次性验证源码或合成日志遗留，持续回归保留。

部署清单：`development/deployments/native-effect-catalogue-20261102-095834-723/manifest.json`，旧 Lua 与上一轮真实日志已备份。只替换 Lua，不改 DCS.exe、遥测协议/计算、preset、输入或存档，不自动执行原生效果或设备 API。

下一次正常使用游戏时可自动记录通用目录，不需要启动 DCS.exe，也不需要换实体手柄；若要立即确认本机布局，只需一次短射击触发已确认的原生请求后退出，分析这些行即可，不是再做力感或类型全覆盖测试。

## 通用目录实测：曲线与播放参数读取成功

用户已完成短射击。日志与当次 Lua 保存在 `development/artifacts/p5-native-curve-confirmed/`，原日志时间 10:09:29～10:11:20；四个观察点注册成功，没有 native_trace_failed 或 unsupported 记录。Internal 共四次请求，均为同一 MissileLaunch 资产；目录只导出一次，后续请求关联同一 ID，并各自记录参数。

实际原始数据：

| 键时间 s | 原始幅值 |
| --- | --- |
| 0 | 0.60000002384185791 |
| 0.18740135431289673 | 0.4577752947807312 |
| 0.2601979672908783 | 0.28115922212600708 |
| 0.28307506442070007 | 0.11016637086868286 |
| 0.28443944454193115 | -0.0035003721714019775 |

- 1 个内联通道，马达标志 LL/LS/RL/RS=`false,true,false,true`，即左右小马达。
- 所有键的 interp/tangent_mode/weight_mode 均为 0，切线与权重均为 0；当前只观测到这种曲线模式，不冒充三次/加权插值已实测。
- max_key_time 与实际 Duration 属性一致，均为 `0.28443944454193115` 秒；DefaultValue=`3.4028234663852886e+38`，pre/post=4。
- 每次参数均 Tag=`Firing`、bLooping=false、bIgnoreTimeDilation=false、bPlayWhilePaused=false。
- 保留负尾键和最大浮点默认值原样，不能把它们直接当作设备幅度；原生引擎的求值/边界/限幅语义需在回放实现中核对。原始峰值 0.6 不等于 MOZA 强度 60%。

这确认了通用读取路径可以获取实际原生内容，而不是按名称造波形。下一步可用这份已采集数据做离线求值、时钟、同 Tag 替换和自然结束验证，无需为该效果再次射击。其他资产、显式停止、循环和不同曲线模式仍需独立证据，设备输出仍关闭。

## 离线回放模型（已实现，未接设备）

`src/bridge/NativeRumble.cs` 是纯标量模型，当前只由 `SelfTest` 调用，没有网络接收、线程、设备 API 或 Cockpit 依赖。正式 `payload/DCS.exe` 未替换，仍为原来的 `EA270BC53328449BFDFFED02C814B5D90F3F90ED029EFD240A68AC4D8EFADB55`。

第一版仅接受已捕获的线性插值（interp=0、weight_mode=0）和常量前后外推（pre/post=4），复制资产数据，不持有外部可变数组。未知模式、空曲线、重复/逆序时间、非有限数据、零时长或超过已采集格式容量时拒绝，不拿近似曲线顶替。

已实现并断言：

- 真实 MissileLaunch 五键数值求值与中间点线性插值，负尾值按输出范围归零，不取绝对值；原始数据仍保留。
- 四路马达按通道标志分配，使用逐马达最大值并限幅到 [0,1]，不是叠加成更强幅值；无额外倍率或 AC7 类型编号。
- 非空/非 None 标签按大小写不敏感身份替换并重置时间；无标签效果并存。按资产与标签的 AND 条件停止，或全部停止。
- 根据 IgnoreTimeDilation 选择调用方提供的 real/game delta；暂停且不允许暂停播放时不输出、保留并冻结时间；显式 PlayWhilePaused 条件生效。
- 自然结束、循环、清空、非法时钟清状态，以及有界活动实例数量。

参考的是公开 UE 较早版源代码：[ForceFeedbackEffect.cpp（2019 版本）](https://raw.githubusercontent.com/folgerwang/UnrealEngine/release/Engine/Source/Runtime/Engine/Private/GameFramework/ForceFeedbackEffect.cpp) 与 [PlayerController.cpp](https://raw.githubusercontent.com/folgerwang/UnrealEngine/release/Engine/Source/Runtime/Engine/Private/PlayerController.cpp)。这不是本版 PW UE4.27 的逐位行为证明。

特别保留并测试该参考实现的边界：移除条件是 PlayTime > Duration，求值时间对 Duration 回绕，因此恰好等于 Duration 时仍可能取第一个键，再到下一步结束；没有擅自把它改成 >=。曲线内部使用 double 中间值避免有限 float 端点差溢出，状态/输出采用 float；也因此不宣称与 PW 的每一位浮点结果完全一致。调用方还需提供正确的引擎 delta，而不能把暂停时的游戏时间差冒充所有 tick delta。

`SelfTest.NativeRumbleTests()` 使用真实捕获的 5 个键作持续回归，同时覆盖替换/并存、停止条件、上下限、max 而非 sum、暂停/恢复、时钟、循环/结束、拒绝未知模式/零时长、复制后不受调用方数组修改影响。Release 零警告/错误，完整遥测/管道/TCP/日志自检也通过；合成录制在自检后删除，没有遗留一次性验证工具。

该模型目前仅作候选方案，不继续默认建设独立接收/重放链路。先确认原生直用与必要接口适配是否可行；确需重放时才补传输、新鲜度/生命周期门控及原生边界核对。离线自检不等于 PW 实机循环/暂停已验证，也不意味着机炮或全部类型已支持。

## 当前判定与清理

暂不要求额外试飞，先完成设备能力与游戏实际输出通路调查，不主动发送效果。

1. **直用成立：** 使用原生功能及最少配置，合并验证启停、重连与 Cockpit 共存；不新增振动 bridge 或重放内核调用。
2. **仅接口不兼容：** 有证据后选择最小适配，保持游戏已生成的原始输出语义；适配所需校准与停止行为不能省。
3. **前两种不可行：** 明确阻塞原因，再评估现有原生曲线和事件驱动重放。原生仍无可靠来源时，只有与用户确认后才考虑事件合成。
4. **观测无调用：** 先检查观测点、设备/设置条件、Steam Input 或反射层被绕过的情况；不能直接判游戏不支持或要求准备三种设备。
5. 用完的临时 Hook、参数/曲线日志和专用验证代码按 AGENTS.md 及时清理；保留必要回归、真实证据与备份。调查阶段不修改正式输出策略，不让新增输出与游戏/Cockpit 并行争用设备。

## 2026-11-03：内置系统失效原因与原生调用采集

用户授权自行推进，仅在确需实机操作时通知。主线是查清内置系统：启动自检已能驱动外设；若游戏原生满足需求，不保留不必要的模组，不因为已有离线内核就先接重放。

### 已核对的静态证据

- Ghidra 12.2/JDK 25 定向分析，EXE 散列仍为本文记录的本机版本；没有修改游戏文件或平台判断。
- 摇杆打开函数 RVA `0x4f987e0` 调用 SDL 创建 type=2、时长 5000 ms、周期 1000 ms、幅度 30000 的固定正弦，运行一次，等待后销毁，与用户启动实测吻合。
- JoystickPlugin 中的 `FeedbackData`/`EFeedbackType` 可表示恒力、弹簧、阻尼、惯性、摩擦、正弦、三角、锯齿和斜坡。类型集合不是游戏实际效果目录；创建实现 `0x4f98ef0`、更新实现 `0x4fa0a60` 的直接 E8/E9 引用仅找到各自 UE exec 包装，不能据此排除间接调用。
- 从 FlyingPawn_C 构造和虚表定位到 HapticFlyBy 的实际入口 `0x271c4f0`；补查共享状态跳转表后，确认先比较平台名与 `ps5`，Windows 返回假后退出。StopHaptics 同样受平台条件约束；后续调用已交叉核实是 PlayerController PlayHapticEffect/StopHapticEffect，尚未接通至 SDL 摇杆效果。
- 扩大 Pak 检查到全部 **10,014 个 .uasset 头、15,433,754 字节**：全数未压缩/未加密，逐项核对存储长度、压缩标志和包头 magic，无跳过/无无效包；FeedbackFunctions、CreateEffect、UpdateEffect、FeedbackData、JoystickPlugin 名称均零命中。HapticFeedbackEffect 仅命中 `HF_FlyBy`，ForceFeedbackEffect 仅命中 `MissileLaunch`。这是名称引用范围，不是全部动态来源证明；本轮未逐项重算条目散列。

完整地址证据和本地 Ghidra 工程见忽略目录 `development/artifacts/pw-sdl-ghidra/`；新摘要为 `all-package-feedback-references.json`，内置诊断为 `BUILTIN-DIAGNOSIS.md`。这些本地证据不包含在常规 Git 提交中。

### 待实机的最小采集

使用隔离安装的 Frida 17.25.1，以独立工具附加到一个 `ProjectWingman-Win64-Shipping.exe`，不替换游戏 DLL、不增加游戏目录模组、不调用设备输出 API、不改动原调用参数或返回值。Frida 在目标进程内安装观察点，不属于完全无侵入的外部读取。

- 工具：`development/artifacts/pw-native-trace/observe.py` 与 `observe.js`；仅调查期间保留，完成后清理。
- 捕获 SDL 自然发生的设备打开/名称/instance ID、haptic 句柄、效果创建/更新/播放/停止、暂停恢复、增益/回中、自然调用的 GetError；效果保留 72 字节原始参数及有限字段解码。还观察 XInputSetState 和 ScePad 振动接口。
- SDL 简单 rumble 的浮点强度寄存器未解码，明确记为 unknown；其他原始效果结构保留。不从 GetError 以外的全局最后错误推断某次调用原因。
- 模块观察覆盖已加载及随后加载的 DLL；特别处理 SDL 首次 API 初始化替换动态派发表，避免只挂在随后失效的默认入口。实现依据为 [Frida 官方 API](https://frida.re/docs/javascript-api/) 和 [SDL 官方动态派发源码](https://github.com/libsdl-org/SDL/blob/SDL2/src/dynapi/SDL_dynapi.c)，并对本机 DLL 做隔离验证。
- 记录调用来源模块/RVA、输入和返回值、时间、模块挂钩结果、心跳计数和丢弃数。没有观察点或存在丢弃/脚本错误时，不能拿空日志当无调用证据。
- 自动按会话保存 `events.jsonl`/`status.json`；退出后保存实际 EXE 散列，若本轮 UE4SS 日志有更新则一并保存，用于核对玩家飞机确实活动。进程退出可能丢失末尾不足 100 ms 的缓冲，标记 `tail_flush_confirmed=false`，不据此保证最后一次停止已捕获。

隔离验证使用本机 SDL DLL，只在测试进程调用 GetError 并检查返回值；另在 Frida 分配的测试内存解码正弦结构。附加前/后加载两种场景均捕获 3 次原调用、返回值未变、无脚本/挂钩错误、丢弃为 0、正弦字段符合预期；没有初始化设备或播放效果。验证摘要保留为 `verification.json`，专用验证源和测试会话用后清除。

实机一轮集中操作：关闭 Cockpit 和 bridge；正常启动 PW，进入任务飞行约 1–2 分钟，包含操纵、短点射、导弹和加力，再暂停恢复、返回菜单并正常退出。采集器自动等待/附加，游戏原有启动 5 秒运动保持原样。此次只观察内置系统，不启用新输出。

### 首轮实机取证与采集生命周期修正

用户已完成实际飞行，反馈仍为“启动运动、飞行无反馈”。UE4SS 日志确认 125 条约 1 Hz 的飞行样本、暂停/恢复和 8 次 MissileLaunch 的 ClientPlayForceFeedback_Internal 请求，因此游戏侧导弹效果触发已成立。

原生采集器却仅记录到更早的进程 PID 222312：UTC 21:41:19–21:41:29，约 10 秒，没有进入飞行。这个进程识别到的 SDL 摇杆是 MOZA MTQ Throttle Panel，JoystickIsHaptic=0；XInput 的零值探测返回 1167。随后实际游戏从 UTC 21:42:56 启动，实际飞行在 UTC 21:44:32–21:47:23，已经超出原采集器生命周期。不能把前一个进程的无效果调用用于判断后一个进程的飞行，也不能把油门设备的 haptic=0 解释为 MOZA 底座不支持。

此前单进程采集器在第一次退出后就结束，是本轮漏掉末端调用的原因。已增加 `--watch` 持续跟踪模式：每个游戏进程独立保存会话，退出后继续等待下一次启动；根目录 `stop-watcher` 标记或 Ctrl+C 才停止整个采集器。`last-finished.json` 指向最近完成的进程会话，避免把随后空的等待会话当作采集结果；结束时将本次 UE4SS 飞行样本数量写入元数据。

新增隔离验证连续启动、退出两个真实测试进程，两次均成功附加、捕获 3 次 GetError 原调用且返回未变，第一次退出后采集器仍存活并捕获第二次。验证结果保存在 `watch-verification.json`，专用验证源及测试会话用后删除。

实际飞行的原始 UE4SS 日志与摘要已保存于本地 `development/artifacts/pw-native-trace/missed-flight-evidence/`；本轮只需补采末端：保持 Cockpit/bridge 关闭，正常启动，进入任务发射一次导弹并飞行约 20–30 秒后正常退出。不要求重复先前完整流程；仍不主动发送效果或改动内置输出。

### 第二轮完整实机：设备可执行，常规反馈转发缺失

会话 `session-20261102T215454491400Z`，PID 135592，EXE 散列与静态分析一致；附加 UTC 21:55:35，正常退出 UTC 21:58:17。启动自检、实际飞行、导弹请求和退出均在同一原生采集会话内。

| 项目 | 结果 |
| --- | --- |
| MOZA MTQ Throttle Panel | JoystickIsHaptic=0；这是油门，不作为底座能力结论 |
| MOZA AB6 FFB Base | JoystickIsHaptic=1，HapticOpenFromJoystick 返回有效句柄，RumbleInit=0 |
| 启动正弦 | type=2、5000 ms、period=1000 ms、magnitude=30000；NewEffect 返回 ID 1，RunEffect=0，约 5 秒后 DestroyEffect |
| 实际飞行 | UTC 21:56:51–21:58:03，66 条 1 Hz 日志样本，4 次 MissileLaunch 的 ClientPlayForceFeedback_Internal 请求 |
| 飞行中的末端输出 | 被观察的 SDL 创建/更新/运行/rumble、XInputSetState、scePadSetVibration 均无调用；并非调用后设备返回失败 |
| 其他输出记录 | XInput 只有启动零值探测，两个 DLL 日志各 4 条，均返回 1167；ScePad 只有启动 SetVibrationMode，未见 SetVibration |
| 覆盖检查 | 80 次心跳，最大间隔 2003 ms，丢弃 0，无挂钩/脚本/参数读取错误；启动正弦作为正向对照已捕获 |

进程退出时尾部不足 100 ms 的缓冲未确认刷盘，不能用本轮保证最终停止时序；这个边界不影响此前完整覆盖的飞行与导弹区间。会话目录保留 `events.jsonl`、`UE4SS.log`、`status.json` 和程序生成的 `analysis.json`。采集器已停止，不继续后台等待。

进一步定向分析发现：摇杆输入对象构造函数 RVA `0x4f97900` 安装主虚表 `0x8200728`，输入事件函数位于槽 2、RVA `0x4fa00a0`，内部处理 SDL_PollEvent。对照 [UE 输入设备接口前缀](https://raw.githubusercontent.com/folgerwang/UnrealEngine/release/Engine/Source/Runtime/InputDevice/Public/IInputDevice.h)，常规 SetChannelValue/SetChannelValues 对应的槽 5/6（RVA `0x8200750`/`0x8200758`）均指向 `0x8a7d00`，机器码 `C2 00 00`，即直接返回的空函数。这是代码层面的转发缺口，解释了为何独立启动自检可运行，常规游戏反馈却没有被接到 SDL。公开接口源为 2019 年参考，不作为整套 UE4.27 ABI 证明；实施时还须核对实际调用和非零混合值。

当前接入方向是必要接口适配：优先取引擎已经计算好的原生四通道值及其归零/生命周期，补到已验证可用的设备路径；先验证实际调用点和设备映射，再验证周期/幅度、独立停止与 Cockpit 共存。不能全局挂共享空函数，也不把最终混合值冒充效果身份，不把启动测试参数直接作为生产振动参数。

本轮尚未发现 PC 内置持续飞行杆力模型，因此已有 Cockpit 杆力仍保留；震动不再默认要求枚举完整曲线或新增整套重放。暂不需要用户继续试飞。完整地址、方法边界及适配方向保存在本地忽略目录 `development/artifacts/pw-sdl-ghidra/BUILTIN-DIAGNOSIS.md`，虚表与函数体为 `joystick-device-contract.txt`。临时 Ghidra 验证源用后清除。

## 最新决策：旧计算只保留代码，先盘点两类原生效果

用户明确区分：游戏事件震动，与速度/姿态等飞行状态产生的杆力。上节“保留 Cockpit 杆力”的当时安排由本节覆盖：旧方案的 `src/bridge/` 计算/输出源码和已有证据保留，当前不构建、运行、部署或调参，不混入原生效果验收。检查时没有旧桥接进程，活动 payload 没有 DCS.exe；无需为了停用方案而删除用户预设或整个采集目录。

### 原生来源清单（当前证据范围）

| 类别 | 来源 | 当前能确认的内容 | 尚需确认 |
| --- | --- | --- | --- |
| 事件震动 | MissileLaunch | 当前 Pak 唯一注册 ForceFeedbackEffect，原始五键曲线、左右小马达通道、约 0.28444 秒；PC 实机已捕获播放请求 | 引擎最终混合值与设备适配、停止边界 |
| 事件 Haptic | HF_FlyBy / HapticFlyBy | Pak 中有 HapticFeedbackEffect 资产；玩家函数经 PlayHapticEffect 播放，入口有 ps5 平台条件，Windows 跳过 | 其他平台实际触发、频率/幅度原始数据和通道语义；不默认解除平台条件 |
| 动态震动 | 原生全四马达调用点 | 游戏代码 RVA 0x26bbd34 直接调用 PlayDynamicForceFeedback，四个马达标志均为真，duration=-1，动作由 Temp_byte_Variable 控制 | 强度来源、触发条件、Windows 实际命中；不能用 Lua UFunction 未命中判定无调用 |
| 动态震动候选 | 与 StopVibrationThrust 关联的状态机 | RVA 0x26efa6e 调用 PlayDynamicForceFeedback，静态 intensity≈0.3、duration=-1，仅左右小马达；有 Start/Update/Stop 状态 | 启动/更新条件、是否与加力/油门等关联、平台/输入条件和实机命中 |
| 持续飞行杆力 | 速度/姿态/操纵相关原生力生成 | 当前尚未定位能生成方向性持续力、回中/弹簧/阻尼参数并实际输出的游戏路径 | 继续查原生生产者与状态关联；有飞行遥测或动态马达震动均不足以证明此类杆力 |
| 初始化测试 | SDL 5 秒正弦 | AB6 创建和播放成功，随后销毁，已经实机核对 | 它不是飞行杆力模型或战斗震动来源，不作为实际效果覆盖项 |

FeedbackData 的恒力、弹簧、阻尼等十种类型是通用插件能力，尚未发现游戏资产引用其创建/更新接口，也未在已测飞行中观察到实际 SDL 效果生产。类型枚举不作为原生效果清单。即使动态震动强度以后证实受速度/姿态影响，它仍是马达震动；需要另有方向/轴/回中等真实力输出证据，才归为持续飞行杆力。

### 新的静态追踪

- 原生注册包装确认引擎 PlayDynamicForceFeedback 实现 RVA 0x6f7d300、SetHapticsByValue 实现 0x6f82110。当前 EXE 的 E8/E9 扫描除引擎包装外，找到两处游戏动态震动调用；没有把间接调用排除在外。
- StopVibrationThrust 的 UFunction exec 在 0x4de5490，虚调用槽 +0x878，对应玩家虚表实际入口 0x2729570；它设置状态 0xa86 并进入共享实现 0x26ede30，后续可进入停止动作分支。
- 对引擎动态震动实现应用已由注册包装核对的 Windows x64 参数类型后，才读取浮点参数；旧的无类型伪 C 参数不作为强度证据。0x26efa6e 的静态强度常量来自 float 0.30000001192092896，duration 初始化为 -1，掩码为 false/true/false/true；动作字段 +0x2335 的反射名为 Temp_byte_Variable_3，存在 0、1、2 三种赋值。
- 全四马达调用 0x26bbd34 已从 unwind 起点线性解码到准确 CALL 指令，R9 和三个栈马达标志均为 1，XMM2 装入 -1；XMM1 强度来自 XMM6，来源仍待核对。其动作字段 +0xfe4 的反射名为 Temp_byte_Variable。
- 共享状态机还涉及 g_IsDestroyed、ScreenShake 等条件；没有据此让用户更改设置，也不把这些局部条件当作完整的启动路径。

本地证据为 `development/artifacts/pw-sdl-ghidra/native-source-api-1.txt` 至 `native-source-api-3.txt`、`native-source-calls.json`。源代码分析没有修改游戏文件或激活旧计算。

### 原生源头观察准备

此前 Lua 的 UFunction 观察可能漏掉 nativized C++ 直接调用。独立采集器增加可选 `--native-sources`：观察上述原生引擎入口，以及 HapticFlyBy、StopVibrationThrust。记录调用来源、对象/资产指针、四马达掩码、动作和普通复制的 latent 信息，关联原始 SDL/XInput/ScePad 输出与飞行日志。

原生入口仅对已核对 EXE 的完整 SHA-256 启用，且安装前逐项核对 8 字节入口签名；不相符则不安装。没有全局挂共享 RET 空函数，没有替换原调用或生成设备效果。浮点寄存器参数当前明确记录 unknown；静态 0.3/-1 单独标为 static_expected，不冒充运行时采样，也不宣称这些记录可直接重放。

隔离验证覆盖 Windows x64 寄存器与栈上的马达标志、动作、latent 指针，读取结果正确；私有测试函数的浮点参数/返回值在挂钩前、期间、卸载后均保持 3.75，卸载恢复原始字节，错误进程身份被拒绝。没有调用设备输出 API。验证摘要保存于本地 `source-verification.json`，验证源和会话用后删除。

当前继续做来源盘点；源头和末端都核对清楚后才确定适配。下一轮若需实机，将集中捕获普通任务中的原生调用，不要求逐个触发来收集曲线，亦不启动旧桥接/Cockpit 计算。

### 原生源头实机结果与最终混合层（2026-11-03）

会话 `session-20261102T230433076132Z`、PID 235812，源头观察在进入飞行前启用 6/6；52 条飞行样本、7 次 MissileLaunch 的 ClientPlayForceFeedback_Internal 请求。70 次心跳、最大间隔 2002 ms、丢弃 0，无挂钩/脚本/读取错误。已排除之前附加超时的另一个进程，不混用会话。

| 原生来源 | 本轮实际调用 | 当前结论 |
| --- | --- | --- |
| 全四马达，返回地址 0x26bbd39 | 2,895 次 Update 与 2,895 次 Stop，逐对同 UUID，间隔最多 2 ms；没有 Start | Windows 确实执行该路径，但不能把这些请求当作正在播放的波形 |
| 左右小马达，返回地址 0x26efa73 | Start 1、Update 1、Stop 5；同一 UUID | 存在有启停的动态震动；静态约 0.3/-1 不冒充运行时浮点采样 |
| StopVibrationThrust | 退出飞行附近 3 次 | 已确认原生 C++ 路径，不依赖 Lua UFunction 是否命中 |
| PlayHapticEffect / SetHapticsByValue / StopHapticEffect / HapticFlyBy | 本轮无调用 | 与已核对的平台条件一致；不据此排除所有未测路径 |

小马达 Start/Update 在 UTC 23:06:09.379，附近加力读数从 0.3054 上升至 0.6736、0.8485；Stop 在 23:06:37.405，随后加力从接近 1 降至 0.1055、负值。时间关联支持继续追推力/加力条件，但 1 Hz 日志不足以确定精确阈值或完整启动条件，亦不能归为方向性杆力。

SDL 仍只有启动自检的 NewEffect/RunEffect/DestroyEffect 各一次，飞行中没有创建/更新/rumble 调用；AB6 haptic 打开、RumbleInit 成功。XInput 和 ScePad 只有启动探测/模式设置。进程退出末尾缓冲未确认刷盘，不能据此保证最后停止。汇总与关联保存在该会话 `analysis.json`。

进一步定位实际 EXE 的引擎混合函数 RVA `0x6f7e0f0`：清空 PlayerController +0x3c0 的四通道值，混合资产/动态请求，应用 +0x44c 的 ForceFeedbackScale 并限幅，然后经虚表 +0xc90 转发。真实函数跨多个 unwind 区段，入口不是包含倍率读指令的 `0x6f7e148`；后者从中途反编译会出现大量 unaff 变量，不能按其伪签名安装入口观察。

APlayerController 构造函数 `0x6f619b0` 安装虚表 `0x7be06b8`，+0xc90 对应转发实现 `0x6f8a0e0`。其完整机器码确认：+0x448 的 0x20 启用位为真时，把 +0x3c0 的 16 字节复制到局部并调用 application 虚表 +0x10；为假时传递四个零。倍率和开关可能构成上游归零，尚不能只凭空的 SDL 接口断言它是唯一原因。

采集器 v4 新增上述两个函数的只读观察，沿用 EXE 完整散列与全部入口签名检查。记录混合函数返回值区的四通道数据、转发函数入口四通道、倍率、启用位、controller ID、实际虚调用目标。`native_gate_values_LL_LS_RL_RS` 是按已核对的转发分支推导的数值，明确不冒充设备测量。没有改开关、倍率或游戏效果，没有全局挂共享 RET，也没有调用设备输出 API。

私有 x64 内存函数隔离验证通过：混合后四通道读取正确、启用/禁用两分支记录正确、controller ID/倍率正确，挂钩前/期间/卸载后返回值保持 123，卸载恢复原始字节；错误进程身份拒绝启用，无读取错误和丢弃。摘要 `mixer-verification.json` 保留，临时验证源用后删除。实际游戏的 8 个入口签名均与磁盘 EXE 对照一致。静态证据为 `native-mixer-1.txt` 至 `native-mixer-3.txt`，临时 Ghidra 源用后删除。

当前最小适配方向：先证实实际有效混合值及上游开关，再接到 SDL 设备出口，保留引擎原有曲线混合与停止语义；不按原始 Update/Stop 请求自行生成震动。持续飞行杆力仍未找到原生生产者，旧计算继续只保留源码。下一次实机仅观察，用一轮短飞行同时解决混合值、反馈开关、倍率、导弹/动态源和暂停/菜单归零问题。

### 最终混合实机已完成：非零反馈没有被送到 SDL

会话 `session-20261102T232322291013Z`，PID 132124，采集器 v4，8/8 入口校验成功。UTC 23:23:28 附加，23:26:40 进程退出；52 条飞行样本、8 次导弹播放请求均在同一会话。95 次心跳，最大间隔 2003 ms，丢弃 0，无挂钩、脚本或读取错误。

| 检查项 | 实测结果 |
| --- | --- |
| 混合与转发函数 | 各 10,690 次；实际转发实现始终为 0x6f8a0e0 |
| 反馈开关 / 倍率 | 全部为 enabled=true、scale=1，排除本轮上游开关/倍率归零 |
| 平台目标 / 控制器 | application 虚表 +0x10 实际指向 0x56246a0；controller ID=0 |
| 有效混合值 | 134 帧非零，均为 [0,small,0,small]；峰值 0.5878962278 |
| 导弹对应 | 8 次请求与 8 段非零值按时间逐段对应，每段含 16–17 个非零帧，首末非零样本跨度 250–267 ms；不是效果完整时长的重新测定 |
| 自然结束 | 8 段各自之后均捕获四通道零；最后一段后又捕获 2,069 次全零，直到 UTC 23:26:38.262 |
| 末端 SDL | 仍只有启动正弦的创建/播放/销毁各一次，飞行中无效果创建、更新或 rumble |

原始曲线五键仍由游戏生成的资产日志提供。用首帧推定采样相位、后续按毫秒墙钟与线性曲线比较，最大差约 0.0441；这只作近似时间核对，不声称获取了精确引擎效果时钟。无需自行重建曲线才能获得实际输出包络，接入应直接采用最终混合值。

本轮全四马达路径只有 2,947 对 Update/Stop，没有 Start；左右小马达状态机只有 4 次 Stop，没有 Start/Update。因此第三轮出现过的小马达动态源已证实能发出启动请求，但尚未证明其有效混合值或完整加力条件。本轮全部非零区间均与导弹对应，不能宣称已支持加力、机炮、受击或连续飞行杆力。

原始记录与派生汇总保存在该会话的 `events.jsonl`、`UE4SS.log`、`status.json`、`analysis.json`；进程退出末尾不足 100 ms 仍可能未刷盘，不以末尾记录证明异常退出停止。正常区间自然归零不等于外设已停止，本轮没有新增硬件输出。

#### 平台分发与插件缺口

对实机实际调用目标 `0x56246a0` 定向反编译并核对机器码：只有 `!byte[0x941afc4] || byte[0x941afc5]` 成立时才分发；先调用内置设备对象 +0x30，再遍历 application +0x70 / +0x78 的外部设备数组，对每个设备先调用虚表 +0x60，返回非零才调用 +0x30 传递 controller ID 和四通道结构。此处保留原始门控条件，不在缺少进一步证据时命名其设置含义。

已由摇杆构造与输入事件路径核对的主虚表 `0x8200728`：+0x60 指向 `0x8a9ad0`，机器码 `32 C0 C3`，固定返回 false；+0x30 指向 `0x8a7d00`，机器码 `C2 00 00`，直接返回。该插件按上述平台接口分发时会先被跳过，即使仅改门控，接收槽仍没有 SDL 输出实现。这里没有逐项抓取运行时外部设备数组，不能冒充已逐设备观察了虚调用；但实际平台目标、静态门控和插件空实现均有独立证据。

因此当前问题已经从“游戏是否生成震动”收敛为“有效原生通道缺少摇杆输出适配”。无需继续让用户试飞来确认同一断点，也不建议改震动设置或绕过反馈开关。定向证据保存为 `native-dispatch-1.txt`，临时 Ghidra 源已清理。

#### 最小原生适配的具体接入思路

1. **接点固定在最终平台通道边界。** 当前版本为 `0x56246a0`，Windows x64 参数为 application、controller ID、四个 float 的结构指针。保持原调用；立即复制普通值，不把 UE 对象/栈指针交给异步输出。按实际 EXE 散列和指令签名保护，版本不符则不启用。首版范围为本轮确认的本地 controller 0。
2. **沿用原生开关和时序。** 此处数值已经过引擎混合、倍率、限幅和反馈启用位处理；适配还必须遵守同一平台分发门控。门控关闭时显式停止已有适配效果。保留四通道输入，不从原始 Start/Update/Stop、按键或遥测生成新波形，也不改插件 +0x60 的全局行为。
3. **按设备能力做最后一层映射。** 查询并选定 AB6，排除 MTQ。若设备支持双马达效果，才映射 large=max(LL,RL)、small=max(LS,RS)；左右空间信息的合并需要明确记录。若只有周期效果，则用原生幅度包络调制独立周期效果；载波频率属于外设适配参数，不是从四通道中读出的游戏原始频率，不能宣称逐位还原手柄震感。当前只证实 AB6 支持启动正弦，尚未证明双马达类型或实际适配载波。
4. **效果与生命周期独立管理。** 只管理适配器自己创建的效果 ID；SDL 可能复用同一设备对象，不能假定新打开就与游戏句柄隔离。避免借用共享的简单 rumble 状态，不调用 StopAll、全局回中或增益去停止自身效果。零值立即停止，并覆盖暂停、菜单、死亡/切机、平台门控关闭、数据失效、断连和卸载；采用有限时长设备效果，让缺失后续帧时也能自然结束。
5. **下一阶段只验证已经证实的范围。** 先完成适配代码、能力判断、限幅、停止和无设备的软件验证，再通知用户启用硬件输出并集中验收。优先打通导弹及其自然结束；动态源仅在真实最终混合非零时自然跟随，不为它单独造效果。当前采集器已停止，没有部署新硬件输出。

这条路线补设备出口，直接复用 PW 已生成的幅度包络，当前不需要另建资产曲线重放系统。持续方向力、弹簧/阻尼或速度姿态杆力仍无已确认的原生生产者，旧计算方案继续只保留源码。

### 最小原生适配已实现：软件验证通过，等待首次硬件整合

实现位于 `src/native-feedback/`，CMake / Visual C++ 2022 x64 构建成功。`PWNativeFeedback.dll` 通过 MinHook v1.3.4 固定提交挂钩真实混合入口和平台四通道入口；`PWNativeFeedbackLauncher.exe` 仅在明确命令后向当前一个 PW 进程加载 DLL。未使用 UE4SS C++ ABI 或 Frida 作为运行依赖，未改游戏目录、输入绑定、预设、存档或旧 bridge。运行包位于项目 `payload/native-feedback/`，附启用/停止命令入口与依赖许可证。

平台入口先复制 16 字节普通值并保持原调用；混合入口只在同线程提供有效反馈帧与暂停状态，不跨线程传递 UE 指针。除原生启用位/倍率已经处理外，适配还遵守实际平台两个布尔量、游戏前台、controller 0、有限值及 `[0,1]` 边界。所有 SDL 设备操作由游戏平台反馈线程执行；工作线程只初始化挂钩、核对版本、等待停止与记录计数。

输出精确选择一个 AB6，重名/缺失/能力不足均不改选其他外设。优先双马达 `max(LL,RL)` / `max(LS,RS)`，否则须支持正弦，首版用 100 Hz 单路、四通道最大值；幅度增益按用户最新决定为 1.0，保留原生幅度。最初自行选择的 0.05 已撤销，150 ms 到期保护与 50 ms 续播保持。这是 API 幅度比例和载波适配，不是物理力矩标定，也不是游戏原始电机频率。首次仅验收已证实的导弹包络，不把未知加力或方向性力模型列为支持。

只创建零幅度的独立效果 ID，不借用游戏 rumble 状态、不调用 StopAll/全局增益/回中。每次 Run 为 150 ms、1 次，有数据时每 50 ms 至多续播；幅度变化更新效果，零值/无效值/暂停/前台丢失/平台禁止/停止请求均停止。断流时请求本身有限；软件验证不能代替实际 AB6 到期停止。SDL 退出钩子先清理自己的效果及引用，SDL 模块与适配 DLL 留驻到进程退出，避免跳板/函数指针失效；不支持热卸载、同进程重新启用或自动断连重试。

软件验证使用私有进程与模拟 SDL，覆盖：双马达/正弦布局、幅度与合并、有限时长/续播、零值/无效值/门控停止、精确设备选择、部分创建失败和运行/更新/停止失败的锁定清理、仅操作自身 ID、重复清理、混合函数浮点/暂停参数与 TLS 恢复、真实 MinHook 对私有函数的参数保持与卸载字节恢复、SDL 退出与子系统退出顺序。另核对实际游戏文件散列，并用同名错误 EXE 验证启动器拒绝注入、DLL 独立拒绝挂钩。没有在验证中调用真实设备 API。

本机 Release DLL/EXE 无编译警告，临时验证目标的合并源码/断言编译有覆盖宏及局部同名警告，不进入产品构建。验证摘要、构建日志、包清单与散列在 `development/artifacts/pw-native-build/`；临时验证源、私有宿主和验证二进制用后清除。

首次实机操作与日志位置见 `src/native-feedback/README.md`。这轮将首次新增真实硬件输出：游戏到主菜单后显式运行启用入口，回到游戏发射导弹并检查自然结束、暂停/恢复、切窗、菜单与正常退出；需要时运行停止入口。真实 PW 注入、设备效果类型/频率接受度、触感、退出停止均仍待这一轮确认。菜单/死亡/切机依赖原生归零或断流及有限效果，死亡/自动重连等尚未完成硬件验收，不扩大首版支持范围。
