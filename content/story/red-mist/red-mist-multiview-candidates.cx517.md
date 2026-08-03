# CX-517 六人十二套服装与墨蛟多视图候选

状态：`needs_review`。本卡只建立静态技术设计候选，不代表美术采用，不写入 Unity，也不恢复 CX-514。

## 生成边界

- 唯一图像提供方：Codex 内置 GPT Image 2 / `imagegen`。
- 实际 ImageGen 调用：15 次；保留 13 张候选，淘汰 2 张施峻道具不合格版本。
- Flow2API、Gemini、Veo、视频与音频调用：0。
- 13 张输出均为 1536×1024、3:2、版本化 PNG，保存在 `content/visual-candidates/cx517/turnarounds/`。
- 所有候选均为 `adopted=false`、`shipInBuild=false`，未经人工审核不得复制到 Unity。
- 六名人物各 12 种表情参考资产共 72 张，延后到多视图身份与服装通过人工审核后另开卡。

## 待审候选

| # | 资产 ID | 输出文件 | 视图 | 人工审核重点 |
|---:|---|---|---|---|
| 1 | `turnaround.outfit.shen_yan_qinglan_travel.v1` | `turnaround_outfit_shen_yan_qinglan_travel_v1.png` | 正 / 左侧 / 背 | 沈砚身份、药匣、青岚行衣、布靴 |
| 2 | `turnaround.outfit.shen_yan_xuanheng_ceremony.v1` | `turnaround_outfit_shen_yan_xuanheng_ceremony_v1.png` | 正 / 左侧 / 背 | 同脸同发型、玄衡礼服仪制、药匣 |
| 3 | `turnaround.outfit.shen_yan_yaoyuan_combat.v1` | `turnaround_outfit_shen_yan_yaoyuan_combat_v1.png` | 正 / 左侧 / 背 | 战斗层次、人体活动性、药匣、布靴 |
| 4 | `turnaround.outfit.chu_mingqi_jiyue_travel.v1` | `turnaround_outfit_chu_mingqi_jiyue_travel_v1.png` | 正 / 左侧 / 背 | 楚明绮身份、月环固定、霁月行装 |
| 5 | `turnaround.outfit.chu_mingqi_danque_ceremony.v1` | `turnaround_outfit_chu_mingqi_danque_ceremony_v1.png` | 正 / 左侧 / 背 | 非婚服、丹阙礼服、月环不变形 |
| 6 | `turnaround.outfit.chu_mingqi_chixiao_combat.v1` | `turnaround_outfit_chu_mingqi_chixiao_combat_v1.png` | 正 / 左侧 / 背 | 战斗护片、人体活动性、月环、布靴 |
| 7 | `turnaround.outfit.shi_jun_cangjin_command.v1` | `turnaround_outfit_shi_jun_cangjin_command_v1.png` | 正 / 左侧 / 背 | 施峻成熟比例、苍金执坛服、短匣非剑 |
| 8 | `turnaround.outfit.shi_jun_anyao_ritual.v1` | `turnaround_outfit_shi_jun_anyao_ritual_v1.png` | 正 / 左侧 / 背 | 黯曜仪式服、短匣连续、非全黑 |
| 9 | `turnaround.outfit.celadon_scout_listening.v1` | `turnaround_outfit_celadon_scout_listening_v1.png` | 正 / 左侧 / 背 | 成人头身比、低辫、听灵玉符固定 |
| 10 | `turnaround.outfit.celadon_scout_night_patrol.v1` | `turnaround_outfit_celadon_scout_night_patrol_v1.png` | 正 / 左侧 / 背 | 夜巡服青玉分色、玉符、布靴 |
| 11 | `turnaround.outfit.cavern_ally_stone_lamp.v1` | `turnaround_outfit_cavern_ally_stone_lamp_v1.png` | 正 / 左侧 / 背 | 洞窟盟友身份、维护良好而非破烂、石珠 |
| 12 | `turnaround.outfit.formation_spirit_star_balance.v1` | `turnaround_outfit_formation_spirit_star_balance_v1.png` | 正 / 左侧 / 背 | 阵灵身份、星衡法礼袍、可缝制服装结构 |
| 13 | `turnaround.creature.ink_dragon.v1` | `turnaround_creature_ink_dragon_v1.png` | 前 3/4 / 左侧 / 后 3/4 / 俯视 | 中国蛟龙头型、双角须鬃、完整四肢四爪、无翼；侧视遮挡须重点审查 |

## 统一审核表

人物候选逐张检查：同一身份、三视图一致、自然成人比例、手、脚、服装结构、材质重量、鞋履、连续性道具、无文字水印、无真人演员相似。墨蛟另检查完整头尾、中国蛟龙头型、角数、须鬃、鳞色、四肢、每足四爪、无翼与非蜥蜴形态。

当前 13 项全部保留为 `pending_human_review`。人工可逐项通过、要求返工或淘汰；只有通过后才可建立采用卡和 72 张表情参考资产卡。
