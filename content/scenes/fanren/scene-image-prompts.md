# 《灵脉余烬》P0 场景包图像提示词

版本：`0.1-draft`  
服务项目：`https://github.com/lucifer8900/Video.git`  
执行方式：使用内置 `image_gen`，每个资产单独调用一次。首版 18/18 个资产已经生成，文件身份和首检偏差见 `scene-image-manifest.json`；提示词不等于正史，生成图也不能覆盖 `scene-catalog.json` 中的 `CANON`。

## 使用规则

- 每个场景必须生成三个独立资产，不能用一张“漂亮风景图”代替场景包。
- `A` 是建立镜头，锁定地标、尺度、通行路线和前中后景；可作为 Veo 环境镜头或首尾参考帧。
- `B` 是昼夜／天气变化板，必须保持与 A 相同地点、地形、建筑、机位和尺度，只改变时间、天象、天气、灯火及湿润度。
- `C` 是模块化资产与人群活动板，供 Unity 拆分建筑、材质、植被、道具和人群循环；不是独立新场景。
- 正文未锁定的内容都是 `ART-INFERENCE`。生成后需人工对照 `scene-catalog.json`，不允许模型自行增加宫殿、瀑布、巨兽、角色或天体。
- 统一真人影视方向：可信东方古典世界、克制超自然、真实旧材质、自然光和电影摄影；不对应某个真实朝代。
- 全部资产统一禁止：任何文字、汉字、字母、数字、图标、标签、边框标题、水印、签名、商标、现代物件、现代建筑、现代车辆、电线、塑料、霓虹、科幻设备、电子游戏 UI、明星脸、过曝法术光、漂浮文字。

---

## FR-SC-004 回春坞·封魂药庐

### FR-SC-004-A 建立镜头板

```text
Use case: stylized-concept
Asset type: AI 真人影视互动游戏的 16:9 环境建立镜头；Unity 场景构图与 Veo 首尾帧参考
Primary request: 回春坞全景建立镜头，严格依据《灵脉余烬》第一卷第五至第三十章的地点事实
Scene/backdrop: 一条林间小路绕过树丛后突然打开为唯一入口的翠绿小山谷；画面左侧是一大片分畦药田，种着形态多样但不过度发光的药草；右侧十余间大小不同、彼此相连的旧木石房屋，其中一间略大的医堂；后方花岗岩山壁中有一扇厚重青石门，暗示掏山而成的练功静室；远端角落有一间较新、粗糙、石灰略白的封闭石屋，但不要暴露室内阵法
Subject: 地点本身是主角；前景只安排一名背药篓的少年弟子经过药田，不展示正脸，不增加其他主要角色
Style/medium: photorealistic live-action cinematic environment, grounded Chinese xianxia, believable practical set, natural imperfections, not a fantasy theme park
Composition/framing: wide eye-level establishing shot from the入口林路，清楚表现左药田、右屋舍、后山壁三段空间与可行走动线；前景树叶形成自然框景，中景药园和房屋，远景封闭山壁
Lighting/mood: humid early morning, soft sun just entering the valley, light ground mist, quiet but subtly watched
Color palette: medicinal green, granite gray, aged dark timber, earth brown, restrained amber window light
Materials/textures: wet soil, uneven stone edging, weathered timber, coarse roof tiles, worn linen, granite, medicinal leaves with real botanical texture
Constraints: preserve the single-entry enclosed-valley geography; exactly one small background figure; all supernatural signs subtle; leave clean camera paths through the center; no text, no watermark, no modern objects
Avoid: giant palace, floating mountains, glowing neon herbs, manicured imperial garden, excessive lanterns, waterfalls not supported by the scene, modern greenhouse, UI, labels
```

### FR-SC-004-B 昼夜与天气变化板

