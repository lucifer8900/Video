# 《赤雾秘苑》CX-505 人工视频生成提示词

状态：`manual_generation_only`。本文件只供人工复制到 Veo on Agent Platform 或其他经人工批准的工具；项目代码不得自动提交、轮询或下载视频。现有 `fmv_gate_arrival`、`fmv_celestial_flight`、`fmv_herb_courtyard`、`fmv_sword_vault` 继续复用，不在本批次重做。

## 模型选择与复制规则

不要全部使用 Gemini Omni Flash。

| 资产类型 | 预览模型 | 最终候选 | 原因 |
|---|---|---|---|
| 10 个无对白主剧情环境视频 | Veo 3.1 Fast | Veo 3.1 | 优先保证电影质感、复杂运动和首尾帧控制；此组不需要角色说话 |
| 30 个带台词 NPC 回应视频 | Gemini Omni Flash | 仍用 Gemini Omni Flash，但必须先过 G2 人工一致性闸门 | Omni Flash 可直接输出带音频视频，适合先验证中文口型；它仍是 Preview，中文和跨片声线必须人工审查 |
| 30 条独立干声 | Gemini 3.1 Flash TTS (Preview) | Gemini 2.5 Pro TTS | 3.1 Flash 用于快速试音；2.5 Pro 用于锁定后的高保真最终干声 |

重要限制：

- Veo 3.1 环境视频选 16:9、24 fps、8 秒；预览先用 720p，接受后再用 Veo 3.1 输出 1080p 或界面允许的更高分辨率。
- Gemini Omni Flash 对话视频选 16:9、24 fps、10 秒。它会自动生成音轨，但中文尚未完成官方评估，也不能上传参考语音或后改声线；不要一次生成全部 30 条。
- 先用同一名青衣侦察者生成 10 条对话预览。至少 8 条的人脸、发型、服装、口型和色调达到人工 4/5，且中文台词没有增删，才继续剩余对话视频。
- TTS 生成的是独立干声和声线母版，不会自动与另一段视频对口型。若 Omni Flash 的原生中文口型不合格，先停下，不要直接把 TTS 覆盖到嘴型不匹配的视频上；后续必须另选人工批准的口型方案。
- 视频页面只复制对应的视频代码块；语音页面只复制对应的语音代码块。下面每一条都已经包含全部必要约束，不再需要手工追加公共段落。
- 视频提示词使用英文，是因为视频模型对英文指令支持最稳定；引号内中文必须原样保留。TTS 使用 Standard Mandarin Chinese，并在界面中固定本文指定的 voice。

人物连续性：沈砚为二十余岁东亚男性，冷静克制，深青窄袖行装、旧皮护腕、暗金飞刃匣；楚明绮为二十余岁东亚女性，沉着威严，月白与暗朱分层战袍、银色月纹发冠、环形月轮法器；石峻为三十余岁东亚男性，瘦削、眼神锐利，褐黑短甲与旧铜扣，手持染血阵钉。青衣侦察者为二十七岁左右的原创东亚女性，低位编发、青灰窄袖袍、炭黑皮肩甲与小型白玉听风符。阵灵为成年中性人形投影。蛟窟临时盟友为二十九岁左右的原创东亚男性，束起黑发、灰白分层轻甲与白光信标。所有人物不得模仿现实演员。

现有参考图位于 Unity 的 Assets/Resources/Art 与 Assets/Resources/Generated 目录。有人物的镜头应上传相应定妆图；同一 NPC 第一条通过后，从该条导出清晰静帧，后续全部作为同一 subject reference 使用。

## 10 个主剧情视频缺口

