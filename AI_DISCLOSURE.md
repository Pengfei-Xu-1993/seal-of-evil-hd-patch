# AI 使用与来源声明

**开发与实现：OpenAI Codex。发布者只负责审核，未编写代码。**

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

静态美术使用 ImageGen 编辑。本声明记录工作分工、验证范围和已有资源的来源。

## 发布者的审核角色

发布者负责审核工作并授权公开发布，包括审阅功能、画面与测试结果。代码实现、开发、构建、包装和下述测试操作由 Codex 完成。

## Codex 参与

Codex 实施 C++ 代理 DLL、显示和输入适配、高清画面呈现、PowerShell 安装与恢复脚本，以及配置、构建、包装和文档。Codex 还执行实机操作、检查运行日志与进程状态，并核对安装、卸载、备份和发布文件哈希。源码与脚本包含 AI 编写和修改的内容。

项目记录区分静态文件核对、隔离安装测试与实际游戏运行。v0.22 的 Steam 证据覆盖主菜单、浮层开关、返回操作和退出；世界分辨率与输入证据来自 v0.21 直接启动测试，Steam 世界场景尚未单独复测。具体结果见 [docs/validation.txt](docs/validation.txt)。

## ImageGen 与 4K 导出

ImageGen 根据原游戏参考画面和提示词编辑主菜单、开场标题与三张品牌静态卡片。主菜单重制以保留中央原有物件和纹理为目标，新增宽屏区域控制物件数量；标题图以保留三件悬挂饰物及中央内容为目标。

| 文件 | ImageGen 原稿尺寸 | 最终 PNG 尺寸 |
| --- | --- | --- |
| `mainmenu.png` | 1672×941 | 3840×2160 |
| `intro-zoom.png` | 1672×941 | 3840×2160 |
| `intro-object.png` | 1672×941 | 3840×2160 |
| `intro-strategy.png` | 1672×941 | 3840×2160 |
| `opening-title.png` | 1671×941 | 3840×2160 |

最终 4K 文件由上述生成原稿经高质量双三次重采样导出。提示词见 [provenance/artwork-prompts.txt](provenance/artwork-prompts.txt)，尺寸和文件哈希见 [provenance/artwork-manifest.json](provenance/artwork-manifest.json)。

## 原游戏与第三方组件

原游戏场景、美术、名称及品牌标识归各自权利人所有。AI 编辑使用了这些已有画面作为参考，其来源在上述工作记录中保留。

dgVoodoo 2.87.6 由 Dege 提供；安装包中的 x86 `D3D8.dll`、`DDraw.dll` 和 `D3DImm.dll` 来自官方发行版，保持未修改。它们属于独立第三方组件，详情见 [docs/THIRD_PARTY.md](docs/THIRD_PARTY.md)。

用户的发布授权针对本项目的制作与发布操作。各项原游戏资源、商标和第三方组件的归属仍分别记录，不能据此推定为用户、Codex 或 ImageGen 原创。
