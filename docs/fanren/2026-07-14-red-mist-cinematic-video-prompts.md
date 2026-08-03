# 《赤雾秘苑》影视化视频镜头清单与 Google AI 提示词

日期：2026-07-14  
适用范围：20–30 分钟双视角垂直切片  
生产目标：28 段 × 8 秒，共 224 秒（3 分 44 秒）预渲染镜头；其余游玩时间使用实时 3D 场景、角色表演、探索、对话、小游戏与战斗。

## 1. 为什么是 28 段

- 8 段用于秘苑开场、双主角导入、御器飞行和药谷抵达。
- 7 段用于尸体线索、救援分支、石峻交涉和铜门禁制。
- 5 段用于地下空间建立、男女主角相遇与墨蛟预警。
- 7 段用于两阶段墨蛟战。
- 3 段用于三个结局。

视频只承担实时 3D 难以稳定表现的宏大转场、危险瞬间和结局蒙太奇。普通走路、对话、调查和战术选择保留在实时场景中，避免游戏变成连续看片。

## 2. 统一生成参数

- 推荐模型：最终镜头使用 Veo 3.1；可先用 Gemini Omni Flash 或 Veo Fast 生成草稿。
- 画幅：16:9。
- 时长：8 秒。
- 帧率：24 fps。
- 草稿分辨率：720p。
- 最终分辨率：优先 4K；预算有限时使用 1080p。
- 每次只生成一个明确镜头，不要求模型在 8 秒内完成多场戏。
- 有人物的镜头必须附带对应人物连续性板；同一镜头最多提供三张参考图。
- 不在生成视频中制作字幕或界面，字幕由 Unity 叠加。
- 不让生成视频承担关键状态结算；Unity 先结算选择，再播放对应镜头。

## 3. 人物连续性参考

| 人物 | 本地参考图 | 固定外观 |
|---|---|---|
| 沈砚 | `content/characters/fanren/images/fr-main-m-001-character-sheet.png` | 成年东亚男性，瘦削结实，黑发束髻；炭黑、深靛、旧灰和暗青窄袖行装；旧皮靴、药囊、竹木卷筒；克制、警觉 |
| 楚明绮 | `content/characters/fanren/images/fr-main-f-001-character-sheet.png` | 成年东亚女性，黑色长发半束；月白和珍珠灰层叠行装、冷银暗纹、暗朱腰封与赤鸾环；冷静、有领导力 |
| 石峻 | `unity/RedMistVerticalSlice/Assets/Resources/Art/shi_jun-v1.png` | 成年东亚男性，风霜面容，凌乱发髻；沙棕、深褐破旧窄袖衣，血褐腰带；旧铜阵钉、弯钩短刃；危险、贪婪但谨慎 |

人物参考图只用于锁定身份、服装和道具。生成时必须加入泥水、汗湿、风压、衣摆重量、真实皮肤和环境反光，避免棚拍感。

## 4. 所有镜头共用的风格块

将以下内容附加到每条英文提示词末尾：

```text
Visual continuity: grounded live-action eastern historical fantasy, physically plausible environments and movement, real adult East Asian performers, natural skin texture, worn layered fabric, oxidized bronze, wet stone and mud. Supernatural light appears only during an actual spell. Restrained color grade, subtle film grain, realistic atmospheric perspective, cinematic dynamic range. One continuous shot, no montage, no dialogue and no lip-sync. Native environmental audio only.

Avoid: static portrait, character-sheet composition, painted concept-art look, game UI, subtitles, text, logos, watermark, celebrity likeness, modern objects, plastic skin, beauty-filter face, weightless cloth, excessive slow motion, neon magic, glowing costumes, floating islands, multiple suns or moons, western dragon anatomy, wings, fire-breathing dragon, random extra weapons, duplicated people, warped hands, changing faces or changing costumes.
```

统一负面提示词：

```text
static image, slideshow, portrait sheet, studio backdrop, illustration, anime, game UI, captions, letters, logo, watermark, celebrity, modern clothing, plastic skin, beauty filter, neon fantasy, weightless fabric, floating islands, extra sun, extra moon, western dragon, wings, fire breath, duplicated limbs, changing identity, changing costume, deformed hands
```

## 5. 28 段镜头总表

