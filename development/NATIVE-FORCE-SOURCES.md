# PW 持续飞行杆力来源核查

核查日期：2026-11-03。对象为当前 Windows EXE，SHA-256 `092e84225624a4de9c05d2404ff55269bd4a2aa2ff0548f2a183c6bf36abc85a`。本轮只读分析已有二进制、先前原生采集与公开资料；未改输出、未运行硬件效果、未恢复旧计算。厂商检索由用户授权的 Luna 子代理完成，关键来源另作交叉核对。

> 后续决策：用户接受既有 Cockpit 桥接的杆力，决定恢复该方案，不另建杆力模型。本文保留原生来源调查结果；下文关于独立模型的建议由最新决策覆盖，实施状态见 PLAN.md。

## 工作结论

**当前没有找到可复用的原生方向性飞行杆力生成链。已有证据更支持该 PC 构建未接入这类功能，而不是已有完整杆力模型只差一个 SDL 转发接口。** 可以据此规划独立杆力模块，不把继续寻找原生模型当作全部开发的前提。

这是一项有范围的工程结论，不声称证明了所有隐藏代码、外部插件、特殊条件或其他平台都不存在实现。原生震动已经证实存在；速度、姿态、载荷相关的机体动力学数据，也不能直接等同于面向操纵杆的力反馈模型。

## 游戏侧证据

### 1. 设备能力有实现，游戏生产者未找到

JoystickPlugin 的 FeedbackData 转换器 RVA `0x4f9a0f0` 包含恒力、周期、弹簧、阻尼等类型转换，但它接收的是调用者给定的效果参数，并不从飞行速度、姿态或载荷生成这些参数。

全 EXE 可执行区的直接 CALL/JMP 候选扫描，与已有定向反编译交叉核对：

| 实现 | 直接上游 |
| --- | --- |
| CreateEffect `0x4f98ef0` | 仅 UE exec 包装 `0x4fa2740` 中的 `0x4fa2922` |
| UpdateEffect `0x4fa0a60` | 仅 UE exec 包装 `0x4fa35a0` 中的 `0x4fa373f` |
| FeedbackData 转换 `0x4f9a0f0` | 仅上述创建/更新实现中的 `0x4f98f85`、`0x4fa0b0e` |

本轮另查了全文件绝对函数指针和可执行区 RIP-relative LEA 候选。创建、更新、转换实现均未发现额外引用；两个 exec 包装只找到原生注册对中的绝对指针 `0x8202bc8` / `0x8202c78`。这补足常见函数指针引用检查，但不等于穷尽运行时反射或计算得到的地址。

### 2. 从 SDL 末端反查，也未出现另一处静态生产者

为避免只扫描插件函数而漏掉内联后的游戏实现，进一步解析 SDL2 的延迟导入，反查导入跳板的 CALL/JMP 候选：

| SDL API | 找到的直接调用点 | 所属路径 |
| --- | --- | --- |
| HapticNewEffect | `0x4f98b30`、`0x4f98fba` | 启动自检、通用 CreateEffect |
| HapticRunEffect | `0x4f98b44`、`0x4f99089` | 启动自检、通用 CreateEffect |
| HapticUpdateEffect | `0x4fa0b5d` | 通用 UpdateEffect |
| HapticSetAutocenter | `0x4fa0800` | 插件通用设备设置函数 |
| HapticSetGain | `0x4fa0910` | 插件通用设备设置函数 |

没有找到独立的飞行逻辑效果创建/更新调用点。直接导入表和延迟导入表没有 DirectInput 设备库；EXE 中也未检出常见 DirectInput 恒力、正弦、弹簧、阻尼、惯性、摩擦、斜坡 GUID，以及 DirectInput8Create/DirectInputCreateEx 名称。SDL DLL 自身当然可能通过 DirectInput 执行效果；这里检查的是有无另一套 EXE 内独立生产路径，不能把未命中当作排除动态加载/其他协议的证明。

### 3. 资产和先前实机结果一致