```text
Use case: stylized-concept
Asset type: game environment lighting and weather continuity board, four equal cinematic panels without borders or labels
Primary request: 同一个回春坞固定机位的四种时间天气状态；地形、药田、房屋数量、山壁青石门和所有物件位置完全一致
Scene/backdrop: use the exact enclosed valley layout from FR-SC-004-A: left medicinal fields, right connected timber-and-stone rooms, rear granite wall and stone door, one rough limewashed stone house in the far corner
Subject: panel 1 cold dawn with dew and low mist; panel 2 humid summer noon with hard leaf shadows; panel 3 autumn drizzle with dark wet roofs and workers covering herb beds; panel 4 clear moonlit night with only one dim oil lamp and tiny dew highlights around the hidden bottle-use area, no visible magic beam
Style/medium: photorealistic live-action cinematic weather reference, grounded xianxia production design
Composition/framing: four equal views from precisely the same wide camera and lens; no captions, no separators containing text; landmarks must align perfectly across panels
Lighting/mood: natural, restrained, readable for video continuity; night panel retains shadow detail
Color palette: consistent medicinal green and granite gray, changing only seasonal moisture, sky light and lamp warmth
Materials/textures: rain-dark wood, wet stone, leaf dew, mud, worn roof tile, thin natural fog
Constraints: change only time, weather, vegetation season and practical lamp state; keep geometry, path, buildings and camera unchanged; no new people except two distant anonymous herb workers in the rain panel; no text, no watermark, no modern objects
Avoid: lightning storm, fantasy aurora, glowing crops, different architecture between panels, oversized moon, day-for-night blue wash, UI
```

### FR-SC-004-C 模块化素材与活动板

```text
Use case: stylized-concept
Asset type: Unity environment modular asset and activity reference board for 回春坞
Primary request: 在一个宽横向无文字设定板上，清晰分组展示回春坞可拆分资产，所有物件来自同一套真实旧材质世界
Scene/backdrop: warm neutral-gray studio-style backdrop with a narrow strip of scene-native wet soil along the bottom; soft neutral lighting, no decorative landscape
Subject: complete modular set containing: one connected-room facade module and roof corner; one granite wall with thick blue-gray stone door; one rough recently limewashed sealed stone-house facade; three medicinal field modules at seedling, mature and harvested states; herb-drying rack; stone mortar and wooden pestle; ceramic medicine jars with blank surfaces; bamboo baskets; oil lamps and candles; blue-green jade pieces and an abstract powder floor-array fragment shown only as a material sample; one anonymous healer, one herb worker and one young disciple in practical looping activities; small bird, insect and leaf-litter ambient elements
Style/medium: photorealistic game production asset board, live-action prop and set reference, grounded historical fantasy
Composition/framing: clean orthographic three-quarter views, consistent scale groups, every object fully visible with generous spacing; no labels or text
Lighting/mood: soft studio lighting that reveals construction, wear and material response
Color palette: old timber brown, granite gray, medicinal green, clay beige, candle amber, restrained jade blue-green
Materials/textures: splintered wood, rough limewash, chiseled granite, handmade pottery, woven bamboo, stained linen, real plant fibers
Constraints: functional ancient-world construction, no decorative imperial luxury, no weapons unrelated to this location; blank jars and blank plaques; no text, no watermark, no modern objects
Avoid: inventory UI, item icons, floating objects, glossy plastic, modern laboratory glassware, neon magic, labels
```

---

## FR-SC-008 雾萝坳·游修初市

### FR-SC-008-A 建立镜头板

```text
Use case: stylized-concept
Asset type: 16:9 game environment establishing frame and cinematic location anchor
Primary request: 游修初市首次进入修仙社会的建立镜头，严格依据第二卷第一百二十六至第一百三十章
Scene/backdrop: viewer has just passed through a narrow cut in dense white mountain fog into a green valley more than one hundred mu, surrounded by mountains on three sides; a broad gray-green brick square fills the middle ground; modest temporary stalls ring the square; farther back stands a cluster of elegant but restrained palace-style timber pavilions; rare flowers and herbs grow around the valley but do not glow; the fog gate remains visibly sealed behind and at one edge
Subject: dozens of low-level cultivators and family youths bargaining quietly, wearing varied but practical layered robes; tiny scale figures, no identifiable protagonist close-up
Style/medium: photorealistic live-action cinematic concept art, grounded Eastern classical cultivation market, real crowd and set textures
Composition/framing: wide elevated three-quarter view that shows fog entrance, square circulation loop, stall ring and central pavilion cluster; clear negative routes for player navigation; no symmetrical imperial courtyard
Lighting/mood: late morning after emerging from mist, soft mountain light, cautious wonder rather than festival exuberance
Color palette: fog white, blue-gray brick, botanical green, aged wood brown, parchment ochre, small restrained jade and copper accents
Materials/textures: hand-laid brick, weathered timber, bamboo stall frames, cloth awnings, paper talismans with no legible writing, herbs, ore, old bronze
Constraints: market is busy but not a carnival; supernatural effects limited to one tiny distant communication talisman ember and faint entrance-array shimmer; no text, no watermark, no modern objects
Avoid: enormous immortal palace, glowing floating shops, red wedding decor, neon spell signs, modern bazaar tents, readable signs, UI
```

