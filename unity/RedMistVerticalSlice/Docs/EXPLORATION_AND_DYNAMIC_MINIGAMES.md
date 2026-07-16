# Third-person exploration and dynamic minigames

This vertical slice now joins the story UI to a real-time third-person exploration layer. The player can move through the Unity scene, orbit the camera, approach world hotspots and start the related activity from the scene instead of selecting every action from a text box.

## Exploration controls

| Input | Action |
| --- | --- |
| `W A S D` / arrow keys | Move relative to the camera |
| Hold `Left Shift` | Run |
| Hold right mouse button and drag | Orbit the camera |
| Mouse wheel | Zoom the camera |
| `E` | Interact with the highlighted nearby hotspot |
| `Esc` | Leave exploration and return to the story |

Available hotspot types are campfire evidence, companion dialogue, the spirit-herb growth area, spiritual-sense traces, the sealed bronze gate, and the return point. The scene displays a proximity prompt and objective hint; a hotspot only triggers when the player is within its interaction radius.

The moving actor keeps a `CharacterController`, gravity, slope handling, fall recovery, collision-aware camera and bounded walkable area. The protagonist and companion use authored front/back transparent character views over the real 3D actor transforms. This is a high-fidelity 2.5D presentation layer on a playable 3D motor, not a fully rigged skeletal character model.

## Dynamic minigame controls

### Spiritual-sense scan

- Move the reticle with the mouse or `W A S D` / arrow keys.
- Hold the left mouse button or `Space` to lock a drifting signal inside the reticle.
- Stable traces build scan progress. Red dangerous echoes increase exposure and can cause overload, so release the channel or move away before locking them.

### Spirit-herb identification

- Move the observation lens with the mouse or `W A S D` / arrow keys.
- Hold the left mouse button or `Space` while a plant is inside the lens to gather it.
- Real herbs breathe and sway steadily. Unstable false targets twitch more rapidly and cause backlash when gathered.

### Formation alignment

- Aim the live formation beam with the mouse or rotate it with `A` / `D` (left/right arrows also use the horizontal input axis).
- Press the left mouse button or `Space` to lock the currently aimed node.
- Lock the sequence `水纹 → 旧铜 → 药叶`. `青石` is a decoy that causes backlash.

### Flying-sword passage

- Move the sword with `W A S D` / arrow keys, or hold the left mouse button to steer toward the pointer.
- Collect spirit crystals and avoid the physical boulder and thorn-arch obstacle sprites until the timer ends.

All four activities update continuously every frame and respond to the moving art, reticles, beams, collisions or hold timing. The three investigation activities no longer use a static option button as their primary interaction.

## Video and network policy

These changes are implemented locally in Unity with real-time animation and generated raster assets. They make no Gemini, Veo, Vertex AI or other video-generation API request and do not require a network connection while playing. Video generation remains forbidden unless the project owner explicitly authorizes a generation run in the current conversation; see `VIDEO_GENERATION_POLICY.md`.
