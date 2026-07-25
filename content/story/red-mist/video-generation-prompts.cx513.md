# CX-513：青衣侦察者 v4 视频首帧采用覆盖

本文件是 `video-generation-prompts.cx505.md` 的首帧替换覆盖层。视频仍由人工在 Veo 页面生成；本卡没有调用 Gemini/Veo，也没有生成视频。

## 使用规则

1. 视频模型、英文无声表演提示词、TTS 模型和中文 Transcript 继续使用 CX-505 对应 response 段落的完整内容。
2. 只替换上传首帧：对下表 responseId 上传对应的 v4 PNG，不能同时上传旧 v2、人物定妆图或额外场景图。
3. 生成音频仍保持关闭；口型和中文字幕不由视频模型生成。TTS 仍按 CX-505 的独立干声流程执行。
4. v4 首帧已采用到 Unity `Resources/Generated/VideoFirstFrames/`，但视频文件仍需人工生成、审核并另行登记，不能把首帧采用误认为视频已完成。

## 首帧映射

| responseId | 上传首帧 |
| --- | --- |
| `npc.response.prologue.inspect_mist` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png` |
| `npc.invalid.calm.abuse` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png` |
| `npc.invalid.calm.irrelevant` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png` |
| `npc.invalid.calm.too_long` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png` |
| `npc.invalid.calm.silence` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png` |
| `npc.invalid.calm.low_confidence` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4.png` |
| `npc.response.alliance.cautious_cooperation` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png` |
| `npc.invalid.ally.abuse` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png` |
| `npc.invalid.ally.irrelevant` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png` |
| `npc.invalid.ally.too_long` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png` |
| `npc.invalid.ally.silence` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png` |
| `npc.invalid.ally.low_confidence` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4.png` |
| `npc.response.rescue.secure_survivor` | `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_v4.png` |

## 继续使用的 CX-505 视频提示词段落

- `RESPONSE npc.response.prologue.inspect_mist`：保留 CX-505 的闭口微表情和雾门动作约束，只替换为 `firstframe_dialogue_scout_mist_v4.png`。
- `RESPONSE npc.response.alliance.cautious_cooperation`：保留 CX-505 的盟台动作约束，只替换为 `firstframe_dialogue_scout_alliance_v4.png`。
- `RESPONSE npc.response.rescue.secure_survivor`：保留 CX-505 的救援廊动作约束，只替换为 `firstframe_dialogue_scout_rescue_v4.png`。
- 五类异常回应（辱骂、跑题、过长、沉默、低置信度）只复用对应场景的同一 v4 首帧和 CX-505 已审核的无声动作模板，不增加新人物、道具或场景变化。