<!-- node:camp -->
### NODE camp — 临时营地备战

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。参考图：red_mist_establishing.png、shen_yan.png、chu_mingqi.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Slow lateral dolly through a temporary cultivator camp built on black volcanic terraces above a sea of vermilion mist. In the foreground, hands check paper wards, medicine gourds, and a compact flying-blade case on a worn jade-lit table. Midground disciples tighten layered robes and test a faint defensive barrier; background bronze gate mechanisms pulse inside the mountain. Shen Yan, an original East Asian man in his twenties wearing a dark-celadon narrow-sleeved travel outfit, old leather wrist guards, and a dark-gold flying-blade case, and Chu Mingqi, an original East Asian woman in her twenties wearing layered moon-white and dark-vermilion battle robes, a silver lunar hair crown, and a circular moon-ring artifact, prepare in separate parts of the same frame so this clip works for either playable route. Both compare the escape trail with the sealed gate and reach toward different supply bundles without choosing. Quiet tension before an expedition, practical firelight mixed with cold jade light, and clean lower-right negative space for later Unity UI. Do not imitate any real actor. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- node:alliance -->
### NODE alliance — 谨慎结伴

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。优先首帧：firstframe_alliance_shen_gu_v1.png 或 firstframe_alliance_chu_petitioners_v1.png；人物参考：shen_yan.png、chu_mingqi.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Medium-wide two-shot on a narrow cliff platform above layered red mist. Shen Yan, an original East Asian man in his twenties wearing a dark-celadon narrow-sleeved travel outfit, old leather wrist guards, and a dark-gold flying-blade case, faces Chu Mingqi, an original East Asian woman in her twenties wearing layered moon-white and dark-vermilion battle robes, a silver lunar hair crown, and a circular moon-ring artifact. They remain at a respectful distance while rival disciples watch from separate formations. A gust lifts cloth edges and reveals concealed talisman light, but neither draws a weapon. Each marks a different safe line across a floating physical stone map, then they briefly agree on one shared route while still hiding their strongest artifacts. Use subtle eye acting and restrained distrust, with mountain shrine silhouettes and moving spirit lanterns in the distance. End on a balanced composition with clean lower-right space for a dialogue-choice overlay. Do not imitate any real actor. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- node:corpse_signs -->
### NODE corpse_signs — 溪涧伏击痕迹

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。参考图：red_mist_weather.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Low tracking shot along a shallow stream beneath a supernatural red-leaf canopy. Water briefly runs backward around a fallen adult cultivator; three hair-thin cuts glow on black bark, and heat-dried silver web strands tremble above wet mud. Show only the playable character's gloved hands and shoulder from behind so the clip works for either route. The character kneels without touching the body and releases a narrow pulse of spiritual perception; translucent fragments reconstruct two attackers moving in opposite directions, then collapse when a distant branch bends. Somber, forensic, tense, with no gore close-up and clean visual room for an interaction prompt. Do not imitate any real actor. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- node:rescue -->
### NODE rescue — 雾后求援

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。参考图：red_mist_weather.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Use a controlled handheld approach toward a broken black-stone wall swallowed by layered vermilion mist. Two short impacts and one long impact shake dust from the wall. An adult wounded disciple is barely visible behind a translucent jade barrier while a second moving shadow circles outside it. Show the playable character from behind signaling the team to stop, one companion raising a defensive ward, and a thin line of jade light testing an old rescue code without anyone entering. Make the dilemma visually clear: a real survivor, a possible ambush, and a closing bronze gate far behind. End before the rescue choice. Do not imitate any real actor. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- node:shijun -->
### NODE shijun — 阵钉交易

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Cinematic medium shot before four monumental dark-bronze doors. Shi Jun is an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. He steps from a deep architectural shadow, turns the spike between two fingers as fresh black mud steams from it, and lays a genuine moon-palace token on a pale-jade plinth while keeping his other hand near a concealed weapon. The playable party is visible only as two guarded silhouettes studying the mud, token, and reflected doors. Hostile negotiation through precise facial acting and a slow push-in; end with the spike, medicine pouch, and doorway all visually readable as bargaining cues. Do not imitate any real actor. No spoken dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- node:formation -->
### NODE formation — 四门禁制

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。优先首帧：scene_celestial_formation_hall_v3.png；辅助参考：red_mist_modules.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Begin overhead and descend into a slow orbit inside a vast circular bronze gate chamber. Four large physical formation hubs made from aged bronze, blue stone, medicinal-leaf crystal, and a suspended water mirror send light around an incomplete circuit. Two adult cultivators walk between them while three hubs respond with elegant jade-gold illumination and the fourth remains dark. When one hand nears the wrong hub, carved guardian-beast eyes open in the wall; when the hand withdraws, they dim. The mechanism must feel luxurious, ancient, tactile, and interactive, with volumetric light and monumental stone scale; show obvious physical cause and effect rather than flat symbols. Do not imitate any real actor. No dialogue, music, flat game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- node:combat_one -->
### NODE combat_one — 墨蛟试探

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。参考图：dragon_cavern-v2.png、dragon_attack-v1.png、shen_yan.png、chu_mingqi.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language. Dynamic wide combat shot in a geothermal black-mud cavern. A massive ink-scaled hornless dragon erupts with believable weight and fluid displacement, using its tail to seal a narrow escape causeway while boiling mud rises toward wounded adult disciples. Shen Yan, in his dark-celadon travel outfit with dark-gold flying blades, tests one scale seam; Chu Mingqi, in moon-white and dark-vermilion battle robes, shapes a defensive arc with her circular moon-ring artifact on the opposite flank. Neither releases a final technique. Sweep the camera low behind shattered jade railings with strong parallax, readable attack lanes, physical debris, and grounded impacts. Build urgency and stop on the tactical decision beat without showing victory. Do not imitate any real actor. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless movement, unexplained teleportation, or abrupt cuts.
~~~

<!-- node:combat_two -->
### NODE combat_two — 底牌与崩塌

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。必须沿用 combat_one 获准首尾帧；参考图：dragon_cavern-v2.png、dragon_attack-v1.png、shen_yan.png、chu_mingqi.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language. Continue in the exact same geothermal cavern with identical faces, costumes, weapons, creature scale, damage, lighting, and color grade as the approved combat-one clip. The wounded ink-scaled hornless dragon slams support pillars beneath a suspended white-jade pavilion; cracks spread, a gold chest starts sinking, and steam briefly hides both fighters. Shen Yan prepares a chained dark-gold blade formation while Chu Mingqi contains a ring of restrained vermilion fire, each watching whether the other will commit. One injured adult disciple struggles toward the previously marked escape route. Use a fast but legible camera and realistic impacts, ending on the split-second choice between ultimate technique, cooperation, and retreat. Do not imitate any real actor. No victory, dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless movement, unexplained teleportation, or abrupt cuts.
~~~

<!-- node:aftermath -->
### NODE aftermath — 战利品与伤员

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。必须沿用 combat_two 获准末帧；参考图：dragon_cavern-v2.png、shen_yan.png、chu_mingqi.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language. Slow exhausted tableau in the same damaged white-jade pavilion after the ink dragon withdraws beneath black mud. The central gold chest, one wounded adult disciple, a bundle of medicinal roots, and the narrowing exit occupy four distinct directions. Shen Yan and Chu Mingqi retain the exact faces, costumes, weapons, injuries, lighting, and color grade from the approved combat clips. They lower their weapons and exchange a guarded look; one reaches toward the injured person while the other catches the sliding chest, then both stop before committing. Dust, steam, and faint red mist cross the frame with practical structural damage and believable fatigue. Keep the emotion restrained and leave clean visual room for the final choice. Do not imitate any real actor. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless movement, or abrupt cuts.
~~~

<!-- node:ending -->
### NODE ending — 雾门余烬

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。参考图：red_mist_establishing.png、shen_yan.png、chu_mingqi.png。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Dawn outside the same mountain gate as the opening, now transformed by the expedition's aftermath. The bronze fissure closes behind a small group of adult survivors as red mist folds inward like a tide. In one continuous crane-up composition, show a rescued disciple supported between companions, a wrapped medicine case, an empty hand stained with black mud, and one concealed artifact that remains unexposed. Shen Yan and Chu Mingqi both appear in the foreground so the clip remains valid for either playable route; they keep exact approved faces, hair, costumes, weapon scale, and accumulated damage, look back once, then walk toward distant sect lanterns without celebrating. The mood is bittersweet continuation rather than final triumph. Do not imitate any real actor. No dialogue, music, title text, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

## 5 个主要语音回应视频

