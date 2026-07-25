# 《赤雾秘苑》CX-505 堂皇版单首帧参考图包

> **青衣侦察者 v4 已直接纳入本 CX-505 参考包：** 三组对话主回应及其五类异常回应均使用 Unity `*_v4.png` 首帧；旧 `*_grand_v2.png` 仅保留作历史场景构图对照，不再作为这些回应的上传首帧。对应的视频与 TTS 正文均在 `video-generation-prompts.cx505.md` 内，不另设覆盖文件。

状态：`generated_with_gpt_image_2`、`grand_v2_active_for_non_scout_assets`、`scout_dialogue_v4_active`、`manual_video_generation_only`。

## 处理结论

本轮不是给旧图增加曝光或对比度，而是用 Codex 内置 GPT Image 2 / imagegen 对 15 张首帧全部重新进行场景设计：建筑尺度、空间功能、材质、日照、人物站位与环境光均已重建。旧的 `*_v1.png` 和 `firstframe_node_ending_v2.png` 仅保留用于回滚与比较，不再上传到 Veo。

非侦察者首帧继续使用 `*_grand_v2.png`；青衣侦察者三组对话及其五类异常回应统一使用 `*_v4.png`。新版世界材质为白玉与暖色浅石、朱漆木构、老化鎏金铜、青瓷瓦、矿物琉璃、云海与瀑布；红雾只作为受控的危险层，不再覆盖整个画面。每张图都有真实亮部、冷暖半影与保留细节的深色构图边缘，人物必须共享场景的主光、反射光、接触阴影与透视。

当前 Veo `Image-to-video` 页面每次只上传一张已合成的 16:9 首帧。不要再追加人物定妆图或场景图；`结束（可选）` 只用于真正的结束帧连续性，不是人物参考槽。

## 40 条视频的 15 张首帧复用表

统一目录：`unity/RedMistVerticalSlice/Assets/Resources/Generated/VideoFirstFrames/`。

| 首帧文件 | 使用范围 | 覆盖视频数 |
|---|---|---:|
| `firstframe_node_camp_grand_v2.png` | NODE `camp` | 1 |
| `firstframe_node_alliance_grand_v2.png` | NODE `alliance` | 1 |
| `firstframe_node_corpse_signs_grand_v2.png` | NODE `corpse_signs` | 1 |
| `firstframe_node_rescue_grand_v2.png` | NODE `rescue` | 1 |
| `firstframe_shijun_negotiation_grand_v2.png` | NODE `shijun`、主回应 `verify_bargain`、5 条 hostile 异常回应 | 7 |
| `firstframe_node_formation_grand_v2.png` | NODE `formation` | 1 |
| `firstframe_node_combat_one_grand_v2.png` | NODE `combat_one` | 1 |
| `firstframe_node_combat_two_grand_v2.png` | NODE `combat_two` 首次预览；最终优先用 `combat_one` 获准末帧 | 1 |
| `firstframe_node_aftermath_grand_v2.png` | NODE `aftermath` 首次预览；最终优先用 `combat_two` 获准末帧 | 1 |
| `firstframe_node_ending_grand_v2.png` | NODE `ending` | 1 |
| `firstframe_dialogue_scout_mist_v4.png` | 主回应 `inspect_mist`、5 条 calm 异常回应 | 6 |
| `firstframe_dialogue_scout_alliance_v4.png` | 主回应 `cautious_cooperation`、5 条 ally 异常回应 | 6 |
| `firstframe_dialogue_scout_rescue_v4.png` | 主回应 `secure_survivor` | 1 |
| `firstframe_dialogue_formation_spirit_grand_v2.png` | 5 条 system 异常回应 | 5 |
| `firstframe_dialogue_cavern_ally_grand_v2.png` | 主回应 `coordinate_retreat`、5 条 encounter 异常回应 | 6 |
| **合计** | 10 个节点视频＋30 个回应视频 | **40** |

## 人工生成视频时的固定步骤

1. 在 `video-generation-prompts.cx505.md` 找到对应条目。
2. 选择 Veo 3.1 Fast 预览或 Veo 3.1 最终，并选择 `Image-to-video`。
3. 只上传条目列出的一个首帧；非侦察者条目使用 `*_grand_v2.png`，青衣侦察者条目使用 `*_v4.png`，不要上传本文件记录的旧源图。
4. NPC 回应画面关闭“生成音频”，只复制无声表演底片提示词；中文只在单独的 TTS 页面生成。
5. 首次结果数量设为 `1`；通过人物、道具、亮暗、物理落地和无字幕检查后再制作最终候选。
6. `combat_two` 与 `aftermath` 的最终版优先只上传上一段获准视频导出的末帧。