| ID | 文件名 | 使用节点 | 内容 | 主要参考图 |
|---|---|---|---|---|
| RMV-001 | `rmv_001_gate_dawn.mp4` | 开场 | 第三日清晨，秘苑雾门与七派营地全景 | 秘苑入口环境锚点 |
| RMV-002 | `rmv_002_mist_awakens.mp4` | 开场 | 赤雾潮汐般苏醒，弟子整队 | 入口环境、群众服装锚点 |
| RMV-003 | `rmv_003_shen_intro.mp4` | 男线入口 | 沈砚检查退路和行囊 | 沈砚、营地环境 |
| RMV-004 | `rmv_004_chu_intro.mp4` | 女线入口 | 楚明绮分配护符、隐藏修为 | 楚明绮、蔽月宫弟子 |
| RMV-005 | `rmv_005_shen_takeoff.mp4` | 男线飞行 | 沈砚低空御器起飞 | 沈砚、狭天隘 |
| RMV-006 | `rmv_006_chu_flight.mp4` | 女线飞行 | 楚明绮带队进入峡谷 | 楚明绮、狭天隘 |
| RMV-007 | `rmv_007_gorge_hazard.mp4` | 飞行失败/危险 | 侦测光与伏击擦过低空航线 | 狭天隘 |
| RMV-008 | `rmv_008_valley_arrival.mp4` | 飞行成功 | 雾开，环岳药谷首次显现 | 药谷环境锚点 |
| RMV-009 | `rmv_009_corpse_clue.mp4` | 神识侦察 | 溪边尸体、剑向异常、焦黑蛛丝 | 湿林环境 |
| RMV-010 | `rmv_010_rescue_careful.mp4` | 谨慎救援 | 护符屏障下抬出伤员 | 主角、伤员、石墙 |
| RMV-011 | `rmv_011_rescue_rush.mp4` | 冒险救援 | 主角冲入合拢赤雾救人 | 主角、赤雾环境 |
| RMV-012 | `rmv_012_shijun_reveal.mp4` | 石峻出场 | 石峻从铜门阴影现身 | 石峻、铜门 |
| RMV-013 | `rmv_013_shijun_standoff.mp4` | 石峻交涉 | 三人同场的紧张对峙 | 沈砚、楚明绮、石峻 |
| RMV-014 | `rmv_014_fog_clears.mp4` | 禁制前 | 月阳宝珠清出短暂光路 | 药谷、宝珠 |
| RMV-015 | `rmv_015_bronze_gate.mp4` | 破阵成功 | 三门依次点亮，第四门保持沉寂 | 铜门禁制 |
| RMV-016 | `rmv_016_descent.mp4` | 地下入口 | 玉栏台阶向热雾深处延伸 | 地下通道 |
| RMV-017 | `rmv_017_swamp_reveal.mp4` | 蛟窟建立 | 黑泥沼泽、白玉亭与悬浮金箱全景 | 蛟窟环境 |
| RMV-018 | `rmv_018_first_meeting.mp4` | 地下相遇 | 沈砚与楚明绮隔着玉栏首次同框 | 沈砚、楚明绮、蛟窟 |
| RMV-019 | `rmv_019_mud_warning.mp4` | 战前侦察 | 泥泡逆向移动，蛟尾暗影接近 | 蛟窟、墨蛟 |
| RMV-020 | `rmv_020_dragon_breach.mp4` | 战斗一开场 | 墨蛟破泥而出 | 墨蛟、蛟窟 |
| RMV-021 | `rmv_021_tail_block.mp4` | 战斗一 | 蛟尾封住狭道、黑泥扑向伤员 | 墨蛟、伤员、蛟窟 |
| RMV-022 | `rmv_022_shen_blades.mp4` | 沈砚战术 | 金蜉连环刃限制蛟尾 | 沈砚、墨蛟 |
| RMV-023 | `rmv_023_chu_fire_ring.mp4` | 楚明绮底牌 | 赤鸾焰环短暂显露并蒸开泥浪 | 楚明绮、墨蛟 |
| RMV-024 | `rmv_024_joint_strike.mp4` | 合击 | 符阵、蒸汽遮挡和火环形成合击 | 两主角、墨蛟 |
| RMV-025 | `rmv_025_pavilion_collapse.mp4` | 战斗二收束 | 阵柱断裂、白玉亭倾斜、金箱下沉 | 蛟窟、墨蛟 |
| RMV-026 | `rmv_026_ending_alliance.mp4` | 谨慎同盟 | 双方带伤撤离、保留秘密 | 两主角、出口 |
| RMV-027 | `rmv_027_ending_costly.mp4` | 代价胜利 | 战利品在手但身份/底牌暴露 | 对应主角、队伍 |
| RMV-028 | `rmv_028_ending_retreat.mp4` | 失败后继续 | 放弃金箱，背负伤员逃出合拢雾门 | 对应主角、队伍 |