<!-- response:npc.response.prologue.inspect_mist -->
### RESPONSE npc.response.prologue.inspect_mist

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频。参考图：red_mist_weather.png；青衣侦察者首条通过后追加其固定定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, a single continuous medium close-up with no scene cuts. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. At the living bronze mist gate, show one original East Asian woman expedition scout, about twenty-seven years old, with an oval face, straight black brows, a low braided ponytail, a muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and a small pale-jade wind-listening talisman. She studies the mist rhythm beside the unseen player, points once toward the safer left stair, then looks back without deciding for the player. Her face, hair, costume, talisman, body proportions, lighting, and color grade must match every other scout clip. Audio: one speaker only, Standard Mandarin Chinese, focused and calm. She says exactly once with natural synchronized lips and no paraphrase: “雾流每九息回卷一次，左侧石阶的风更稳。你看清后，再从眼前方案里决定路线。” Use only quiet layered wind, distant bronze resonance, and soft cloth movement behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- tts:response:npc.response.prologue.inspect_mist -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: She has measured the supernatural mist cycle at a dangerous bronze gate and gives the player one useful observation without choosing the route for them.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, calm focus, restrained urgency, medium-low volume, and an even pace. Keep the performance human and cinematic, not like a system assistant or audiobook narrator. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾流每九息回卷一次，左侧石阶的风更稳。你看清后，再从眼前方案里决定路线。”
~~~

<!-- response:npc.response.alliance.cautious_cooperation -->
### RESPONSE npc.response.alliance.cautious_cooperation

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频。参考图：red_mist_establishing.png、已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, a single continuous medium shot with no scene cuts. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. On a narrow cliff alliance platform, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. She takes the left guard position while keeping her weapon sheathed. Moving red mist and distant sect lantern formations remain visible behind her. Her face, hair, costume, talisman, body proportions, lighting, and color grade must match every other scout clip. Audio: one speaker only, Standard Mandarin Chinese, patient and steady with guarded warmth. She says exactly once with natural synchronized lips and no paraphrase: “我守住左侧，你保留自己的底牌。先活着越过山隘，之后再谈彼此的来路。” Use only mountain wind, faint ward resonance, and soft cloth movement behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- tts:response:npc.response.alliance.cautious_cooperation -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: On a dangerous cliff route she offers limited cooperation while respecting that the player will keep a hidden technique in reserve.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, patient steadiness, guarded warmth, medium-low volume, and an even pace. Keep the performance human and cinematic, not romantic and not like a narrator. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “我守住左侧，你保留自己的底牌。先活着越过山隘，之后再谈彼此的来路。”
~~~

<!-- response:npc.response.rescue.secure_survivor -->
### RESPONSE npc.response.rescue.secure_survivor

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频。参考图：red_mist_weather.png、已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, a single continuous medium close-up with no scene cuts. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language; the red mist must never look like ordinary forest fog. Beside a broken black-stone rescue wall, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. She hears a second footstep inside the mist, raises one hand to stop the group, and glances toward a hidden wounded survivor while a physical jade ward shimmers behind. Her face, hair, costume, talisman, body proportions, lighting, and color grade must match every other scout clip. Audio: one speaker only, Standard Mandarin Chinese, urgent but controlled. She says exactly once with natural synchronized lips and no paraphrase: “雾后还有第二个人的脚步声。先用旧暗号核验身份，我会贴着石墙等你下令。” Use only muffled impacts behind the wall, layered mist wind, and a faint ward hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, weightless camera, or abrupt cuts.
~~~

<!-- tts:response:npc.response.rescue.secure_survivor -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: She has detected a possible second attacker beyond a broken wall and urgently proposes verifying the survivor before entering.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled urgency, precise consonants, medium volume, and a brisk but fully intelligible pace. Keep the performance human and tactical, never panicked. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾后还有第二个人的脚步声。先用旧暗号核验身份，我会贴着石墙等你下令。”
~~~

<!-- response:npc.response.shijun.verify_bargain -->
### RESPONSE npc.response.shijun.verify_bargain

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频。参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, a single continuous medium close-up with no scene cuts. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language. Before four monumental bronze doors, show Shi Jun as the same original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. He holds the heated spike near camera so fresh black mud steams, then gives a thin guarded half-smile without advancing or drawing another weapon. His face, hair, costume, spike, body proportions, lighting, and color grade must match every Shi Jun clip. Audio: one speaker only, Standard Mandarin Chinese, suspicious and lightly taunting. He says exactly once with natural synchronized lips and no paraphrase: “想验货可以，阵钉上的热泥来自门后。至于这句话值不值一株药，由你自己判断。” Use only low bronze resonance, gentle steam, and distant stone movement behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, sudden attack, weightless camera, or abrupt cuts.
~~~

<!-- tts:response:npc.response.shijun.verify_bargain -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: He presents real evidence but makes the player decide whether the information is worth a medicinal plant.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, a dry lightly gravelly texture, restrained mockery, medium-low volume, and deliberate pacing. Keep the threat implicit rather than shouted. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “想验货可以，阵钉上的热泥来自门后。至于这句话值不值一株药，由你自己判断。”
~~~

<!-- response:npc.response.underground.coordinate_retreat -->
### RESPONSE npc.response.underground.coordinate_retreat

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频。参考图：dragon_cavern-v2.png；首条通过后追加蛟窟临时盟友固定定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, a single continuous medium two-layer shot with no scene cuts. Ancient dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion mist, and muted celadon cloth form the shared visual language. In the geothermal black-mud cavern, show one original East Asian man temporary ally, about twenty-nine years old, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman. He stands behind a cracked white-jade railing, marks the first retreat stone with a narrow white beam, and keeps watching the distant ink dragon's tail. The scene is tactical rather than romantic. His face, hair, costume, talisman, body proportions, lighting, and color grade must match every cavern-ally clip. Audio: one speaker only, Standard Mandarin Chinese, focused and controlled. He says exactly once with natural synchronized lips and no paraphrase: “玉栏后的青石是第一处退点。我守住蛟尾方向，你用一道白光示警，是否合击仍由你决定。” Use only distant mud movement, stressed stone, steam, and a low creature rumble behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, excessive bloom, sudden attack, weightless camera, or abrupt cuts.
~~~

