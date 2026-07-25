# 《赤雾秘苑》CX-508 视觉圣经与首帧候选

状态：`needs_review`。本文件和 JSON 清单只记录采用前候选，不代表已替换游戏资产。

## 本轮结论

- 使用 Codex 内置 ImageGen 生成静态图；未调用 Gemini、Veo 或任何视频生成服务。
- 7 张身份/灵兽锚点与 15 张 CX-505 首帧候选均为 1664×936 PNG，严格 16:9。
- 输出只在 `content/visual-candidates/cx508/`；未复制到 Unity `Resources`、`StreamingAssets` 或 Addressables。
- 现有 `*_grand_v2.png` 未覆盖。全部候选保持 `adopted=false`、`shipInBuild=false`。
- 每张图的完整规范化生成提示词、参考图 ID/哈希/角色、输出哈希、裁切方式、复用视频 ID 和九项审核状态，以 `red-mist-visual-bible.cx508.json` 为唯一机器可读记录。

## 世界材质与摄影合同

原创真人影视化修仙世界统一使用象牙白玉、暖色浅石、朱漆木构、老化鎏金铜、青瓷瓦、云海和瀑布。赤雾只作为受控危险层，不覆盖整幅画面。建筑必须具备可施工的柱梁、承重、台阶与尺度；人物必须共享主光、天空填充、地面反射和接触阴影。

禁止文字、伪文字、水印、现实演员或公众人物相似、真实地标复刻、清代服饰、现代物件、动漫/插画质感、蜡像皮肤、贴片人物、非等比拉伸、漂浮道具和重复肢体。

人物照片只承担姿势、重心、皮肤和微表情参考，`identityReuse=false`；建筑照片只承担结构/材料作用，未复制真实地标。所有来源图均为 `referenceOnly=true`、`shipInBuild=false`，未验证来源不得声称商业或修改权。

## 身份与道具连续性

| 资产 ID | 锚点 | 固定连续性 |
|---|---|---|
| `identity.shen_yan.v1` | 沈砚 | 深青窄袖行装；双手空；一只闭合暗金飞刃匣 |
| `identity.chu_mingqi.v1` | 楚明绮 | 月白/暗朱战袍；一枚腰挂刚性月轮；不得变成包裹 |
| `identity.shi_jun.v1` | 石峻 | 褐黑短甲；右手一枚 28 厘米短阵钉；不得变成长矛 |
| `identity.celadon_scout.v1` | 青衣侦察者 | 低位编发；单侧肩甲；胸前一枚固定听风玉符 |
| `identity.cavern_ally.v1` | 蛟窟盟友 | 灰白轻甲；胸前一枚固定白玉信标；双手空 |
| `identity.formation_spirit.v1` | 阵灵 | 单一中性成人投影；一只实体晶座；玉色边缘不过曝 |
| `creature.ink_dragon.v1` | 墨蛟 | 单头、无角、无翼、总共两条前肢、一段连续躯干和尾巴 |

石峻初版把阵钉放大成长矛、墨蛟初版多出后肢，均已定点重生成；最终清单分别引用 `identity_shi_jun_v2.png` 和 `creature_ink_dragon_v2.png`。旧迭代只保留在 `rejected/` 供比较。

## 15 张首帧与视频复用

| 资产 ID | 输出 | 复用范围 |
|---|---|---|
| `firstframe.node.camp.v3` | `firstframe_node_camp_v3.png` | `shot.camp.primary` |
| `firstframe.node.alliance.v3` | `firstframe_node_alliance_v4.png` | `shot.alliance.primary` |
| `firstframe.node.corpse_signs.v3` | `firstframe_node_corpse_signs_v3.png` | `shot.corpse_signs.primary` |
| `firstframe.node.rescue.v3` | `firstframe_node_rescue_v3.png` | `shot.rescue.primary` |
| `firstframe.shijun.negotiation.v3` | `firstframe_shijun_negotiation_v4.png` | 石峻节点、主回应、5 个 hostile 异常回应 |
| `firstframe.node.formation.v3` | `firstframe_node_formation_v3.png` | `shot.formation.primary` |
| `firstframe.node.combat_one.v3` | `firstframe_node_combat_one_v3.png` | `shot.combat_one.primary` |
| `firstframe.node.combat_two.v3` | `firstframe_node_combat_two_v3.png` | `shot.combat_two.primary` 的首次预览 |
| `firstframe.node.aftermath.v3` | `firstframe_node_aftermath_v3.png` | `shot.aftermath.primary` 的首次预览 |
| `firstframe.node.ending.v3` | `firstframe_node_ending_v4.png` | `shot.ending.primary` |
| `firstframe.dialogue.scout_mist.v3` | `firstframe_dialogue_scout_mist_v3.png` | 主回应 `inspect_mist` 与 5 个 calm 异常回应 |
| `firstframe.dialogue.scout_alliance.v3` | `firstframe_dialogue_scout_alliance_v3.png` | 主回应 `cautious_cooperation` 与 5 个 ally 异常回应 |
| `firstframe.dialogue.scout_rescue.v3` | `firstframe_dialogue_scout_rescue_v3.png` | 主回应 `secure_survivor` |
| `firstframe.dialogue.formation_spirit.v3` | `firstframe_dialogue_formation_spirit_v3.png` | 5 个 system 异常回应 |
| `firstframe.dialogue.cavern_ally.v3` | `firstframe_dialogue_cavern_ally_v3.png` | 主回应 `coordinate_retreat` 与 5 个 encounter 异常回应 |

结盟图的门额伪字、石峻图的案台伪字和结局图缺少帮扶者已经定点修订；最终清单引用三张 `v4`，原 `v3` 留作拒绝对照。战斗二与战后的最终视频连续性规则不变：人工获准前段视频后，优先使用获准末帧，而不是静态候选。

## 后处理

常规原始输出为 1672×941；采用居中等比裁切到 1664×936，只移除边缘像素，不做缩放或拉伸。三张基于已裁切输入的定点修订图直接输出 1664×936，记录为 `postProcess.mode=none`。

## 人工审核清单

下列九项目前全部为 `pending`，必须由人工逐图确认后才可另开采用卡：

1. 真实皮肤与布料；
2. 脚部承重；
3. 接触阴影；
4. 共享环境光；
5. 透视与尺度；
6. 手部与道具稳定；
7. 无文字/伪文字/水印；
8. 未复刻真实地标；
9. 未出现现实演员或公众人物相似。

联络表：`content/visual-candidates/cx508/reports/contact-sheet.cx508.png`。

本卡只产出采用前视觉候选，不修改剧情、台词、角色名称、世界规则、Unity 运行时资源或视频生成计划。