- 先前同版 Pak 的 10,014 个 uasset 头全部扫描，未命中 FeedbackFunctions、CreateEffect、UpdateEffect、FeedbackData、JoystickPlugin 名称引用。它不是全部运行时代码图，不能单独证明无间接使用。
- 适配器加入前的完整飞行采集，SDL 只有启动 5 秒正弦，无飞行中的创建/更新/播放或 rumble。四通道原生震动请求确实存在，属于另一条引擎马达震动链路。
- 已定位的游戏动态震动源使用单一强度及四马达掩码，已测输出为对称马达强度；没有提供供操纵杆使用的轴方向、回中位置、弹簧系数或阻尼系数。
- 初始化正弦、适配器新建的 100 Hz 正弦以及设备软件固定回中，都不作为游戏原生飞行杆力的证据。

## 开发者与外设公开资料

开发者的 [Platform Update 2.0 官方公告](https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/5220291886505774663)（2023-10-03）提到云中控制器反馈、加力反馈调整，并汇总了旧版本的加力振动修复。这支持已发现的控制器震动机制，没有明确说明方向性操纵杆载荷力。记录通过 [Steam 官方新闻 API](https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=895870&count=100&maxlength=0) 获取，摘要见本地 `official-patch-notes.json`。

| 厂商/软件 | 检索结果与可确认机制 |
| --- | --- |
| [MOZA AB6 官方支持](https://support.mozaracing.com/en/support/solutions/articles/70000679473-moza-ab6-flight-simulator-support) | 未找到 PW 方向性杆力适配声明。通用设备模式说明轴/按钮映射；列举部分其他游戏的遥测反馈，不能据此推出 PW 杆力支持，也不能据不完整名单证明不兼容 |
| [VPforce TelemFFB 官方项目](https://github.com/walmis/VPforce-TelemFFB) | README 支持列表未列 PW；明确区分游戏原生 FFB 与软件按遥测生成效果。仅核查公开支持表，不声称审计其全部代码 |
| [BRUNNER Simux](https://simux.brunner-innovation.swiss/) / CLS2Sim | Any Game 模式是静态效果、没有模拟器遥测；远程接口允许外部客户端控制，不代表 PW 已实现客户端 |
| FFBeast / simFFB | 未找到 PW 专属方向性杆力适配声明。通用 DirectInput 效果与独立弹簧/阻尼/配平工具不能证明游戏提供力 |
| PXN / WINWING / Thrustmaster / SimShaker | 检索资料有通用摇杆震动、特定游戏触觉或座垫/换能器支持，未找到明确的 PW 方向性杆力支持声明 |

社区线索也作了反例核查：FFB2 “运行正常”的说法未具体说明力效；Steam Haptics 说法未描述方向性载荷；另有用户明确通过 Xbox 360 模拟器把 PW 手柄 rumble 转到 FFB2 马达。这些可解释“某设备在 PW 有反馈”，但不能证明有随速度/气动力改变的操纵杆载荷。

VPforce 大 PDF 的正文抓取超过工具输出上限，未将搜索摘要当作已全文验证的手册证据；已改用官方 TelemFFB README 交叉核对。完整厂商链接、短引用、社区线索与获取边界保存在 `development/artifacts/pw-native-force-research/vendor-claims.md`。

## 对三块定位的影响

1. 原生震动适配继续复用游戏已有幅度包络，改善频段和通道映射。
2. 缺失事件反馈需要真实事件/状态来源，效果由模组设计时明确标注。
3. 持续飞行杆力按需要独立模型来规划，其直接来源将是模组依据飞行状态计算的效果，不称作“恢复 PW 原生杆力”。本轮未开始实现或恢复旧模型。

只有出现明确的飞行逻辑效果生产者，或厂商可复现的 PW 方向性/随载荷变化的杆力适配，才有必要重开原生杆力路线。无需为了已有的相同断点再次试飞。

本轮本地证据：`native-force-reference-scan.json`、`native-force-pointer-scan.json`、`sdl-effect-import-calls.json`、`alternative-output-scan.json`、`official-patch-notes.json`，均位于 `development/artifacts/pw-native-force-research/`。字节模式结果是候选引用，不替代完整控制流或所有运行时状态证明。