### FR-SC-008-B 昼夜与天气变化板

```text
Use case: stylized-concept
Asset type: four-panel time and weather continuity board for 雾萝坳
Primary request: 同一雾萝坳集市固定高位机位的四态变化；迷雾入口、青砖广场、摊位环和中央楼阁完全一致
Scene/backdrop: the exact valley geometry from FR-SC-008-A
Subject: panel 1 dawn setup with heavy entrance fog and vendors unpacking; panel 2 busy clear midday; panel 3 light mountain rain with awnings lowered, wet blue-gray bricks and fewer visitors; panel 4 calm night market with sparse warm oil lamps, moonlight on fog and most stalls closed
Style/medium: photorealistic live-action environment continuity board, grounded xianxia
Composition/framing: four equal views, same camera, focal length and landmark alignment; no captions and no text separators
Lighting/mood: progression from anticipation to commerce to retreat to guarded quiet
Color palette: consistent fog white, brick gray-green and aged timber; practical amber lamps only at night
Materials/textures: wet brick, canvas and bamboo awnings, leaf moisture, thin fog, worn cloth
Constraints: change only crowd density, time, rain, lamp state and stall-open state; no new buildings; the moon is normal human-realm single moon; no text, no watermark, no modern objects
Avoid: fireworks, festival lantern sea, giant moon, snowfall, glowing fog, different stall layout, UI
```

### FR-SC-008-C 模块化素材与活动板

```text
Use case: stylized-concept
Asset type: Unity modular environment, prop, vegetation and crowd-activity reference board for 游修初市
Primary request: 雾萝坳集市场景的完整模块板，清晰拆出建筑、摊位、交易物和人群循环
Scene/backdrop: neutral warm-gray reference backdrop, faint blue-gray brick floor strip, no full landscape
Subject: pavilion bay module; carved but restrained railing and stair module; three bamboo-and-cloth stall types; blue-gray brick paving and mountain-stone edge samples; fog-gate standing stone and array shimmer sample without symbols; blank paper talismans, blank jade slips, spirit stones, ore, medicinal herbs, plain bottles, low-grade blades and storage pouches; rare-flower and fern clumps; anonymous vendor displaying goods, buyer inspecting, two youths exchanging items, steward watching, porter packing a stall
Style/medium: photorealistic live-action prop and set-design board, realistic game production reference
Composition/framing: wide clean board, orthographic and three-quarter object views, full silhouettes and consistent scale, no labels
Lighting/mood: soft neutral studio light, enough contrast to read cloth, stone, metal and plants
Color palette: parchment ochre, bamboo tan, aged wood, blue-gray brick, jade green, oxidized bronze
Materials/textures: handmade paper, silk worn at edges, rough ore, unpolished stone, woven basket, natural dyes
Constraints: objects look valuable through craft and rarity, not glow; blank signs and blank papers; no text, watermark, modern objects or UI
Avoid: icon grid, loot rarity colors, plastic gemstones, gold-plated luxury, neon magic, readable calligraphy
```

---

## FR-SC-012 赤雾秘苑·环岳药谷

### FR-SC-012-A 建立镜头板