<!-- tts:response:npc.response.underground.coordinate_retreat -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: While tracking an ink dragon in a collapsing cavern, he identifies a retreat point and proposes a simple signal while leaving the final cooperation choice to the player.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, tactical focus, medium-low volume, firm articulation, and a measured pace that remains audible over danger. Keep it cooperative but not intimate. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “玉栏后的青石是第一处退点。我守住蛟尾方向，你用一道白光示警，是否合击仍由你决定。”
~~~

## 25 个异常输入回应视频（按角色族复用）

以下五个角色族保持固定机位、服装和环境，只改变表情、停顿与台词；这样 25 条视频可跨多个节点复用，同时不会把一个普通森林镜头误当成仙家场景。

### Calm Scout / 冷静侦察者

统一画面：original celadon-robed expedition scout, medium close-up beside layered red mist and a dim bronze mechanism, subtle background movement, direct but non-aggressive eye line toward the player, exact Mandarin lip sync.

<!-- response:npc.invalid.calm.abuse -->
#### RESPONSE npc.invalid.calm.abuse

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Beside layered translucent vermilion mist and a dim living bronze mechanism, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. She reacts with one stern micro-expression, then deliberately resets to a controlled neutral posture without touching her sheathed weapon. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, firm but controlled. She says exactly once with natural synchronized lips and no paraphrase: “言语再重也不会改变雾中的局势。若要继续，就说清你准备怎么做。” Use only faint supernatural wind and bronze resonance behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.calm.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player has used insulting language during a dangerous mist investigation; she refuses escalation and redirects attention to an actionable decision.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, restrained firmness, medium-low volume, and an even pace. Show a trace of disapproval without anger or sarcasm. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “言语再重也不会改变雾中的局势。若要继续，就说清你准备怎么做。”
~~~

<!-- response:npc.invalid.calm.irrelevant -->
#### RESPONSE npc.invalid.calm.irrelevant

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Beside layered translucent vermilion mist and a dim living bronze mechanism, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. She briefly looks toward a moving shadow inside the active danger, then returns a direct but non-aggressive eye line to the player. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, calm and redirecting. She says exactly once with natural synchronized lips and no paraphrase: “那件事与眼前的险境无关。先看清此地，再决定下一步。” Use only faint supernatural wind and bronze resonance behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.calm.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player has changed the subject while danger remains active; she redirects them to the immediate environment without sounding mechanical.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, calm focus, medium-low volume, and an even patient pace. The correction is concise and nonjudgmental. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “那件事与眼前的险境无关。先看清此地，再决定下一步。”
~~~

<!-- response:npc.invalid.calm.too_long -->
#### RESPONSE npc.invalid.calm.too_long

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Beside layered translucent vermilion mist and a dim living bronze mechanism, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. The mist pulse visibly accelerates; she makes one concise hand signal asking for brevity while keeping watch on the gate. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, concise and time-aware. She says exactly once with natural synchronized lips and no paraphrase: “雾势正在变化。把话收短，只说你此刻要做什么。” Use only rising supernatural wind and a restrained bronze pulse behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.calm.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The supernatural mist is changing while the player gives an overlong answer; she needs one immediate executable choice.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled urgency, medium volume, and a brisk but intelligible pace. Do not sound impatient with the person; urgency comes from the environment. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾势正在变化。把话收短，只说你此刻要做什么。”
~~~

<!-- response:npc.invalid.calm.silence -->
#### RESPONSE npc.invalid.calm.silence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Beside layered translucent vermilion mist and a dim living bronze mechanism, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. She waits in attentive silence for one full second, then gives a small reassuring nod without moving closer or pressuring the player. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, patient and reassuring. After the visible pause she says exactly once with natural synchronized lips and no paraphrase: “不必仓促开口。先看清线索，想好后再作选择。” Use only faint supernatural wind and bronze resonance behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.calm.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player remains silent while examining clues; she permits reflection without taking control of the decision.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, patient reassurance, medium-low volume, and a slightly slower pace. Do not insert leading silence into the audio file; the game video supplies the pause. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “不必仓促开口。先看清线索，想好后再作选择。”
~~~

<!-- response:npc.invalid.calm.low_confidence -->
#### RESPONSE npc.invalid.calm.low_confidence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Beside layered translucent vermilion mist and a dim living bronze mechanism, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. She makes one small listening gesture near the jade talisman and shows a subtly uncertain brow, without pretending to understand. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, candid and helpful. She says exactly once with natural synchronized lips and no paraphrase: “这句话没有听清。换个简短说法，或直接选择眼前的方案。” Use only faint supernatural wind and bronze resonance behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.calm.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: Wind and mist have made the player's intent unclear; she honestly asks for a shorter restatement or a fixed choice.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, mild uncertainty without confusion, medium-low volume, and an even helpful pace. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “这句话没有听清。换个简短说法，或直接选择眼前的方案。”
~~~

### Ally Scout / 同行侦察者

统一画面：same original team scout on a cliff route, defensive ward and sect silhouettes behind, medium close-up, guarded warmth, exact Mandarin lip sync.

<!-- response:npc.invalid.ally.abuse -->
#### RESPONSE npc.invalid.ally.abuse

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. On a narrow cliff route above layered translucent vermilion mist, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. A defensive ward and distant sect silhouettes remain behind her. She shows a brief hurt but disciplined expression while her weapon remains fully sheathed, then returns attention to the route. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, wounded but controlled. She says exactly once with natural synchronized lips and no paraphrase: “若连同行者都只剩恶言，这条路便走不远。说出你的判断，别把力气浪费在争吵上。” Use only cliff wind and a faint ward hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.ally.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: A temporary companion has insulted her during a dangerous crossing; she acknowledges the damage but refuses to waste time escalating.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, restrained hurt beneath discipline, medium-low volume, and deliberate pacing. Do not sound melodramatic or hostile. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “若连同行者都只剩恶言，这条路便走不远。说出你的判断，别把力气浪费在争吵上。”
~~~

