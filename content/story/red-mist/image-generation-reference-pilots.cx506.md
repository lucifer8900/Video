# CX-506 真实参考图与内置 imagegen 试制记录

状态：`needs_review`。四张图只用于验证“真实摄影参考 -> 原创世界视觉”的生产路线；没有覆盖 CX-505 的任何 `*_grand_v2.png`，没有调用 Gemini 或 Veo，也没有自动接入正式剧情节点。

人物照片只允许参考姿态、表情、视线、手势与重心，禁止复用真人身份或面部。所有参考图的作者、来源页、下载地址、许可和哈希均记录在 `content/reference-library/catalog.json`。

## 1. 建筑环境：原创宗门药苑

输出：`unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/cx506-architecture-environment-v1.png`

参考：

- `ref-architecture-001-b5834f783d`：木构受力、斗拱深度、屋面重量与石基尺度。
- `ref-architecture-003-0d1eebe4a2`：水院亭阁的人体比例与游线。
- `ref-architecture-004-5f8ba81573`：远景竖向节奏与层叠檐线，不复制双塔。
- `ref-landscape-001-74d99e0571`：云海纵深和冷色山层。

实际完整提示词：

```text
Create a brand-new production-quality cinematic environment still for the original Chinese cultivation-fantasy game world “Lingmai Ember / Red Mist Secret Garden”; do not edit, reproduce, trace, or make a collage of any one reference. Landscape 16:9, photoreal live-action location photography, premium physical set construction, 35 mm lens, deep foreground–midground–background staging.

REFERENCE ROLES ONLY:
1. Use image 1 only for believable historic timber load paths, bracket depth, roof weight, aged wood and masonry scale.
2. Use image 2 only for the human proportions of a waterside pavilion, covered walkway, pond edge and garden circulation.
3. Use image 3 only for distant vertical rhythm and layered eave silhouettes; do not copy the real pagodas.
4. Use image 4 only for cloud-sea depth, atmospheric perspective and cool mountain layers.
Do not reproduce any recognizable real building, landmark, layout, person, sign or identity from the references.

DESIGN: An entirely original monumental sect medicinal sanctuary built across three mountain terraces. A broad ivory-limestone arrival court leads to a warm vermilion timber herb-record hall with physically plausible columns, brackets and celadon roofs. An original octagonal waterside refining pavilion sits beside a clear jade-green medicinal pool. Two non-matching, original slim observation towers rise far apart in the distance rather than forming a copied pair. Add tiered herb beds, narrow irrigation channels, bronze rain chains, weathered stone retaining walls, hanging seed-drying frames with no writing, one arched bridge, waterfalls and a luminous cloud sea. The site must feel inhabited and functional, not an empty concept-art palace: exactly three small adult attendants in practical muted robes work at different depths, correctly scaled and naturally integrated, with no prominent face.

LIGHTING: Grand post-rain morning. Warm directional sunlight strikes roof edges, wet stone and upper mist; cool sky fill preserves detail beneath eaves; water bounce reaches the lower columns. Bright and majestic with real shadow structure and visible deep shade, not globally dark, flat, overexposed or merely contrast-boosted. Natural moisture, stone pores, timber grain, moss edges, cloth weight, contact shadows, reflections and atmospheric haze.

COMPOSITION: Low eye-level wide establishing shot from under a shaded timber gateway, using the dark gateway edge only as a foreground frame. The sunlit sanctuary occupies the middle and right thirds; the pool and path lead into depth; mountains remain visible. Physically buildable scale, coherent perspective, restrained fantasy limited to faint amber pollen motes above the herb beds and a thin controlled vermilion mist seam far below the terraces.

AVOID: generic forest, copied Forbidden City, copied temple, tourist-site signage, readable text, pseudo-writing, flags, logos, modern objects, floating buildings, impossible cantilevers, excessive ornament, giant energy beams, neon, holograms, anime, illustration, painterly concept art, game screenshot, plastic CGI surfaces, miniature look, warped roofs, duplicated people, pasted-on figures, malformed anatomy, excessive bloom, black bars or watermark.
```

检查：建筑、水院、药圃、云海和三名工作人员形成了可信空间；保持 `needs_review`，尚未接入正式场景。

## 2. 风景天气：明暗分区的秘苑山谷

输出：`unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/cx506-landscape-weather-v1.png`