```text
Use case: stylized-concept
Asset type: 16:9 game environment establishing frame, vertical-slice exploration and Veo reference
Primary request: 赤雾秘苑中心区第二层环形山和药谷建立镜头，依据第三卷第一百九十四至第二百零三章
Scene/backdrop: viewpoint from the outer garden layer toward a vast circular mountain ring; a recently opened corridor cuts through formerly impenetrable white-gray fog after the Moon-Yang Pearl dispersed it; inside are steep cliffs, cave mouths, hidden herb valleys, old stone houses and small stone halls embedded irregularly in rock; foreground has rare decorative plants, middle ground reveals a reachable medicinal valley, and far beyond the ring a hundred-zhang ancient tower rises from dense forest but remains inaccessible behind a subtle distortion barrier
Subject: a very small mixed group of cautious low-level cultivators entering the fog corridor while other isolated figures choose separate paths; no close hero portrait; one distant guardian beast silhouette near a herb site, not attacking
Style/medium: photorealistic live-action cinematic environment, grounded dangerous cultivation realm, realistic geology and botany, restrained supernatural effects
Composition/framing: grand wide shot with readable concentric geography: outer garden, fog ring mountain, inner forbidden tower; foreground path offers player route choice; avoid impossible floating terrain
Lighting/mood: cold early morning, sharp beams through moving fog, beautiful but lethal, strong depth
Color palette: fog gray, wet rock blue-gray, deep forest green, restrained medicinal purple and amber, old stone
Materials/textures: natural cliff strata, wet moss, old chiseled stone, rare herbs with believable plant structure, thin volumetric fog
Constraints: ring mountain and three-layer geography must be unmistakable; magic limited to pearl-cleared fog path and distant barrier refraction; no text, no watermark, no modern objects
Avoid: theme-park canyon, luminous rainbow plants, floating pagodas, gigantic dragons, full army, neon barrier dome, UI
```

### FR-SC-012-B 昼夜与天气变化板

```text
Use case: stylized-concept
Asset type: four-panel time, fog and weather continuity board for 赤雾秘苑环形山
Primary request: 同一环形山固定建立机位的四种剧情状态；所有山体、洞口、石屋、药谷和远塔完全一致
Scene/backdrop: exact concentric landscape from FR-SC-012-A
Subject: panel 1 predawn with impenetrable dense fog hiding all mountain paths; panel 2 third-day morning as a single pale pearl light opens several corridors through fog; panel 3 clear humid noon after dispersal, exposing guardian-beast tracks and wet herb valleys; panel 4 dangerous moonlit night as fog slowly returns, most paths lost and the distant tower barely visible
Style/medium: photorealistic live-action cinematic continuity board
Composition/framing: four equal aligned views, same camera and lens, no labels or text
Lighting/mood: procedural danger readable at a glance; night remains detailed and physically plausible
Color palette: stable rock gray and forest green; pearl light moon-white, no rainbow magic
Materials/textures: rolling fog, dew, wet rock, natural leaf translucency, old stone
Constraints: change only fog coverage, time, moisture and distant activity; human-realm single sun and single moon; no new buildings or creatures; no text, watermark, modern objects
Avoid: thunderstorm not required, giant glowing orb, aurora, changed mountain silhouette, neon fog, UI
```

### FR-SC-012-C 模块化素材与活动板

```text
Use case: stylized-concept
Asset type: Unity biome kit, ancient-ruin kit, medicinal vegetation and encounter-activity reference board
Primary request: 赤雾秘苑环形山和药谷可拆分素材全集，不画新的全景
Scene/backdrop: neutral dark-warm gray backdrop with small natural rock and soil swatches under objects
Subject: four cliff modules with cave entrances and climb-blocking faces; narrow hidden-valley floor; old stone-house and stone-hall facade modules; weathered copper gate fragment; fog-volume edge sample; medicinal plants including jade-like fungus, purple monkey-flower analogue and spirit-fruit vine designed as believable rare botany without labels; common rare flowers; guardian-beast den, tracks and shed scale; anonymous cultivator scouting, harvesting, tending a wound and placing a retreat marker; baskets, jade containers, rope, low-grade formation flags and spent talismans with blank surfaces
Style/medium: photorealistic game production asset board, live-action set and prop realism
Composition/framing: wide organized board with clean spacing, orthographic and three-quarter views, all silhouettes complete, no labels
Lighting/mood: neutral inspection light with a second subtle fog-light sample
Color palette: wet stone gray, moss green, medicinal restrained purple, old copper, parchment ochre
Materials/textures: chiseled ancient stone, moss, damp soil, fibrous leaves, oxidized copper, worn cloth
Constraints: every asset must belong to the same ancient garden complex; plants readable by silhouette rather than glow; no text, watermark, modern objects or UI
Avoid: inventory icons, labels, generic fantasy crystals, excessive skulls, neon potion plants, floating assets
```