<!-- response:npc.invalid.ally.irrelevant -->
#### RESPONSE npc.invalid.ally.irrelevant

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. On a narrow cliff route above layered translucent vermilion mist, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. A defensive ward and distant sect silhouettes remain behind her. She points first toward the ancient entrance and then toward the waiting adult team, calmly returning the conversation to the active choice. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, practical and patient. She says exactly once with natural synchronized lips and no paraphrase: “那件事可以以后再谈。入口和同伴都在等我们决定。” Use only cliff wind and a faint ward hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.ally.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player raises an unrelated matter while the entrance and companions wait; she redirects without dismissing the topic forever.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, practical patience, medium-low volume, and an even pace. Keep the tone collaborative and concise. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “那件事可以以后再谈。入口和同伴都在等我们决定。”
~~~

<!-- response:npc.invalid.ally.too_long -->
#### RESPONSE npc.invalid.ally.too_long

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. On a narrow cliff route above layered translucent vermilion mist, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. A defensive ward and distant sect silhouettes remain behind her. She gives one urgent glance toward the visibly closing bronze gate, then marks three physical route directions with a compact hand gesture. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, urgent and concise. She says exactly once with natural synchronized lips and no paraphrase: “没有时间听完整段缘由。告诉我结论：前进、观察，还是撤回？” Use only cliff wind, a closing bronze mechanism, and a faint ward hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.ally.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The gate is closing while the player gives a long explanation; she requests one of three clear conclusions.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled time pressure, medium volume, and a brisk but fully intelligible pace. Give equal verbal weight to “前进、观察，还是撤回” without sounding like a menu announcer. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “没有时间听完整段缘由。告诉我结论：前进、观察，还是撤回？”
~~~

<!-- response:npc.invalid.ally.silence -->
#### RESPONSE npc.invalid.ally.silence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. On a narrow cliff route above layered translucent vermilion mist, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. A defensive ward and distant sect silhouettes remain behind her. She takes and holds the left-flank guard position, waits without pressure, then looks back to confirm that the decision remains with the player. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, understanding but firm about agency. She says exactly once with natural synchronized lips and no paraphrase: “你还在权衡，我明白。我会守住这一侧，但决定必须由你作出。” Use only cliff wind and a faint ward hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.ally.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player remains silent while weighing options; she commits to guarding one side but explicitly leaves the decision to them.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, quiet understanding, medium-low volume, and measured confidence. Place subtle emphasis on “必须由你作出” without sounding coercive. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “你还在权衡，我明白。我会守住这一侧，但决定必须由你作出。”
~~~

<!-- response:npc.invalid.ally.low_confidence -->
#### RESPONSE npc.invalid.ally.low_confidence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的青衣侦察者定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. On a narrow cliff route above layered translucent vermilion mist, show the exact same approved East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. A defensive ward and distant sect silhouettes remain behind her. She leans slightly toward the player to hear over a gust, then indicates the still-visible physical route choices without guessing. Her face, hair, costume, talisman, proportions, lighting, and color grade must match every scout clip. Audio: one speaker only, Standard Mandarin Chinese, candid and cooperative. She says exactly once with natural synchronized lips and no paraphrase: “我没听清你的意思。再说一遍，或指明你支持哪个方案。” Use only cliff wind and a faint ward hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.ally.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: A strong cliff wind has obscured the player's intent; she asks for repetition or a direct indication without pretending to understand.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, mild uncertainty, cooperative warmth, medium-low volume, and an even pace. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “我没听清你的意思。再说一遍，或指明你支持哪个方案。”
~~~

### Formation Spirit / 阵灵界面

统一画面：a life-sized translucent human-faced formation spirit projected from a physical jade-bronze hub, elegant immortal mechanism rather than flat UI, clear mouth and natural Mandarin lip sync, surrounding runes react softly to speech.

<!-- response:npc.invalid.system.abuse -->
#### RESPONSE npc.invalid.system.abuse

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；首条通过后固定阵灵人形静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Inside a monumental jade-bronze formation chamber, show the exact same approved life-sized translucent adult androgynous formation spirit projected from a physical hub. The spirit has a calm human face, pale-jade inner light, fine dark-bronze circuit filigree, readable natural lips, and no floating game panels. Insulting input causes the surrounding runes to dim and physically reject the signal, while the spirit remains composed. Face, projection geometry, color, light level, hub design, camera, and grade must match every formation-spirit clip. Audio: one speaker only, Standard Mandarin Chinese, even and neutral. The spirit says exactly once with natural synchronized lips and no paraphrase: “无效言语不会改变结算。请给出可执行指令。” Use only a low formation hum and one soft rejection pulse behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, flat UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate faces, design drift, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.system.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: The formation receives abusive but non-executable speech and safely refuses to treat it as an action.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even measured tone, medium-low volume, and precise articulation. Sound ancient and humane rather than robotic or like a modern assistant. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “无效言语不会改变结算。请给出可执行指令。”
~~~

<!-- response:npc.invalid.system.irrelevant -->
#### RESPONSE npc.invalid.system.irrelevant

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的阵灵人形静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Inside a monumental jade-bronze formation chamber, show the exact same approved life-sized translucent adult androgynous formation spirit projected from a physical hub. The spirit has a calm human face, pale-jade inner light, fine dark-bronze circuit filigree, readable natural lips, and no floating game panels. A physical route channel, carved formation eye, and three-dimensional tactical marker illuminate in sequence around the hub as the spirit redirects an irrelevant input. Face, projection geometry, color, light level, hub design, camera, and grade must match every formation-spirit clip. Audio: one speaker only, Standard Mandarin Chinese, even and explanatory. The spirit says exactly once with natural synchronized lips and no paraphrase: “输入与当前目标无关。请围绕路线、阵眼或战术作答。” Use only a low formation hum and three soft illumination pulses behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, readable text, flat UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate faces, design drift, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.system.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: The input is unrelated to the active objective; the spirit identifies the three valid subject areas without choosing one.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even informative tone, medium-low volume, and deliberate articulation. Sound ancient and humane rather than robotic. Give equal emphasis to “路线、阵眼或战术”. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “输入与当前目标无关。请围绕路线、阵眼或战术作答。”
~~~