参考：

- `ref-landscape-002-de6242e92d`：砂岩峰林地质与空气透视。
- `ref-landscape-003-a4f0e0224f`：矿物水色、岸线反射和水下细节。
- `ref-weather-003-5e27f0f2d4`：真实暴雨前云层与破云暖光。
- `ref-waters-001-2f77906cbd`：可航行水面尺度和喀斯特节奏。

实际完整提示词：

```text
Generate a brand-new cinematic landscape still for the original Chinese cultivation-fantasy game world “Lingmai Ember / Red Mist Secret Garden.” Do not edit, reproduce, trace, or collage any one source photo. Landscape 16:9, photoreal live-action nature cinematography, physically plausible terrain and weather, 28 mm lens, camera at a traversable human-height overlook.

REFERENCE ROLES ONLY:
1. Image 1 supplies the geological logic, vertical sandstone pillar scale, vegetation ledges and layered atmospheric depth.
2. Image 2 supplies transparent mineral-water color, underwater stone detail and realistic shoreline reflections.
3. Image 3 supplies the structure of a real pre-storm sky: warm broken sunlight above, cool heavy cloud underside and directional shafts.
4. Image 4 supplies calm river perspective, navigable water width and karst rhythm.
Do not reproduce any recognizable real location, village, boat, person or landmark from the references.

DESIGN: Create an entirely original secret-garden valley at the moment a mountain storm divides the light. Tall weathered stone needles form a broad horseshoe around a clear turquoise river-lake. A physically walkable pale-stone path descends from the foreground through medicinal meadows, crosses two low original bridges, then splits: one route climbs toward a sunlit ivory-and-vermilion sanctuary gate cut into the mid-distance cliff, while the other follows the water into a controlled seam of thin red mist. Add several small waterfalls, wind-bent pines, wet lichen, white lotus patches, ruined but structurally credible boundary stones with no writing, and one distant suspended rope-and-timber service walkway anchored to rock—not floating architecture.

WEATHER AND LIGHT: The right half receives majestic warm late-afternoon sun through broken storm cloud, illuminating the gate, water caustics and yellow-green meadow. The left foreground remains under a cool passing rain shadow with visible fine rain, damp stone and detailed dark foliage. A soft rainbow fragment may appear only in waterfall spray. Strong bright-dark storytelling through motivated weather, not a globally dark grade and not a simple contrast adjustment. Preserve information in all shadows. Real volumetric distance, cloud shadow on terrain, rain haze, water ripples and wet reflections.

GAME READABILITY: The two possible exploration routes must be visually legible without UI. Include exactly four tiny adult expedition figures at different path depths for scale, all wearing practical muted travel robes, grounded with contact shadows and no recognizable faces. One person kneels to inspect a plant while the others watch the weather; no weapons raised, no heroic posing.

AESTHETIC: Premium live-action fantasy location plate, natural surface variation, rock strata, plant anatomy, water optics and human scale. Restrained supernatural elements only: a faint amber shimmer above one medicinal meadow and the distant thin red-mist seam.

AVOID: copied Zhangjiajie or Jiuzhaigou composition, copied village or raft, generic jungle, uniformly murky darkness, neon cyan water, oversaturated postcard grade, science-fiction portals, floating islands, impossible waterfalls, giant energy beams, fantasy clutter, readable text, pseudo-glyphs, logos, modern objects, anime, illustration, painterly concept art, plastic CGI, miniature diorama, pasted people, warped anatomy, black bars or watermark.
```

检查：冷雨、暖阳、两条路线和远处雾门可读；实际生成了五名远景队员而非四名，不能直接当作锁定人数的剧情连续帧。

## 3. 原创人物：青衣侦察者环境互动照

输出：`unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/cx506-original-character-v1.png`

参考：

- `ref-people-003-4763a77d34`：只参考自然站姿、重心、肩部和手部张力，不复用身份。
- `ref-people-001-6c50e4da3b`：只参考未磨皮皮肤、自然不对称与警惕目光，不复用年龄或面孔。
- `ref-costumes-textiles-002-dbb9476d1a`：织物重量、刺绣起伏、缝线和分层构造。
- `ref-costumes-textiles-003-2f32f515d0`：裙片、包边、褶裥和丝棉材质差异。
- `ref-architecture-003-0d1eebe4a2`：水院尺度、石岸反光和游线。

