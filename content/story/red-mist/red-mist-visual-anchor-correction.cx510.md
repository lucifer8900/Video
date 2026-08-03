# CX-510 青衣侦察者比例修正

本卡只修正 `identity.celadon_scout.v2` 的头身比例。v2 及 CX-509 其它四张身份锚点保持原文件不变；v3 是新的、尚未采用的人工复核候选。

## 产物

- 候选：`content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png`
- 对照联络表：`content/visual-candidates/cx510/reports/contact-sheet.cx510.png`
- 清单：`red-mist-visual-anchor-correction.cx510.json`
- Schema：`server/Contracts/schemas/red-mist-visual-anchor-correction.schema.json`

## 修正约束

- 保留 v2 的青绿色魏晋/宋制衣着语言、配色、宏伟仙门、云海和明亮冷暖光；不引入现代或清朝服饰，不加入鞋履以外的现代物件。
- 使用自然人体约 `7.5 heads`、标准 `50mm` 等效镜头、`no wide-angle distortion`；头部不能放大，肩、躯干、双腿和脚部比例自然，完整露出双脚。
- 双手保持空置，不改变角色道具数量；人物与石质地面有接触阴影，建筑竖线和环境光保持连续。
- 输出严格为 1664×936 PNG，等比中心裁切；不覆盖 v2，不进入 Unity、Resources、StreamingAssets 或 Addressables。

## 状态

候选仍为 `needs_review`、`adopted=false`、`shipInBuild=false`。本卡只使用 Codex 内置 ImageGen；未调用 Gemini、Veo 或其它视频服务。是否采用由人工视觉复核决定。