<!-- response:npc.invalid.system.too_long -->
#### RESPONSE npc.invalid.system.too_long

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的阵灵人形静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Inside a monumental jade-bronze formation chamber, show the exact same approved life-sized translucent adult androgynous formation spirit projected from a physical hub. The spirit has a calm human face, pale-jade inner light, fine dark-bronze circuit filigree, readable natural lips, and no floating game panels. An overlong physical light waveform circles the hub, safely collapses, and leaves one empty carved command socket glowing. Face, projection geometry, color, light level, hub design, camera, and grade must match every formation-spirit clip. Audio: one speaker only, Standard Mandarin Chinese, concise and safety-focused. The spirit says exactly once with natural synchronized lips and no paraphrase: “指令过长，无法安全判定。请缩减为一个明确动作。” Use only a low formation hum and a soft collapsing-light sound behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, readable text, flat UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate faces, design drift, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.system.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: An overlong instruction cannot be mapped safely to one atomic action, so the formation asks for a single clear command.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even safety-focused tone, medium-low volume, and concise measured pacing. Sound ancient and humane rather than robotic. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “指令过长，无法安全判定。请缩减为一个明确动作。”
~~~

<!-- response:npc.invalid.system.silence -->
#### RESPONSE npc.invalid.system.silence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的阵灵人形静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Inside a monumental jade-bronze formation chamber, show the exact same approved life-sized translucent adult androgynous formation spirit projected from a physical hub. The spirit has a calm human face, pale-jade inner light, fine dark-bronze circuit filigree, readable natural lips, and no floating game panels. The spirit waits without movement for one second while several fixed physical mechanisms remain gently illuminated and fully available. Face, projection geometry, color, light level, hub design, camera, and grade must match every formation-spirit clip. Audio: one speaker only, Standard Mandarin Chinese, neutral and reassuring. After the visible pause the spirit says exactly once with natural synchronized lips and no paraphrase: “未收到有效指令。场景仍在等待，固定方案保持可用。” Use only a low formation hum behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, readable text, flat UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate faces, design drift, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.system.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: No valid instruction was received; the formation remains safely paused and confirms that fixed choices are still available.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even reassuring tone, medium-low volume, and unhurried pacing. Do not insert leading silence into the audio file; the game video supplies the pause. Sound ancient and humane rather than robotic. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “未收到有效指令。场景仍在等待，固定方案保持可用。”
~~~

<!-- response:npc.invalid.system.low_confidence -->
#### RESPONSE npc.invalid.system.low_confidence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；使用已通过的阵灵人形静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Inside a monumental jade-bronze formation chamber, show the exact same approved life-sized translucent adult androgynous formation spirit projected from a physical hub. The spirit has a calm human face, pale-jade inner light, fine dark-bronze circuit filigree, readable natural lips, and no floating game panels. Several ambiguous physical rune branches appear around the hub, then fade safely without firing any mechanism; the fixed carved choices remain lit. Face, projection geometry, color, light level, hub design, camera, and grade must match every formation-spirit clip. Audio: one speaker only, Standard Mandarin Chinese, precise and non-alarming. The spirit says exactly once with natural synchronized lips and no paraphrase: “指令置信度不足。请重新表述，或使用固定方案。” Use only a low formation hum and a soft fading pulse behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, readable text, flat UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate faces, design drift, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.system.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: The formation cannot map an ambiguous instruction with sufficient confidence and offers a safe restatement or fixed-choice fallback.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even non-alarming tone, medium-low volume, and precise measured pacing. Sound ancient and humane rather than robotic. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “指令置信度不足。请重新表述，或使用固定方案。”
~~~

### Shi Jun / 敌对谈判者

统一画面：Shi Jun at the four bronze doors, exact continuity, formation spike in hand, medium close-up, no attack, natural Mandarin lip sync.

<!-- response:npc.invalid.hostile.abuse -->
#### RESPONSE npc.invalid.hostile.abuse

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Before four monumental dark-bronze doors, show the exact same Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. His dry smile disappears; he shifts one step to block the doorway while keeping every concealed weapon undrawn. Face, hair, armor, spike, proportions, lighting, camera, and color grade must match every Shi Jun clip. Audio: one speaker only, Standard Mandarin Chinese, dry and controlled. He says exactly once with natural synchronized lips and no paraphrase: “骂得再响，也换不来一条真路。拿出筹码，或者让开。” Use only low bronze resonance and faint steaming mud behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.hostile.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player insults him at a guarded doorway; he refuses to be moved and demands a real bargaining chip or passage.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, dry gravelly restraint, medium-low volume, and deliberate pacing. The threat is credible but never shouted. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “骂得再响，也换不来一条真路。拿出筹码，或者让开。”
~~~

<!-- response:npc.invalid.hostile.irrelevant -->
#### RESPONSE npc.invalid.hostile.irrelevant

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Before four monumental dark-bronze doors, show the exact same Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. Without advancing, he taps the formation spike, a medicine pouch, and the blocked doorway in that order to define the only current bargaining subjects. Face, hair, armor, spike, proportions, lighting, camera, and color grade must match every Shi Jun clip. Audio: one speaker only, Standard Mandarin Chinese, impatient but controlled. He says exactly once with natural synchronized lips and no paraphrase: “少绕弯子。此刻只谈阵钉、灵药和谁先让路。” Use only low bronze resonance, three small object taps, and faint steam behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.hostile.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player has gone off topic during a tense standoff; he narrows the bargain to the spike, medicine, and right of passage.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, dry impatience, medium-low volume, and clipped deliberate pacing. Give separate weight to “阵钉、灵药和谁先让路”. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “少绕弯子。此刻只谈阵钉、灵药和谁先让路。”
~~~

