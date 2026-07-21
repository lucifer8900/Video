# 《赤雾秘苑》CX-505 人工视频生成提示词

状态：`manual_generation_only`。本文件只供人工复制到 Veo on Agent Platform 或其他经人工批准的工具；项目代码不得自动提交、轮询或下载视频。现有 `fmv_gate_arrival`、`fmv_celestial_flight`、`fmv_herb_courtyard`、`fmv_sword_vault` 继续复用，不在本批次重做。

## 模型选择与复制规则

本批次不使用 Gemini Omni Flash 生成对白视频：Veo 只负责无声画面，Gemini TTS 只负责独立干声。

| 资产类型 | 预览模型 | 最终候选 | 原因 |
|---|---|---|---|
| 10 个无对白主剧情环境视频 | Veo 3.1 Fast | Veo 3.1 | 优先保证电影质感、复杂运动和首尾帧控制；此组不需要角色说话 |
| 30 个 NPC 回应无声表演底片 | Veo 3.1 Fast（Image-to-video） | Veo 3.1（Image-to-video） | 只生成人物微表情和动态背景，不输入中文台词，避免假口型与乱码字幕；对白由独立 TTS 生成 |
| 30 条独立干声 | Gemini 3.1 Flash TTS (Preview) | Gemini 2.5 Pro TTS | 3.1 Flash 用于快速试音；2.5 Pro 用于锁定后的高保真最终干声 |

重要限制：

- Veo 3.1 环境视频选 16:9、24 fps、8 秒；预览先用 720p，接受后再用 Veo 3.1 输出 1080p 或界面允许的更高分辨率。
- NPC 回应画面统一使用 Veo 3.1 `Image-to-video`、16:9、24 fps、8 秒，并关闭“生成音频”；画面只做闭口微表情与动态背景，提示词中不得出现中文台词。
- 先用青衣侦察者的 `inspect_mist` 生成 1 条无声表演底片。确认人脸、发型、服装、闭口状态、人物落地感、亮暗层次均达到人工 4/5 后，再生成其余回应画面。
- 若界面允许选择“结果数量”，首轮一律设为 `1`；只有单条提示词稳定后才可按人工需要增加候选。界面生成的多条结果只是随机候选，供人工筛选，不代表不同剧情分支，也不得把四条候选全部登记进游戏。
- TTS 只生成独立干声，不需要也不能上传首帧。Veo 无声底片与 TTS 干声不能直接叠加冒充口型同步；最终口型合成属于单独人工决策点，在批准工具与流程前暂停。
- 视频页面只复制对应的视频代码块；语音页面只复制对应的语音代码块。下面每一条都已经包含全部必要约束，不再需要手工追加公共段落。
- Veo 视频提示词全部使用英文，且不含任何中文台词；TTS 提示词保留完整中文 Transcript，使用 Standard Mandarin Chinese，并固定本文指定的 voice。

人物连续性：沈砚为二十余岁东亚男性，冷静克制，深青窄袖行装、旧皮护腕、暗金飞刃匣；楚明绮为二十余岁东亚女性，沉着威严，月白与暗朱分层战袍、银色月纹发冠、环形月轮法器；石峻为三十余岁东亚男性，瘦削、眼神锐利，褐黑短甲与旧铜扣，手持染血阵钉。青衣侦察者为二十七岁左右的原创东亚女性，低位编发、青灰窄袖袍、炭黑皮肩甲与小型白玉听风符。阵灵为成年中性人形投影。蛟窟临时盟友为二十九岁左右的原创东亚男性，束起黑发、灰白分层轻甲与白光信标。所有人物不得模仿现实演员。

本批次已用 GPT Image 2 将人物身份、场景、光线和镜头构图合成到 15 张 16:9 首帧中，统一位于 `unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/`。在当前 Veo `Image-to-video` 页面，每次只上传对应条目列出的这一张首帧；不要再追加人物定妆图、场景图或素材模块表，也不要把可选结束图槽位当成人物参考槽位。15 张图片的复用关系和 GPT Image 2 生成提示词见 `video-first-frame-reference-pack.cx505.md`。

## 10 个主剧情视频缺口

<!-- node:camp -->
### NODE camp — 临时营地备战

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_camp_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. Keep all key action inside a 2.39:1 cinematic safe area without rendering black bars. The temporary command camp occupies a majestic ivory-jade terrace beneath vermilion lacquer columns and an expedition awning; a physical relief route table stands before Shen Yan and Chu Mingqi, while gilded sanctuary roofs, waterfalls, cloud sea and one sealed bronze mountain gate fill the bright distance. Preserve visually calm lower-right space for later Unity UI.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Treat the uploaded first frame as a locked production plate. Preserve the exact camera side, character count, faces, hair, costumes, body proportions, screen positions, camp architecture, table layout, barrier location, lighting direction, and color grade. Shen Yan and Chu Mingqi remain in their original positions and never cross or leave frame.
PROP LOCK: Chu Mingqi's single circular moon-ring remains rigidly fixed at the exact waist attachment, position and orientation shown in the first frame; its diameter, metal shape, surface pattern and red tassel never change. Shen Yan's single dark-gold flying-blade case stays closed and fixed. The physical relief table and every visible lantern or table fitting keep their exact count, shape, material, color, orientation and position. Both characters remain empty-handed; no new scroll, bundle, package or handheld prop may appear.
PRIMARY ACTION: The only deliberate character action is Chu Mingqi shifting her gaze once from the physical route map toward the sealed bronze gate; her head turns less than ten degrees and her hands remain still.
ALLOWED MOTION: Natural blinking and breathing, tiny cloth-edge and loose-hair movement, low fire flicker, a faint barrier shimmer, slow layered red-mist drift, one restrained gate-light pulse, and a very slow lateral camera dolly of less than five percent of frame width.
FORBIDDEN TRANSITIONS: No object substitution, morphing, folding, melting, growth, duplication, disappearance, reappearance, hand-object fusion, costume drift, face drift, pseudo-writing, new glyphs, new props, unscripted gestures, walking, teleportation, or camera cuts. No hand, cloth, fog, or foreground object may fully occlude a face or locked prop. The moon-ring must never become cloth, a pouch, rope, scroll, belt, or package.
END STATE: In the final frame every person, prop, building, light source, and barrier has the same identity, count, material, attachment, and position as the first frame; only Chu Mingqi's gaze and the phase of the ambient mist and light may differ. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, excessive bloom, or imitation of any real actor.
~~~

<!-- node:alliance -->
### NODE alliance — 谨慎结伴

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_alliance_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken shot with no cuts. On a vast sunlit ivory-jade oath terrace above a cloud sea, Shen Yan and Chu Mingqi hold a wary two-shot across one carved physical relief map; separate adult disciple formations wait under vermilion colonnades while gilded crane lanterns, a celestial embassy gate and mountain palaces remain in depth. Preserve visually calm lower-right space for a later dialogue-choice overlay.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's camera side, faces, hairstyles, costumes, body proportions, character count, spacing, cliff geometry, disciple formations, shrine silhouettes, lighting direction, and color grade. No person changes formation or crosses the frame.
PROP LOCK: Chu Mingqi's circular moon-ring and Shen Yan's dark-gold flying-blade case remain rigidly attached exactly where shown. The physical stone route map, talismans, sheathed weapons, ward fittings, and distant spirit lanterns keep the same count, geometry, material, and position. No hand touches the map, a weapon, a talisman, or another person.
PRIMARY ACTION: The only story action is one already-carved jade route line on the stone map illuminating from near to far, stopping before the mountain pass, and fading back to its original brightness. Neither character causes it with a gesture.
ALLOWED MOTION: Natural blinking, breathing, restrained eye movement toward the map, a small gust moving only loose cloth edges and hair tips, slow red-mist parallax, distant lantern drift, and an almost imperceptible camera push-in.
FORBIDDEN TRANSITIONS: No prop exchange, drawing weapons, pointing, map contact, object morphing, substitution, duplication, disappearance, new route lines, changing inscriptions, pseudo-text, costume or face drift, new people, walking, teleportation, occluded faces, or cuts.
END STATE: The final frame restores the route line to its initial brightness and preserves every face, body, costume, prop, character position, building, and map feature exactly as at the start. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, excessive bloom, or imitation of any real actor.
~~~

<!-- node:corpse_signs -->
### NODE corpse_signs — 溪涧伏击痕迹

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_corpse_signs_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, natural skin and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous unbroken low tracking shot through an abandoned celestial outer garden. A bright white-jade stream stair passes gilded water wheels, moon bridges and red maples beside one motionless fallen adult cultivator, exactly three hair-thin cuts in one black-barked tree, and one heat-dried silver web trace over wet stones. Show only the playable character's existing empty gloved hand and shoulder from behind. Keep clean visual room for an interaction prompt and avoid a gore close-up.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's camera side, corpse pose and clothing, visible player silhouette, exact three bark cuts, web pattern, stream banks, tree count, canopy, lighting, and color grade. The fallen cultivator remains completely motionless and unchanged.
PROP LOCK: Gloves, wrist clothing, any visible weapon attachment, the three cuts, web strands, stones, leaves, and forensic traces keep their exact shapes, counts, materials, and positions. The player never touches the body, bark, water, web, or any object.
PRIMARY ACTION: One narrow jade spiritual-perception pulse travels once across the existing three bark cuts and fades; the cuts brighten without changing shape or number.
ALLOWED MOTION: Gentle reverse water flow around fixed stones, slight web vibration, tiny leaf movement, natural breathing of the player silhouette, and a slow low camera track that never changes viewing side.
FORBIDDEN TRANSITIONS: No reconstructed attackers, new silhouettes, moving corpse, gore reveal, prop pickup, hand gesture, object morphing, changing cut count, changing web geometry, duplicated limbs, new text or symbols, face reveal, teleportation, heavy fog occlusion, or cuts.
END STATE: The pulse has faded and all bodies, traces, props, plants, stones, and camera relationships match the first frame; only water and leaves may be at a later natural phase. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, excessive bloom, or imitation of any real actor.
~~~

