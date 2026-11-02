# PW 原生输入绑定测试（不安装模组）

状态：PW 已枚举 MOZA MTQ 与 AB6，用户确认按键本身绑定通过，轴此前确认响应；按用户要求转回 FFB 主线。高编号按钮、完整轴行程及重启持久性未单独验证，语言与无边框设置问题暂缓。

## 已检查

- AC7 样例：`../AC7-MOZA-FFB/presets/input/Input.ini`。常规绑定走游戏原生机制；样例明确说明 Button33–128 依赖 AC7 输入扩展，不能据此断言 PW 也有相同限制。
- 首次检查只有 SaveGames；启动游戏并绑定后已生成 `%LOCALAPPDATA%/ProjectWingman/Saved/Config/WindowsNoEditor/Input.ini`。其中 `[/Script/Engine.InputSettings]` 下实际出现 `AxisMappings=(AxisName="Roll Axis",Scale=1.000000,Key=Joystick_MOZAAB6FFBBase_1_Axis0)`，确认 AB6 横滚的独立文本配置。其他轴/按钮的 Key 必须捕获后确认，不从 AC7 编号猜测。编辑前退出游戏并备份 INI，避免退出时覆盖修改；其他曲线/设置是否都在此文件中仍待验证。
- Windows 当前在线 PnP 设备名称查询未匹配 `MOZA/MHG/MTQ/AB6`。这只是设备名称筛选，不证明设备未连接或游戏无法识别。
- 用户截图：DirectInput Devices 列出 `MOZAMTQTHROTTLEPANEL`、`MOZAAB6FFBBASE`。设备枚举已通过；左侧 Pitch/Roll 等动作指示未响应，须区分未绑定与原始输入捕获失败。先不使用 Reset 或 Full Test。
- 社区资料指向游戏 `Settings → Controls` 的 `Axis Bindings` 和 `Key Bindings` 页面；以本地版本实际界面为准。

## 准备

1. 接好底座、手柄和油门，先在 Cockpit 或 Windows 游戏控制器页面确认轴和按钮能响应。测试期间不启动 `DCS.exe` 桥接，不安装 UE4SS/ASI；使用安全的低力矩/无动态力设置。
2. 关闭游戏后备份整个 `Saved/SaveGames` 目录到独立时间戳目录。该目录含游戏进度，禁止为重置绑定直接删除。若后续要恢复，先再次保存当前进度副本，注意 Steam 云同步可能覆盖本地文件。
3. 记录当前 Steam Input 设置；只有发现设备识别/重复输入问题才尝试针对 PW 禁用，并记录结果。
4. 启动游戏，截图控制设置和设备选择列表。先测试原生输入，不照抄 AC7 的 GUID 段落或轴名语法。

## 第一轮：核心轴

每次只移动一个轴；绑定后回中/回到起点，再测下一个。优先在设置页验证，随后在可控任务里验证。

| 测试项 | 物理操作与通过条件 | 结果 |
| --- | --- | --- |
| 横滚 | 杆左右移动，只影响横滚，方向正确，回中稳定 | 用户确认绑定有响应；INI 已记录 AB6 Axis0；方向、范围与回中待测 |
| 俯仰 | 杆前后移动；拉杆抬头，推杆低头，必要时只反转此轴 | 待测 |
| 偏航 | 若有扭杆或脚舵，左右操作，方向正确且不串轴 | 待测 |
| 油门 | 从最小平滑推到最大，覆盖全范围，无半程跳变/反向 | 待测 |
| 多设备同时工作 | 保持杆输入时操作油门，两者都有效、不互相顶替 | 待测 |

双发油门先只绑定一个物理轴，另一个停放不动；首轮不做轴合并、虚拟设备或映射软件。轴死区/曲线仅在发现漂移后最小调整。

## 第二轮：按钮与帽键

下面是从 AC7 参考的**候选布局**，不是已验证的 PW 绑定。PW 动作名称、设备按钮编号须在本机确认；没有对应功能的动作跳过。

| 动作 | AC7 参考物理按钮 | PW 实测 |
| --- | --- | --- |
| 机炮 / 导弹 | AB6 Button1 / Button2 | 待测 |
| 武器切换 / 目标切换 | AB6 Button10 / Button8 | 待测 |
| 雷达或地图 / 干扰弹 | AB6 Button4 / Button5 | 待测 |
| 视角 / 暂停 | AB6 Button23 / Button22（或 MTQ Button9，先二选一） | 待测 |
| 视角帽键 | AB6 Button12/14/15/13，若实际呈现 POV 则直接绑定 POV | 待测 |
| 菜单确认 / 返回 | AB6 Button19 / Button18，如游戏允许独立绑定 | 待测 |
| 高编号按钮 | 选设备上实际可按到的 >32 按钮，AC7 样例 MTQ Button65 可作候选 | 待测 |