实际完整提示词：

```text
Create a brand-new photoreal live-action cinematic character still for the existing GAME-ORIGINAL “celadon-clad scout” in the original Lingmai Ember / Red Mist Secret Garden game world. This is not a portrait of any real person. Do not reproduce, average, trace, or imitate the identity, facial geometry, age, clothing, body or setting of any reference subject. Landscape 16:9, premium historical-fantasy film still, 50 mm real optical lens, full body visible head to boots, environment interaction and depth rather than a studio character sheet.

REFERENCE ROLES ONLY:
1. Image 1 supplies only natural standing weight distribution, absorbed downward gaze, relaxed shoulders and believable hand tension. Do not copy her face, hair, modern clothes, glasses, book or identity.
2. Image 2 supplies only unretouched skin microtexture, lived-in facial asymmetry and a wary sideways gaze. Do not copy her age, face, hat, clothes or identity.
3. Image 3 supplies only historical textile weight, hand embroidery relief, seam logic and layered ceremonial construction.
4. Image 4 supplies only skirt-panel layering, edge binding, pleat behavior and silk-to-cotton material contrast; ignore its bright colors.
5. Image 5 supplies only waterside pavilion scale, stone edge, reflected fill and sheltered garden circulation. Do not copy the real pavilion.
All reference people are pose/expression studies only; identity reuse is forbidden.

APPROVED CHARACTER INVARIANTS: One entirely original twenty-seven-year-old East Asian woman scout; oval but individual face, straight black brows, alert dark eyes, naturally textured medium-light skin with pores and tiny imperfections, low braided ponytail with a few damp flyaway hairs. She wears a practical muted celadon narrow-sleeved travel robe over charcoal inner layers, a single weathered charcoal embossed-leather shoulder guard, dark fitted trousers, stitched cloth-and-leather boots, and exactly one small pale-jade wind-listening talisman securely pinned flat at the upper chest. No other jewelry, no handheld tablet, no sword, no glamour crown. Her appearance must not resemble any known actor or reference person.

ACTION AND SET: She has just stepped from a covered original timber walkway into a wet medicinal garden after rain. Her forward boot is planted on dark damp stone with a firm contact shadow; the rear heel is lifting naturally. Her left fingertips lightly steady against an aged wooden column at hip height, visibly taking weight; her right hand hangs relaxed and empty. She turns her head toward a thin red-mist movement beyond the pond, expression quiet, assessing and slightly concerned—not blank, seductive, heroic or exaggerated. The pinned talisman catches a small warm reflection but does not glow. Behind her: an original low refining pavilion, celadon eaves, white-limestone pond edge, medicinal grasses, water reflections and misty mountain terraces. No other foreground person; at most two tiny unfocused workers far behind for scale.

LIGHTING AND PHYSICAL INTEGRATION: Bright overcast post-rain daylight with a warm sun break striking one side of her face and shoulder, cool pond bounce under the chin and robe, soft eave shadow on the opposite side, natural eye catchlights, wet-stone reflection and correct environmental color spill. Real skin pores, peach fuzz, individual hair, slight under-eye texture, cloth thickness, seam puckering, worn leather grain, damp boot edges and grounded occlusion. Keep natural asymmetry and a documentary moment; no beauty retouch, porcelain skin, fashion pose or frozen mannequin.

COMPOSITION: Full-body three-quarter view on the left-middle third, face large enough to read but boots fully visible; layered walkway, pond and pavilion fill the right side, with clear foreground, midground and background. Cinematic, dignified, realistic, physically buildable.

AVOID: copying any reference identity, real actor likeness, generic AI beauty face, doll skin, wax skin, oversized eyes, perfect bilateral symmetry, pasted-on person, billboard look, floating feet, missing contact shadow, impossible cloth, costume clutter, low-cut clothing, modern makeup, modern objects, extra talismans, object morphing, pseudo-writing, text, logo, watermark, neon magic, anime, illustration, painterly concept art, plastic CGI, malformed fingers, extra limbs, cropped feet or black bars.
```

检查：脸、手、脚、衣褶、木柱接触和环境同光源均通过试制检查；人物仍偏影视选角级整洁，必须由人工审核相似性和美术方向。

## 4. 原创灵兽：无名云蹄兽生态照

输出：`unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/cx506-original-spirit-creature-v1.png`