<!-- node:rescue -->
### NODE rescue — 雾后求援

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_rescue_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous restrained push inside a damaged but majestic sanctuary procession gallery of vermilion columns, ivory relief walls and gilded ward anchors. One wounded adult disciple and exactly one unresolved adult shadow remain behind the translucent jade barrier; Shen Yan and Chu Mingqi are already stopped before it while the distant sunlit bronze gate closes. End before any rescue choice.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded frame's camera side, exact person count, silhouettes, poses, faces if visible, costumes, wall fracture, barrier boundary, outside shadow outline, distant gate geometry, lighting, and grade. Nobody enters, exits, approaches, falls, or changes pose.
PROP LOCK: All wards, weapons, talismans, wall stones, barrier fittings, debris, and costume attachments remain fixed in count, shape, material, orientation, and attachment. Hands remain still and away from every prop.
PRIMARY ACTION: After one muted offscreen impact, the existing jade barrier emits one restrained light ripple across its unchanged surface and returns to its starting brightness.
ALLOWED MOTION: Natural blinking and breathing, small cloth and hair-tip movement, a light dust fall from the already-broken wall, slow mist layering that never hides a person, and a very slow camera push-in.
FORBIDDEN TRANSITIONS: No second impact sequence, new person, circling shadow, hand signal, raised ward, rescue-code drawing, prop interaction, object or body morphing, barrier shape change, shadow becoming a creature, disappearance, duplication, pseudo-text, teleportation, or cuts.
END STATE: All people, the outside shadow, props, wall pieces, barrier geometry, and gate structure have the same identities, counts, positions, and materials as the first frame; the barrier is back at its initial brightness. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, excessive bloom, or imitation of any real actor.
~~~

<!-- node:shijun -->
### NODE shijun — 阵钉交易

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, natural skin, realistic armor, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous cinematic medium shot in the majestic Four-Gate audience court, where ivory-jade mechanical doors rise beneath vermilion beams, gilded brackets and bright clerestory windows. Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor and worn copper fasteners, faces two guarded playable-party shoulder silhouettes. The single formation spike, moon-palace token, medicine pouch, pale-jade plinth and blocked doors remain separately readable.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hair, armor, body proportions, pose, character count, silhouette positions, four-door architecture, plinth, reflected light, camera side, and color grade. Shi Jun remains at the same distance and never advances or leaves frame.
PROP LOCK: The blood-marked bronze formation spike remains rigidly held in the same hand, grip, orientation, length, metal shape, mud pattern, and screen position. The token and medicine pouch remain untouched on their exact first-frame surfaces. Concealed weapons stay concealed; every visible object keeps its count, material, shape, and position.
PRIMARY ACTION: The only deliberate action is Shi Jun shifting his eyes once from the fixed token to the playable party and forming a restrained half-smile; his head, torso, arms, hands, and spike remain still.
ALLOWED MOTION: Natural blinking and breathing, minimal facial muscle movement, a thin stable thread of steam above the existing mud, one low door-light pulse, and a slow push-in of less than five percent.
FORBIDDEN TRANSITIONS: No stepping, hand movement, spike rotation, item placement, prop exchange, weapon draw, mud growth, object substitution, morphing, duplication, disappearance, new symbols, pseudo-text, face or armor drift, silhouette movement, attack, teleportation, occlusion of spike or face, or cuts.
END STATE: Shi Jun, both silhouettes, the spike, token, pouch, plinth, and four doors retain exactly their starting identities, counts, shapes, materials, grips, and positions; only his gaze and expression may differ. No spoken dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, stretched anatomy, excessive bloom, or imitation of any real actor.
~~~

<!-- node:formation -->
### NODE formation — 四门禁制

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_formation_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, realistic stone and metal, and monumental foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous slow descending shot inside the vast circular Four-Gate sun court beneath an open oculus. Four large physical formation hubs—aged bronze astronomical armature, lapis-blue stone chime, medicinal-leaf crystal reliquary and a suspended water mirror—stand around one incomplete jade-gold floor circuit. Shen Yan and Chu Mingqi remain stationary at human scale between ivory-jade mechanisms, vermilion galleries and gilded guardian reliefs.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's chamber diameter, camera side, four hub identities and positions, circuit geometry, guardian-beast carvings, two character identities and poses, lighting, scale, and color grade. The camera may descend gently but may not orbit to a new side.
PROP LOCK: Each hub retains its exact material, shape, moving-part count, carved details, and position. The water mirror remains water, the leaf crystal remains crystal, and the blue-stone and bronze hubs never exchange materials. All character weapons and costume attachments remain fixed and untouched.
PRIMARY ACTION: Only the existing blue-stone hub emits one jade-gold pulse into the already-carved circuit; the pulse travels to the dark fourth hub, stops there, and fades without activating anything else.
ALLOWED MOTION: Slow volumetric-light drift, slight dust motes, restrained reflection movement inside the water mirror, natural character blinking and breathing, and a smooth vertical camera descent of less than ten percent of chamber height.
FORBIDDEN TRANSITIONS: No hand approaches a hub, no walking, guardian eyes opening, hub rotation, new runes, changing inscriptions, flat icons, pseudo-text, prop morphing, material swapping, duplication, disappearance, projection panels, character drift, teleportation, extreme bloom, weightless camera, or cuts.
END STATE: The pulse has faded; all four hubs, circuit paths, guardian carvings, characters, props, and architectural geometry exactly match their starting identities, counts, materials, and positions, with the fourth hub still dark. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, or imitation of any real actor.
~~~

<!-- node:combat_one -->
### NODE combat_one — 墨蛟试探

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_combat_one_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, realistic skin, cloth, scales, pale jade and localized black mineral water, with strong spatial depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous wide tactical shot in a collapsed but magnificent geothermal treasure pavilion beneath a sunlit fractured oculus. Exactly one massive hornless ink-scaled dragon, one narrow ivory-jade escape causeway, two wounded adult disciples, Shen Yan on the left flank and Chu Mingqi on the right form a clear decision tableau among gilded columns and distant palace towers. Keep attack lanes readable and stop before any player choice or victory.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's camera side, dragon species, head and limb anatomy, scale pattern, body size, character count, approved faces, hair, costumes, damage, poses, cavern geometry, jade railings, causeway, lighting, and grade. Both protagonists keep their feet planted and remain on their original flanks.
PROP LOCK: Shen Yan's dark-gold blades and case remain in the exact first-frame configuration; Chu Mingqi's circular moon-ring remains rigid with the same diameter, material, pattern, grip or attachment, and screen position. Wards, wounded disciples' equipment, rail fragments, and every visible object keep the same count and form. No protagonist activates or releases a weapon.
PRIMARY ACTION: The dragon performs one short, heavy tail press against the existing causeway edge, displacing a small amount of black mud, then holds the blocked position; its torso, head, limbs, and scale pattern remain anatomically stable.
ALLOWED MOTION: Natural blinking and breathing, restrained cloth and hair movement, small mud ripples at the tail contact point, low steam that never covers a face or weapon, tiny loose debris settling, and a slow low camera slide of less than five percent.
FORBIDDEN TRANSITIONS: No eruption, leap, full-body spin, second strike, protagonist attack, blade flight, ring expansion, spell formation, weapon or limb morphing, changing dragon anatomy, duplicated people, disappearing props, hand-object fusion, unexplained damage, new fire, pseudo-text, teleportation, face or costume drift, fast camera sweep, or cuts.
END STATE: The tail rests against the causeway after the single press; every creature limb, person, weapon, prop, wounded disciple, railing, and architectural element retains its starting identity, count, material, and readable position. No victory, dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, excessive bloom, or imitation of any real actor.
~~~

