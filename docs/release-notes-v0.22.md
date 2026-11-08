# Seal of Evil HD Patch v0.22

**开发与实现：OpenAI Codex。发布者只负责审核，未编写代码。**

Implementation and development: OpenAI Codex. The publisher only reviewed the work and did not write the code.

适用于已校验构建的 Steam 版《复活：秦殇前传》（Seal of Evil）。完整安装包为 **`SealHD_v0.22.zip`**。下载后完整解压，退出游戏，再运行 `Install.cmd`；其他 Steam 库位置请用 `-GameDirectory` 指定包含 `SOE.EXE` 的游戏目录，详见 [README](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/v0.22/README.md)。

## 本版行为

- 无需开启 Windows 兼容模式，以无边框全屏显示游戏。
- 游戏内 Options 提供 800×600、1024×768、1920×1080、2560×1440、3840×2160 五档渲染尺寸，支持应用、保存和取消，默认 1080p。
- 按当前游戏画布映射鼠标与对话框，ESC、Options 和 Load Game 菜单居中。
- 高清主菜单保留原图中央物件与装饰作为重制目标，左右延展区域保持简洁，移除“试玩版”字样，并配有局部篝火动态效果。
- 提供高清开场标题和三张品牌静态卡片。品牌卡片总时长约 19.167 秒，支持 Esc、空格及点击跳过；显示期间游戏主窗口保持前台。
- 安装管理五张 PNG，升级保留首次备份，卸载恢复已记录的原文件并保留备份。

## 验证与限制

v0.22 已通过 Steam 主菜单启动、Shift+Tab 浮层打开与关闭，以及返回游戏后的 Options 和左右分辨率选择。Steam 记录该次运行退出码为 0，审计窗口内未见相关新崩溃事件或新转储。退出触发方未从记录确定。

世界分辨率与输入证据来自 v0.21 的直接启动测试：同进程完成 4K→1440p→1080p 切换与保存，重启保持设置，角色移动、ESC 菜单居中、Cancel 和 20 毫秒短点击均已实测。v0.22 保留这些核心路径；Steam 启动后的世界场景尚未单独复测。

v0.22 已实测高清标题、三张品牌卡片的前台全屏显示、ESC 跳过、持续主菜单呈现、Options 和 Load Game。Windows PowerShell 5.1 隔离测试已通过备份记录升级、重复安装与卸载还原。详细范围见 [验证摘要](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/v0.22/docs/validation.txt)。

开发机器上观察到 4K 且 Options 打开时约 13 FPS，1080p 且 Options 关闭时约 39.5 FPS。界面状态不同，不能作为严格的性能对照；本版默认 1080p。

五张美术文件均为 3840×2160 导出。四张 ImageGen 原稿为 1672×941，标题原稿为 1671×941，再经高质量双三次重采样导出 4K。流程与来源见 [AI 使用声明](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/v0.22/AI_DISCLOSURE.md) 和 [provenance](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/tree/v0.22/provenance)。

## 文件校验

完整 ZIP 为 **47,575,985 字节**，共 **26 个文件**，含记录其余 25 个文件的 `manifest.json`。

```text
SealHD_v0.22.zip
SHA256 30CF8156225D758334955A379D974296F1F02D61CA23E2AA5BB352702D33DA9C

payload/dinput8.dll
186880 bytes
SHA256 3F921349A94ADC578316C5C6F235B8480E905B3104CB512E876D9CF9546AE2A3
```

发布包已实际解压，逐文件大小和哈希与 manifest 一致；包内七个源码与对应构建源文件一致。

安装脚本只接受以下原游戏构建：

```text
SOE.EXE
452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B

Gfx3d.dll
1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078
```

包内包含 Dege 官方发行版的未修改 dgVoodoo 2.87.6 x86 组件。原游戏程序、用户存档和带有私人信息的测试截图未收入发布包。第三方归属见 [docs/THIRD_PARTY.md](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/blob/v0.22/docs/THIRD_PARTY.md)。