<!-- response:npc.invalid.hostile.too_long -->
#### RESPONSE npc.invalid.hostile.too_long

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Before four monumental dark-bronze doors, show the exact same Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. He turns the spike once between two fingers with visible impatience, then holds it still and waits for one concise condition. Face, hair, armor, spike, proportions, lighting, camera, and color grade must match every Shi Jun clip. Audio: one speaker only, Standard Mandarin Chinese, dismissive but controlled. He says exactly once with natural synchronized lips and no paraphrase: “我没兴致听你铺陈来历。用一句话报出条件。” Use only low bronze resonance and a small metal movement behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.hostile.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player gives an overlong personal history instead of a bargain; he demands one concise condition.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled dismissal, medium-low volume, and clipped pacing. Avoid theatrical villain laughter or shouting. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “我没兴致听你铺陈来历。用一句话报出条件。”
~~~

<!-- response:npc.invalid.hostile.silence -->
#### RESPONSE npc.invalid.hostile.silence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Before four monumental dark-bronze doors, show the exact same Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. He lets the silence stretch for one full second while the door light visibly wanes, then tilts the spike once without attacking. Face, hair, armor, spike, proportions, lighting, camera, and color grade must match every Shi Jun clip. Audio: one speaker only, Standard Mandarin Chinese, dry and patient in a threatening way. After the visible pause he says exactly once with natural synchronized lips and no paraphrase: “沉默也是价码，只是通常最贵。想好了就开口。” Use only low bronze resonance and fading mechanism light behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.hostile.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player remains silent while the door window closes; he treats silence as a bargaining choice and waits.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, dry implied threat, medium-low volume, and slow deliberate pacing. Do not insert leading silence into the audio file; the game video supplies the pause. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “沉默也是价码，只是通常最贵。想好了就开口。”
~~~

<!-- response:npc.invalid.hostile.low_confidence -->
#### RESPONSE npc.invalid.hostile.low_confidence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：shi_jun-v1.png、red_mist_modules.png。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous medium close-up with no cuts. Before four monumental dark-bronze doors, show the exact same Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike. His eyes narrow at an ambiguous statement, but his body remains still and he makes no sudden aggressive movement. Face, hair, armor, spike, proportions, lighting, camera, and color grade must match every Shi Jun clip. Audio: one speaker only, Standard Mandarin Chinese, suspicious and cautionary. He says exactly once with natural synchronized lips and no paraphrase: “你这句话含糊得很。再说清楚，免得我按最坏的意思理解。” Use only low bronze resonance and faint steam behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.hostile.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player's meaning is ambiguous during a hostile bargain; he asks for clarification before assuming the worst.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, wary suspicion, medium-low volume, and deliberate pacing. Keep aggression restrained and make the warning credible. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “你这句话含糊得很。再说清楚，免得我按最坏的意思理解。”
~~~

### Cavern Ally / 蛟窟临时盟友

统一画面：original temporary ally behind cracked white-jade railing in the black-mud cavern, ink dragon movement visible far behind, tactical medium close-up, exact Mandarin lip sync.

<!-- response:npc.invalid.encounter.abuse -->
#### RESPONSE npc.invalid.encounter.abuse

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：dragon_cavern-v2.png，并使用已通过的蛟窟临时盟友定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous tactical medium close-up with no cuts. In a geothermal black-mud cavern behind a cracked white-jade railing, show the exact same approved East Asian man temporary ally, about twenty-nine, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman. Distant ink-dragon movement remains visible through steam. Instead of escalating an insult, he checks the dragon's location, keeps his weapon ready but lowered, and returns attention to the escape route. Face, hair, armor, talisman, proportions, lighting, camera, and color grade must match every cavern-ally clip. Audio: one speaker only, Standard Mandarin Chinese, urgent but disciplined. He says exactly once with natural synchronized lips and no paraphrase: “黑泥下的东西不会等我们吵完。收起敌意，先决定如何活着离开。” Use only mud movement, stressed stone, steam, and a low distant creature rumble behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack toward camera, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.encounter.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: The player directs hostility at him while an ink dragon moves beneath the mud; he refuses the argument and returns focus to survival.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, disciplined urgency, medium volume, and firm measured pacing. Do not sound offended or preachy; the danger drives the line. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “黑泥下的东西不会等我们吵完。收起敌意，先决定如何活着离开。”
~~~

<!-- response:npc.invalid.encounter.irrelevant -->
#### RESPONSE npc.invalid.encounter.irrelevant

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：dragon_cavern-v2.png，并使用已通过的蛟窟临时盟友定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous tactical medium close-up with no cuts. In a geothermal black-mud cavern behind a cracked white-jade railing, show the exact same approved East Asian man temporary ally, about twenty-nine, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman. Distant ink-dragon movement remains visible through steam. He indicates a gold chest, the marked retreat line, and a new mud ripple in that order, keeping all three physical facts visible in depth. Face, hair, armor, talisman, proportions, lighting, camera, and color grade must match every cavern-ally clip. Audio: one speaker only, Standard Mandarin Chinese, practical and focused. He says exactly once with natural synchronized lips and no paraphrase: “宝匣、退路和墨蛟才是眼前事实。别让无关的话暴露我们的空隙。” Use only mud movement, stressed stone, steam, and a low distant creature rumble behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack toward camera, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.encounter.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: The player goes off topic during the encounter; he identifies the chest, retreat route, and ink dragon as the immediate facts.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, tactical focus, medium volume, and crisp measured pacing. Give distinct weight to “宝匣、退路和墨蛟”. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “宝匣、退路和墨蛟才是眼前事实。别让无关的话暴露我们的空隙。”
~~~

<!-- response:npc.invalid.encounter.too_long -->
#### RESPONSE npc.invalid.encounter.too_long

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：dragon_cavern-v2.png，并使用已通过的蛟窟临时盟友定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous tactical medium close-up with no cuts. In a geothermal black-mud cavern behind a cracked white-jade railing, show the exact same approved East Asian man temporary ally, about twenty-nine, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman. Distant ink-dragon movement remains visible through steam. A large physical formation pillar develops a spreading crack during the shot; he glances once at it and compresses his hand signal to one executable action. Face, hair, armor, talisman, proportions, lighting, camera, and color grade must match every cavern-ally clip. Audio: one speaker only, Standard Mandarin Chinese, urgent and concise. He says exactly once with natural synchronized lips and no paraphrase: “阵柱正在断裂。把计划压成一句，我只需要可执行的部分。” Use only cracking stone, mud movement, steam, and a low distant creature rumble behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack toward camera, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.encounter.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: A formation pillar is breaking while the player gives a long plan; he needs the one executable part immediately.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled urgency, medium volume, and a brisk but intelligible pace. The first sentence reports danger; the second requests one action. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “阵柱正在断裂。把计划压成一句，我只需要可执行的部分。”
~~~