<!-- node:combat_two -->
### NODE combat_two — 底牌与崩塌

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_combat_two_grand_v2.png`

连续性说明：首次预览使用上面的静态首帧；`combat_one` 通过后，最终生成时改为只上传该获准视频导出的末帧，不要再同时上传静态图。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, realistic skin, cloth, scales, pale jade and localized mineral water, with strong spatial depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous wide shot deeper inside the same grand geothermal treasure pavilion. Continue the approved combat-one identities, damage, sunlit oculus, ivory-and-gold architecture and grade. Exactly one wounded hornless ink dragon, one already-cracked ivory support pillar, one stationary closed gold chest, one injured adult disciple, Shen Yan and Chu Mingqi form a clear choice between commitment, cooperation and retreat.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded frame's camera side, dragon anatomy and scale pattern, approved faces, hair, costumes, injuries, body proportions, character count, pavilion geometry, existing cracks, chest position, retreat route, lighting, and grade. No person or creature changes side or leaves frame.
PROP LOCK: Shen Yan's blades and case stay in their exact visible configuration with no chained formation; Chu Mingqi's moon-ring keeps its exact diameter, material, pattern, grip or attachment, and position with no fire ring. The gold chest remains closed, rigid, and stationary. All weapons, wards, railings, and belongings retain their count, shape, material, and attachment.
PRIMARY ACTION: The dragon applies one brief foreclaw impact to the already-cracked support pillar; one existing crack extends a short fixed distance and releases a small dust fall, then all motion stops.
ALLOWED MOTION: Natural blinking and breathing, restrained cloth and hair movement, a small mud ripple, low steam that never hides people or props, dust from the one crack, and a slow stable camera push of less than five percent.
FORBIDDEN TRANSITIONS: No pillar collapse, chest sinking or opening, injured disciple crawling, ultimate technique, blade formation, fire manifestation, second impact, weapon or body morphing, dragon anatomy drift, prop substitution, duplication, disappearance, hand-object fusion, face or costume drift, heavy occlusion, teleportation, fast camera, victory, or cuts.
END STATE: The one crack is slightly longer and the dust is settling; otherwise every person, dragon limb, weapon, chest, prop, railing, and pavilion element has the same identity, count, material, attachment, and position as the starting frame. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, excessive bloom, or imitation of any real actor.
~~~

<!-- node:aftermath -->
### NODE aftermath — 战利品与伤员

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_aftermath_grand_v2.png`

连续性说明：首次预览使用上面的静态首帧；`combat_two` 通过后，最终生成时改为只上传该获准视频导出的末帧，不要再同时上传静态图。

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, realistic fatigue, cloth, pale jade, dust, and depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous slow exhausted tableau in the same damaged but sunlit treasure pavilion after the ink dragon withdraws beneath localized black mineral residue. One closed gold chest on a raised plinth, one wounded adult disciple on a dry jade step, one medicinal-root bundle in a physical tray and one bright narrowing exit remain in four separate readable directions. Leave clean visual room for the final choice.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded frame's camera side, approved faces, hair, costumes, injuries, body proportions, poses, character count, weapon positions, chest, wounded disciple, roots, exit, pavilion damage, lighting, and grade. Shen Yan and Chu Mingqi remain planted and do not commit to any option.
PROP LOCK: Every weapon stays lowered and fixed; Chu Mingqi's moon-ring and Shen Yan's blade equipment keep their exact shapes, materials, grips or attachments, and positions. The closed gold chest remains on its raised dry plinth, and the single root bundle remains in its physical tray. The roots, rubble, and injured disciple's equipment remain untouched and unchanged.
PRIMARY ACTION: The only character action is one brief guarded exchange of eye contact between Shen Yan and Chu Mingqi; heads move less than five degrees and all hands remain still.
ALLOWED MOTION: Natural blinking, breathing, slight fatigue in posture without displacement, tiny cloth-edge movement, settling dust, low steam and faint red mist that never occlude a choice cue, and a slow stable push-in.
FORBIDDEN TRANSITIONS: No reaching, catching, lifting, treating, looting, opening, walking, weapon activation, prop exchange, body or object morphing, duplication, disappearance, hand-object fusion, new damage, pseudo-text, face or costume drift, heavy occlusion, teleportation, or cuts.
END STATE: Every person, weapon, chest, root bundle, injury, prop, exit, and architectural element retains the same identity, count, material, attachment, and position as the first frame; only the protagonists' gaze and ambient dust phase may differ. No dialogue, music, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, excessive bloom, or imitation of any real actor.
~~~

<!-- node:ending -->
### NODE ending — 雾门余烬