## 6. 逐镜头英文提示词

以下每条提示词后都应追加第4节的“共用风格块”。

### RMV-001 — 秘苑第三日清晨

```text
An eight-second cinematic establishing shot at the third-day dawn outside the Red Mist Secret Garden. The camera begins above wet pine branches and slowly cranes down to reveal a vast natural mountain basin, ancient gray stone walls and four oxidized bronze gates half swallowed by restrained crimson mist. Several temporary sect camps occupy the foreground with real adult disciples tending low fires, packing medicine bundles and tightening practical travel gear. A single pale sun rises behind cloud cover; flags and robe hems move in the cold valley wind. Audio: distant bronze bell resonance, damp wind, low camp movement, birds suddenly becoming quiet.
```

### RMV-002 — 赤雾苏醒

```text
One continuous ground-level tracking shot through the temporary camps as the crimson mist beyond the stone boundary rises like a slow tidal breath. Dozens of adult cultivator disciples stop their preparations and turn toward the gate; no one poses for the camera. Firelight dims under the thickening moisture, bronze talismans tremble, and a narrow seam briefly opens inside the fog. The camera passes behind layered shoulders and ends with the ancient gate framed between two anxious groups. Audio: fabric, boots on wet soil, restrained murmurs, a deep pressure pulse from the fog, no spoken dialogue.
```

### RMV-003 — 沈砚入口

```text
Use the Shen Yan character reference exactly. At a rain-darkened camp edge, Shen Yan crouches beside a low fire and checks three practical objects: a worn ward talisman, a small medicine bottle and a compact chained bronze blade case. The camera makes a slow three-quarter orbit that keeps other disciples, tents, wet trees and the dangerous gate visible behind him. He first studies a marked retreat path in the mud, then the injured companions, and only then the fog-covered valley. His clothes show real wear and damp weight. Audio: quiet fire, leather straps, glass bottle, distant bell, controlled breathing.
```

### RMV-004 — 楚明绮入口

```text
Use the Chu Mingqi character reference exactly. In the Moon-Veil sect camp, Chu Mingqi walks through a working formation of adult female and male disciples, placing one ward talisman into an injured disciple's hands and adjusting the evacuation order with restrained gestures. The camera tracks beside her at shoulder height, showing healers, damp tents, pale robes stained by travel and the red mist gate in the background. A faint crimson reflection briefly moves inside the ring at her waist, then disappears before anyone notices. Audio: rain dripping from canvas, talisman paper, quiet boots, low wind, no dialogue.
```

### RMV-005 — 沈砚低空起飞

```text
Use the Shen Yan reference and the narrow gorge environment reference. Shen Yan steps onto a compact, weathered flying implement and launches only a few meters above a shallow forest stream. The camera follows close behind and slightly to the side, revealing wet cliff walls, pine roots, scattered disciples moving on foot and old scorch marks from an ambush. His robe and hair react to real wind pressure; he deliberately stays below the mist line instead of climbing into open sky. Audio: rushing air, stream water, wood-and-metal resonance from the flying implement, distant magical impact.
```

### RMV-006 — 楚明绮带队飞行

```text
Use the Chu Mingqi reference and the narrow gorge reference. Chu Mingqi leads a small formation of four adult Moon-Veil disciples through the lower part of a one-line-sky gorge. The camera travels parallel to them, close enough to see tired faces and practical formation spacing while cliffs, crooked pines and broken stone markers race past in depth. She opens a brief translucent safe corridor with a restrained hand seal, never becoming surrounded by neon light. One injured disciple flies lower and the formation adjusts around them. Audio: layered wind, cloth strain, synchronized flying tools, a short crystalline ward tone.
```

### RMV-007 — 峡谷伏击擦过

```text
A low-altitude chase shot inside the narrow mountain gorge. The camera skims above water and wet boulders as a thin hostile detection beam sweeps across the open air overhead. A hidden projectile strikes the cliff, showering realistic stone fragments and pine needles across the flight corridor. Two distant adult cultivators dive behind rock cover while the main route bends sharply under a fallen tree. Keep the geography readable and physically plausible; danger comes from terrain and unseen ambushers, not spectacle. Audio: air rush, a sharp magical crack, stone debris, brief alarm calls without intelligible words.
```

### RMV-008 — 环岳药谷显现

