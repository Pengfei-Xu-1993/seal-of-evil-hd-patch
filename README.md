# Seal of Evil HD Patch

《复活：秦殇前传》（Seal of Evil）Steam 版的启动兼容与高清显示补丁。提供无边框全屏、游戏内分辨率选择、居中的菜单，以及重制的主菜单与开场静态画面。

Compatibility and HD display patch for the Steam edition of *Seal of Evil*.

**开发与实现：OpenAI Codex。发布者只负责审核，未编写代码。**

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

## 下载与安装

普通用户请下载 [v0.22 Release](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.22) 附件 **`SealHD_v0.22.zip`**，完整解压后运行 `Install.cmd`，无需自行编译。GitHub 自动提供的 `Source code` 压缩包用于阅读和构建源码，缺少安装所需的完整 `payload`。

1. 先退出游戏。
2. 将完整安装包解压到一个目录，保留其中的 `payload` 文件夹。
3. 双击 `Install.cmd` 安装。脚本使用预设游戏目录，并不会自动查找各个 Steam 库；安装在其他位置时，请按下面的方式指定目录。
4. 安装完成后通过 Steam 启动游戏。默认分辨率为 **1920×1080**。

在安装包的解压目录打开 PowerShell，将示例路径替换为自己的游戏目录，即包含 `SOE.EXE` 的文件夹：

```powershell
.\Install.cmd -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"
```

安装无需启用 Windows 的兼容模式或“以 640×480 屏幕分辨率运行”。补丁通过游戏目录中的 `dinput8.dll` 加载，配合 dgVoodoo 2.87.6 的 Direct3D 11 呈现路径工作。

## 画面与操作

- 无边框全屏显示，鼠标坐标按当前游戏画布映射，ESC、Options 与 Load Game 对话框居中。
- 游戏内 `Options → Screen Resolution` 提供五档实际渲染尺寸：

  | 选项 | 尺寸 |
  | --- | --- |
  | 800×600 | 800×600 |
  | 1024×768 | 1024×768 |
  | 1080p | 1920×1080 |
  | 1440p（本项目所称 2K） | 2560×1440 |
  | 4K | 3840×2160 |

- 使用左右控件切换，`OK` 应用并保存，`Cancel` 保留当前设置。当前选择写入游戏目录中的 `SealHD.ini`，由 `ProfileIndex` 记录。
- 高清主菜单通过 GPU 合成层显示，保留原游戏菜单交互，并提供局部篝火动态效果。
- 高清开场包含标题图和三张品牌静态卡片；品牌卡片总时长约 19.167 秒，可用 Esc、空格或点击跳过。

较低的分辨率选项仍可供用户选择，安装和启动不要求将桌面设置为这些尺寸。

## 高清美术来源

五张 PNG 均导出为 **3840×2160**。主菜单以用户提供的原图为参考，重制以保留中央原有器物、装饰和纹理为目标，左右延展区域减少新增杂物，移除左下角“试玩版”字样。标题图保留原图三件悬挂饰物。

这些画面使用 ImageGen 编辑：主菜单和三张品牌卡片的生成原稿为 **1672×941**，标题图原稿为 **1671×941**，之后经高质量双三次重采样导出 4K。4K 是最终文件尺寸。提示词、原稿尺寸和导出文件哈希见 [provenance](provenance/)；协作方式见 [AI 使用声明](AI_DISCLOSURE.md)。

## 已验证范围与性能

验证基于开发机器，详细记录见 [docs/validation.txt](docs/validation.txt)。