模型与设置：Veo 3.1 Fast 预览，Veo 3.1 最终；16:9，24 fps，8 秒。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_node_ending_grand_v2.png`

完整视频提示词（直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, grand sunrise lighting, natural skin, realistic cloth and hair, and clear foreground, midground, and background depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous restrained crane-up on a wet vermilion-and-jade overlook outside the monumental ivory-and-bronze mountain gate. Shen Yan, Chu Mingqi and a small coherent survivor group with exactly one supported rescued disciple remain still before a radiant cloud sea, waterfalls and tiered palace mountains while restrained red mist folds only inside the gate fissure.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's approved faces, hairstyles, costumes, accumulated damage, body proportions, survivor count, support poses, formation spacing, gate geometry, distant lantern pattern, dawn light direction, and color grade. No survivor walks, turns around, enters, exits, or changes support role.
PROP LOCK: Every visible weapon, Chu Mingqi's single moon-ring, and Shen Yan's single blade case remain in their exact first-frame shapes, scales, materials, attachments, and positions. Shen Yan's lowered hand remains empty. The one concealed artifact stays fully concealed; no package appears and no prop changes hands.
PRIMARY ACTION: The bronze fissure closes by one small mechanically plausible increment while the adjacent red mist folds inward once; all people remain still.
ALLOWED MOTION: Natural blinking and breathing, tiny cloth and hair-tip movement, slow dawn haze, restrained lantern flicker, the single gate increment, layered mist flow that never hides a face, and a smooth vertical crane of less than ten percent.
FORBIDDEN TRANSITIONS: No walking, looking back, celebration, changing survivor count, unsupported injured person, item exchange, revealed artifact, object or body morphing, duplication, disappearance, changing costume damage, pseudo-text, new buildings, teleportation, heavy fog occlusion, black bars, or cuts.
END STATE: The fissure is slightly more closed; every survivor, support contact, face, costume, injury, weapon, concealed artifact, building, and lantern formation keeps the same identity, count, material, attachment, and relative position as the first frame. Shen Yan remains empty-handed. No dialogue, music, title text, game UI, captions, subtitles, burned-in text, logos, modern objects, anime style, plastic skin, excessive bloom, or imitation of any real actor.
~~~

## 5 个主要回应（Veo 无声画面＋独立 TTS 干声）

<!-- response:npc.response.prologue.inspect_mist -->
### RESPONSE npc.response.prologue.inspect_mist

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous medium close-up with no cuts. At the living bronze mist gate, show the approved original East Asian woman expedition scout, about twenty-seven, with an oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and small pale-jade wind-listening talisman. Layered vermilion mist and the safer left stair stay readable behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Treat the uploaded first frame as a locked plate. Preserve her exact face, hairline, braid, costume seams, body proportions, pose, screen position, background architecture, stair geometry, lighting direction, lens, and color grade for the entire clip.
PROP LOCK: The pale-jade talisman remains rigidly pinned at its exact attachment point with the same size, shape, carving, material, and brightness. Her weapon remains fully sheathed and fixed; hands stay visible where shown and never touch a prop.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The scout shifts her eyes once toward the already-visible safer stair and back, keeping a calm analytical expression; her head moves less than three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, pointing, hand gesture, walking, turning away, prop contact, talisman transformation, weapon movement, new people, object substitution, morphing, duplication, disappearance, face or costume drift, new glyphs, pseudo-text, fog covering her face, camera move, or cuts.
END STATE: At the end, her posture, hands, face identity, costume, talisman, weapon, stair, gate, and all background geometry match the first frame; only her natural gaze and ambient mist phase may differ. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.response.prologue.inspect_mist -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: She has measured the supernatural mist cycle at a dangerous bronze gate and gives the player one useful observation without choosing the route for them.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, calm focus, restrained urgency, medium-low volume, and an even pace. Keep the performance human and cinematic, not like a system assistant or audiobook narrator. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾流每九息回卷一次，左侧石阶的风更稳。你看清后，再从眼前方案里决定路线。”
~~~

<!-- response:npc.response.alliance.cautious_cooperation -->
### RESPONSE npc.response.alliance.cautious_cooperation

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous medium shot with no cuts. On the narrow cliff alliance platform, show the exact same approved expedition scout already holding the left guard position, with her approved oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal leather shoulder guard, and pale-jade wind-listening talisman. The defensive ward, layered red mist, and distant sect lantern formations remain visible behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock her exact face, hairline, braid, costume seams, proportions, pose, screen position, cliff edge, ward boundary, lantern formation, lighting, lens, and color grade to the uploaded first frame. She never changes guard position.
PROP LOCK: Her talisman remains rigidly pinned at the same attachment point and brightness; her weapon remains fully sheathed with an unchanged hilt and scabbard. All ward fittings and background lanterns retain their count and geometry. Hands remain still and never touch any prop.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The scout holds guarded eye contact, then allows one restrained trace of cooperative warmth to reach her eyes; her head and hands remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, stepping into position, hand signal, weapon draw, talisman contact, prop movement, body or object morphing, substitution, duplication, disappearance, new disciples, changing lantern count, face or costume drift, pseudo-text, fog occlusion, camera move, or cuts.
END STATE: Her posture, hands, face identity, costume, talisman, weapon, ward, cliff, and lantern formation match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.response.alliance.cautious_cooperation -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: On a dangerous cliff route she offers limited cooperation while respecting that the player will keep a hidden technique in reserve.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, patient steadiness, guarded warmth, medium-low volume, and an even pace. Keep the performance human and cinematic, not romantic and not like a narrator. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “我守住左侧，你保留自己的底牌。先活着越过山隘，之后再谈彼此的来路。”
~~~

<!-- response:npc.response.rescue.secure_survivor -->
### RESPONSE npc.response.rescue.secure_survivor

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_rescue_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic cloth and hair, and clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous medium close-up with no cuts. Beside the broken black-stone rescue wall, show the exact same approved expedition scout in her first-frame stopped pose, with her approved face, low braided ponytail, muted celadon robe, charcoal shoulder guard, and pale-jade wind-listening talisman. The physical jade ward and hidden wounded-survivor area remain readable behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock her exact face, hairline, braid, costume seams, proportions, pose, screen position, wall fracture, ward boundary, survivor silhouette if visible, mist layers, lighting, lens, and color grade to the uploaded first frame.
PROP LOCK: Her talisman remains pinned with unchanged shape, carving, material, and brightness; weapon and ward equipment stay fixed and untouched. Her hands remain in the same visible position and never rise, signal, or cross a prop.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The scout makes one controlled urgent glance toward the already-visible survivor area and returns her gaze to the player; her head moves less than three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, raised hand, stop signal, walking, turning away, prop contact, talisman or ward morphing, new shadow, changing survivor, duplication, disappearance, face or costume drift, pseudo-text, heavy mist occlusion, camera move, or cuts.
END STATE: Her posture, hands, face identity, costume, talisman, weapon, wall, ward, and survivor area match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.response.rescue.secure_survivor -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: She has detected a possible second attacker beyond a broken wall and urgently proposes verifying the survivor before entering.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled urgency, precise consonants, medium volume, and a brisk but fully intelligible pace. Keep the performance human and tactical, never panicked. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾后还有第二个人的脚步声。先用旧暗号核验身份，我会贴着石墙等你下令。”
~~~

<!-- response:npc.response.shijun.verify_bargain -->
### RESPONSE npc.response.shijun.verify_bargain

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic armor, and clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous medium close-up with no cuts. Before four monumental bronze doors, show the approved Shi Jun, an original lean East Asian man in his thirties with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike, exactly as in the uploaded first frame.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock Shi Jun's exact face, hair, armor seams, proportions, pose, screen position, four-door geometry, plinth, lighting, lens, and color grade. He never advances, withdraws, or changes stance.
PROP LOCK: The heated spike remains in the exact same hand, grip, orientation, length, metal shape, blood mark, mud pattern, and screen position. The medicine pouch, token, concealed weapon, and all door fittings remain fixed and untouched. No hand moves across the spike.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: Shi Jun shifts his eyes once from the fixed token to the player and forms one restrained taunting half-smile; his head, hands and spike remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, moving the spike toward camera, rotating it, changing grip, laying down or exchanging an item, weapon draw, stepping, attack, prop or body morphing, substitution, duplication, disappearance, new marks, pseudo-text, face or armor drift, steam covering face or spike, camera move, or cuts.
END STATE: Shi Jun's pose, hands, face identity, armor, spike, mud, pouch, token, plinth, and four doors match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.response.shijun.verify_bargain -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: He presents real evidence but makes the player decide whether the information is worth a medicinal plant.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, a dry lightly gravelly texture, restrained mockery, medium-low volume, and deliberate pacing. Keep the threat implicit rather than shouted. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “想验货可以，阵钉上的热泥来自门后。至于这句话值不值一株药，由你自己判断。”
~~~

<!-- response:npc.response.underground.coordinate_retreat -->
### RESPONSE npc.response.underground.coordinate_retreat

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_cavern_ally_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action with grounded physical materials, restrained spiritual VFX, film-quality lighting, natural skin, realistic ash-white armor, and clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one continuous tactical medium shot with no cuts. In the geothermal black-mud cavern, show the approved original East Asian man temporary ally, about twenty-nine, with tied black hair, a narrow weathered face, layered ash-white light armor, and a small white-light signal talisman, standing behind the cracked white-jade railing. The already-marked first retreat stone and distant ink-dragon tail remain readable in depth.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock his exact face, hair, armor seams, proportions, pose, screen position, railing cracks, retreat-stone marker, distant dragon silhouette, cavern geometry, lighting, lens, and color grade to the uploaded first frame.
PROP LOCK: The white-light talisman remains rigidly attached with the same shape, size, material, and brightness. His weapon remains fixed and lowered or sheathed exactly as shown. The retreat marker does not move or redraw; all railing pieces and equipment remain untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The ally glances once toward the existing retreat stone and returns to the player with a focused cooperative expression; his head moves less than three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, new white beam, hand signal, prop touch, weapon lift, dragon attack, stepping, object or body morphing, substitution, duplication, disappearance, changing railing cracks, new glyphs, pseudo-text, face or armor drift, steam occlusion, camera move, or cuts.
END STATE: His posture, hands, face identity, armor, talisman, weapon, railing, retreat marker, dragon silhouette, and cavern geometry match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.response.underground.coordinate_retreat -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、无音乐无混响。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: While tracking an ink dragon in a collapsing cavern, he identifies a retreat point and proposes a simple signal while leaving the final cooperation choice to the player.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, tactical focus, medium-low volume, firm articulation, and a measured pace that remains audible over danger. Keep it cooperative but not intimate. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “玉栏后的青石是第一处退点。我守住蛟尾方向，你用一道白光示警，是否合击仍由你决定。”
~~~

## 25 个异常输入回应视频（按角色族复用）

以下五个角色族保持固定机位、服装和环境，只改变闭口微表情、视线与停顿；台词只在各自 TTS 区块中生成。这样 25 条画面可跨多个节点复用，也不会让 Veo 伪造中文口型或字幕。

### Calm Scout / 冷静侦察者

统一画面：original celadon-robed expedition scout, medium close-up beside layered red mist and a dim bronze mechanism, subtle background movement, direct but non-aggressive eye line toward the player, closed-mouth silent performance.

<!-- response:npc.invalid.calm.abuse -->
#### RESPONSE npc.invalid.calm.abuse

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded bronze and jade, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Beside layered vermilion mist and a sunlit living bronze mechanism, show the approved celadon-robed expedition scout facing the unseen player.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hairline, low braid, straight brows, costume seams, proportions, posture, screen position, bronze mechanism, mist layers, lighting, lens, and grade.
PROP LOCK: Her pale-jade wind-listening talisman stays rigidly pinned with identical shape, carving, brightness, and attachment. Her weapon remains fully sheathed; hands stay still and do not touch any object.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one restrained flash of disapproval that settles into controlled eye contact; the head and hands remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, hand gesture, weapon movement, talisman contact, stepping, turning away, attack, prop or body morphing, substitution, duplication, disappearance, face or costume drift, new symbols, pseudo-text, face occlusion, camera move, or cuts.
END STATE: After speaking, her pose, hands, face identity, costume, talisman, weapon, mechanism, and background geometry match the first frame. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.calm.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player has used insulting language during a dangerous mist investigation; she refuses escalation and redirects attention to an actionable decision.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, restrained firmness, medium-low volume, and an even pace. Show a trace of disapproval without anger or sarcasm. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “言语再重也不会改变雾中的局势。若要继续，就说清你准备怎么做。”
~~~

<!-- response:npc.invalid.calm.irrelevant -->
#### RESPONSE npc.invalid.calm.irrelevant

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded bronze and jade, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Beside layered vermilion mist and a sunlit living bronze mechanism, show the exact same approved celadon-robed expedition scout and the same already-present distant shadow.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact scout face, hairline, braid, costume, proportions, posture, position, distant shadow outline, mechanism geometry, mist layers, lighting, lens, and grade. The shadow never approaches or changes form.
PROP LOCK: Her pale-jade talisman remains pinned and unchanged; her weapon remains fully sheathed. Hands and all visible mechanisms stay fixed and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character makes one brief redirecting eye movement toward the already-visible immediate danger or bargaining cue, then returns to the player.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, moving shadow, new creature, hand gesture, weapon movement, talisman contact, stepping, attack, object or body morphing, substitution, duplication, disappearance, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her pose, hands, identity, costume, talisman, weapon, fixed shadow, mechanism, and background geometry match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.calm.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player has changed the subject while danger remains active; she redirects them to the immediate environment without sounding mechanical.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, calm focus, medium-low volume, and an even patient pace. The correction is concise and nonjudgmental. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “那件事与眼前的险境无关。先看清此地，再决定下一步。”
~~~

<!-- response:npc.invalid.calm.too_long -->
#### RESPONSE npc.invalid.calm.too_long

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded bronze and jade, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Beside layered vermilion mist and a sunlit living bronze mechanism, show the exact same approved celadon-robed expedition scout watching the gate.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hairline, braid, costume, proportions, posture, screen position, gate and mechanism geometry, mist layers, lighting, lens, and grade.
PROP LOCK: Her pale-jade talisman remains rigidly pinned and her weapon fully sheathed. Hands stay at their first-frame positions; all mechanism parts remain unchanged and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows controlled time pressure through a slight brow change and one short eye movement toward the active danger; no gesture follows.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, hand signal, pointing, weapon movement, talisman contact, stepping, attack, new fog shape, prop or body morphing, substitution, duplication, disappearance, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her pose, hands, identity, costume, talisman, weapon, gate, mechanism, and mist-layer layout match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.calm.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The supernatural mist is changing while the player gives an overlong answer; she needs one immediate executable choice.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled urgency, medium volume, and a brisk but intelligible pace. Do not sound impatient with the person; urgency comes from the environment. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾势正在变化。把话收短，只说你此刻要做什么。”
~~~

<!-- response:npc.invalid.calm.silence -->
#### RESPONSE npc.invalid.calm.silence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded bronze and jade, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Beside layered vermilion mist and a sunlit living bronze mechanism, show the exact same approved celadon-robed expedition scout waiting beside the unseen player.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hairline, braid, costume, proportions, posture, screen position, mechanism geometry, mist layers, lighting, lens, and grade.
PROP LOCK: Her pale-jade talisman stays rigidly pinned and her weapon fully sheathed. Hands remain still and away from every prop for the entire clip.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character waits in stillness, blinks once naturally, then gives one very small patient head inclination under three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, moving closer, hand gesture, weapon movement, talisman contact, stepping, attack, prop or body morphing, substitution, duplication, disappearance, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her body position, hands, face identity, costume, talisman, weapon, mechanism, and background geometry match the first frame; only the completed small nod and ambient phase may differ. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.calm.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player remains silent while examining clues; she permits reflection without taking control of the decision.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, patient reassurance, medium-low volume, and a slightly slower pace. Do not insert leading silence into the audio file; the game video supplies the pause. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “不必仓促开口。先看清线索，想好后再作选择。”
~~~

<!-- response:npc.invalid.calm.low_confidence -->
#### RESPONSE npc.invalid.calm.low_confidence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_mist_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded bronze and jade, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Beside layered vermilion mist and a sunlit living bronze mechanism, show the exact same approved celadon-robed expedition scout listening to the unseen player.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hairline, braid, costume, proportions, posture, screen position, mechanism geometry, mist layers, lighting, lens, and grade.
PROP LOCK: Her pale-jade talisman remains rigidly pinned with unchanged carving and brightness; her weapon remains fully sheathed. Hands stay at their first-frame positions and never approach the talisman.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one subtle uncertainty or wary narrowing of the eyes, then returns to a neutral attentive hold without moving the hands.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, listening hand gesture, talisman touch, weapon movement, stepping, attack, prop or body morphing, substitution, duplication, disappearance, face or costume drift, new glyphs, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her pose, hands, identity, costume, talisman, weapon, mechanism, and background geometry match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.calm.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: Wind and mist have made the player's intent unclear; she honestly asks for a shorter restatement or a fixed choice.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, mild uncertainty without confusion, medium-low volume, and an even helpful pace. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “这句话没有听清。换个简短说法，或直接选择眼前的方案。”
~~~

### Ally Scout / 同行侦察者

统一画面：same original team scout on a cliff route, defensive ward and sect silhouettes behind, medium close-up, guarded warmth, closed-mouth silent performance.

<!-- response:npc.invalid.ally.abuse -->
#### RESPONSE npc.invalid.ally.abuse

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded ward light, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. On the narrow cliff route above layered vermilion mist, show the exact same approved celadon-robed expedition scout with the same defensive ward and distant sect silhouettes behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, braid, brows, costume seams, proportions, posture, screen position, cliff, ward boundary, sect silhouettes, lighting, lens, and grade.
PROP LOCK: Her pale-jade talisman remains rigidly pinned and her weapon fully sheathed; hands, ward fittings, and every visible prop stay fixed and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one restrained flash of disapproval that settles into controlled eye contact; the head and hands remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, hand gesture, weapon or talisman movement, stepping, turning away, attack, prop or body morphing, substitution, duplication, disappearance, changing ward geometry, new people, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her pose, hands, face identity, costume, talisman, weapon, ward, cliff, and sect silhouettes match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.ally.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: A temporary companion has insulted her during a dangerous crossing; she acknowledges the damage but refuses to waste time escalating.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, restrained hurt beneath discipline, medium-low volume, and deliberate pacing. Do not sound melodramatic or hostile. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “若连同行者都只剩恶言，这条路便走不远。说出你的判断，别把力气浪费在争吵上。”
~~~

<!-- response:npc.invalid.ally.irrelevant -->
#### RESPONSE npc.invalid.ally.irrelevant

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded ward light, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. On the narrow cliff route above layered vermilion mist, show the exact same approved celadon-robed expedition scout; the ancient entrance, waiting adult team, defensive ward, and distant sect silhouettes remain visible and stationary behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, braid, costume, proportions, posture, position, entrance geometry, team count and positions, ward boundary, silhouettes, lighting, lens, and grade.
PROP LOCK: Her talisman remains rigidly pinned and weapon fully sheathed. Hands, ward fittings, team equipment, and every visible prop stay fixed and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character makes one brief redirecting eye movement toward the already-visible immediate danger or bargaining cue, then returns to the player.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, pointing, hand gesture, team movement, weapon or talisman movement, stepping, attack, object or body morphing, substitution, duplication, disappearance, changing ward geometry, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her pose, hands, identity, costume, talisman, weapon, entrance, team, ward, and sect silhouettes match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.ally.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player raises an unrelated matter while the entrance and companions wait; she redirects without dismissing the topic forever.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, practical patience, medium-low volume, and an even pace. Keep the tone collaborative and concise. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “那件事可以以后再谈。入口和同伴都在等我们决定。”
~~~

<!-- response:npc.invalid.ally.too_long -->
#### RESPONSE npc.invalid.ally.too_long

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded ward light, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. On the narrow cliff route above layered vermilion mist, show the exact same approved celadon-robed expedition scout with the defensive ward, distant sect silhouettes, and visibly closing bronze gate behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, braid, costume, proportions, posture, screen position, cliff, ward boundary, gate geometry, silhouettes, lighting, lens, and grade.
PROP LOCK: Her talisman remains pinned and weapon fully sheathed. Hands stay at their first-frame positions; all ward and gate parts keep their exact shapes and counts.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows controlled time pressure through a slight brow change and one short eye movement toward the active danger; no gesture follows.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, three-direction hand signal, pointing, weapon or talisman movement, stepping, attack, gate morphing, object or body substitution, duplication, disappearance, new route markers, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her pose, hands, face identity, costume, talisman, weapon, ward, cliff, gate structure, and sect silhouettes match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.ally.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The gate is closing while the player gives a long explanation; she requests one of three clear conclusions.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled time pressure, medium volume, and a brisk but fully intelligible pace. Give equal verbal weight to “前进、观察，还是撤回” without sounding like a menu announcer. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “没有时间听完整段缘由。告诉我结论：前进、观察，还是撤回？”
~~~

<!-- response:npc.invalid.ally.silence -->
#### RESPONSE npc.invalid.ally.silence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded ward light, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. On the narrow cliff route above layered vermilion mist, show the exact same approved celadon-robed expedition scout already holding the left-flank guard position, with the same defensive ward and distant sect silhouettes behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, braid, costume, proportions, left-flank posture, screen position, cliff, ward boundary, silhouettes, lighting, lens, and grade. She does not move into or out of position.
PROP LOCK: Her talisman remains pinned and weapon fully sheathed. Hands, ward fittings, and every visible prop stay fixed and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character waits in stillness, blinks once naturally, then gives one very small patient head inclination under three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, taking a new stance, walking, hand signal, weapon or talisman movement, attack, prop or body morphing, substitution, duplication, disappearance, changing ward geometry, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her left-flank pose, hands, face identity, costume, talisman, weapon, ward, cliff, and sect silhouettes match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.ally.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: The player remains silent while weighing options; she commits to guarding one side but explicitly leaves the decision to them.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, quiet understanding, medium-low volume, and measured confidence. Place subtle emphasis on “必须由你作出” without sounding coercive. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “你还在权衡，我明白。我会守住这一侧，但决定必须由你作出。”
~~~

<!-- response:npc.invalid.ally.low_confidence -->
#### RESPONSE npc.invalid.ally.low_confidence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_scout_alliance_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, natural skin, realistic cloth and hair, grounded ward light, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. On the narrow cliff route above layered vermilion mist, show the exact same approved celadon-robed expedition scout with the defensive ward, visible physical route choices, and distant sect silhouettes behind her.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, braid, costume, proportions, posture, screen position, route geometry, ward boundary, silhouettes, lighting, lens, and grade.
PROP LOCK: Her talisman remains pinned and weapon fully sheathed. Hands and all physical route markers stay fixed, readable, and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one subtle uncertainty or wary narrowing of the eyes, then returns to a neutral attentive hold without moving the hands.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, leaning forward, pointing at choices, hand gesture, weapon or talisman movement, route-marker change, stepping, attack, object or body morphing, substitution, duplication, disappearance, face or costume drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: Her body position, hands, identity, costume, talisman, weapon, route markers, ward, cliff, and sect silhouettes match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.ally.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Kore；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved celadon expedition scout, an original East Asian woman around twenty-seven, observant, disciplined, and quietly protective. Use the selected Kore voice consistently for every scout line.
Scene: A strong cliff wind has obscured the player's intent; she asks for repetition or a direct indication without pretending to understand.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, mild uncertainty, cooperative warmth, medium-low volume, and an even pace. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “我没听清你的意思。再说一遍，或指明你支持哪个方案。”
~~~

### Formation Spirit / 阵灵界面

统一画面：a life-sized translucent human-faced formation spirit projected from a physical jade-bronze hub, elegant immortal mechanism rather than flat UI, clear mouth and closed-mouth silent performance, surrounding runes react softly to speech.

<!-- response:npc.invalid.system.abuse -->
#### RESPONSE npc.invalid.system.abuse

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_formation_spirit_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action materials with a restrained supernatural projection, clear depth, monumental jade-bronze architecture. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Show the approved life-sized translucent adult androgynous formation spirit projected from the same physical hub, with a calm human face, pale-jade inner light, dark-bronze circuit filigree, readable natural lips, and no floating panels.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact human face, projection silhouette, height, opacity, filigree paths, hub geometry, carved rune positions, architecture, camera, lighting, and grade.
PROP LOCK: The physical hub, sockets, rings, carved runes, and surrounding mechanisms retain identical shapes, counts, materials, positions, and labels-free surfaces. The projection stays anchored to the same emitter and never splits.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one restrained flash of disapproval that settles into controlled eye contact; the head and hands remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, new runes, moving inscriptions, floating UI, readable text, hand gesture, projection morphing, face duplication, emitter change, hub rotation, material swap, disappearance, camera move, excessive bloom, or cuts.
END STATE: The spirit's face, body geometry, opacity, filigree, emitter, hub, runes, and chamber match the first frame at the end; the perimeter ring returns to starting brightness. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.system.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: The formation receives abusive but non-executable speech and safely refuses to treat it as an action.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even measured tone, medium-low volume, and precise articulation. Sound ancient and humane rather than robotic or like a modern assistant. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “无效言语不会改变结算。请给出可执行指令。”
~~~

<!-- response:npc.invalid.system.irrelevant -->
#### RESPONSE npc.invalid.system.irrelevant

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_formation_spirit_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action materials with a restrained supernatural projection, clear depth, monumental jade-bronze architecture. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Show the exact same approved life-sized translucent adult androgynous formation spirit anchored to the physical hub; the already-carved route channel, formation eye, and tactical marker remain separate and readable physical features.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, projection silhouette, height, opacity, filigree, hub geometry, route channel, formation eye, tactical marker, architecture, camera, lighting, and grade.
PROP LOCK: Every physical mechanism and carving keeps the same count, shape, material, and position. The projection stays on one emitter; no part becomes a panel, icon, text, or another object.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character makes one brief redirecting eye movement toward the already-visible immediate danger or bargaining cue, then returns to the player.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, sequential activation of three objects, new runes, moving inscriptions, floating UI, readable text, hand gesture, projection or hub morphing, duplication, disappearance, camera move, excessive bloom, or cuts.
END STATE: The spirit, emitter, hub, route channel, formation eye, marker, and chamber match the first frame at the end; the route channel returns to starting brightness. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.system.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: The input is unrelated to the active objective; the spirit identifies the three valid subject areas without choosing one.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even informative tone, medium-low volume, and deliberate articulation. Sound ancient and humane rather than robotic. Give equal emphasis to “路线、阵眼或战术”. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “输入与当前目标无关。请围绕路线、阵眼或战术作答。”
~~~

<!-- response:npc.invalid.system.too_long -->
#### RESPONSE npc.invalid.system.too_long

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_formation_spirit_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action materials with a restrained supernatural projection, clear depth, monumental jade-bronze architecture. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Show the exact same approved life-sized translucent adult androgynous formation spirit anchored to the physical hub; one existing empty carved command socket remains visible.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, projection silhouette, height, opacity, filigree, hub geometry, command socket, carved mechanisms, architecture, camera, lighting, and grade.
PROP LOCK: The hub, socket, rings, and runes retain identical shapes, counts, materials, and positions. The projection remains anchored to one unchanged emitter.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows controlled time pressure through a slight brow change and one short eye movement toward the active danger; no gesture follows.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, circulating or collapsing waveform, new branches, new runes, moving inscriptions, floating UI, readable text, hand gesture, projection or hub morphing, duplication, disappearance, camera move, excessive bloom, or cuts.
END STATE: The spirit, emitter, hub, socket, runes, and chamber match the first frame at the end; the socket returns to starting brightness. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.system.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: An overlong instruction cannot be mapped safely to one atomic action, so the formation asks for a single clear command.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even safety-focused tone, medium-low volume, and concise measured pacing. Sound ancient and humane rather than robotic. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “指令过长，无法安全判定。请缩减为一个明确动作。”
~~~

<!-- response:npc.invalid.system.silence -->
#### RESPONSE npc.invalid.system.silence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_formation_spirit_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action materials with a restrained supernatural projection, clear depth, monumental jade-bronze architecture. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Show the exact same approved life-sized translucent adult androgynous formation spirit anchored to the physical hub while the same fixed physical mechanisms remain gently illuminated.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, projection silhouette, height, opacity, filigree, hub geometry, mechanism count and positions, architecture, camera, lighting, and grade.
PROP LOCK: Every hub part, socket, ring, rune, and fixed mechanism retains identical shape, material, position, and brightness relationship. The projection remains anchored to one emitter.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character waits in stillness, blinks once naturally, then gives one very small patient head inclination under three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, new mechanism, changing choice, new rune, moving inscription, floating UI, readable text, hand gesture, projection or hub morphing, duplication, disappearance, camera move, excessive bloom, or cuts.
END STATE: The spirit, emitter, hub, runes, fixed mechanisms, and chamber match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.system.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: No valid instruction was received; the formation remains safely paused and confirms that fixed choices are still available.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even reassuring tone, medium-low volume, and unhurried pacing. Do not insert leading silence into the audio file; the game video supplies the pause. Sound ancient and humane rather than robotic. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “未收到有效指令。场景仍在等待，固定方案保持可用。”
~~~

<!-- response:npc.invalid.system.low_confidence -->
#### RESPONSE npc.invalid.system.low_confidence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_formation_spirit_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action materials with a restrained supernatural projection, clear depth, monumental jade-bronze architecture. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Show the exact same approved life-sized translucent adult androgynous formation spirit anchored to the physical hub; the same fixed carved choices remain visible and lit.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, projection silhouette, height, opacity, filigree, hub geometry, fixed-choice carvings, architecture, camera, lighting, and grade.
PROP LOCK: Every hub part, socket, ring, rune, and carved choice keeps the same count, shape, material, and position. The projection remains anchored to one emitter and never divides.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one subtle uncertainty or wary narrowing of the eyes, then returns to a neutral attentive hold without moving the hands.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, ambiguous branches appearing, new runes, mechanism firing, moving inscriptions, floating UI, readable text, hand gesture, projection or hub morphing, duplication, disappearance, camera move, excessive bloom, or cuts.
END STATE: The spirit, emitter, hub, runes, fixed choices, and chamber match the first frame at the end; the choices return to starting brightness. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.system.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Schedar；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved formation spirit, an adult androgynous human-like intelligence projected by an ancient jade-bronze mechanism, composed, precise, and nonjudgmental. Use the selected Schedar voice consistently for every formation-spirit line.
Scene: The formation cannot map an ambiguous instruction with sufficient confidence and offers a safe restatement or fixed-choice fallback.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, an even non-alarming tone, medium-low volume, and precise measured pacing. Sound ancient and humane rather than robotic. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “指令置信度不足。请重新表述，或使用固定方案。”
~~~

### Shi Jun / 敌对谈判者

统一画面：Shi Jun at the four bronze doors, exact continuity, formation spike in hand, medium close-up, no attack, closed-mouth silent performance.

<!-- response:npc.invalid.hostile.abuse -->
#### RESPONSE npc.invalid.hostile.abuse

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, grounded bronze and jade, realistic skin and armor, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Before four monumental dark-bronze doors, show the exact same approved Shi Jun already holding the doorway, with sharp eyes, brown-black short armor, worn copper fasteners, and a blood-marked bronze formation spike.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hair, armor seams, proportions, posture, screen position, four-door geometry, plinth, lighting, lens, and grade. He never changes position.
PROP LOCK: The spike remains rigidly fixed in the same hand, grip, orientation, length, metal shape, blood mark, mud pattern, and screen position. Token, pouch, concealed weapons, and all door fittings remain untouched and unchanged.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one restrained flash of disapproval that settles into controlled eye contact; the head and hands remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, step, spike rotation, grip change, hand gesture, prop exchange, weapon draw, attack, object or body morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, steam occlusion, camera move, or cuts.
END STATE: Shi Jun's pose, hands, face identity, armor, spike, mud, token, pouch, plinth, and four doors match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.hostile.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player insults him at a guarded doorway; he refuses to be moved and demands a real bargaining chip or passage.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, dry gravelly restraint, medium-low volume, and deliberate pacing. The threat is credible but never shouted. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “骂得再响，也换不来一条真路。拿出筹码，或者让开。”
~~~

<!-- response:npc.invalid.hostile.irrelevant -->
#### RESPONSE npc.invalid.hostile.irrelevant

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, grounded bronze and jade, realistic skin and armor, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Before four monumental dark-bronze doors, show the exact same approved Shi Jun; the formation spike in his hand, medicine pouch, token, plinth, and blocked doorway remain separately readable.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, hair, armor, proportions, posture, screen position, four-door geometry, bargaining-object positions, lighting, lens, and grade.
PROP LOCK: The spike remains fixed in the same grip and orientation. The medicine pouch and token remain on their exact surfaces; concealed weapons and door fittings remain untouched. Every bargaining object keeps its count, shape, material, and position.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character makes one brief redirecting eye movement toward the already-visible immediate danger or bargaining cue, then returns to the player.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, tapping or pointing, spike rotation, grip change, prop exchange, weapon draw, stepping, attack, object or body morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, steam occlusion, camera move, or cuts.
END STATE: Shi Jun, spike, token, pouch, plinth, doorway, and every visible object match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.hostile.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player has gone off topic during a tense standoff; he narrows the bargain to the spike, medicine, and right of passage.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, dry impatience, medium-low volume, and clipped deliberate pacing. Give separate weight to “阵钉、灵药和谁先让路”. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “少绕弯子。此刻只谈阵钉、灵药和谁先让路。”
~~~

<!-- response:npc.invalid.hostile.too_long -->
#### RESPONSE npc.invalid.hostile.too_long

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, grounded bronze and jade, realistic skin and armor, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Before four monumental dark-bronze doors, show the exact same approved Shi Jun holding the blood-marked bronze formation spike exactly as in the uploaded first frame.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the exact face, hair, armor seams, proportions, posture, screen position, four-door geometry, plinth, lighting, lens, and grade.
PROP LOCK: The spike remains rigidly fixed in the same hand, grip, orientation, length, shape, markings, mud pattern, and screen position. Token, pouch, concealed weapons, and door fittings remain untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows controlled time pressure through a slight brow change and one short eye movement toward the active danger; no gesture follows.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, turning or lifting the spike, finger movement around it, grip change, hand gesture, prop exchange, weapon draw, step, attack, morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, camera move, or cuts.
END STATE: Shi Jun's pose, hands, face identity, armor, spike, mud, token, pouch, plinth, and four doors match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.hostile.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player gives an overlong personal history instead of a bargain; he demands one concise condition.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled dismissal, medium-low volume, and clipped pacing. Avoid theatrical villain laughter or shouting. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “我没兴致听你铺陈来历。用一句话报出条件。”
~~~

<!-- response:npc.invalid.hostile.silence -->
#### RESPONSE npc.invalid.hostile.silence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, grounded bronze and jade, realistic skin and armor, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Before four monumental dark-bronze doors, show the exact same approved Shi Jun holding the blood-marked formation spike exactly as in the uploaded first frame.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock his exact face, hair, armor, proportions, posture, position, spike grip, four-door geometry, plinth, lighting, lens, and grade.
PROP LOCK: The spike, token, pouch, concealed weapons, and door fittings remain completely fixed, unchanged, and untouched for the entire clip.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character waits in stillness, blinks once naturally, then gives one very small patient head inclination under three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, spike tilt or rotation, grip change, hand gesture, prop exchange, weapon draw, step, attack, object or body morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, camera move, or cuts.
END STATE: Shi Jun's body position, hands, identity, armor, spike, token, pouch, plinth, and door geometry match the first frame; the door light returns to starting brightness. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.hostile.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player remains silent while the door window closes; he treats silence as a bargaining choice and waits.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, dry implied threat, medium-low volume, and slow deliberate pacing. Do not insert leading silence into the audio file; the game video supplies the pause. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “沉默也是价码，只是通常最贵。想好了就开口。”
~~~

<!-- response:npc.invalid.hostile.low_confidence -->
#### RESPONSE npc.invalid.hostile.low_confidence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_shijun_negotiation_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, grounded bronze and jade, realistic skin and armor, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous medium close-up. Before four monumental dark-bronze doors, show the exact same approved Shi Jun holding the blood-marked formation spike exactly as in the uploaded first frame.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the exact face, hair, armor, proportions, posture, screen position, four-door geometry, plinth, lighting, lens, and grade.
PROP LOCK: The spike remains fixed in the same hand, grip, orientation, markings, mud pattern, and position. Token, pouch, concealed weapons, and door fittings remain unchanged and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one subtle uncertainty or wary narrowing of the eyes, then returns to a neutral attentive hold without moving the hands.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, sudden movement, spike rotation, grip change, hand gesture, prop exchange, weapon draw, stepping, attack, morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, camera move, or cuts.
END STATE: Shi Jun's pose, hands, identity, armor, spike, mud, token, pouch, plinth, and four doors match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.hostile.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Algenib；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: Shi Jun, an original lean East Asian man in his thirties, sharp-eyed, calculating, distrustful, and accustomed to bargaining under threat. Use the selected Algenib voice consistently for every Shi Jun line.
Scene: The player's meaning is ambiguous during a hostile bargain; he asks for clarification before assuming the worst.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, wary suspicion, medium-low volume, and deliberate pacing. Keep aggression restrained and make the warning credible. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “你这句话含糊得很。再说清楚，免得我按最坏的意思理解。”
~~~

### Cavern Ally / 蛟窟临时盟友

统一画面：original temporary ally behind cracked white-jade railing in the black-mud cavern, ink dragon movement visible far behind, tactical medium close-up, closed-mouth silent performance.

<!-- response:npc.invalid.encounter.abuse -->
#### RESPONSE npc.invalid.encounter.abuse

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_cavern_ally_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, realistic skin and ash-white armor, grounded jade and black mud, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous tactical medium close-up. Behind the cracked white-jade railing, show the approved temporary ally with tied black hair, narrow weathered face, layered ash-white light armor, and small white-light signal talisman; the same distant ink-dragon silhouette remains visible through thin steam.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hair, armor seams, proportions, guard pose, screen position, railing cracks, retreat route, dragon silhouette, cavern geometry, lighting, lens, and grade.
PROP LOCK: His signal talisman remains rigidly attached with unchanged shape and brightness; his weapon stays fixed in its lowered or sheathed first-frame position. Every railing fragment and piece of equipment remains untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one restrained flash of disapproval that settles into controlled eye contact; the head and hands remain still.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, stance change, weapon movement, hand signal, talisman contact, dragon approach or attack, prop or body morphing, substitution, duplication, disappearance, changing railing cracks, face or armor drift, pseudo-text, heavy steam, camera move, or cuts.
END STATE: His pose, hands, identity, armor, talisman, weapon, railing, route, dragon silhouette, and cavern geometry match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.encounter.abuse -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: The player directs hostility at him while an ink dragon moves beneath the mud; he refuses the argument and returns focus to survival.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, disciplined urgency, medium volume, and firm measured pacing. Do not sound offended or preachy; the danger drives the line. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “黑泥下的东西不会等我们吵完。收起敌意，先决定如何活着离开。”
~~~

<!-- response:npc.invalid.encounter.irrelevant -->
#### RESPONSE npc.invalid.encounter.irrelevant

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_cavern_ally_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, realistic skin and ash-white armor, grounded jade and black mud, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous tactical medium close-up. Behind the cracked white-jade railing, show the exact same approved temporary ally; the stationary gold chest, marked retreat line, and distant ink-dragon silhouette remain separately readable in depth.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, hair, armor, proportions, guard pose, screen position, railing cracks, chest, retreat line, dragon silhouette, cavern geometry, lighting, lens, and grade.
PROP LOCK: His signal talisman and weapon remain fixed; the chest stays closed and stationary; the retreat marker, railing fragments, and all equipment retain exact shape, count, material, and position.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character makes one brief redirecting eye movement toward the already-visible immediate danger or bargaining cue, then returns to the player.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, pointing at three objects, hand signal, chest movement or opening, new mud ripple as an object, weapon or talisman movement, dragon attack, morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, heavy steam, camera move, or cuts.
END STATE: His pose, hands, identity, armor, talisman, weapon, chest, route, railing, dragon silhouette, and cavern match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.encounter.irrelevant -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: The player goes off topic during the encounter; he identifies the chest, retreat route, and ink dragon as the immediate facts.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, tactical focus, medium volume, and crisp measured pacing. Give distinct weight to “宝匣、退路和墨蛟”. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “宝匣、退路和墨蛟才是眼前事实。别让无关的话暴露我们的空隙。”
~~~

<!-- response:npc.invalid.encounter.too_long -->
#### RESPONSE npc.invalid.encounter.too_long

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_cavern_ally_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, realistic skin and ash-white armor, grounded jade and black mud, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous tactical medium close-up. Behind the cracked white-jade railing, show the exact same approved temporary ally; the existing crack in one physical formation pillar and the distant ink-dragon silhouette remain visible.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hair, armor, proportions, guard pose, screen position, railing cracks, pillar geometry and existing crack, dragon silhouette, cavern, lighting, lens, and grade.
PROP LOCK: His talisman and weapon remain fixed and untouched. The pillar, railing, route marker, and all equipment keep exact shapes, counts, materials, and positions.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows controlled time pressure through a slight brow change and one short eye movement toward the active danger; no gesture follows.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, spreading crack, pillar break, compressed hand signal, weapon or talisman movement, dragon attack, object or body morphing, substitution, duplication, disappearance, new damage, face or armor drift, pseudo-text, heavy steam, camera move, or cuts.
END STATE: His pose, hands, identity, armor, talisman, weapon, pillar crack, railing, dragon silhouette, and cavern geometry match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.encounter.too_long -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: A formation pillar is breaking while the player gives a long plan; he needs the one executable part immediately.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, controlled urgency, medium volume, and a brisk but intelligible pace. The first sentence reports danger; the second requests one action. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “阵柱正在断裂。把计划压成一句，我只需要可执行的部分。”
~~~

<!-- response:npc.invalid.encounter.silence -->
#### RESPONSE npc.invalid.encounter.silence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_cavern_ally_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, realistic skin and ash-white armor, grounded jade and black mud, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous tactical medium close-up. Behind the cracked white-jade railing, show the exact same approved temporary ally already holding the conservative guard pose over the marked retreat route; the distant ink-dragon silhouette remains visible.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the first frame's exact face, hair, armor, proportions, guard pose, screen position, railing cracks, retreat marker, dragon silhouette, cavern geometry, lighting, lens, and grade. He never moves into a new stance.
PROP LOCK: His signal talisman and weapon stay fixed exactly as shown; there is no second signal object. Every route marker, railing fragment, and equipment item remains unchanged and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character waits in stillness, blinks once naturally, then gives one very small patient head inclination under three degrees.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, taking a stance, readying a second signal, hand gesture, weapon or talisman movement, dragon attack, morphing, substitution, duplication, disappearance, changing route marker, face or armor drift, pseudo-text, heavy steam, camera move, or cuts.
END STATE: His guard pose, hands, identity, armor, talisman, weapon, retreat marker, railing, dragon silhouette, and cavern match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.encounter.silence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: The player remains silent during an active encounter; he chooses the safest temporary guard while preserving the player's chance to change course.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, calm pressure, medium volume, and firm measured pacing. The line must protect agency rather than sound like an ultimatum. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “你不表态，我便按最保守的方式守住退路。仍可在墨蛟扑出前改变决定。”
~~~

<!-- response:npc.invalid.encounter.low_confidence -->
#### RESPONSE npc.invalid.encounter.low_confidence

视频模型与设置：Veo 3.1 Fast 预览、Veo 3.1 最终；Image-to-video；16:9，24 fps，8 秒；关闭生成音频。

上传首帧（仅上传这一张）：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/firstframe_dialogue_cavern_ally_grand_v2.png`