```text
An eight-second reveal of the Ring-Mountain Medicine Valley. The camera emerges from dense gray mist behind a moving traveler and rises just enough to reveal a gigantic circular mountain, wet herb terraces, narrow streams, cliff caves, ancient blue-gray stone halls and a distant hundred-zhang tower silhouette. A single morning sun breaks through for only a moment. Small groups of adult cultivators cross different routes in the middle distance while guardian beasts disturb foliage far below. The environment must feel navigable and inhabited, not like a painted background. Audio: mist wind, water, leaf movement, distant beast call, faint bronze hum.
```

### RMV-009 — 溪边尸体线索

```text
One slow investigative dolly along a shallow forest creek. An adult fallen cultivator lies partly against wet stones, respectfully framed without gore. Their sword points opposite the body's fall direction; a storage pouch remains attached; three fine cuts mark nearby bark and a strand of scorched spider silk clings to mud. Two living adult disciples search the deeper background while the camera settles on ripples moving against the current. Damp forest depth, real insects and drifting mist make the scene active. Audio: creek water, a suddenly absent bird chorus, insects, distant branch crack.
```

### RMV-010 — 谨慎救援

```text
Use the selected protagonist reference. At the outer edge of a stone wall, the protagonist and three adult companions raise overlapping paper wards into a modest translucent shelter, then carefully lift an injured disciple from the red mist. The camera stays in a wide three-quarter view so the rescue team, stone cover, tightening fog and possible ambush lanes remain visible at once. The injured person has weight; carriers struggle on slick ground and communicate through eye contact. A hidden projectile hits the ward and disperses without fireworks. Audio: strained breath, wet boots, paper wards fluttering, a dull impact.
```

### RMV-011 — 冒险冲入赤雾

```text
Use the selected protagonist reference. The protagonist runs from the safety of a mossy stone wall into a rapidly closing bank of crimson mist after hearing three stone knocks. The camera follows at waist height through branches and wet grass; silhouettes of both a wounded adult disciple and an uncertain attacker appear and disappear at the sides, giving a real sense of spatial danger. The protagonist reaches the wounded person as a concealed wire trap snaps across the route. No superhero leap, only urgent grounded movement. Audio: heavy breath, fabric, wet vegetation, stone knocks, wire tension.
```

### RMV-012 — 石峻从铜门现身

```text
Use the Shi Jun character reference exactly and the bronze-gate environment reference. Inside the deep shadow of an ancient bronze doorway, Shi Jun steps into mist-filtered morning light while rotating a blood-marked old bronze formation nail between his fingers. The camera begins wide enough to show broken stone, several retreat paths and two distant watching disciples, then makes a restrained push toward a medium view. His hooked blade remains partly concealed; he watches the protagonist rather than performing for camera. Audio: bronze nail clicking against a ring, slow boots on grit, wind through the gate.
```

### RMV-013 — 三方对峙

```text
Use Shen Yan, Chu Mingqi and Shi Jun as three separate identity references. One continuous lateral camera move frames all three adult characters in the same real environment before the ancient bronze gate. Shi Jun controls the central threshold, Shen Yan stands near a marked retreat lane with one hand away from his weapon, and Chu Mingqi keeps wounded disciples behind stone cover. Their eyes track each other; small changes of stance communicate distrust. Background disciples and drifting mist remain active. No one attacks and no one speaks. Audio: cloth in wind, distant water, bronze mechanism, controlled breathing.
```

### RMV-014 — 月阳宝珠驱雾

```text
A weathered moon-sun pearl embedded above the medicine valley emits a restrained pearl-white pulse. The camera follows the light as it travels across oxidized bronze, wet blue stone, herb leaves and the surface of a narrow stream, clearing only a temporary navigable corridor through heavy gray mist. Adult disciples in the middle distance immediately move into the revealed route while guardian-beast silhouettes withdraw along the cliffs. Keep a single natural sun in the sky; the pearl is an artifact, not a second celestial body. Audio: deep resonant hum, fog moisture, leaves, distant movement.
```

### RMV-015 — 四门禁制开启

```text
One symmetrical but physically grounded shot facing four ancient bronze gates arranged around a circular stone court. Three linked array points activate in sequence: oxidized copper lines warm faintly beneath rainwater, old stone dust lifts, and internal mechanisms turn with heavy resistance. The fourth gate deliberately remains dark, preserving the retreat route. Several adult disciples hold formation flags and visibly react to the pressure. The chosen gate opens only a hand's width at first, releasing hot subterranean vapor into the cold valley air. Audio: stone grinding, bronze strain, low array tone, steam hiss.
```