高编号按钮分两步：游戏是否能捕获绑定、任务中动作是否真的触发。失败时记录 Cockpit 编号、游戏显示名称及设备，不预设 PW 有 32 按钮上限，也不先写扩展模组。菜单导航始终保留键鼠作为退路。

## 第三轮：持久化与回归

- 设置保存后退出并重启游戏，确认轴、按钮和多设备身份保留。
- 退出游戏后重新插拔设备，再启动，确认绑定不会串到另一设备。
- 检查是否有默认重复绑定、自动驾驶/暂停等按钮误触、轴反转或菜单漂移。
- 完成后记录实际修改的文件及时间；只有确认存储格式后才考虑提供可复用配置。

## 已应用的第一版测试配置

已在确认游戏进程退出后修改本机 `Saved/Config/WindowsNoEditor/Input.ini`。

- 原配置备份：`development/input-backups/Input.before-moza-20261102-061152.ini`。恢复时先退出游戏，再用该文件覆盖本机 INI；不需要恢复任务存档。
- 只读读取游戏 PAK 内 `ProjectWingman/Config/DefaultInput.ini`，校验其 SHA1，确认实际动作名。未修改游戏资源。
- 通过游戏的 SDL2 仅初始化 joystick 子系统：当前 MTQ index=0（8 轴、128 按钮），AB6 index=1（8 轴、128 按钮、1 帽键）。未初始化 haptic 或输出力。
- 原横滚绑定保持不变，新增 5 项轴映射和 17 项按钮动作；按钮通过 `+ActionMappings` 追加到默认配置，保留所有原配置行与键鼠/手柄默认输入。静态断言检查通过，游戏读取与硬件操作仍待验证。
- 以下轴分配参考 AC7 布局和常规 SDL 轴顺序，仅 Roll Axis0 已由游戏绑定证实；其余轴号、方向和 SDL Button0 与 Cockpit Button1 的对应关系均需实测。

| 功能 | 测试绑定 |
| --- | --- |
| 横滚 / 俯仰 / 偏航 | AB6 Axis0（保留）/ Axis1（反转）/ Axis5 |
| 油门 | MTQ Axis4（反转），若无反应先在游戏里重新捕获 Thrust Axis，不能认定硬件故障 |
| 视角左右 / 上下 | AB6 Axis3 / Axis4（反转） |
| 机炮 / 导弹 | AB6 Button1 / Button2（下列按钮号按 Cockpit 的 1 起始候选编号描述） |
| 武器切换 / 目标 | AB6 Button10 / Button8 |
| 地图 / SP Module | AB6 Button4 / Button5；SP Module 是 PW 特殊模块动作，不保证所有机型都表现为干扰弹 |
| 切换视角 | AB6 Button23 |
| 武器 1/2/3/4 | AB6 Button12/13/15/14；仅限任务中可用的武器 |
| 菜单确认 / 返回 | AB6 Button19 / Button18 |
| 菜单上/右/左/下 | AB6 Button12/13/15/14 |

没有猜测 Pause、AutoPilot 等未在默认 ActionMappings 中发现的动作名；暂停暂用键盘 Esc。未绑定 MTQ 的开关和高编号按钮，避免保持式开关导致持续误触；待基础输入验证后再补。

用户反馈：新增轴绑定已生效，按钮绑定疑似不正确，并在游戏内重绑 Change Camera 和 Expand Map 作为参考。经重新计数，PW 默认 Pitch、Thrust、Yaw 和两个 View Padlock 轴各有 4 项，我追加后各有 5 项；用户指出游戏界面只有 4 个绑定槽，这可能造成设置无法保存，尚未验证因果。默认每个动作另有 3 项，我又用 `+ActionMappings` 追加，游戏实际加载/保存行为也未经验证。用户要求保留轴绑定、将每个轴限制在四项以内：游戏退出后已备份当前 INI 至 `development/input-backups/Input.before-four-slot-20261102-062855.ini`，把 5 项超额的 MOZA 轴分别替换一条原映射；6 条 MOZA 轴及全部按钮内容不变。静态检查确认本地 INI 各轴均不超过 4 项；继承后生效数量及游戏实际保存仍待验证。未修改 `.sav`。此前检查磁盘 Input.ini 仍只有手写的 `+ActionMappings`：Camera=Button22、Map=Button3，未出现可区分的游戏保存样本；文件非只读。当前仅修正轴条数，按钮保持不动；须验证 Saved 配置中 `+ActionMappings` 的实际加载/保存行为，不能只归因于按钮编号。

四槽测试结果：用户再次修改几个按钮后游戏已退出，但 `Input.ini` 最后修改时间仍为本轮手工编辑时，17 条手写按钮映射没有变化；因此缩减本地轴条数尚未解决保存问题，也无法据此确认其因果。`savegame.sav` 时间更新，但仅凭时间不能认定保存了按键。