完整视频提示词（Veo 3.1 无声表演底片，直接复制）：

~~~text
Original cinematic Chinese cultivation-fantasy world, same universe as Red Mist Secret Garden. Photoreal live-action, realistic skin and ash-white armor, grounded jade and black mud, restrained spiritual VFX, clear depth. Landscape 16:9, 24 fps, exactly 8 seconds, one locked continuous tactical medium close-up. Behind the cracked white-jade railing, show the exact same approved temporary ally listening while the distant ink-dragon silhouette remains visible through thin steam.
LIGHTING DESIGN: Match the uploaded grand-v2 first frame exactly: preserve its motivated daylight, luminous ivory-jade and gilded-bronze highlights, vermilion accents, cool detailed shadows, face exposure, eye catchlights, and three-dimensional depth. Do not revert to a globally dark, murky, moonlit, monochrome, or crushed-black grade.
PHYSICAL INTEGRATION: Keep every person grounded in the photographed set with stable contact shadows, shared floor and wall bounce light, correct reflections, occlusion, perspective and scale, natural skin texture, cloth weight, and hair. No person may look pasted on, waxy, miniature, billboard-like, or isolated from the environment lighting.
INVARIANT STATE: Lock the uploaded first frame's exact face, hair, armor, proportions, posture, screen position, railing cracks, dragon silhouette, cavern geometry, lighting, lens, and grade.
PROP LOCK: His signal talisman remains rigidly attached and his weapon fixed as shown. Hands, railing fragments, route markers, and equipment remain unchanged and untouched.
SILENT PERFORMANCE PLATE: Generate visual performance and ambient set motion only. The mouth and jaw remain naturally closed and at rest for the entire clip. No dialogue, voice, lip sync, visible phoneme, generated audio, captions, subtitles, or text.
PRIMARY ACTION: The character shows one subtle uncertainty or wary narrowing of the eyes, then returns to a neutral attentive hold without moving the hands.
ALLOWED MOTION: Natural blinking and breathing, the single specified micro-expression or eye movement, tiny loose-hair and cloth-edge motion, and subtle reversible motion already present in the uploaded first frame. The mouth and jaw stay closed. Keep the camera locked.
FORBIDDEN TRANSITIONS: No mouth opening, lip sync, spoken performance, subtitle generation, leaning through steam, hand uncertainty signal, weapon or talisman movement, dragon attack, prop or body morphing, substitution, duplication, disappearance, face or armor drift, pseudo-text, face occlusion, camera move, or cuts.
END STATE: His body position, hands, identity, armor, talisman, weapon, railing, route, dragon silhouette, and cavern match the first frame at the end. The mouth is closed in a neutral rest pose. No dialogue, voices, music, captions, subtitles, burned-in text, game UI, logos, modern objects, anime style, plastic skin, excessive bloom, or real-actor imitation.
~~~