### RMV-016 — 玉栏地道下行

```text
The camera descends behind a small group of adult cultivators along narrow blue-stone stairs bordered by chipped white-jade rails. Their hand-held moonstones illuminate sweat, wet walls, old tool marks and vapor moving upward from an unseen vast chamber. The surface daylight shrinks behind them while the temperature visibly changes: condensation forms, robes cling, and breathing grows heavier. One distant impact shakes dust from the ceiling near the end. The shot must reveal real surrounding architecture and people at multiple depths. Audio: footsteps, dripping water, hot wind, stone vibration.
```

### RMV-017 — 玄泥蛟窟全景

```text
An immense subterranean reveal from the final stair landing: a chamber more than thirty zhang high and several li across, filled with bubbling black-mud wetlands, ridges of dark soil, hot convection mist and old blue-stone supports. A small white-jade pavilion stands near the center beneath a restrained floating dark-gold chest. Adult cultivator figures cross narrow safe ridges at different distances, establishing scale. The camera slowly cranes forward while mud bubbles create changing reflections. No visible monster yet. Audio: deep underground room tone, mud bubbles, hot wind, remote jade chime.
```

### RMV-018 — 地下首次相遇

```text
Use Shen Yan and Chu Mingqi identity references plus the cavern environment. A wide over-the-shoulder camera begins behind Shen Yan at one jade-railed landing and reveals Chu Mingqi across a broken section of walkway with an injured adult disciple beside her. Neither approaches the floating chest. Shen Yan marks three retreat routes in damp soil; Chu Mingqi notices a shadow moving through his blind side and shifts her stance to warn him without exposing her full power. The camera slides sideways to keep both people, the surrounding swamp and the chest in one spatially coherent frame. Audio: mud, hot wind, fabric, distant submerged movement.
```

### RMV-019 — 泥下预警

```text
At ground level beside a black-mud ridge, bubbles abruptly stop, then restart in a line moving against the natural current. The camera tracks that line beneath the reflective surface while adult cultivators retreat into defensive positions in the background. A long dark scaled silhouette passes under the mud close to the camera, displacing heavy material and making the white-jade rail vibrate. Show only a partial shape, never the full creature. Audio: bubbles stopping, deep subsonic movement, jade rattle, one sharp inhale.
```

### RMV-020 — 墨蛟破泥

```text
Use the black-green eastern flood-dragon reference and the cavern reference. In one continuous wide action shot, a single wet black-green eastern flood dragon erupts from heavy mud between two soil ridges, displacing realistic mass and muddy water. It has a long serpentine body, four compact clawed limbs, horned eastern head and no wings. Adult cultivators scatter along readable escape lanes; the white-jade pavilion and floating chest remain visible behind the creature. The camera absorbs one grounded impact shake and then stabilizes. Audio: mud eruption, stone debris, a deep animal roar, people running, no dialogue.
```

### RMV-021 — 蛟尾封路

```text
Use the same eastern flood dragon and cavern. A continuous side-tracking shot follows the dragon's heavy scaled tail as it sweeps through black mud and slams across the narrow retreat corridor without cutting bodies. The impact breaks a jade rail and sends a realistic sheet of hot mud toward two injured adult disciples. Other cultivators pull them behind a soil ridge while the dragon turns its head toward the floating chest instead of blindly attacking the nearest person. Keep full arena geography visible. Audio: tail through mud, jade fracture, shouted nonverbal exertion, steam.
```

### RMV-022 — 沈砚连环刃

```text
Use Shen Yan and the same eastern flood dragon. Shen Yan stays behind partial cover and releases a compact set of linked bronze blades low across the mud, not as glowing swords in the sky. The camera follows the chained blades as they anchor around broken stone and tighten across the dragon's tail, limiting its movement for only a moment. Shen Yan is pulled forward by the real force and braces with both feet while other adult cultivators move wounded people through the opened lane. Restrained moss-green sparks appear only where metal contacts scale. Audio: chain tension, metal scraping scale, mud drag, strained breath.
```

### RMV-023 — 楚明绮赤鸾焰环

```text
Use Chu Mingqi and the same eastern flood dragon. Chu Mingqi steps between wounded disciples and an incoming wave of black mud, draws the dark-red phoenix ring from her waist, and releases one controlled crescent of deep crimson fire close to the ground. The fire flash-boils part of the mud into dense white steam, revealing her true strength for only seconds while leaving fabric and faces lit by physically correct red reflections. The camera arcs around her to show allies, dragon and escape route together. No fire wings and no full-body aura. Audio: ring chime, low flame surge, violent steam, dragon hiss.
```

