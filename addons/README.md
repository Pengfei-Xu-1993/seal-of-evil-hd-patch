# 汉化与技能 MOD 附加包

这些是现有高清补丁之外的可选附加包，使用自己的受支持英文 Steam 版游戏。

| 下载 | 内容 |
| --- | --- |
| [汉化补丁 ZIP](packages/SealHD-Chinese-Port.zip?raw=true) | 中文菜单、对白、物品/技能文本、人物显示名和地图名，以及缺少的 UI 字形映射 |
| [技能 MOD ZIP](packages/SealHD-Skill-Mod.zip?raw=true) | 主角团零 CD；地焰全向范围 ×2；投石、排山倒海和胆战心惊全向范围 ×3，包含角色招式清单修复 |

[SHA256 校验值](packages/SHA256SUMS.txt)。每个 ZIP 都带完整安装、恢复脚本和原资源副本，不含个人存档或完整游戏。

先安装 [HD 完整安装包](https://github.com/Pengfei-Xu-1993/seal-of-evil-hd-patch/releases/tag/v0.26)，再安装汉化。技能 MOD 可独立安装，与汉化可任意先后安装。

1. 保存进度并退出游戏，先备份游戏目录及存档。
2. 将需要的 ZIP 完整解压。
3. 运行包内 `Install-Chinese.cmd` 或 `Install-Skill-Mod.cmd`。
4. 重新从 Steam 启动并读取存档。恢复时退出游戏并运行对应 Restore 脚本。

默认目录为 `D:\Steam\steamapps\common\Seal of Evil`。其他目录可从终端运行：

```bat
Install-Chinese.cmd -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"
Install-Skill-Mod.cmd -GameDirectory "E:\SteamLibrary\steamapps\common\Seal of Evil"
```

脚本检查原版、已安装版及明确支持的上一版哈希；遇到独立修改的资源会停止。两个包都保留高清 DLL、图片和显示设置，不编辑个人存档。

## 技能修改与验证范围

- 五个主角职业的全部156条技能定义（含关联形态）取消冷却，动作/忙碌时间及资源消耗保留。
- 地焰（火焰阵）改为12方向，范围6→12、强化8→16。炎龙烈火的攻击方式不改。
- 天外之石（投石）改为24方向，范围6→18、强化10→30。
- 排山倒海（爆破）和胆战心惊/惊天动地（震地）均为施法者周身全向攻击；震地由原先一条线改为12方向，范围6→18。
- 原生判伤是覆盖圆盘的方形区域；视觉发射覆盖360度。使用原敌我过滤及每目标单次命中上限。

**角色招式清单和胆战心惊的最终修正版没有再次进行游戏内验证。** 已完成静态原生逻辑、范围传递、白名单、引用、文件格式和安装/恢复检查；不将这些检查当作实战测试。具体范围见包内 `VALIDATION.json`。

汉化覆盖启动、菜单、设置、1080p、开场对白和原存档加载，未完成全剧情通关；旧存档缓存的英文名称可能保留。本包不含中文配音或影片。

中文文本按旧中文版资源与 Steam 现有编号移植；旧版安装器/程序没有运行或移入。资源原权利归其权利人所有。移植、技能修改和打包由 OpenAI Codex 开发实现，项目所有者审核发布。