---

## FR-SC-019 引辰岛大港

### FR-SC-019-A 建立镜头板

```text
Use case: stylized-concept
Asset type: 16:9 game environment establishing frame and AI video harbor reference
Primary request: 引辰岛大港首次抵达镜头，依据第四卷第三百六十七至第三百六十八章
Scene/backdrop: a vast sheltered island harbor capable of holding roughly two to three hundred vessels; six or seven exceptionally large deep-sea merchant ships are visible among many medium island traders and small fishing craft; one great ship has no mast or sail and is pulled by several massive non-magical sea fish through heavy harness lines; stepped stone quays, timber piers, warehouses, loading ramps and a simple stone registration house stand near the water; a crowded road of unusual sheep-ox draft carts leads inland past low white-stone towns toward distant green mountains protected by a nearly invisible island array
Subject: dense but readable working crowd of sailors, porters, merchants, interpreters and a few low-key cultivator guards; no single hero close-up
Style/medium: photorealistic live-action cinematic port, grounded maritime xianxia, practical historical-fantasy construction and real salt weathering
Composition/framing: wide elevated approach from the arriving ship, strong foreground rope and wet rail, midground harbor traffic, background island road and mountains; show scale through vessel tiers and crowd without visual clutter
Lighting/mood: clear windy morning, salt haze, energetic commerce and guarded order
Color palette: deep sea blue, salt gray, white stone, aged timber brown, oxidized copper, muted cloth colors
Materials/textures: wet rope, tarred wood, salt-crusted stone, patched sailcloth on ordinary ships, bronze fittings, woven cargo mats
Constraints: large crowd and ships must remain plausible; giant pulling fish are animals, not dragons and not glowing; supernatural guards subtle; no text, flags with legible symbols, watermark or modern objects
Avoid: tropical resort, pirate cliché, steam engines, European galleons, modern cranes, neon magic, skyscrapers, UI
```

### FR-SC-019-B 昼夜与天气变化板

```text
Use case: stylized-concept
Asset type: four-panel harbor time and weather continuity board
Primary request: 同一引辰岛港固定到港机位的四态；码头层级、仓库、登记石屋、船位和远山轮廓完全一致
Scene/backdrop: exact harbor geometry from FR-SC-019-A
Subject: panel 1 pale dawn with arriving fishing fleet and light salt mist; panel 2 busy clear midday with full cargo activity; panel 3 severe rain and pre-heavenly-wind warning with ships double-moored, awnings tied down and people securing cargo, no catastrophic wave; panel 4 calm moonlit harbor with sparse watch fires, guard patrols and reflections
Style/medium: photorealistic live-action environment continuity board
Composition/framing: four equal aligned frames from identical camera and lens, no labels, no text dividers
Lighting/mood: working realism; storm panel tense, night panel safe but watched
Color palette: consistent sea blue, salt gray, white stone and timber, with weather-dependent saturation only
Materials/textures: wet quay, taut rope, rain sheets, salt haze, moonlit water, oil-lamp glassless fixtures
Constraints: change only weather, time, crowd activity and ship securing state; no new ships or buildings; human-realm single moon; no text, watermark or modern objects
Avoid: tsunami, lightning striking ships, modern floodlights, tropical palms, giant fantasy moon, UI
```

### FR-SC-019-C 模块化素材与活动板