<!-- tts:response:npc.invalid.encounter.low_confidence -->

语音模型与设置：Gemini 3.1 Flash TTS (Preview) 试音，Gemini 2.5 Pro TTS 最终；voice 选 Iapetus；Standard Mandarin Chinese；WAV/LINEAR16、mono、干声。

完整语音提示词（直接复制）：

~~~text
Audio profile: The approved cavern temporary ally, an original East Asian man around twenty-nine, clear-headed, battle-worn, practical, and willing to cooperate without surrendering agency. Use the selected Iapetus voice consistently for every cavern-ally line.
Scene: Steam and supernatural noise obscure the player's words; he requests a clearer statement or direct action without guessing.
Director's notes: Speak in natural Standard Mandarin Chinese with clear mainland pronunciation, alert candor, medium volume, and a concise even pace. Use a natural cinematic pace with clear clause pauses; never rush to fit an eight-second video, because the final approved lip-sync duration follows this voice. Read the transcript exactly once. Do not add, remove, paraphrase, translate, repeat, sing, laugh, whisper, announce punctuation, or produce sound effects, music, ambience, room tone, or reverb. Output clean dry mono speech only.
Transcript: “雾声盖住了你的话。再说得明确些，或者直接选定行动。”
~~~

## 人工交付规则

### 第一批：先做 G2 小样，不要一次生成全部文件

