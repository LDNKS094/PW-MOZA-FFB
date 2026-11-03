# PW 原生震动资源整库检查

> 技术证据，不是新增的细分里程碑。执行计划仍见 [../PLAN.md](../PLAN.md)。本轮没有启动游戏、修改游戏资源、加载/触发效果或连接设备。

## 结果

**本机当前 Pak 的 AssetRegistry 共解析 11,828 条资产记录，ForceFeedbackEffect 类只有一项：**

```text
/Game/ProjectWingman/Blueprints/PlayerActor/MissileLaunch.MissileLaunch
```

这是按类读取全部已序列化资产后的结果，不是关键词命中文件名，也不是“只玩过导弹，所以只找到导弹”。因此可以直接确定此容器注册的同类资源集合，无须让用户逐项触发来收集。

这个结论的边界是 **当前包内注册的 ForceFeedbackEffect 资产**，不能扩张成“PW 全部震动只有一种”。动态效果、嵌入蓝图/原生代码的配置、其他设备接口和未注册对象仍需按相应来源判断，不能凭本次注册表排除。也不能照搬 AC7 的 46 类型表，声称 PW 同样具有那些效果。

## 容器与完整性检查

- 本机仅发现 `pakchunk0-WindowsNoEditor.pak`，大小 **17,578,387,442 字节**，Pak v11，索引未加密。
- 读取主索引 354,438 字节、完整目录索引 920,959 字节，均通过容器提供的 SHA-1 验证。
- 完整目录包含 **1,694 个目录、25,661 个文件项、10,014 个 .uasset 包**。资产记录与包数量不是同一个概念，一个包可以登记多个资产。
- 从 Pak 定点读取 `ProjectWingman/AssetRegistry.bin`（4,552,999 字节、未压缩），通过条目 SHA-1。
- 按版本 8 的 NameBatch、FixedTags Store 和 FAssetData 格式解析；校验 35,687 个名字的字节总量、FStore 结束标记、每项名字索引以及 ObjectPath=PackageName.AssetName。
- 读取完全部 11,828 条资产后仅余 16 字节尾部；没有解析其依赖图，也不声称已建立完整调用图。

主要散列：

```text
完整目录索引 SHA-1  0bea0780b52e673a5dfddad7511cc3d29157f9c6
AssetRegistry SHA-1  56d30fe2f8b7f36330ecebc6c822c045de4ea3fc
MissileLaunch.uexp   a4406872cb78aa2a0be33c7c7e0d9d722ca0e905
```

机器可读摘要保存在本地忽略目录：`development/artifacts/p5-resource-inventory/summary.json`。没有把整包或游戏原始资源纳入 Git。

## 曲线及引用交叉检查

- 已直接从包中定点读取 MissileLaunch 的 .uasset（1,224 字节）和 .uexp（718 字节），均验证条目 SHA-1；没有解包 17 GB 游戏。
- 在原始导出中找到已采集的五组 Time/Value float 对，每组恰一次，与先前运行时曲线一致。这是原始字节交叉检查，不冒充完整的 .uexp 属性解析器。
- 注册表中有 5 个 CurveFloat：玩家组件 RollCurve、三条 TrueSky 光照/曝光曲线、Engine 的 ContentIntroCurve；另有 10 个 CurveLinearColor，路径均为 SkyCreator 云/天空材质。这些不能因“也是曲线”就当作原生震动。
- 对主游戏 Blueprint 目录的 348 个 .uasset 头进行只读名字检查，全部条目散列通过、没有压缩跳过项。ForceFeedbackEffect 类名只在 MissileLaunch 头出现；MissileLaunch 另一个名字命中来自语音提示数据表。名字命中仅是引用候选，不证明调用关系。
- 这个头部检查不覆盖全部插件、nativized Blueprint 的生成代码或运行时动态配置；`FlyingPawn_C` 在既有日志中是 DynamicClass，尤其不能把头部查无调用名当成原生代码没有调用。

## 对交付的影响

本轮结论是资源范围与原始数据证据，**不是必须建立曲线重放系统的理由**。当前按 PLAN.md 优先验证原生直用：游戏实际向谁输出、MOZA 支持哪些效果、启停与 Cockpit 是否兼容；能直接正确使用时不额外捕获重放。接口/设备不兼容时先考虑必要适配，确实不可行才使用这些曲线和事件建设重放链路。

若最终需要重放，资源发现与播放时序仍分开：配置来自已定位的集合，原生 Hook 提供激活、参数和停止，不靠逐个触发积累曲线。注册表只有一个同类资产既不排除动态/其他接口输出，也不证明 MOZA 能原生使用。不能因为 AC7 有更多效果就人造同样数量的类型，不暗中退回事件合成，不要求用户继续逐种试飞采样。

## 解析依据与清理

格式依据为 CUE4Parse 的公开实现：[AssetRegistry reader](https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/AssetRegistry/Objects/FAssetRegistryReader.cs)、[FAssetData](https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/AssetRegistry/Objects/FAssetData.cs)、[FStore](https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/AssetRegistry/Objects/FStore.cs)、[NameBatch](https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/Objects/UObject/FNameEntrySerialized.cs)。使用本机实际版本、散列与边界断言核对，不借最新解析器版本推测本机结构。

分析使用标准库在工具沙箱执行，没有安装新依赖、落地一次性解析源码或遗留合成测试文件；保留的只有真实证据摘要和本技术记录。未启用振动输出，未重新发布正式 DCS.exe。
