# CX-512：青衣侦察者 v3 首帧传播候选

本卡把已经采用到 Unity 的 `identity.celadon_scout.v3` 作为唯一人物身份锚点，重新生成三张对话首帧候选：雾门（mist）、盟台（alliance）、救援廊（rescue）。三张候选均由 Codex 内置 ImageGen 生成静态图，再按声明的 1672×941 → 1664×936 居中裁切输出。

## 参考关系

- 人物：`unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/identity_celadon_scout_v3.png`，只用于保持脸型、发式、比例、青色衣着和单枚玉符的一致性。
- 场景：对应的 `firstframe_dialogue_scout_*_grand_v2.png` 只用于构图、建筑、光线和空间关系参考；三个旧 v2 文件保持原样，不覆盖、不移动、不进入候选输出。
- 对比审阅：`content/visual-candidates/cx512/reports/contact-sheet.cx512.png` 左列为旧 grand v2，右列为本卡 v4 候选。

## 候选清单

| 场景 | 候选首帧 | 状态 | 后续用途 |
| --- | --- | --- | --- |
| 雾门 | `content/visual-candidates/cx512/first-frames/firstframe_dialogue_scout_mist_v4.png` | `needs_review` | 人工确认后，另开卡接入对应对白视频首帧 |
| 盟台 | `content/visual-candidates/cx512/first-frames/firstframe_dialogue_scout_alliance_v4.png` | `needs_review` | 人工确认后，另开卡接入对应对白视频首帧 |
| 救援廊 | `content/visual-candidates/cx512/first-frames/firstframe_dialogue_scout_rescue_v4.png` | `needs_review` | 人工确认后，另开卡接入对应对白视频首帧 |

本卡不生成、不下载、不接入任何 Gemini/Veo 视频，也不修改 Unity 运行时资源。候选必须先由人工逐张确认身份、场景、比例、接触阴影、光线、道具数量和无文字，再决定是否在后续卡片中采用。