用户另反馈无边框窗口和语言无法记住，但帧率与 Gamma 可记住。网络调查：其他玩家报告语言和控制设置重启后重置（<https://steamcommunity.com/app/895870/discussions/0/586147117689562910/>）；第三方语言修复项目称游戏启动会将语言写回 en-US，方案是修改游戏 EXE 并改存档/配置（<https://github.com/sheno4/ProjectWingman-LanguageFix>），该说法尚未独立验证，且未经用户明确同意不运行补丁或修改 EXE。未找到经确认的无边框重置通用修复。只读检查：`GameUserSettings.ini` 有 `Culture=zh-CN`、`FullscreenMode=1` 和 `FrameRateLimit=144`，但最近一次修改早于 `savegame.sav`；`savegame.sav` 的序列化 `Language` 是 `en-US`，并包含 FrameLimit 与 Gamma 属性，Steam appmanifest 的语言为 `english`。这些是相互冲突的候选来源，不足以确认哪一项启动时生效或被覆盖；窗口值是否被游戏实际使用亦待实测。两个文件属性不是只读，Steam AutoCloud 标记只证明存在云同步元数据，不证明云端覆盖。用户表示 Steam 属性无语言选项，同意移除存档做排查。游戏退出后已备份整个 `Saved/SaveGames` 至 `development/save-backups/SaveGames.before-settings-test-20261102-070231/` 并校验，随后仅移走 `savegame.sav`；战役、征服、统计存档未删除。下一步在游戏内将语言、窗口模式改为目标值，记录运行时及正常退出后的文件值，再重启对比；不编辑二进制存档或禁用云同步。Steam 云同步可能在启动时恢复旧存档，需检查新文件是否为云端恢复，不能假定已彻底清除。测试后游戏已退出：新的 `savegame.sav` 为 1841 字节，与旧备份的 3074 字节不同，包含 `Language=en-US`、桌面 Gamma 约 0.602；这只能说明最终内容不同，不能排除云端曾恢复旧文件。`GameUserSettings.ini` 未更新，仍为 `Culture=zh-CN`、`FullscreenMode=1`、帧率 144；无法据此证明游戏有读取这些设置。战役、征服、统计存档与备份逐字节相同。用户确认曾修改；按用户请求，在游戏退出时备份当前设置存档到 `development/save-backups/savegame.before-language-2026-11-01T23-40-07-710Z.sav`，验证 GVAS 中唯一的 Language StrProperty 后原位等长替换 `en-US`→`zh-CN`；校验只有预期 4 个字节变化且长度不变。GameUserSettings.ini 已为 `Culture=zh-CN`，未修改。下一步启动验证是否生效并观察存档是否被覆盖；Steam Cloud 也可能覆盖。恢复时先退出游戏，备份任何新 `savegame.sav`，再将备份目录中的原文件复制回去；注意云同步可能覆盖。

用户要求干净测试：确认游戏已退出后，备份完整 Input.ini 到 `development/input-backups/Input.before-clean-test-20261102-063313.ini`，校验副本一致后移除游戏目录里的 Input.ini；未改动 `.sav` 或其他配置。用户随后手动绑定，游戏运行期间新 Input.ini 已写入真实映射：Change Camera Button22、Change Target Button7、Expand Map Button3、Gun Fire Button0、Missile Launch Button1；均使用无 `+` 前缀的 `ActionMappings=`，每个动作共 3 条。与此前猜测的按钮号一致，但此前额外追加的第四条动作可能影响保存；不能仅凭此确认原因。游戏已退出，5 条绑定仍在磁盘文件中；尚需重启确认游戏读取和持久化。新文件没有 `AxisMappings`，需要用户重新手动绑定或后续按游戏实际格式配置。需要恢复旧文件时先退出游戏，再将备份复制回 `Saved/Config/WindowsNoEditor/Input.ini`。

下一轮启动后：先看轴页是否显示这些映射，再验证左侧校准区，之后才进任务；尤其检查油门全行程、俯仰方向和机炮/导弹按钮。重启后设备次序变化可能使 `_0` / `_1` 身份失配，届时应重新捕获确认。

## 结果记录格式

每次只需记录：设备型号 → 游戏显示的轴/按钮 → 对应动作 → 成功/失败现象，最好附控制设置截图。先得到核心轴结果，再决定是否需要输入适配代码。

参考线索（不是本地验证结论）：
- <https://steamcommunity.com/app/895870/discussions/1/4891437254521726923/>
- <https://steamcommunity.com/app/895870/discussions/1/3105763714516939913/>
- <https://www.pcgamingwiki.com/wiki/Project_Wingman>
