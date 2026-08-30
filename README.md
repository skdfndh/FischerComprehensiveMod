# Fischer Comprehensive Mod

《Fischer's Fishing Journey》的 BepInEx 5 综合模组。它为单人离线游玩提供时间倍率与日常操作自动化功能。

> 本项目为非官方模组，与游戏开发商及发行平台无关联。使用前请自行备份存档，并确认符合你所在平台和游戏的使用规则。

## 功能

- 时间倍率：按 `F6` 在 1 倍、2 倍、4 倍、8 倍之间切换。
- 自动唤醒小猫：小猫偷懒时自动唤醒，也保留手动唤醒按钮。
- 自动投放窝料：效果结束后投放一层；优先使用购买的区域窝料，再使用原版定时恢复、上限为 5 的免费窝料。
- 神奇鱼缸整理：将收益更高的鱼放入神奇鱼缸；伙伴任务所需的鱼会保留在鱼篓。
- 自动完成鱼群聚集：检测到小游戏出现后自动完成。
- 自动伙伴交互：自动处理普通对话、接取伙伴任务，并在材料齐全时提交。
- 加速后的商店刷新与自动采购同步。

## 安装

### 1. 安装 BepInEx 5

本模组需要 **BepInEx 5（Mono 版本）**。将 BepInEx 解压到游戏根目录；该目录应包含游戏的可执行文件。首次启动游戏后，应出现 `BepInEx` 文件夹。

### 2. 安装模组

从本仓库的 Releases 下载 `FischerTimeFlow.dll`，复制到：

```text
游戏根目录\BepInEx\plugins\FischerTimeFlow.dll
```

如果 `plugins` 文件夹不存在，请先启动一次已安装 BepInEx 的游戏，或自行创建该文件夹。

### 3. 启动并确认

启动游戏后，左上角会出现 `Time Flow` 面板。BepInEx 的日志文件 `BepInEx\LogOutput.log` 中应包含：

```text
Loading [Fischer 综合 Mod]
```

## 使用方法

左上角面板中的开关均会保存到 BepInEx 配置中，重启游戏后仍会保留。

| 控件 | 作用 |
| --- | --- |
| `Switch (F6)` | 切换时间倍率。 |
| `Auto wake cat` | 自动唤醒正在偷懒的小猫。 |
| `Auto sprinkle bait` | 在窝料效果结束后自动投放一层。不会自动购买窝料。 |
| `Organize magic tank` | 立即整理一次神奇鱼缸。 |
| `Auto finish fish group` | 小猫头上出现鱼群聚集感叹号时自动进入并完成小游戏。 |
| `Auto complete NPC tasks` | 自动完成伙伴普通对话、接取任务，并在鱼篓材料满足后提交。 |
| `Wake cat` | 仅在小猫偷懒时显示，用于手动唤醒。 |

## 从源码构建

需要安装 .NET SDK，并准备好已安装 BepInEx 5 的游戏目录。

```powershell
git clone <仓库地址>
cd FischerComprehensiveMod
dotnet build .\FischerTimeFlow.csproj --configuration Release "-p:GameDir=游戏根目录"
```

编译成功后，DLL 位于：

```text
bin\Release\net472\FischerTimeFlow.dll
```

## 一键同步到游戏

修改源码后，**先关闭游戏**，再执行：

```powershell
.\Deploy-Game.ps1 -GameDir "游戏根目录"
```

脚本会编译、复制 DLL 到 `BepInEx\plugins` 并校验 SHA-256 哈希。需要在同步完成后启动游戏时：

```powershell
.\Deploy-Game.ps1 -GameDir "游戏根目录" -StartGame
```

## 故障排查

- 面板没有出现：确认 DLL 位于 `BepInEx\plugins`，并检查 `BepInEx\LogOutput.log` 的加载报错。
- 启动后报错：确认使用的是 Mono 版 BepInEx 5，并确认游戏更新后程序集接口没有变化。
- 同步脚本拒绝执行：请完全退出游戏和启动器后再运行脚本。
- 功能异常：关闭相应自动开关，备份存档，并附上 `BepInEx\LogOutput.log` 中与本模组相关的日志提交 Issue。