“无名云蹄兽”只是在本文件中便于指代试制图的描述性暂名，不进入世界正史，也不加入正式名称对照表。

参考：

- `ref-animals-004-051b9e4dcb`：山地有蹄类体重、蹄部负重和毛发结块，不复制羚牛个体。
- `ref-landscape-004-aa24c63490`：高海拔纵深、晨光和冷云层。
- `ref-plants-003-ca8f3ee16c`：只参考环境花朵的象牙、暗红、黄绿色和蜡质微观颜色。
- `ref-light-fog-fire-003-60a5911c43`：逆光薄雾、松树剪影和山层。

实际完整提示词：

```text
Create a brand-new photoreal wildlife-cinema still of one unnamed GAME-ORIGINAL spirit creature for the Lingmai Ember / Red Mist Secret Garden world. This is a restrained biological design study, not a mythical-animal collage and not a copy of a real species or location. Landscape 16:9, premium live-action nature documentary look, 85 mm lens from low human-knee height, exactly one creature, complete body and all four feet visible.

REFERENCE ROLES ONLY:
1. Image 1 supplies only believable mountain-ungulate mass, shoulder-to-hip balance, cloven-hoof stance, fur clumping and sure-footed behavior. Do not copy the real takin’s exact face, horns, coat or individuals.
2. Image 2 supplies only high-altitude valley scale, cold cloud layers, sunrise rim light and dark foreground geology.
3. Image 3 supplies only the natural ivory, muted chartreuse, burgundy-speckle and waxy microtexture palette for tiny environmental plants—not a flower-shaped animal and not a body pattern.
4. Image 4 supplies only backlit mist, pine silhouette and layered mountain haze.
Do not reproduce any recognizable animal individual, real place, building or photographic composition.

CREATURE DESIGN: One coherent new cliff-dwelling adult herbivore, approximately the size of a small donkey, evolved for steep medicinal mountain terraces. It has a compact deep chest, slightly longer hind limbs, a short muscular neck, a narrow calm muzzle, small rounded ears and four correctly jointed legs ending in broad split hooves with rough dark pads. Its dense double coat is smoky charcoal at the legs and belly, weathered celadon-gray over the shoulders, with sparse warm ivory guard hairs along the spine. Exactly two short backward-swept horns grow from anatomically credible bases; the outer keratin is dark and worn, while only the very tips show subtle translucent pale-jade mineral striation in direct sun—no glow source, no antlers, no branches. Eyes are dark brown with wet reflections; nose and lips have realistic texture. Slight natural asymmetry, a small healed scratch on one flank, damp fur around the lower legs. It must read first as a real, living mountain animal and only second as supernatural.

ACTION AND HABITAT: The creature stands diagonally on a wet sloped ledge beside a cluster of small slipper orchids, lowering its head to scent rather than eat one flower. The forward hoof compresses moss and displaces a few water droplets; the rear legs hold weight convincingly. Behind it, original dark metamorphic cliffs descend through pine and cloud layers toward a distant pale sunrise peak. A narrow natural stream crosses the ledge. No handler, saddle, harness, shrine, text or second animal.

LIGHTING: Grand dawn with warm rim light on horn tips and upper guard hairs, cool cloud fill across the face and chest, detailed deep shade under the belly, moist rock reflections and thin mist moving through the middle distance. Bright-dark contrast is driven by real sunrise and cloud geometry; retain detail in dark fur. Natural depth of field keeps the entire creature sharp while the distant valley softens.

REALISM: Correct mammalian skeleton, muscle tension, hoof loading, fur direction, breath moisture, contact shadows, occlusion, wet stone, moss compression and plant anatomy. Subtle restrained fantasy only in the horn-tip mineral material and a few amber pollen specks near the orchids.

AVOID: dragon, qilin, deer antlers, wings, scales, feathers, feline paws, human hands, stitched-together chimera, extra horns, extra legs, duplicated body parts, pet-cute proportions, giant eyes, aggressive roar, glowing neon body, armor, jewelry, floating animal, plastic CGI fur, game render, anime, illustration, painterly concept art, copied takin, copied Everest composition, text, logo, watermark, black bars or cropped hooves.
```

检查：骨骼、四足负重、蹄部接触、湿毛和双角均连贯；奇幻特征克制。保持无名、非正史和 `needs_review`。