1. 在语音页面先生成 4 个声线小样：青衣侦察者选 Kore、阵灵选 Schedar、石峻选 Algenib、蛟窟盟友选 Iapetus。语音页不上传图片；每个角色先用本组第一条 TTS 提示试听。
2. 在视频页面先只生成青衣侦察者 `inspect_mist` 的 1 个 Veo 3.1 Fast 无声预览：上传对应 `*_grand_v2.png`，关闭生成音频，只复制视频代码块。
3. 该样片的人脸、发型、服装、闭口状态、道具稳定、人物落地感、日光层次和无字幕均达到人工 4/5 后，再按角色族逐批生成；失败时先停下调整提示词，不用增加候选数掩盖问题。
4. 10 个无对白主剧情镜头可独立用 Veo 3.1 Fast 做预览；最终 Veo 3.1 版本必须等人物和新版堂皇世界风格通过后再生成。

### 文件命名

- 主剧情视频：`node__{nodeId}__preview_v01.mp4`
- NPC 回应视频：`response__{responseId}__preview_v01.mp4`
- 独立语音：`voice__{responseId}__preview_v01.wav`
- 最终版本把 `preview` 改为 `final`，不覆盖预览文件。

### 交付与暂停点

生成后把文件放入一个新的人工暂存目录并告诉 Codex 该目录的绝对路径。不要覆盖现有 `fmv_gate_arrival`、`fmv_celestial_flight`、`fmv_herb_courtyard`、`fmv_sword_vault` 四段 MP4。在收到明确指令前，Codex 不得把新视频登记为 `approved`、不得修改非零预算、不得调用任何视频 API。