<!-- response:npc.invalid.encounter.silence -->
#### RESPONSE npc.invalid.encounter.silence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：dragon_cavern-v2.png，并使用已通过的蛟窟临时盟友定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous tactical medium close-up with no cuts. In a geothermal black-mud cavern behind a cracked white-jade railing, show the exact same approved East Asian man temporary ally, about twenty-nine, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman. Distant ink-dragon movement remains visible through steam. He takes a conservative guard stance covering the marked retreat route, waits without attacking, and keeps a second signal ready so the player can still change the decision. Face, hair, armor, talisman, proportions, lighting, camera, and color grade must match every cavern-ally clip. Audio: one speaker only, Standard Mandarin Chinese, calm under pressure. He says exactly once with natural synchronized lips and no paraphrase: “你不表态，我便按最保守的方式守住退路。仍可在墨蛟扑出前改变决定。” Use only mud movement, stressed stone, steam, and a low distant creature rumble behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack toward camera, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.encounter.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: The player remains silent during an active encounter; he chooses the safest temporary guard while preserving the player's chance to change course.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, calm pressure, medium volume, and firm measured pacing. The line must protect agency rather than sound like an ultimatum. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “你不表态，我便按最保守的方式守住退路。仍可在墨蛟扑出前改变决定。”
~~~

<!-- response:npc.invalid.encounter.low_confidence -->
#### RESPONSE npc.invalid.encounter.low_confidence

视频模型与设置：Gemini Omni Flash；16:9，24 fps，10 秒，原生音频；参考图：dragon_cavern-v2.png，并使用已通过的蛟窟临时盟友定妆静帧。

完整视频提示词（Gemini Omni Flash，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 10 seconds, one continuous tactical medium close-up with no cuts. In a geothermal black-mud cavern behind a cracked white-jade railing, show the exact same approved East Asian man temporary ally, about twenty-nine, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman. Distant ink-dragon movement remains visible through steam. He leans slightly through a passing steam cloud to listen, then gives one clear uncertainty signal rather than guessing the player's intent. Face, hair, armor, talisman, proportions, lighting, camera, and color grade must match every cavern-ally clip. Audio: one speaker only, Standard Mandarin Chinese, candid and focused. He says exactly once with natural synchronized lips and no paraphrase: “雾声盖住了你的话。再说得明确些，或者直接选定行动。” Use only mud movement, stressed stone, steam, and a low distant creature rumble behind the clean voice. Do not imitate any real actor. No narrator, extra voices, music, echo, subtitles, captions, text, game UI, logos, modern objects, anime style, plastic skin, stretched anatomy, duplicate people, costume drift, attack toward camera, excessive bloom, weightless camera, or cuts.
~~~

<!-- tts:response:npc.invalid.encounter.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: Steam and supernatural noise obscure the player's words; he requests a clearer statement or direct action without guessing.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, alert candor, medium volume, and a concise even pace. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾声盖住了你的话。再说得明确些，或者直接选定行动。”
~~~

## 人工交付规则

### 第一批：先做 G2 小样，不要一次生成全部文件

1. 在语音页面先生成 4 个声线小样：青衣侦察者选 Kore、阵灵选 Schedar、石峻选 Algenib、蛟窟盟友选 Iapetus。每个角色先用本组第一条语音提示试听。
2. 人工确认四个 voice 后，先生成青衣侦察者的 10 个 Omni Flash 视频预览：3 个主要回应、5 个 calm 回应，再从 ally 组选前 2 个。生成时始终上传同一张获准侦察者定妆静帧。
3. 只有至少 8/10 达到人脸、发型、服装、口型、台词准确度和色调人工 4/5，才继续剩余 20 个 NPC 回应视频。若失败，停下并告诉 Codex，不要用增加生成次数掩盖问题。
4. 10 个无对白主剧情镜头可独立用 Veo 3.1 Fast 做预览，但最终 Veo 3.1 版本必须等人物和世界风格通过后再生成。

### 文件命名

- 主剧情视频：`node__{nodeId}__preview_v01.mp4`
- NPC 回应视频：`response__{responseId}__preview_v01.mp4`
- 独立语音：`voice__{responseId}__preview_v01.wav`
- 最终版本把 `preview` 改为 `final`，不覆盖预览文件。

### 交付与暂停点

生成后把文件放入一个新的人工暂存目录并告诉 Codex 该目录的绝对路径。不要覆盖现有 `fmv_gate_arrival`、`fmv_celestial_flight`、`fmv_herb_courtyard`、`fmv_sword_vault` 四段 MP4。在收到明确指令前，Codex 不得把新视频登记为 `approved`、不得修改非零预算、不得调用任何视频 API。

独立 TTS 干声不能直接替换 Omni Flash 视频中的音轨来冒充口型同步。若 Omni Flash 中文台词、声线或口型不通过，需要人工决定下一种口型工具或制作方法，Codex 将在这里暂停。

## 官方能力依据

- [Gemini Omni Flash 官方文档](https://ai.google.dev/gemini-api/docs/omni)：可从文字生成带音频视频，但仍为 Preview；官方说明中文等非英语尚未评估，并且当前不支持上传参考音频或 voice editing。
- [Veo 3.1 官方模型说明](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/veo/3-1-generate)：用于稳定的文本/图片到视频、首尾帧与高分辨率环境镜头。
- [Gemini TTS 官方说明](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts)：文字输入、音频输出，可用自然语言控制角色、语气、节奏和口音；Gemini 3.1 Flash TTS 适合快速试音，Gemini 2.5 Pro TTS 适合最终高保真语音。