```text
Use case: stylized-concept
Asset type: Unity maritime environment, ship, prop and crowd-loop reference board for 引辰岛港
Primary request: 引辰岛大港的模块化资产全集，突出海运经济和修凡共存
Scene/backdrop: neutral warm-gray background with a narrow wet-stone quay strip, no full seascape
Subject: three quay modules; timber pier, warehouse bay, simple registration stone-house facade and gate booth; large no-mast merchant-ship hull section with fish harness; ordinary island trader boat and fishing skiff; sheep-ox draft cart; cargo crates, woven bales, ceramic jars with blank surfaces, ropes, bollards, ramps, harbor jade pass with no writing, simple array warning bell; massive harness fish shown in side and three-quarter scale reference; anonymous porter lifting, interpreter greeting, sailor tying line, cultivator guard checking a pass, cart driver looping activities
Style/medium: photorealistic live-action prop, creature and set production board
Composition/framing: clean wide board, consistent human scale, full silhouettes, orthographic and three-quarter views, no labels
Lighting/mood: neutral daylight revealing salt wear, joinery and cloth damage
Color palette: salt gray, white stone, dark wet wood, sea blue, copper green, muted linen
Materials/textures: rope fiber, tar, salt crust, hand-hewn stone, hammered copper, coarse cloth, thick fish hide
Constraints: pulling fish are plausible marine animals with no magical glow; blank signs and passes; no text, watermark, modern machinery or UI
Avoid: cargo containers, cranes, engines, pirate skulls, dragons, tropical tourist styling, item-icon grid
```

---

## FR-SC-023 空冥殿·悬玉外庭

### FR-SC-023-A 建立镜头板

```text
Use case: stylized-concept
Asset type: 16:9 cinematic game environment establishing frame and Veo hero-shot reference
Primary request: 空冥殿悬于碎辰海千丈高空的首次显现，依据第四卷第四百三十一至第四百四十六章
Scene/backdrop: empty open sea far below, vast cloud layers, and a monumental palace about one hundred zhang high floating motionless roughly one thousand zhang above the water; the entire structure is made of flawless pale jade with precise ancient joinery, surrounded by one thick restrained golden protective veil; a deep entrance sits above the cloud edge; through the entrance one can glimpse an impossibly tall narrow jade corridor but no readable inscription
Subject: several tiny cultivators ascending through the golden veil, each reduced to scale silhouettes; architecture is the primary subject
Style/medium: photorealistic live-action cinematic ancient-cultivator megastructure, grounded material realism, sublime scale, not science fiction
Composition/framing: very wide low-angle view from above the sea and below the palace, clouds crossing foreground, palace centered slightly off-axis, visible sea horizon for scale; strong negative sky
Lighting/mood: late-afternoon cold sun catching jade edges, golden veil barely luminous, awe mixed with threat
Color palette: jade white, cloud gray-blue, deep sea blue, restrained antique gold, cool interior cyan
Materials/textures: translucent mineral depth without glassy modern surfaces, carved stone joints, weatherless ancient jade, dense volumetric cloud
Constraints: palace remains one coherent ancient complex, no floating islands, no wings, no engines; magic is only the golden veil and subtle jade luminescence; no text, inscription, watermark or modern objects
Avoid: heavenly city sprawl, sci-fi spaceship, white marble European palace, neon gold dome, angels, giant statues, UI
```

### FR-SC-023-B 昼夜与天气变化板

```text
Use case: stylized-concept
Asset type: four-panel sky, cloud and lighting continuity board for 空冥殿
Primary request: 同一空冥殿低角度固定机位的四态；宫殿几何、海平线、入口与金色光罩完全一致
Scene/backdrop: exact floating jade palace above open sea from FR-SC-023-A
Subject: panel 1 cold dawn above a low cloud sea; panel 2 clear high noon showing jade construction and tiny entrants; panel 3 cloud-storm dusk with distant rain curtains and stronger wind below while the palace and veil remain motionless; panel 4 clear single-moon night with stars, faint jade glow and sparse moving cultivator lights
Style/medium: photorealistic live-action cinematic continuity board
Composition/framing: four equal identical views, precise landmark alignment, no captions or text
Lighting/mood: timeless, monumental, physically believable; magic never overexposes jade detail
Color palette: consistent jade white and antique gold, weather changes sky only
Materials/textures: clouds, rain curtains, mineral jade, sea reflection
Constraints: change only time, cloud, distant rain, sea surface and small visitor activity; human-realm one sun and one moon; no structural changes; no text, watermark, modern objects
Avoid: lightning striking palace, aurora, extra moons, changing palace style, rainbow clouds, UI
```

