# Fischer 时间流速

《Fischer's Fishing Journey》的 BepInEx 插件：提供时间倍率、自动投放窝料、自动伙伴交互、神奇鱼缸整理等功能。

## 开发位置

以后只在本目录修改源码：`D:\666\mycreate\FischerTimeFlow`。

主要文件：

- `FischerTimeFlowPlugin.cs`：插件功能代码。
- `FischerTimeFlow.csproj`：项目和游戏程序集引用。
- `Deploy-Game.ps1`：编译并同步 DLL 到游戏目录。

## 编译

```powershell
dotnet build .\FischerTimeFlow.csproj --configuration Release
```

生成文件位于 `bin\Release\net472\FischerTimeFlow.dll`。

## 同步到游戏

先退出游戏，再在本目录执行：

```powershell
.\Deploy-Game.ps1
```

脚本会编译项目、覆盖游戏的 `BepInEx\plugins\FischerTimeFlow.dll`，并验证 SHA-256 哈希。需要同步后启动游戏时：

```powershell
.\Deploy-Game.ps1 -StartGame
```

## 依赖

- 游戏已安装 BepInEx 5。
- .NET SDK（用于编译 `net472` 项目）。

项目文件目前按本机游戏路径引用 DLL：`D:\SteamLibrary\steamapps\common\Fischer's Fishing Journey`。若游戏安装位置变动，请同步修改 `FischerTimeFlow.csproj` 与 `Deploy-Game.ps1` 中的路径。
