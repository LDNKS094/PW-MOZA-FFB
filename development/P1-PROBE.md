# P1：UE4SS 加载与只读生命周期探针

## 当前状态

首轮加载与菜单→机库→飞行→主菜单测试已通过：日志含 12 次本地 Controller 的 Pawn 切换，确认 `FlyingPawn_C` 完整路径，没有探针异常。证据已保存在 `development/artifacts/p1-first-run/UE4SS.log`。这不覆盖长时间稳定性或死亡/重生场景。

当前探针已升级为 P2 低频原始采样，见 [P2-SAMPLING.md](P2-SAMPLING.md)；下文的生命周期行为和初始部署清单保留作 P1 基线记录。

- PW build ID：`16671873`，EXE 含 UE4.27 标记。
- 游戏 EXE SHA-256：`092e84225624a4de9c05d2404ff55269bd4a2aa2ff0548f2a183c6bf36abc85a`；部署前后不变。
- 官方包：[UE4SS v3.0.1](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/v3.0.1)。
- ZIP 本地 SHA-256：`4b47d4bceddd2f561a4e395bfa00924ccfc945af576a2d0c613e6537846c57ec`；上游未提供 digest，此值仅用于本地追踪，不作为独立真实性证明。
- 参考：[PWCoop](https://github.com/exraven99-design/PWCoop) README 声称使用 3.0.1，源码通过 `PlayerController:ClientRestart` 获取新 Pawn；没有运行或复制其联机辅助程序/安装器。

## 已部署的新增文件

根目录为：

```text
F:/SteamLibrary/steamapps/common/Project Wingman/ProjectWingman/Binaries/Win64/
```

仅新增：

```text
dwmapi.dll
UE4SS.dll
UE4SS-settings.ini
Mods/mods.txt
Mods/PWProbe/Scripts/main.lua
```

其中 `mods.txt` 仅启用 `PWProbe`。保留官方配置的引擎版本自动检测和默认缓存，关闭 GUI console；不安装发行包内置的游戏修改模组，不改 EXE、PAK、输入配置或存档。

逐文件哈希、目录清单和部署前用户配置/存档备份：

```text
development/deployments/p1-2026-11-01T23-49-05-914Z/
  manifest.json
  user-backup/
```

这是部署前的最新用户数据备份，不自动恢复，避免覆盖后续游戏进度。

## 探针行为

源码：`mod/PWProbe/Scripts/main.lua`。

- 注册 `ClientRestart` 的 post-hook，记录 Controller、是否本地、Pawn 对象和完整类名。
- 回调只读取对象，不保存 UObject 引用、不轮询、不修改对象或游戏状态。
- 暂不将 `FlyingPawn_C` 硬编码成“有效飞行”；先收集本机对象证据。
- 没有速度采样、命名管道、桥接、联机、振动或底座输出。
- 异常写入日志，最多 5 条回调错误；hook 注册失败单独记录。

日志在 Win64 的 `UE4SS.log`，探针消息带 `[PWProbe]`。日志仅在事件触发时出现；暂停本身不一定触发 ClientRestart，因此本轮不能证明暂停状态识别已经完成。

## 用户测试步骤

1. 保持所有遥测桥接关闭，从 Steam 正常启动 PW。
2. 先停在主菜单，确认没有崩溃或卡死。若失败，停止反复启动，保留当次日志。
3. 加载可控的普通任务或自由飞行，进入飞机后等待数秒。
4. 暂停/恢复，再返回菜单；有条件可重进一次任务，验证切图没有崩溃。
5. 正常退出后告知结果，读取 `UE4SS.log` 确认加载、hook 注册、玩家 Controller/Pawn 的类与切换顺序。

首次验证不要求连接 Cockpit；未通过前不添加飞行 tick 或任何力反馈输出。菜单加载与进任务应分开判断，不把“主菜单能打开”等同于全部兼容。

## 自检

```text
npx --yes --package=fengari-node-cli fengari development/test-probe.lua
```

P1 版已通过：本地/远端 Controller、空/失效 Pawn、受限错误日志、注册失败。测试脚本现已扩展为 P2，覆盖项见 `P2-SAMPLING.md`。使用临时 Lua 5.3 解释器运行通用 Lua 语法，不向项目添加运行依赖；不模拟 UE4SS 的原生反射、线程或 ABI。测试脚本会替换测试进程的 `print` / `RegisterHook`，只应独立运行，不作为游戏模组加载。

## 回退

先退出游戏，复制保存 `UE4SS.log`。将上面列出的 5 个新增文件移到项目备份目录即可禁用；若仅需迅速禁用加载，先移走这次新增的 `dwmapi.dll`。不要删除系统 DLL，不删除第三方后来加入的文件，不恢复旧进度存档。

UE4SS 可能另外生成日志、缓存、dump；回退后先保留作诊断，不依据目录名盲删。若要完整清理，按部署 manifest 和实际生成清单逐项检查。若游戏更新、DLL 哈希改变或已有其他加载器，停止套用本轮清单并重新核对。