| 范围 | 实测结果 |
| --- | --- |
| v0.22 Steam 启动 | 已进入主菜单，Steam 跟踪到对应游戏进程 |
| v0.22 Steam 浮层 | Shift+Tab 完整打开、关闭；返回后 Options 及左右分辨率选择正常 |
| v0.22 退出 | Steam 记录退出码 0，审计窗口内未见相关新崩溃事件或新转储；退出触发方未由记录确定 |
| 世界分辨率与输入 | v0.21 直接启动时完成同进程 4K→1440p→1080p 切换、保存、重启保持；角色移动、ESC 居中、取消和短点击操作已实测 |
| 高清开场和菜单 | 标题、三张品牌卡片前台全屏显示；ESC 跳过、Options、Load Game、菜单持续呈现已实测 |
| 安装与恢复 | Windows PowerShell 5.1 隔离测试通过 11→12 项备份迁移、重复安装及卸载还原 |

v0.22 沿用已经验证的世界分辨率与输入路径。**Steam 启动后的世界场景尚未单独复测**。

开发机器上曾观察到 4K 且 Options 打开时约 **13 FPS**，1080p 且 Options 关闭时约 **39.5 FPS**。两次界面状态不同，不构成严格的分辨率性能对照。默认采用 1080p，用户可按实际表现选择更高分辨率。

## 适用游戏版本

安装脚本校验以下原游戏文件的 SHA256，匹配后才会安装。其他游戏构建尚未验证。

| 原文件 | SHA256 |
| --- | --- |
| `SOE.EXE` | `452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B` |
| `Gfx3d.dll` | `1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078` |

## 修改初始分辨率与卸载

退出游戏后，在完整安装包目录运行以下命令，并为各次操作传入同一个实际游戏目录。

设置初始分辨率为 1440p：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Manage-SealHD.ps1 -Action Resolution -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil" -Width 2560 -Height 1440
```

卸载并恢复首次安装前的受管文件：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Manage-SealHD.ps1 -Action Uninstall -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"
```

首次安装备份保存在游戏目录的 `SealHD-backup`。升级保留首次备份，并为新加入的素材追加记录；卸载后保留备份。对于首次安装时不存在、安装后又被修改的新增文件，卸载脚本会保留该文件并输出提示。

## 源码与构建

仓库与完整安装包分开组织：

| 路径 | 内容 |
| --- | --- |
| `source/` | C++ 源文件、导出定义、三个头文件、构建脚本与初始配置，共七个文件 |
| `packaging/` | `Manage-SealHD.ps1` 与 `Install.cmd`；运行安装需要完整 Release 的 `payload` |
| `docs/` | 验证摘要与第三方说明 |
| `provenance/` | 美术提示词、尺寸和文件哈希 |

构建需要 Visual Studio 2022 Build Tools 的 MSVC C++ 工具与 Windows SDK，目标为 **x86 / 32 位**。源码包含 x86 专用实现，应使用 32 位工具链。

```powershell
cd source
.\build.cmd
```

`build.cmd` 当前调用 VS 2022 Build Tools 默认安装位置的 `vcvars32.bat`。使用其他 VS 安装位置时，先调整脚本中的工具链路径。构建产生 `source/dinput8.dll`；第三方 DLL 与美术资源由完整 Release 安装包提供。

v0.22 安装包中的二进制 `dinput8.dll` 为 186,880 字节，SHA256：

```text
3F921349A94ADC578316C5C6F235B8480E905B3104CB512E876D9CF9546AE2A3
```

## 发布文件与第三方说明

`SealHD_v0.22.zip`：47,575,985 字节，SHA256：

```text
30CF8156225D758334955A379D974296F1F02D61CA23E2AA5BB352702D33DA9C
```

安装包带有逐文件 `manifest.json`。它包含补丁源码、安装脚本、配置、第三方呈现组件和五张美术资源。原游戏程序与用户存档由用户自己的游戏安装提供。

dgVoodoo 2.87.6 来自 [Dege 的官方发行版](https://github.com/dege-diosg/dgVoodoo2/releases/tag/2.87.6)，包内三份 x86 DLL 保持原样。原游戏美术、品牌和第三方组件归各自权利人所有，详情见 [第三方说明](docs/THIRD_PARTY.md)。
