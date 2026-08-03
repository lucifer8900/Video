# 《赤雾秘苑》实时3D视觉样板

## 目标

把原垂直切片的全屏概念图表现替换为可探索的实时3D空间。视觉样板位于秘苑入口，目标游玩时间为3–5分钟，用于确认材质比例、环境密度、光照、雾效、人物尺度与镜头运动。

视觉基准图：`content/scenes/fanren/visual-targets/crimson-mist-sanctuary-entrance-visual-target-v1.png`

## 已实现的样板要素

- 实时生成的峡谷地形、溪流、岩壁、苔石路径、秘苑门楼与台阶。
- 湿苔岩壁和苔石路采用1K PBR材质；贴图按固定世界尺度平铺，不拉伸到整块地形。
- 动态水面、指数雾、低空赤雾粒子、灵气微粒、程序化天空、软阴影和电影调色。
- 竹林、松林、灌木、岩石与两个远景剧情人物均是3D几何体，不是背景贴图。
- 自动电影镜头与自由观察模式；`WASD`移动、鼠标右键观察、`Q/E`升降、`C`切换镜头、`Esc`返回。
- 原有背景、人物图和视频层新增等比中心裁切，删除强制宽高拉伸。

## 视频数量

最终计划为28段，每段约8秒：

| 类型 | 数量 | 镜头编号 |
|---|---:|---|
| 世界、氛围与探索 | 8 | RMV-001、002、008、009、014–017 |
| 人物、对峙与分支 | 7 | RMV-003、004、010–013、018 |
| 飞行与旅行 | 3 | RMV-005–007 |
| 战斗与动作 | 7 | RMV-019–025 |
| 分支结局 | 3 | RMV-026–028 |

先生成10段视觉基准镜头：RMV-001–008、RMV-012、RMV-020。确认人物和场景连续性后，再生成其余18段。

## 外部材质

- `mossy_rock`，Poly Haven，CC0，1K diffuse/normal/roughness。
- `mossy_cobblestone`，Poly Haven，CC0，1K diffuse/normal/roughness。
- `rock_moss_set_02`，Poly Haven，CC0，1K FBX扫描苔岩组。
- `pine_sapling_small`，Poly Haven，CC0，1K FBX松树模型及枝叶贴图。

素材页：

- https://polyhaven.com/a/mossy_rock
- https://polyhaven.com/a/mossy_cobblestone
- https://polyhaven.com/a/rock_moss_set_02
- https://polyhaven.com/a/pine_sapling_small

## 视觉基准图生成提示词

```text
Use case: historical-scene
Asset type: cinematic visual target and environment concept for a Unity 2022.3 interactive xianxia game
Primary request: Create a highly refined, photorealistic cinematic establishing frame of the entrance to an original Chinese cultivation secret realm called Crimson Mist Sanctuary. The player should feel physically present inside a real place, not looking at a painted backdrop.
Scene/backdrop: a vast ancient mountain ravine at dawn after rain; towering dark basalt cliffs recede through atmospheric perspective; an original ruined bronze-and-stone gateway is embedded in the cliff; wet slate steps and mossy retaining walls lead toward it; a clear stream crosses the foreground over rounded stones; dense bamboo, pine, ferns and medicinal plants occupy multiple depth layers; thin crimson supernatural mist flows close to the ground and curls naturally around rocks; drifting pale spirit motes; distant waterfalls and cloud sea; believable ancient timber structures visible beside the gateway, with two small robed cultivator figures walking in the midground for human scale.
Style/medium: photoreal live-action Chinese fantasy film production still, physically based materials, realistic vegetation, natural weathering, detailed wet stone and aged bronze, cinematic but believable VFX
Composition/framing: 16:9 wide frame, eye-level 35mm lens, strong foreground-middle-background separation, leading path from lower left toward the gateway, no close-up portrait, no flat wall of scenery
Lighting/mood: cool blue-green overcast dawn with soft golden sun shafts breaking through clouds, subtle volumetric light, restrained crimson mist glow, mysterious and inviting rather than horror
Color palette: wet charcoal stone, jade green foliage, muted aged bronze, cool cyan haze, small restrained accents of crimson
Materials/textures: no stretched textures; consistent real-world texel scale; stone cracks, puddle reflections, moss edge growth, damp wood grain, bronze patina, leaf translucency
Constraints: original environment and architecture; no recognizable named characters; landscape orientation; no text, UI, subtitles, logo, frame or watermark; scene must read as buildable 3D environment reference
Avoid: illustration, concept sketch, anime, painterly brushwork, flat 2D backdrop, plastic surfaces, oversaturated red fog, excessive bloom, impossible scale, duplicated plants, malformed people, game UI, watermark
```
