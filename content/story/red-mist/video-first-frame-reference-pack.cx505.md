# 《赤雾秘苑》CX-505 单首帧参考图包

状态：`generated_with_gpt_image_2`、`manual_video_generation_only`。

## 处理结论

不需要重做全部旧素材。`red_mist_establishing.png`、`dragon_cavern-v2.png`、`dragon_attack-v1.png`、`scene_celestial_formation_hall_v3.png` 以及沈砚、楚明绮、石峻的定妆图质量足够，已作为 GPT Image 2 的内部世界观或人物身份参考。旧的天气四联图、模块表、竖版人物图不再直接上传到 Veo。

当前 Veo `Image-to-video` 页面每次只需要一张已合成的 16:9 起始图。下面 15 张图已经把人物、场景、光线、法器和镜头构图合并好；生成视频时不得再追加人物图或场景图。`结束（可选）` 只用于真正的结束帧连续性，不是人物参考槽位。

## 40 条视频的 15 张首帧复用表

所有路径均相对于仓库根目录。

| 首帧文件 | 使用范围 | 覆盖视频数 |
|---|---|---:|
| `firstframe_node_camp_v1.png` | NODE `camp` | 1 |
| `firstframe_node_alliance_v1.png` | NODE `alliance` | 1 |
| `firstframe_node_corpse_signs_v1.png` | NODE `corpse_signs` | 1 |
| `firstframe_node_rescue_v1.png` | NODE `rescue` | 1 |
| `firstframe_shijun_negotiation_v1.png` | NODE `shijun`、主回应 `verify_bargain`、5 条 hostile 异常回应 | 7 |
| `firstframe_node_formation_v1.png` | NODE `formation` | 1 |
| `firstframe_node_combat_one_v1.png` | NODE `combat_one` | 1 |
| `firstframe_node_combat_two_v1.png` | NODE `combat_two` 的首次预览；最终优先改用 `combat_one` 获准末帧 | 1 |
| `firstframe_node_aftermath_v1.png` | NODE `aftermath` 的首次预览；最终优先改用 `combat_two` 获准末帧 | 1 |
| `firstframe_node_ending_v2.png` | NODE `ending` | 1 |
| `firstframe_dialogue_scout_mist_v1.png` | 主回应 `inspect_mist`、5 条 calm 异常回应 | 6 |
| `firstframe_dialogue_scout_alliance_v1.png` | 主回应 `cautious_cooperation`、5 条 ally 异常回应 | 6 |
| `firstframe_dialogue_scout_rescue_v1.png` | 主回应 `secure_survivor` | 1 |
| `firstframe_dialogue_formation_spirit_v1.png` | 5 条 system 异常回应 | 5 |
| `firstframe_dialogue_cavern_ally_v1.png` | 主回应 `coordinate_retreat`、5 条 encounter 异常回应 | 6 |
| **合计** | 10 个节点视频＋30 个回应视频 | **40** |