## GPT Image 2 统一重建设计合同

下面这一段是所有 15 张图共同执行的美术与质量合同；各资产小节记录实际使用的源图和场景补充。图像已生成，无需用户再次调用 Image 2。

~~~text
EDIT / RECREATE the referenced image as a new production-quality first frame, not a simple exposure or contrast adjustment. Landscape 16:9 cinematic still, no black bars. Original Chinese cultivation-fantasy world, photoreal live-action, premium large-budget historical fantasy production design, no imitation of any real actor. Preserve approved character identity, age, hair, costume identity and story facts while substantially rebuilding the set.

WORLD MATERIAL BIBLE: luminous ivory jade and warm pale limestone, vermilion lacquered timber, aged gilded bronze, celadon glazed tile, translucent mineral glass, silk without writing, cloud sea, waterfalls, and restrained vermilion mist only in danger layers. Architecture must be monumental, sacred, richly layered and physically buildable, never generic dark ruins, a primitive forest, or science-fiction UI.

LIGHTING DESIGN: bright motivated post-rain daylight or grand sunrise, sunlit architecture and sky balanced with cool detailed shadows used as framing. Faces are properly exposed with shared key, ambient fill, environment bounce, rim light and eye catchlights. Do not globally underexpose, crush blacks, use a murky monochrome night grade, or merely increase contrast.

PHYSICAL INTEGRATION: grounded feet, contact shadows, reflected floor and wall light, correct reflections, occlusion, perspective and scale, natural skin pores, cloth weight and fine hair. Every person must look photographed on the physical set, never pasted on, waxy, miniature, billboard-like, or isolated from the environment light.

CONTINUITY: keep prop and person counts small and unambiguous. No text, pseudo-writing, subtitles, captions, logos, UI, watermark, modern objects, anime, illustration, giant energy beam, neon holographic circle, excessive bloom, malformed hands, extra fingers, stretched anatomy, duplicates, floating props, or object transformations.
~~~

## 15 张实际首帧重建提示词

### `firstframe_node_camp_grand_v2.png`

源图：`firstframe_node_camp_v1.png`，只锁两位主角身份与备战关系。

~~~text
Rebuild the rough dark campsite as a majestic expedition command terrace of an ancient celestial sanctuary. Shen Yan stands at screen-left in deep teal travel robes with one closed dark-gold flying-blade case. Chu Mingqi stands at screen-right in layered moon-white and dark-vermilion battle robes with exactly one rigid circular moon-ring attached at her waist. A physical relief route table separates them. Use ivory-jade flagstones, vermilion columns, gilded roof brackets, silk awnings without writing, carved balustrades, ordered lanterns, waterfalls, cloud sea, palace roofs and a monumental sealed bronze mountain gate. Medium-wide 35mm two-shot with a shaded awning foreground and bright sanctuary background. Both faces receive warm sun, cool cloud fill and jade-table bounce. Hands remain empty; no package, scroll or handheld prop appears.
~~~

### `firstframe_node_alliance_grand_v2.png`

源图：`firstframe_node_alliance_v1.png`，锁主角身份、对峙方向和阵营分组。

~~~text
Create a vast sunlit oath terrace above a golden-white cloud sea, serving as the forecourt of a celestial embassy palace. Shen Yan remains left and Chu Mingqi right across one ivory-jade physical relief map; her single waist moon-ring and his single closed blade case remain attached. Separate adult disciple formations wait under shaded vermilion colonnades. Add jade balustrades, gilded crane lanterns, flying bridges and monumental embassy gates. Use a balanced 40mm medium-wide two-shot, warm sunlight across both faces and map, cool colonnade shadows, wet-stone reflections and no hand touching any prop.
~~~

### `firstframe_node_corpse_signs_grand_v2.png`

源图：`firstframe_node_corpse_signs_v1.png`，锁第一人称调查、尸体和三处痕迹。

~~~text
Replace the generic dark forest with an abandoned celestial outer garden: bright white-jade stream stairs, red maple canopy, gilded water wheels, moon bridges, shrine pavilions, waterfalls and distant sunlit palace terraces. Keep exactly one motionless fully clothed fallen adult, exactly three hair-thin cuts in one black-barked tree and one heat-dried silver web trace. Show only one empty dark gloved hand and a small shoulder edge in the shaded foreground. Use warm sun shafts, bright water caustics and cool canopy shadow. No perception spell, spectral attackers, extra people or gore in the first frame.
~~~

### `firstframe_node_rescue_grand_v2.png`