独立 TTS 干声不能直接叠加到闭口 Veo 底片上冒充口型同步。最终口型工具、授权方式和预算必须由人工决定；在获批前，Codex 会停在“无声底片＋独立干声”交付状态。

### 逐帧时序一致性验收（任何一项失败即退回）

不要只看首帧和末帧。每条 8 秒 Veo 视频至少检查 `0 / 2 / 4 / 6 / 8` 秒，并以正常速度再完整播放一次；TTS 干声单独检查自然语速、停连和逐字准确性，不要求硬塞进 8 秒。

1. 人脸、发际线、发型、服装层数、伤痕和人物数量全程一致；人物没有无脚本地进出画面。
2. 每件锁定道具的数量、形状、材质、尺寸、花纹、握持手、佩挂点和屏幕位置保持一致；不得变成包裹、布、绳、卷轴或另一件道具。
3. 手指、手掌、衣袖、雾和前景物不得与脸或锁定道具融合，也不得长时间遮住它们来掩盖变形。
4. 建筑、阵枢、门、栏杆、路线、阵纹和怪物肢体数量保持稳定；不得新增伪文字、随机符号或未要求的发光图案。
5. 全片只能发生该条 `PRIMARY ACTION`；出现额外拿取、指点、交换、攻击、走位、传送或镜头切换即退回。
6. NPC 无声底片必须全程闭口、无声音、无字幕、无乱码；TTS 必须逐字核对中文台词、单一说话者、自然停顿和声线，任何增删、改写、串音、旁白或赶语速均退回。
7. `END STATE` 必须成立。即使中间只有一帧发生变形，随后又恢复，也不能通过。

若界面一次给出多条结果，它们仍是同一镜头的候选，不是分支素材。只保留通过全部检查且综合评分最高的一条；其余保留在人工暂存区，不登记为 `approved`，也不进入 Unity 媒体清单。

## 官方能力依据

- [Veo 3.1 官方模型说明](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/veo/3-1-generate)：用于稳定的文本/图片到视频、首尾帧与高分辨率环境镜头。
- [Gemini TTS 官方说明](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts)：文字输入、音频输出，可用自然语言控制角色、语气、节奏和口音；Gemini 3.1 Flash TTS 适合快速试音，Gemini 2.5 Pro TTS 适合最终高保真语音。