统一目录：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/`。

## 人工生成视频时的固定步骤

1. 选择对应条目指定的视频模型和 `Image-to-video`。
2. 在“输入图片”只上传 `video-generation-prompts.cx505.md` 对应条目列出的那一张 PNG。
3. 不上传本文件下方列出的 GPT Image 2 内部参考源；它们只用于制作首帧。
4. 复制对应条目的完整视频提示词，不再追加公共段落。
5. 首次只生成 1 个 720p 候选；人物、构图和动作通过后再生成最终候选。
6. `combat_two` 与 `aftermath` 的最终版优先使用上一段获准视频导出的末帧，并且只上传该末帧。

## GPT Image 2 最终首帧提示词

以下记录用于可重复生成或以后制作变体。每个小节列出的参考图可以同时交给 GPT Image 2；这些多图只发生在静态首帧制作阶段，不是 Veo 上传步骤。

### `firstframe_dialogue_scout_mist_v1.png`

内部参考：`Art/red_mist_establishing.png`（只锁世界与色彩）。

~~~text
Create a brand-new cinematic first-frame still for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is WORLD AND COLOR LANGUAGE ONLY: preserve its monumental dark-bronze mechanisms, pale jade accents, wet black stone, layered translucent vermilion spirit mist, mountain sanctuary scale, cool blue-gray atmosphere, and restrained gold light; do not copy any tiny background people from it.

Output a photoreal live-action 16:9 landscape composition intended to be cropped cleanly to 1920x1080. One original adult East Asian woman, age about 27, is the fixed celadon expedition scout. She must be visually distinct from the two protagonists: narrow oval face, calm observant dark-brown eyes, straight brows, natural skin texture, low practical braided ponytail with two loose rain-damp strands, no glamour makeup. Costume continuity anchor: muted celadon-gray narrow-sleeved robe, charcoal-black worn leather shoulder guard only on her left shoulder, dark cloth forearm wraps, small rectangular white-jade listening talisman hanging beside her collar, compact field satchel, no crown, no white battle robe.

Medium cinematic dialogue shot from waist/chest up, the scout stands on a wet black-stone overlook at the sanctuary entrance, body turned three-quarters toward camera right as if listening to the unseen player just off lens. Her mouth is closed in a neutral attentive rest pose so later video can animate speech. One hand lightly touches the white-jade listening talisman; the other rests beside the satchel. Behind her, layered red spirit mist moves between immense bronze gate ribs and distant suspended shrine terraces, unmistakably supernatural rather than an ordinary forest. Practical cold jade rim light plus soft warm lantern key light, 35mm lens, realistic depth, fine cloth and skin detail, restrained atmospheric VFX, premium film production design. Keep the character fully inside the central cinematic safe area and leave clean darker negative space in the lower right for Unity dialogue choices. No text, captions, UI, logos, watermark, collage, split screen, character sheet, extra foreground people, duplicate limbs, deformed hands, anime, illustration, waxy skin, modern objects, imitation of any real actor, or black letterbox bars.
~~~

### `firstframe_dialogue_scout_alliance_v1.png`

内部参考：上一张斥候首帧（锁人物）＋`Generated/Scenes/firstframe_alliance_chu_petitioners_v1.png`（只锁环境）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic dialogue first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the FIXED CHARACTER IDENTITY: reproduce the same celadon expedition scout exactly—same original East Asian woman's face, age about 27, low braided ponytail, rain-damp loose strands, muted celadon-gray narrow-sleeved robe, charcoal leather guard on her left shoulder, dark forearm wraps, field satchel, and small rectangular white-jade listening talisman by her collar. Reference Image 2 is ENVIRONMENT AND PRODUCTION DESIGN ONLY: use its wet monumental dark-bronze cliff platform, jade-lit railings, suspended sanctuary architecture, moonlit storm clouds and layered vermilion spirit mist, but do not reproduce its foreground people or their faces.

Medium-wide conversational shot at a cautious alliance checkpoint. The fixed scout stands at screen left, three-quarter view toward an unseen player just off camera right. She holds a physical palm-sized black-stone route tablet over a waist-high bronze map table; one celadon jade path glows across the relief while two unsafe red-mist routes stay dim. Her mouth rests closed, expression attentive and guarded, ready for later speech animation. In the midground, two adult expedition silhouettes remain well separated and out of focus; no duplicate scout. Wind gently lifts robe hems and hanging bronze chimes. Premium film lighting, realistic skin, cloth and wet stone, 35mm lens, layered depth, restrained jade-gold VFX. Keep face and hands sharp, character inside cinematic safe area, and clean darker lower-right space for Unity choices. No text, subtitles, UI, logos, watermark, collage, split screen, character sheet, modern objects, anime, illustration, waxy skin, deformed hands, extra fingers, costume drift, imitation of any real actor, or black bars.
~~~

### `firstframe_dialogue_scout_rescue_v1.png`

内部参考：首张斥候首帧（锁人物）＋`Art/red_mist_establishing.png`（只锁世界）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic dialogue first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the FIXED CHARACTER IDENTITY: reproduce the exact same celadon expedition scout—same original East Asian woman's face, age, low braided ponytail, loose wet strands, muted celadon-gray robe, charcoal leather guard on her left shoulder, dark wraps, satchel, and rectangular white-jade listening talisman. Reference Image 2 is WORLD LANGUAGE ONLY: monumental dark bronze, pale jade, wet black stone, layered translucent vermilion spirit mist and distant suspended sanctuary structures.

Tense medium shot beside a fractured black-stone rescue wall. The scout crouches behind a waist-high jade barrier at screen left and looks toward the unseen player at camera right, mouth closed in a focused rest pose. Her left hand holds the white-jade listening talisman near the wall; its faint pulse reveals two short marks and one long mark in dust without any written symbols. Through the translucent barrier, an injured adult disciple is only a soft silhouette; farther back, a second ambiguous shadow moves inside layered red mist. A massive bronze gate is visibly closing in the distant background. Cold jade barrier light, warm practical lantern edge, wet stone, realistic natural skin and cloth, restrained supernatural effects, 50mm cinematic lens. Keep her face, talisman and one clean hand readable; preserve lower-right negative space for choices. No gore close-up, text, captions, UI, logos, watermark, collage, split screen, extra foreground people, duplicate scout, modern objects, anime, illustration, waxy skin, malformed hands, costume drift, imitation of any real actor, or black bars.
~~~

### `firstframe_dialogue_formation_spirit_v1.png`

内部参考：`Generated/Scenes/scene_celestial_formation_hall_v3.png`（锁环境）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic dialogue first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the EXACT ENVIRONMENT AND COLOR LANGUAGE: retain the vast circular dark-bronze formation hall, four physical luminous hubs, jade-gold channels, hanging blades, moonlit red cloud abyss, wet engraved metal and monumental scale.

Introduce the fixed Formation Spirit as one original adult androgynous East Asian humanoid projection hovering twenty centimeters above the central hub. The figure is elegant and unmistakably supernatural but not science-fiction: translucent pale-jade skin with subtle gold kintsugi-like formation veins, shoulder-length black hair floating slowly, calm symmetrical face, layered gender-neutral robe made of light and semi-transparent ancient silk, no armor, no crown. Medium-wide low-angle dialogue composition with the spirit at screen left-center, body three-quarter turned toward the unseen player at camera right, mouth closed and eyes attentive. One open palm is held above a tactile bronze mechanism; three hubs glow softly while the fourth stays dark, creating obvious physical cause and effect. Volumetric moonlight, warm bronze practicals, realistic materials, restrained jade particles, 35mm lens, premium film production design. Keep the face and one anatomically correct hand sharp, full silhouette readable, and clean lower-right negative space for Unity choices. No readable glyphs, text, captions, UI, logos, watermark, collage, split screen, duplicate figure, cyberpunk hologram, neon interface, anime, illustration, waxy skin, deformed anatomy, imitation of any real actor, or black bars.
~~~

### `firstframe_dialogue_cavern_ally_v1.png`

内部参考：`Art/dragon_cavern-v2.png`（锁环境）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic dialogue first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the EXACT CAVERN WORLD AND COLOR LANGUAGE: preserve the immense geothermal hall, dark engraved stone and bronze arches, chained white-jade shrine, glossy black mud, cold blue overhead light, warm distant fire and realistic steam; do not add the dragon in this frame.

Introduce the fixed Cavern Temporary Ally: one original East Asian man about 29, clearly distinct from Shen Yan and Shi Jun, angular but open face, straight nose, alert dark eyes, a short healed scar through the left eyebrow, clean-shaven, black hair tied high with one loose strand. Costume continuity anchor: practical gray-white layered light armor made from aged cloth and pale ceramic lamellae, charcoal under-robe, dark leather bracers, narrow ash-blue sash, and a palm-sized white-light beacon clipped at the sternum; no teal robe, no brown ragged armor, no crown. Medium waist-up dialogue shot from behind a cracked jade railing. He stands at screen left-center, three-quarter toward the unseen player at camera right, mouth closed, one hand holding the glowing beacon above a physical route scratched in wet black stone while the other signals a restrained stop. In the background, wounded adult silhouettes withdraw across a narrow causeway as mud bubbles and a distant chain vibrates. Realistic skin, hair, cloth, ceramic armor, mud and steam, restrained white-jade VFX, 50mm lens, premium film lighting. Keep face, both hands and beacon clear, with darker lower-right negative space for Unity choices. No text, captions, UI, logos, watermark, collage, split screen, extra foreground character, duplicate limbs, deformed hands, modern gear, sci-fi armor, anime, illustration, waxy skin, imitation of any real actor, or black bars.
~~~

### `firstframe_shijun_negotiation_v1.png`

内部参考：`Art/shi_jun-v1.png`（锁人物）＋`Generated/Scenes/scene_celestial_formation_hall_v3.png`（只锁材质与尺度）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the FIXED SHI JUN IDENTITY AND COSTUME: preserve the same original lean East Asian man in his thirties, same sharp tired face, tied unkempt black hair, short mustache and goatee, brown-black layered worn armor and robe, old copper fasteners, red-brown sash, leather forearm guards, and weathered physical realism. Reference Image 2 is WORLD MATERIAL AND SCALE ONLY: monumental dark bronze, pale jade mechanisms, wet engraved floor, hanging ancient blades, jade-gold light and red cloud abyss; do not copy its exact empty overhead camera.

Cinematic waist-up negotiation shot before four monumental dark-bronze formation doors. Shi Jun stands at screen left-center in three-quarter view facing the unseen player at camera right, mouth closed in a guarded rest pose. He turns a blood-marked bronze formation spike between two anatomically correct fingers above a pale-jade plinth. On the plinth are one genuine moon-palace token and a small medicine pouch; fresh black mud steams from the spike. His other hand remains near a concealed hooked weapon. Two adult party members appear only as dim out-of-focus shoulder silhouettes at the extreme right edge, never obscuring the clean lower-right choice area. Slow-push-in-ready composition, 50mm lens, practical warm bronze key light and cold jade rim, realistic skin pores, cloth, metal, mud and restrained spirit vapor, premium production design. Keep Shi Jun's face, hands, spike and bargaining objects sharp. No spoken text rendered, captions, UI, logos, watermark, collage, split screen, character sheet, duplicate Shi Jun, modern objects, anime, illustration, waxy skin, deformed hands, costume drift, imitation of any real actor, or black bars.
~~~

### `firstframe_node_camp_v1.png`

内部参考：`Art/red_mist_establishing.png`（锁世界）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁两位主角）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the WORLD, ARCHITECTURE, ATMOSPHERE AND COLOR LANGUAGE: monumental mountain sanctuary, dark-bronze mechanisms, pale jade, wet black stone, layered translucent vermilion spirit mist, cool blue-gray air and restrained warm lanterns. Reference Image 2 is the FIXED SHEN YAN IDENTITY: preserve his original East Asian male face, age in his twenties, topknot, dark-celadon narrow-sleeved travel outfit, old leather wrist guards, medicine gourd and dark-gold flying-blade case. Reference Image 3 is the FIXED CHU MINGQI IDENTITY: preserve her original East Asian female face, age in her twenties, long black hair with silver ornament, moon-white embroidered layered battle robe, dark-vermilion belt and circular moon-ring artifact.

Wide but intimate expedition camp shot on black volcanic terraces above a sea of supernatural red mist. Shen Yan stands in the left midground checking a compact dark-gold flying-blade case and paper wards; Chu Mingqi stands in the right midground tightening the strap of her circular moon-ring artifact beside medicine gourds. They occupy separate preparations and do not pose for camera, yet both faces remain readable and faithful to references. Foreground: a worn jade-lit supply table with talismans, rope, medicine and a physical route relief. Background: tent cloths, two indistinct adult disciples testing a faint defensive barrier, and an enormous bronze gate pulse inside the mountain. Quiet pre-expedition tension, realistic firelight mixed with cold jade light, 35mm lens, rich foreground/midground/background depth, natural skin and cloth, restrained VFX. Keep all people inside the cinematic safe area and leave clean darker lower-right negative space for Unity UI. No text, captions, UI, logos, watermark, collage, split screen, duplicate protagonists, extra copies, deformed hands, costume drift, modern objects, anime, illustration, waxy skin, imitation of any real actor, or black bars.
~~~

### `firstframe_node_alliance_v1.png`

内部参考：旧联盟首帧（只锁环境）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁两位主角）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is ENVIRONMENT AND PRODUCTION DESIGN ONLY: preserve the wet monumental dark-bronze cliff platform, jade-lit rails, suspended sanctuary towers, moonlit storm, waterfalls and layered vermilion spirit mist; do not copy its foreground people. Reference Image 2 is the FIXED SHEN YAN identity and costume: same original East Asian male face, twenties, topknot, dark-celadon travel layers, old wrist guards and dark-gold flying-blade case. Reference Image 3 is the FIXED CHU MINGQI identity and costume: same original East Asian female face, twenties, long black hair with silver ornament, moon-white embroidered battle robe, dark-vermilion belt and circular moon-ring artifact.

Medium-wide cautious-alliance tableau around a waist-high floating physical black-stone route map. Shen Yan stands at screen left and Chu Mingqi at screen right, both in three-quarter profile facing each other at respectful distance. Each places one hand near a different glowing route; a single jade line joins the two paths at the center, while neither draws a weapon. Their exact faces remain readable, expressions restrained and distrustful. Four rival adult disciples stay small and out of focus in two separate background groups. Wind lifts cloth edges, hidden talisman light briefly shows under sleeves, distant spirit lanterns move through the rain. Premium realistic skin, wet cloth, stone and bronze, 40mm lens, restrained jade-gold VFX, rich depth. Keep both characters and hands inside cinematic safe area and preserve dark clean lower-right space for dialogue choices. No text, captions, UI, logos, watermark, collage, split screen, duplicate protagonists, modern objects, anime, illustration, waxy skin, malformed hands, costume drift, imitation of real actors, or black bars.
~~~

### `firstframe_node_corpse_signs_v1.png`

内部参考：`Art/red_mist_establishing.png`（只锁世界与色彩）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is WORLD AND COLOR LANGUAGE ONLY: dark-bronze ruins, pale jade accents, wet black stone, monumental sanctuary scale, cool blue-gray air and layered translucent vermilion spirit mist; the mist must feel supernatural, never like an ordinary forest.

Low forensic tracking-shot composition beside a shallow mountain stream under a sparse red-leaf canopy. Foreground water bends and begins to run backward around black stones. An adult fallen cultivator lies respectfully in the midground, fully clothed and partly obscured by reeds, with no gore. Three hair-thin luminous cuts cross a charred black-bark trunk; heat-dried silver web strands tremble above wet mud; two sets of footprints split in opposite directions. Show only the playable character's leather-gloved left hand and one shoulder entering from the lower left, releasing a narrow translucent jade perception pulse that reconstructs two faint human motion echoes in the far background. One distant branch bends under unseen weight. Somber, tense, cinematic realism, 28mm lens close to water, natural wet surfaces, restrained jade and vermilion VFX, strong foreground/midground/background depth. Keep evidence readable and leave uncluttered dark lower-right space for an interaction prompt. No close-up corpse face, gore, text, captions, UI, logos, watermark, collage, split screen, monsters, modern objects, anime, illustration, plastic surfaces, deformed anatomy, or black bars.
~~~

### `firstframe_node_rescue_v1.png`

内部参考：斥候救援首帧（只锁环境与局面）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁两位主角）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is ENVIRONMENT AND SITUATION ONLY: preserve the fractured black-stone rescue wall, translucent pale-jade barrier, wet dark bronze, layered red spirit mist, one wounded silhouette and one ambiguous threatening silhouette; do not copy the celadon scout. Reference Image 2 is the FIXED SHEN YAN identity and dark-celadon travel costume. Reference Image 3 is the FIXED CHU MINGQI identity and moon-white/dark-vermilion battle costume with circular moon-ring artifact.

Controlled handheld-ready rescue dilemma. Camera is behind and slightly beside Shen Yan at screen left, showing his recognizable profile and dark-gold flying-blade case as he signals stop. Chu Mingqi stands at screen center-right raising a restrained crescent jade barrier with the moon-ring, her face visible in three-quarter profile. Behind the fractured wall, one injured adult disciple is clearly real but partly veiled by jade glass; a second uncertain shadow circles in layered vermilion mist. Far background: an enormous bronze gate is visibly closing. Both protagonists look toward the same unseen threat, no one crosses the barrier, no attack yet. Realistic skin, rain-damp hair and cloth, wet stone, practical lantern light, cool jade rim, 35mm lens and premium film depth. Keep faces, stop gesture, barrier and two silhouettes readable; reserve a darker clean lower-right choice area. No text, captions, UI, logos, watermark, collage, split screen, duplicate protagonists, gore, modern objects, anime, illustration, waxy skin, malformed hands, costume drift, imitation of any real actor, or black bars.
~~~

### `firstframe_node_formation_v1.png`

内部参考：`Generated/Scenes/scene_celestial_formation_hall_v3.png`（锁阵殿）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁两位主角）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the EXACT FORMATION HALL architecture, mechanism layout, dark-bronze and jade-gold material language, hanging blades, moonlit red-cloud abyss and monumental scale. Reference Image 2 is FIXED SHEN YAN identity and dark-celadon travel costume. Reference Image 3 is FIXED CHU MINGQI identity and moon-white/dark-vermilion battle costume with circular moon-ring artifact.

Reframe the vast hall at a human-height 28mm perspective so the mechanism feels physically explorable rather than like a flat diagram. Four large tactile formation hubs occupy distinct positions: aged bronze, blue stone, medicinal-leaf crystal, and suspended water mirror. Shen Yan stands near the left hub with one hand hovering over its carved bronze control; Chu Mingqi stands farther right studying the water mirror, both exact faces visible in three-quarter profile. Three hubs answer with elegant jade-gold light flowing through engraved floor channels; the fourth hub remains dark. Stone guardian-beast eyes in a wall relief are just beginning to open, making the cause-and-effect readable. Strong foreground mechanism, midground characters, immense background architecture, volumetric moonlight, realistic metal, stone, water and cloth, restrained particles, premium film production design. Keep faces, hands and all four physical hubs readable and leave a darker clean lower-right area for the interactive choice overlay. No text, runes that look like readable writing, flat UI, captions, logos, watermark, collage, split screen, duplicate protagonists, modern objects, anime, illustration, waxy skin, malformed hands, costume drift, imitation of real actors, or black bars.
~~~

### `firstframe_node_combat_one_v1.png`

内部参考：`Art/dragon_attack-v1.png`（锁墨蛟与洞窟）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁两位主角）。

~~~text
Create a brand-new photoreal live-action 16:9 cinematic combat first frame for an original Chinese cultivation-fantasy interactive film. Reference Image 1 is the EXACT CREATURE, CAVERN, SCALE AND MATERIAL LANGUAGE: preserve the massive ink-black hornless flood dragon, wet layered scales, geothermal black-mud displacement, engraved ancient hall, chained white-jade shrine, cold blue and warm fire lighting. Reference Image 2 is FIXED SHEN YAN identity and dark-celadon travel costume with dark-gold flying-blade case. Reference Image 3 is FIXED CHU MINGQI identity and moon-white/dark-vermilion battle costume with circular moon-ring artifact.

Dynamic but clearly readable tactical wide shot from low behind a shattered jade railing. The dragon erupts at center with believable heavy mud displacement and sweeps its tail across a narrow escape causeway. Shen Yan occupies the left flank, exact face in tense three-quarter profile, releasing only three dark-gold flying blades toward one scale seam. Chu Mingqi occupies the right flank, exact face visible, bracing a restrained pale-jade crescent barrier with her circular moon-ring to shield two wounded adult disciples. Neither uses a final technique; this is the decision beat before commitment. Grounded foot placement, physical debris, steam, readable attack lanes, strong parallax, realistic skin, wet hair, cloth, metal and creature anatomy, 24mm cinematic lens, restrained magic and premium action-film lighting. Keep dragon head, both protagonists, weapons and escape route inside safe area; leave the lower right readable but not empty. No victory, gore, text, captions, UI, logos, watermark, collage, split screen, duplicate characters, extra dragon heads, malformed limbs, weightless poses, anime, illustration, waxy skin, costume drift, imitation of real actors, or black bars.
~~~

### `firstframe_node_combat_two_v1.png`

内部参考：获准的第一轮战斗首帧（锁连续性）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（再次锁脸与服装）。

~~~text
Create the next photoreal live-action 16:9 cinematic combat first frame in exact continuity with Reference Image 1. Preserve the identical geothermal black-mud cavern, chained white-jade shrine, same massive ink-black hornless dragon scale and anatomy, damage direction, lighting and color grade. Reference Image 2 confirms Shen Yan's exact face, dark-celadon costume and dark-gold flying blades. Reference Image 3 confirms Chu Mingqi's exact face, moon-white/dark-vermilion costume and circular moon-ring.

A few moments later, the wounded dragon slams a carved support pillar and the suspended shrine begins to tilt. One gold reliquary chest slides toward boiling black mud. Shen Yan remains on the left, now preparing a restrained linked dark-gold blade formation; Chu Mingqi remains on the right, containing a thin ring of vermilion-jade fire with her moon-ring. Both exact faces are readable, each watching whether the other will commit. An injured adult disciple crawls toward the previously visible escape causeway. Steam crosses the frame but does not hide tactical information. Dynamic diagonal composition, cracked stone and realistic heavy debris, grounded feet, believable cloth and hair motion, strong parallax, 24mm action-film lens. End-state image must clearly present three options: final technique, cooperation, or retreat; no victory yet. Keep lower-right sufficiently dark for choices. No extra dragon, duplicate protagonists, unexplained costume or injury changes, text, captions, UI, logos, watermark, collage, split screen, weightless motion, malformed anatomy, anime, illustration, waxy skin, imitation of real actors, or black bars.
~~~

### `firstframe_node_aftermath_v1.png`

内部参考：获准的第一轮战斗首帧（锁洞窟连续性）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁脸与服装）。

~~~text
Create a photoreal live-action 16:9 cinematic aftermath first frame in the exact same geothermal cavern continuity as Reference Image 1. Preserve the chained white-jade shrine, glossy black mud, engraved ancient hall, cold overhead blue and fading warm fire, dragon scale, camera-world geography and realistic damage. The ink-black hornless dragon has withdrawn beneath the mud; show only one broad fading wake and a few submerged scales, never a second creature. Reference Image 2 fixes Shen Yan's exact face, dark-celadon costume and dark-gold flying-blade case. Reference Image 3 fixes Chu Mingqi's exact face, moon-white/dark-vermilion costume and circular moon-ring.

Slow exhausted decision tableau. Shen Yan kneels at screen left beside one wounded adult disciple, exact face mud-streaked but recognizable, one hand hovering before treatment. Chu Mingqi braces at screen right and catches a sliding gold reliquary chest with the rim of her moon-ring; her exact face is tense and tired. Between them lies a bundle of luminous medicinal roots. The narrowing escape causeway is visible in the background under falling dust. Each key objective—injured person, medicine roots, chest, exit—occupies a distinct direction, and both protagonists stop before choosing. Realistic fatigue, grounded posture, wet cloth, skin, metal, mud, steam and restrained spirit light, 35mm lens, premium film lighting. Leave clean dark lower-right space for the final choice. No triumphant pose, duplicate characters, visible full dragon, gore, text, captions, UI, logos, watermark, collage, split screen, malformed hands, costume drift beyond believable damage, anime, illustration, waxy skin, imitation of real actors, or black bars.
~~~

### `firstframe_node_ending_v2.png`

内部参考：未采用的结尾 v1（只保留环境与构图）＋`Art/shen_yan.png`、`Art/chu_mingqi.png`（锁两位主角）；v2 明确移除了重复沈砚与重复背匣。

~~~text
Regenerate a corrected photoreal live-action 16:9 ending first frame. Reference Image 1 provides the approved dawn sanctuary gate, wet path, closing red spirit fissure, survivor-group staging, lighting and bittersweet composition, but it contains a continuity error: the central teal-clothed companion resembles a duplicate Shen Yan and carries a duplicate cylindrical flying-blade case. Remove and redesign that companion completely. Reference Image 2 is the ONLY fixed Shen Yan identity: reproduce exactly one Shen Yan, at screen left, with his exact face, dark-celadon outfit and exactly one cylindrical dark-gold flying-blade case. Reference Image 3 is the ONLY fixed Chu Mingqi identity: reproduce exactly one Chu Mingqi, at screen right, with her exact face, moon-white/dark-vermilion robe and exactly one circular moon-ring artifact.

Keep one injured adult survivor in the middle, supported by two companions. Both companions must be visually distinct minor NPCs in plain ash-gray and muted brown robes, faces mostly turned away, no teal clothing, no white battle robe, no topknot silhouette matching Shen Yan, no cylindrical case, no moon-ring. Shen Yan looks back toward camera once while carrying a small medicine bundle in his hand; Chu Mingqi supports the survivor and glances toward the closing gate. Preserve monumental ancient bronze, pale jade, wet black stone, mountain shrines, cold dawn and restrained vermilion spirit threads. Natural fatigue and damage, realistic skin, cloth and mud, 32mm cinema lens, clean darker lower-right area. Exactly one Shen Yan and exactly one Chu Mingqi. No duplicate main character, duplicate signature weapon, text, captions, UI, logos, watermark, collage, split screen, modern objects, anime, illustration, waxy skin, malformed anatomy, imitation of any real actor, or black bars.
~~~