源图：`firstframe_node_rescue_v1.png`，之后进行定点修订，删除门后的额外人影。

~~~text
Rebuild the rescue point as a majestic damaged sanctuary procession gallery with vermilion colonnades, white-jade relief panels, gilded ward anchors, celadon eaves and a sunlit bronze gate across a cloud chasm. Keep exactly four adult presences: Shen Yan foreground-left, Chu Mingqi center with one waist moon-ring, one wounded disciple behind the translucent jade ward, and exactly one unresolved standing shadow behind the ward. Warm gate daylight and cool jade bounce must illuminate the same bodies and floor. No fifth silhouette, extra ring, active hand spell, creature or writing.
~~~

### `firstframe_shijun_negotiation_grand_v2.png`

源图：`firstframe_shijun_negotiation_v1.png`，锁石峻、阵钉与交易物。

~~~text
Place the same lean, sharp-eyed Shi Jun in a majestic Four-Gate audience court of ivory-jade mechanical doors, vermilion beams, gilded brackets and bright clerestory windows. He holds exactly one slender blood-marked bronze formation spike upright in one still hand. One token and one closed medicine pouch rest separately on a pale-jade plinth; two party members appear only as guarded shoulder silhouettes at lower right. Use a 50mm medium shot, warm clerestory key, cool jade fill and deep gate recesses. Do not place a cyan circle behind his head; no spike movement, extra weapon or text.
~~~

### `firstframe_node_formation_grand_v2.png`

源图：`firstframe_node_formation_v1.png`，锁两位主角与四枢功能；重建为日照阵廷。

~~~text
Rebuild the formation hall as a vast circular Four-Gate sun court beneath an open oculus and coffered celestial roof. Exactly four tactile hubs occupy distinct positions: aged-bronze astronomical armature, lapis-blue stone chime mechanism, translucent medicinal-leaf crystal reliquary and suspended circular water mirror. One incomplete carved jade-gold floor circuit links them. Shen Yan and Chu Mingqi stand at believable human scale near separate hubs, hands lowered. Use ivory-jade floors, vermilion galleries, gilded guardian reliefs, bright oculus sunlight, cool floor bounce and deep balcony shadows. No giant beam, holographic panel, floating sword or activated pulse in the still.
~~~

### `firstframe_node_combat_one_grand_v2.png`

源图：`firstframe_node_combat_one_v1.png`，锁阵营站位与墨蛟身份；重建战斗空间。

~~~text
Create a collapsed but magnificent geothermal treasure pavilion below the sanctuary: pale-jade causeways, gilded support columns, vermilion galleries, a sunlit fractured oculus, controlled black mineral pools and amber vents. Exactly one hornless ink-scaled dragon has one stable head, two forelimbs and a coherent serpentine body. Shen Yan holds the left flank; Chu Mingqi with one waist moon-ring holds the right; exactly two wounded disciples shelter behind a solid jade railing. Keep the bright escape route visible. Use a low 32mm tactical wide shot with warm oculus rim, amber vent light and cool jade fill. No spell burst, extra human, duplicated dragon anatomy or splash explosion.
~~~

### `firstframe_node_combat_two_grand_v2.png`

源图：`firstframe_node_combat_two_v1.png`，锁第二轮局面并沿用同一地宫材质。

~~~text
Continue in the same grand geothermal pavilion with exactly one wounded hornless ink dragon, one already-cracked ivory support pillar, Shen Yan left, Chu Mingqi right, exactly one injured disciple low in protected foreground and one closed gold reliquary chest on a dry raised plinth. Sunlight defines the dragon scales, pillar crack and chest; cool jade bounce exposes faces while localized mineral water provides dark framing. The foreclaw is near but not striking the crack. Keep all visible blades, the single moon-ring, chest and dragon anatomy stable; no pillar collapse, floating chest, fire ring or second creature.
~~~

### `firstframe_node_aftermath_grand_v2.png`

源图：`firstframe_node_aftermath_v1.png`，锁战后四个选择目标。

~~~text
Show the same magnificent pavilion after the dragon withdraws: pale-jade floor remains visible above localized dark residue, with gilded columns, vermilion galleries and a bright broken roof. Exactly three adults are present: Shen Yan, Chu Mingqi and one wounded disciple. Keep one closed gold chest on its plinth, one medicinal-root bundle in a physical tray and one bright narrowing exit, all spatially separated. Neither protagonist touches an objective. Use late-afternoon sun through dust, cool jade floor bounce and detailed gallery shadow; bittersweet rather than horror-dark.
~~~

### `firstframe_node_ending_grand_v2.png`