### FR-SC-023-C 模块化素材与活动板

```text
Use case: stylized-concept
Asset type: Unity ancient-megastructure kit, material, interior and crowd-activity reference board for 空冥殿
Primary request: 空冥殿外殿和玉柱大厅的模块化制作板
Scene/backdrop: neutral cool-gray backdrop with a small jade floor strip; no complete palace landscape
Subject: jade wall and corner modules; tall narrow corridor bay scaled to a tiny human; entrance arch with blank lintel; golden-veil edge effect sample with no symbols; huge carved jade column variants featuring original fictional birds and beasts, no text; blue-light doorway; jade stair and platform; seated cultivator, standing watcher, cautious entrant and distant robe silhouettes as scale/activity loops; residual-map cloth shown blank; ancient lamp recess and floor joints
Style/medium: photorealistic live-action production design and game environment asset board, ancient mineral construction
Composition/framing: wide clean board, front, side and three-quarter views, full silhouettes, consistent scale references, no labels
Lighting/mood: soft cool inspection light with one restrained golden rim sample
Color palette: jade white, cool cyan, cloud gray, antique gold, muted robe accents
Materials/textures: deep mineral grain, fine ancient carving, seamless heavy blocks, woven cloth, no glossy plastic
Constraints: every motif original and non-readable; architecture ancient, massive and structurally coherent; no text, watermark, modern objects or UI
Avoid: museum labels, European capitals, spaceship panels, neon runes, glowing floor grid, item icons
```

---

## FR-SC-044 天堑城

### FR-SC-044-A 建立镜头板

```text
Use case: stylized-concept
Asset type: 16:9 game environment establishing frame, Unity world-layout and Veo city reference
Primary request: 七曜灵陆天堑城蛮荒侧巨墙、塔群和人妖分区建立镜头，依据第八卷第一千三百一十五至第一千三百三十三章
Scene/backdrop: a colossal frontier city covering tens of thousands of li, seen from a high approach outside the wilderness side; a sheer stone wall dozens of zhang tall has no gate at all; layered ancient defensive arrays are embedded in stone and metal, subtle not neon; behind it rise many immense military towers, with one central tower broader and taller; a vast pale energy partition divides the human and demon-cultivator halves of the city; beyond are mountain ranges, plains, markets and teleportation halls; the sky unmistakably belongs to the Spirit Realm, showing a canonical daytime state of three bright suns and four faint moon shadows in stable positions
Subject: tiny patrol formations on wall walks, a golden court-boat approaching a teleport platform, black-, blue-green- and gold-armored guards as small scale cues; no close characters
Style/medium: photorealistic live-action cinematic frontier megacity, grounded Chinese xianxia engineering, ancient stone, bronze and restrained formation technology, not science fiction
Composition/framing: very wide aerial three-quarter establishing shot, wilderness foreground, gate-less wall cutting across frame, tower grid and central tower behind, energy partition visible through the city; readable city hierarchy and navigation zones
Lighting/mood: hard clear Spirit-Realm daylight, high-altitude haze, disciplined and formidable, a civilization built for survival
Color palette: pale stone, blue-gray, black and blue-green armor, restrained cold gold, soft white partition light, natural sky blue
Materials/textures: monumental masonry, hammered dark metal, oxidized bronze, weathered banners with no symbols, real road dust, tiled roofs scaled beneath towers
Constraints: no gate on wilderness wall; three suns and four moon shadows exactly, physically consistent light direction led by one dominant sun; city contains ancient streets and buildings, not futuristic towers; no text, watermark, modern objects
Avoid: sci-fi space city, glass skyscrapers, laser walls, neon circuitry, floating traffic, seven equally bright suns, random planets, readable banners, UI
```

### FR-SC-044-B 昼夜与天气变化板