### RMV-024 — 双主角合击

```text
Use Shen Yan, Chu Mingqi and the eastern flood dragon references. A single coordinated action shot in the cavern: Shen Yan detonates pre-positioned paper wards behind a soil ridge, driving a controlled burst of steam across the dragon's sightline; Chu Mingqi redirects her restrained crimson ring through that steam while the chained blades pull the dragon away from the injured group. The camera moves with the human team, never orbiting weightlessly, and ends on the dragon striking an already weakened array pillar. Show practical teamwork, readable positions and real recoil. Audio: paper snaps, muffled blast, steam, chain, short fire surge, collapsing stone.
```

### RMV-025 — 白玉亭崩塌

```text
The damaged eastern flood dragon collides with an ancient support pillar beside the white-jade pavilion. In one wide shot, cracks race through the pillar, the pavilion tilts, the floating dark-gold chest loses altitude and the black mud begins swallowing its lower edge. Adult cultivators must choose between the chest and their wounded companions; some reach toward it, others drag people toward the shrinking exit. The dragon withdraws into steam and mud rather than exploding. Audio: deep structural fracture, falling jade, mud suction, retreating animal growl, urgent footsteps.
```

### RMV-026 — 谨慎同盟结局

```text
Use Shen Yan and Chu Mingqi references. At the secret garden exit near late morning, both adult protagonists emerge separately from thinning red mist with injured companions between their groups. Their clothes are muddy, torn and damp; neither displays the recovered objects openly. The camera tracks backward as they exchange one brief, guarded look across several meters, then turn to help their own people while the ancient gate begins closing behind them. Other survivors and camps create a living background. Audio: exhausted breath, stretcher wood, wind, distant closing bronze gate, no triumphant music.
```

### RMV-027 — 代价胜利结局

```text
Use the selected protagonist reference. The protagonist exits the red mist carrying a wrapped core medicine or dark-gold case while several adult sect witnesses stare at the unmistakable residue of a recently exposed hidden technique: scorched crimson ring traces for Chu Mingqi, or linked-blade and forbidden-resource evidence for Shen Yan. An injured companion is missing from the formation, leaving a visible empty place. The camera makes a slow lateral move through watching faces and ends on the protagonist recognizing the political cost. Audio: subdued camp movement, whisper-like crowd texture without intelligible speech, cloth, distant gate.
```

### RMV-028 — 放弃战利品撤退

```text
Use the selected protagonist reference. In a continuous urgent retreat shot, the protagonist and a small group of adult survivors carry an injured companion through the narrowing ancient gate as red mist closes behind them. Far inside, the dark-gold chest sinks out of reach and the eastern flood dragon's silhouette moves through steam, but no one turns back. The camera crosses the threshold with the group, then faces them from outside as the bronze doors shut and cold daylight replaces underground firelight. Failure feels costly but survivable. Audio: heavy steps, strained breath, bronze doors, fading dragon roar, morning wind.
```

## 7. 首批视觉基准段

先生成以下10段，用于5–8分钟影视化基准段：

1. RMV-001
2. RMV-002
3. RMV-003
4. RMV-004
5. RMV-005
6. RMV-006
7. RMV-007
8. RMV-008
9. RMV-012
10. RMV-020

这10段能验证入口规模、群众、双主角一致性、飞行速度、场景纵深、反派表演和妖兽可信度。未通过前不批量生成剩余18段。

## 8. 交付命名与验收

- 原始生成文件保存为 `RMV-###-take-01.mp4`，通过后再复制为表格中的运行时文件名。
- 每段至少保留生成提示词、模型名、seed（若接口提供）、参考图清单和生成日期。
- 人物镜头必须检查：脸、发型、年龄、服装层级、腰间法器、左右手和鞋靴连续性。
- 场景镜头必须检查：单一日月、路线连通、人物尺度、雾与风方向、前后景均有运动。
- 墨蛟必须始终是同一条无翼东方蛟，不能在镜头间变成西方龙。
- 输出视频不嵌入文字、对白或剧情结算。

## 9. API密钥安全

如果改为由本机脚本批量生成，不要在聊天中粘贴 Google API Key。应在本机把密钥设置为环境变量（例如 `GEMINI_API_KEY`），确认已经设置后再运行生成脚本。密钥不写入 Unity 项目、Git、日志或提示词清单。