源图：`firstframe_node_ending_v2.png`；首轮新版出现布包，已定点修订为沈砚空手。

~~~text
Create a grand sunrise overlook outside the monumental ivory-and-bronze mountain gate, with a wet vermilion ceremonial terrace, jade balustrades, hanging lantern towers, waterfalls, cloud sea and radiant palace mountains. Preserve exactly four adults: Shen Yan empty-handed at left, one helper, one supported rescued disciple and Chu Mingqi supporting at right. Shen's single blade case and Chu's single moon-ring remain attached; the artifact stays concealed. Warm sunrise backlight and peach clouds balance cool mountain shadows and detailed gate shade. No cloth bag, package, extra survivor, revealed artifact or celebratory pose.
~~~

### `firstframe_dialogue_scout_mist_v4.png`

源图：`firstframe_dialogue_scout_mist_v1.png`；本图为青衣侦察者新版身份母版。

~~~text
Create a silent performance plate for the approved twenty-seven-year-old East Asian woman scout: oval face, straight black brows, low braided ponytail, muted celadon narrow-sleeved robe, charcoal embossed leather shoulder guard and exactly one pale-jade wind-listening talisman pinned at her chest. Place her in a 65mm medium close-up beside a majestic sunlit living-bronze mist gate, white-jade safe stair, vermilion colonnade, ritual lantern towers, waterfalls and cloud palaces. Mouth naturally closed, hands lowered. Warm morning key, cool cloud fill, gold bounce and a dark column edge. Exactly one foreground scout; no held tablet, gesture, dialogue or text.
~~~

### `firstframe_dialogue_scout_alliance_v4.png`

源图：新版雾门身份母版＋`firstframe_dialogue_scout_alliance_v1.png` 的剧情位置。

~~~text
Use the exact same scout face, hair, robe, shoulder guard and pinned talisman from the identity master. Place her at the left guard position of a magnificent sunlit alliance terrace with a physical ivory-jade relief map in the lower foreground, disciplined adult formations under shaded colonnades, gilded crane lanterns, bridges, waterfalls and an embassy gate across cloud mountains. Use a 65mm medium shot with the scout large on the left third, mouth closed, hands lowered and empty. Do not reproduce the old handheld black tablet. Match warm sun, cool cloud fill, contact shadow and reflected floor light.
~~~

### `firstframe_dialogue_scout_rescue_v4.png`

源图：新版雾门身份母版＋`firstframe_dialogue_scout_rescue_v1.png` 的救援信息。

~~~text
Use the exact same scout identity beside a damaged but majestic sanctuary gallery of vermilion columns, white jade, gilded ward anchors and a bright cloud gate. Behind a waist-high translucent jade ward, exactly one wounded adult disciple and exactly one unresolved standing shadow provide context. The scout remains large on the left third in a 65mm stopped guard shot, mouth closed, hands empty and lowered, with her one talisman pinned. Warm gate rim and cool jade bounce integrate all bodies. No handheld tile, extra foreground person, generated text or speaking pose.
~~~

### `firstframe_dialogue_formation_spirit_grand_v2.png`

源图：`firstframe_dialogue_formation_spirit_v1.png`，锁阵灵身份与四枢关系。

~~~text
Show exactly one elegant adult androgynous formation spirit as a human-scale translucent jade projection with calm East Asian facial structure, long dark hair and a ceremonial robe traced by fine aged-gold seams. It emerges from one small physical crystal socket in the bright Four-Gate sun court. A 55mm medium shot keeps the spirit large at left-center and exactly four physical hubs in depth. The projection receives oculus sunlight, casts restrained jade contact glow and reflects gold floor light. Mouth closed; one relaxed hand stays above but does not touch a hub. No ghost swarm, floating panels, swords, giant energy disc or text.
~~~

### `firstframe_dialogue_cavern_ally_grand_v2.png`

源图：`firstframe_dialogue_cavern_ally_v1.png`，锁临时盟友身份；重建地宫采光。

~~~text
Show the approved twenty-nine-year-old East Asian temporary ally with narrow weathered face, tied black hair, ash-white layered light armor over charcoal cloth and exactly one rigid white-jade signal talisman mounted at his chest. Place him in a 65mm tactical medium close-up at a grand geothermal sanctuary undercroft with cracked white-jade causeway, colossal ivory arches, gilded suspension machinery, vermilion remnants, amber vents and a strong daylight shaft from a fractured ceiling. Hands relaxed and empty, mouth closed. A single distant stable dragon-tail segment may remain far behind the railing; no crowd, handheld orb, raised hand or creature attack.
~~~
