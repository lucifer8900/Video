# GPT Image production asset manifest

Generated with the built-in `imagegen` workflow for the Red Mist vertical slice. The images are original production assets; no Google AI video generation was used.

## Runtime assets

| Runtime resource | Role | Story/gameplay use |
| --- | --- | --- |
| `Generated/Scenes/scene_immortal_sanctuary_gate_v3` | unified celestial environment plate | title, prologue, camp, outer-gate beats and ending |
| `Generated/Scenes/scene_immortal_herb_garden_v3` | unified celestial medicine-garden plate | herb route, investigation and rescue |
| `Generated/Scenes/scene_sword_seal_vault_v2` | cinematic environment plate | underground meeting, both combat rounds and aftermath |
| `Generated/Scenes/scene_celestial_storm_route_v2` | cinematic environment plate | flying-sword story node and minigame |
| `Generated/Scenes/scene_celestial_formation_hall_v3` | interactive formation hall | formation story node and four-nexus puzzle |
| `Generated/Scenes/firstframe_alliance_shen_gu_v1` | integrated male-route actor plate / video first frame | Shen Yan and Gu Jingyu alliance choice |
| `Generated/Scenes/firstframe_alliance_chu_petitioners_v1` | integrated female-route actor plate / video first frame | Chu Mingqi and outer-sect petitioners alliance choice |
| `Generated/Characters/portrait_shen_yan_v2` | male-route dialogue portrait | Shen Yan route |
| `Generated/Characters/portrait_chu_mingqi_v2` | female-route dialogue portrait | Chu Mingqi route |
| `Generated/Characters/portrait_bronze_warden_v2` | antagonist dialogue portrait | warden encounter |
| `Generated/Characters/explorer_shen_yan_front_v3` | transparent full-body exploration view | Shen Yan front-facing presentation while the 3D motor remains playable |
| `Generated/Characters/explorer_shen_yan_back_v3` | transparent full-body exploration view | Shen Yan rear-facing presentation while walking away from the camera |
| `Generated/Characters/explorer_chu_mingqi_front_v3` | transparent full-body exploration view | Chu Mingqi front-facing companion presentation |
| `Generated/Characters/explorer_chu_mingqi_back_v3` | transparent full-body exploration view | Chu Mingqi rear-facing companion presentation |
| `Generated/Minigame/flying_sword_v2` | transparent gameplay object | controllable flying sword |
| `Generated/Minigame/seal_boulder_v2` | transparent gameplay object | floating obstacle |
| `Generated/Minigame/thorn_arch_v2` | transparent gameplay object | thorn-and-stone obstacle |
| `Generated/Minigame/spirit_crystal_v2` | transparent gameplay object | collectible |

The unmodified generated sources, including chroma-key originals, are retained under `GeneratedSource/GPTImage2`. Runtime PNGs live under `Assets/Resources/Generated`.

The earlier `Assets/Resources/Art` images remain available as archive boards and compatibility fallbacks. Primary story nodes now use the coherent production plates above; route-specific alliance plates place actors inside the environment and deliberately suppress the duplicate portrait card.

## Asset generation prompt history

The current video prompts and world-style lock are maintained in `FMV_VISUAL_BIBLE_AND_PROMPTS.md`. The production-image briefs that produced the active v3 plates are summarized below.

All environment prompts requested cinematic photorealism, an original Chinese xianxia setting, ultra-wide 16:9 composition, realistic film-production materials, strong foreground/midground/background depth, no people, no text, no logo, no watermark, and explicitly avoided anime, flat illustration, modern objects and oversaturation.