```text
Use case: stylized-concept
Asset type: four-panel canonical celestial, weather and siege-readiness continuity board for 天堑城
Primary request: 同一天堑城固定高空机位的四态；无门巨墙、塔群、中央塔、分区光幕、道路和山脉完全一致
Scene/backdrop: exact city geometry from FR-SC-044-A
Subject: panel 1 dawn transition with one sun emerging and six fading moon forms; panel 2 canonical midday with three strong suns and four faint moon shadows, normal patrol traffic; panel 3 storm front from the wilderness with towers sealing apertures and patrols doubling, celestial bodies obscured by cloud rather than moved; panel 4 full night with seven moons of graduated brightness, sparse tower lamps, active formation seams and no suns
Style/medium: photorealistic live-action cinematic continuity board, physically readable fantasy sky
Composition/framing: four equal frames from identical camera and lens, landmarks perfectly aligned, no captions or labels
Lighting/mood: dawn watchfulness, daylight order, storm alert, moonlit vigilance
Color palette: constant pale stone and armor colors; sky and practical lighting change naturally
Materials/textures: weathered masonry, rain-dark stone, low cloud, moonlit metal, restrained array light
Constraints: celestial count follows canonical transition logic; do not show suns and moons as decorative random orbs; change only celestial state, weather, patrol level and lighting; no damage in this board; no text, watermark, modern objects
Avoid: aurora, rainbow sky, giant planets, seven identical moons, laser searchlights, changed architecture, UI
```

### FR-SC-044-C 模块化素材与活动板

```text
Use case: stylized-concept
Asset type: Unity frontier-city architecture, defense, street, prop and population reference board for 天堑城
Primary request: 天堑城可复用模块全集，清楚区分城市结构、守卫等级、人族与妖族生活层
Scene/backdrop: neutral cool-warm gray backdrop with small pale-stone street and wall-top floor strips; no full city panorama
Subject: gate-less giant wall section with patrol walk; defensive tower base, midsection and roof modules; broader central-tower segment; teleportation hall facade and altar platform; huge pale partition-light edge sample; market street bay and cave-like demon-cultivator district bay built from the same city engineering; black iron armor, blue-green Qingming armor and gold Tianyuan armor displayed on anonymous non-identifiable guards; jade identity token with blank surface, alien-detection disk, teleport crystal socket, formation maintenance tools, supply cart; patrol march, token inspection, array repair, market exchange and demon-beast handler activity loops
Style/medium: photorealistic live-action set, costume and prop production board for a grounded xianxia city
Composition/framing: wide organized board, orthographic and three-quarter views, complete silhouettes, clear human scale, no labels
Lighting/mood: neutral inspection lighting plus one subtle array-active sample
Color palette: pale stone, charcoal metal, blue-green, restrained gold, oxidized bronze, muted market cloth
Materials/textures: chiseled megastone, layered metal plates, worn armor, jade, heavy timber, linen, creature tack
Constraints: ancient engineering, no modern mechanisms; human and demon halves differ in habitation details but share defense standards; blank banners and tokens; no text, watermark, modern objects or UI
Avoid: sci-fi armor, guns, holograms, computer terminals, skyscraper glass, neon runes, inventory icons
```

## 生成后的人工验收

每个资产至少检查以下内容：

1. 与 `scene-catalog.json` 的 `CANON` 一致，未将 `ART-INFERENCE` 冒充正文事实。
2. A、B、C 共享同一地点的地标、材质、尺度与色板；B 不得产生建筑漂移。
3. A 能看出至少一条玩家动线、一个剧情焦点和一个撤退／转场方向。
4. C 中建筑、植被、道具和人群循环能被 Unity 独立拆分，不是纯装饰拼贴。
5. 天堑城天空遵守七日七月状态；尘寰界场景只出现单日单月。
6. 无文字、水印、现代物件、现代车辆、现代妆造、科幻 UI 或未经正文支持的新增人物与奇观。
7. `scene-image-manifest.json` 已写入生成方法、提示词版本、文件哈希和 `sceneStateVersion`；内置工具没有暴露的种子／稳定任务 ID 保持缺省，不虚构元数据。
