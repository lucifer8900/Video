# Optional approved video overrides

The vertical slice always remains playable with Unity animated-still fallbacks.

To replace a cue with an approved H.264 video, place `<cue-id>.mp4` in this folder. The runtime `MediaDirector` loads local files only; it does not contain API keys and does not let generated video decide game state.

`rmv_001_gate_dawn.mp4` is the approved Veo baseline for the prologue. The game uses it first when present and retains `prologue.mp4` as the fallback.

Recommended delivery: 1920x1080, H.264 High Profile, AAC 48 kHz, 4–8 seconds per combat cue or 8–20 seconds per story cue.

## Approved environment FMV set (2026-07-15)

These four files were copied byte-for-byte from `C:\Users\lucifer\Downloads` and renamed to stable runtime-facing IDs. They are environment-only cinematic plates: **none contains an actor, speaking character, rider, or player avatar**. Use them for establishing shots, transitions, animated backgrounds, and pre-interaction context. Actor-led branching drama requires separate continuity-matched performance clips.

All four share the same delivery specification: MP4 container; H.264 High Profile Level 3.1, progressive, 1280×720, 24 FPS, 240 video frames, 10.000 seconds; AAC-LC stereo, 48 kHz, approximately 128 kbps, 10.005 seconds. No transcoding was applied.

| Runtime filename | Original download filename | Story/game mapping | SHA-256 |
| --- | --- | --- | --- |
| `fmv_gate_arrival.mp4` | `Sanctuary entrance reveal.mp4` | Sanctuary entrance establishing shot; prologue-to-camp / first arrival transition | `757915A75443E5D490415392F952C77657498AE2B4C7D3EB6FF93FF8ABC4A7D5` |
| `fmv_celestial_flight.mp4` | `scene_celestial_storm_route_v2.mp4` | `flight` story node and flying-sword minigame introduction | `75EFB1C2DC7E79877A485D5EC7111DE3008A252F737D3DD1B27B9A6231E2DE21` |
| `fmv_herb_courtyard.mp4` | `scene_spirit_herb_courtyard_v2.mp4` | `herb_route` / `corpse_signs`; herb investigation before active gathering | `75D156438FA91F4A35F02AC0370253079F87484E2E73B0454CC1B7CE58F02553` |
| `fmv_sword_vault.mp4` | `scene_sword_seal_vault_v2.mp4` | `underground` success/bypass entrance after the seal; a failed formation skips this shot and branches into emergency combat | `E8309C400513EA613B42752315A7DA7F1819C3A546101AB408CDE5598FFDBAD2` |