1. **Immortal sanctuary gate v3:** monumental black-basalt and aged-bronze celestial gate suspended above a cloud sea; floating sanctuary city, cyan jade qi rails, crimson spirit mist, formation rings, upward water columns and a broad wet ceremonial causeway.
2. **Immortal herb garden v3:** floating concentric medicine terraces, central alchemical pavilion and armillary, levitating irrigation ribbons, glassy jade shelters, rare luminous herbs, distant medicine towers and the same moonlit sanctuary skyline.
3. **Sword-seal vault:** enormous subterranean ritual vault; fractured basalt stairs, circular bronze formation, embedded and chained swords, narrow waterfalls, black pools, roots, mineral veins and a distant sealed doorway under cold fissure light and low braziers.
4. **Celestial storm route:** forward-facing chase view over a cloud ocean; floating black-rock islands, razor peaks, broken sky bridges, waterfalls, thunderheads, pale moon, restrained red storm mist and a clear central flight corridor.
5. **Celestial formation hall v3:** elevated three-quarter view of a physical bronze, jade and black-stone ritual array with four readable cardinal nexus pedestals, a central lotus core, dormant connecting channels, hanging sword seals and cloud-sea depth.
6. **Male alliance first frame:** Shen Yan and Gu Jingyu exchange side-route intelligence on the rain-wet celestial bridge, with stable eyelines, complete hands and a choice-safe composition.
7. **Female alliance first frame:** Chu Mingqi, a Moon-Palace flag bearer and two outer-sect petitioners occupy the same celestial bridge in a story-correct ensemble choice frame.

All character prompts requested one original Chinese character, photorealistic historical-fantasy film costume, natural skin and hair, worn practical textiles, three-quarter vertical framing, no real-actor likeness, no text/logo/watermark, and avoided anime, plastic skin, modern clothing and anatomy errors.

8. **Shen Yan:** observant man around 25; lean build, restrained expression, long black hair in a practical knot, layered dark teal and charcoal travel robes, worn leather belt, medicine pouches and muted bronze fasteners on a wet bamboo-path background.
9. **Chu Mingqi:** determined woman around 24; athletic poise, braided high hair with one jade pin, layered ivory-gray travel robes with muted crimson inner panels, fitted sleeves, dark waist guard and talisman case in the herb courtyard.
10. **Bronze-Mask Warden:** intimidating man around 42; weathered face with narrow cracked bronze half-mask, gray-streaked low-tied hair, black-brown ceremonial combat robes, aged bronze guards and seal cords in the underground vault.

Gameplay objects were requested as single photorealistic objects with complete silhouettes and generous padding on perfectly flat chroma-key backgrounds, with no shadow, floor, reflection, text, logo or watermark.

11. **Flying sword:** exact horizontal side profile pointing right; narrow steel blade, dark jade and aged-bronze hilt, small trailing tassel and restrained engraved rune; flat `#ff00ff` key.
12. **Seal boulder:** levitating jagged stratified boulder, pale lichen, cracked bronze circular seal and a tight cluster of small shards; flat `#ff00ff` key.
13. **Thorn arch:** broken gray ritual arch wrapped in wet black thorn vines with restrained crimson resin; flat `#00ff00` key.
14. **Spirit crystal:** three translucent cyan mineral crystals with bubbles and inclusions held by a weathered bronze ring; flat `#ff00ff` key.

The exploration characters use two-view turnaround sheets so their authored costume silhouettes remain consistent as the camera moves. Both prompts requested a landscape sheet with the strict front view on the left and strict rear view on the right, equal full-body scale, complete head-to-boots silhouette, realistic cinematic game-character photography, flat uniform `#ff00ff` chroma background, and no ground, cast shadow, text, logo or watermark.

15. **Shen Yan exploration turnaround:** the same original male protagonist as the dialogue portrait; lean young Chinese cultivator, observant face, long black hair tied in a practical knot, layered dark-teal and charcoal travel robes, worn leather belt, wrist wraps, medicine pouches and muted bronze fasteners. Source: `GeneratedSource/GPTImage2/explorer_shen_yan_turnaround_chroma_v3.png`; runtime views: `explorer_shen_yan_front_v3` and `explorer_shen_yan_back_v3`.
16. **Chu Mingqi exploration turnaround:** the same original female protagonist as the dialogue portrait; athletic young Chinese cultivator, determined face, braided long hair with a restrained jade ornament, layered ivory-gray and muted burgundy travel robes, fitted sleeves, leather waist guard, bracers and practical boots. Source: `GeneratedSource/GPTImage2/explorer_chu_mingqi_turnaround_chroma_v3.png`; runtime views: `explorer_chu_mingqi_front_v3` and `explorer_chu_mingqi_back_v3`.

Chroma removal used the imagegen skill helper with border auto-key detection, soft matte, thresholds 12/220 and despill. The four runtime object PNGs and four exploration-character views were validated as RGBA with transparent backgrounds. The uncut front/back chroma sources are retained beside both turnaround sheets for reproducibility.
