# CX-513：青衣侦察者 v4 首帧采用

用户已明确确认 CX-512 的三张候选可以继续采用。本卡将它们以新文件名逐字节复制到 Unity `Resources/Generated/VideoFirstFrames/`，不覆盖任何旧资源：

| 场景 | Unity 资源 | responseId 覆盖 |
| --- | --- | --- |
| 雾门 | `Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_v4` | `npc.response.prologue.inspect_mist` + `npc.invalid.calm.*` 五类异常 |
| 盟台 | `Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_v4` | `npc.response.alliance.cautious_cooperation` + `npc.invalid.ally.*` 五类异常 |
| 救援廊 | `Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_v4` | `npc.response.rescue.secure_survivor` |

运行时 `GeneratedArtCatalog.FirstFrameForResponse` 会把这些 response 映射到对应 v4 首帧；语音回应显示时使用该整合首帧并隐藏旧的独立人物贴图，避免人物重复或拉伸。节点切换时仍恢复节点场景图。

视频仍未生成。人工在 Veo 页面生成时，直接复制 CX-505 中已经改为 v4 首帧路径的完整无声动作提示词和 TTS 提示词；不再另设 CX-513 提示词覆盖文件。视频文件生成、口型审核和视频清单登记属于后续独立任务。

旧 `grand_v2` 首帧、CX-512 review-only 清单、三张 v4 候选源文件和对比联络表均保持不变。
