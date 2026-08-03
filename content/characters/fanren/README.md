# 《灵脉余烬》角色视觉圣经：第一批

版本：`lingmai-yujin-character-sheet-v2`  
状态：16/16 张 AI 概念定妆已生成并完成首轮技术检查，等待人工美术与版权审核

项目归属：本目录中的正文分析结果、人物数据、生成提示词和图像均只作为 [`lucifer8900/Video`](https://github.com/lucifer8900/Video.git) 的互动影视游戏生产资产使用。

命名层：当前显示名全部来自《灵脉余烬》固定原创化映射；稳定 ID 才是角色身份。内部源名映射禁止进入发行包，详见 [`content/originalization/README.md`](../../originalization/README.md)。现有图片本轮只改文件名和引用，尚未因此重绘。

## 交付范围

第一批选择 16 个生产角色，覆盖当前需求的全部人物类别：

| 类别 | 数量 | 人物 |
|---|---:|---|
| 主角 | 2 | 沈砚、楚明绮 |
| 主要反派 | 4 | 陆百草、裴玄蜮、玄冥岛主、初烬魔祖 |
| 主要配角 | 4 | 顾惊雨、苏绛璃、月珞、太衍君 |
| 一般配角 | 3 | 铁生、洛清音、乔清倩 |
| 剧情人物 | 1 | 尹知微 |
| 路人 NPC | 2 | 雾萝坳游修药农、引辰岛海舟女通译 |

每张角色板固定包含：

1. 正面半身。
2. 严格 90° 侧面全身服装。
3. 正面全身服装。

## 资产索引

| ID | 人物 | 分类 | 图片 |
|---|---|---|---|
| FR-MAIN-M-001 | 沈砚 | 主角 | [fr-main-m-001-character-sheet.png](images/fr-main-m-001-character-sheet.png) |
| FR-MAIN-F-001 | 楚明绮 | 主角 | [fr-main-f-001-character-sheet.png](images/fr-main-f-001-character-sheet.png) |
| FR-VIL-001 | 陆百草 | 主要反派 | [fr-vil-001-character-sheet.png](images/fr-vil-001-character-sheet.png) |
| FR-VIL-002 | 裴玄蜮 | 主要反派 | [fr-vil-002-character-sheet.png](images/fr-vil-002-character-sheet.png) |
| FR-VIL-003 | 玄冥岛主 | 主要反派 | [fr-vil-003-character-sheet.png](images/fr-vil-003-character-sheet.png) |
| FR-VIL-004 | 初烬魔祖 | 主要反派 | [fr-vil-004-character-sheet.png](images/fr-vil-004-character-sheet.png) |
| FR-SUP-001 | 顾惊雨 | 主要配角 | [fr-sup-001-character-sheet.png](images/fr-sup-001-character-sheet.png) |
| FR-SUP-002 | 苏绛璃 | 主要配角 | [fr-sup-002-character-sheet.png](images/fr-sup-002-character-sheet.png) |
| FR-SUP-003 | 月珞 | 主要配角 | [fr-sup-003-character-sheet.png](images/fr-sup-003-character-sheet.png) |
| FR-SUP-004 | 太衍君 | 主要配角 | [fr-sup-004-character-sheet.png](images/fr-sup-004-character-sheet.png) |
| FR-MIN-001 | 铁生 | 一般配角 | [fr-min-001-character-sheet.png](images/fr-min-001-character-sheet.png) |
| FR-MIN-002 | 洛清音 | 一般配角 | [fr-min-002-character-sheet.png](images/fr-min-002-character-sheet.png) |
| FR-MIN-003 | 乔清倩 | 一般配角 | [fr-min-003-character-sheet.png](images/fr-min-003-character-sheet.png) |
| FR-STORY-001 | 尹知微 | 剧情人物 | [fr-story-001-character-sheet.png](images/fr-story-001-character-sheet.png) |
| FR-NPC-001 | 雾萝坳游修药农 | 路人 NPC | [fr-npc-001-character-sheet.png](images/fr-npc-001-character-sheet.png) |
| FR-NPC-002 | 引辰岛海舟女通译 | 路人 NPC | [fr-npc-002-character-sheet.png](images/fr-npc-002-character-sheet.png) |

## 配套文件

- [世界圣经](../../../docs/fanren/2026-07-13-fanren-game-world-bible.md)
- [全书 110 项人物／生灵名录](full-cast-roster.md)
- [全书人物机器可读数据](full-cast-roster.json)
- [机器可读人物圣经](character-bible.json)
- [完整生成提示词](character-image-prompts.md)
- [图像生成清单与 SHA-256](image-generation-manifest.json)

## 人工锁定前检查

- 角色是否与现有影视、动画、游戏版本或现实演员产生明显相似。
- 三视图脸部、年龄、发型、衣服和道具是否一致。
- 是否符合原文章节锚点；美术补完有没有被误写成正史。
- 服装能否真实制作，并支持 4–8 秒视频动作和口型近景。
- 女性角色是否避免同脸、过度妆容和不合角色背景的暴露服装。
- 反派是否依靠性格、姿态和材料表达，而不是统一做成黑甲魔王。
- 通过人工审核后，再把资产状态从 `draft` 改为 `approved` 并用于视频首尾帧。
