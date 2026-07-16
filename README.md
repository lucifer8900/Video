# AI Video Interactive Game

This repository contains design notes for a Steam-targeted AI-generated cinematic interactive video game.

Current archive:

- `docs/superpowers/specs/2026-07-09-ai-video-interactive-game-conversation-archive.md`
- `docs/superpowers/specs/2026-07-09-ai-video-interactive-game-full-session.md`

Current development baseline:

- `docs/codex/2026-07-15-codex-development-handbook.md` (single entry point for AI coding assistants; milestones CX-001..CX-505)
- `docs/superpowers/specs/2026-07-12-ai-video-game-vertical-slice-design.md`
- `docs/superpowers/specs/2026-07-12-ai-video-game-vertical-slice-implementation-plan.md`
- `docs/superpowers/specs/2026-07-12-ai-video-game-resource-checklist.md`

Lingmai Yujin prototype content (development tree retains the `fanren` path for compatibility):

- `content/originalization/README.md`
- `content/originalization/source-to-game-name-map.json` (internal migration only; do not ship)

- `docs/fanren/2026-07-13-fanren-game-world-bible.md`
- `docs/fanren/2026-07-13-fanren-combat-progression-system.md`
- `content/characters/fanren/README.md`
- `content/characters/fanren/character-bible.json`
- `content/characters/fanren/full-cast-roster.md`
- `content/characters/fanren/full-cast-roster.json`
- `content/characters/fanren/character-image-prompts.md`
- `content/characters/fanren/image-generation-manifest.json`
- `content/characters/fanren/images/`
- `content/systems/fanren/combat-taxonomy.json`
- `content/scenes/fanren/README.md`
- `content/scenes/fanren/scene-catalog.json`
- `content/scenes/fanren/scene-image-prompts.md`
- `content/scenes/fanren/scene-image-manifest.json`
- `content/scenes/fanren/images/`

Playable Unity vertical slice:

- `unity/RedMistVerticalSlice/README.md`
- Unity `2022.3.62f3c1`, Windows x64 IL2CPP
- Dual protagonist routes, four minigames, two-stage cinematic combat, three ending classes, save/journal/accessibility support
- Local build output: `unity/RedMistVerticalSlice/Builds/Windows/LingmaiYujin-RedMist.exe` (ignored by Git)

## Ledger runtime safety

- `Ledger:Enabled` is `false` by default, so the public Ledger routes are not mapped and PostgreSQL is not initialized.
- Authenticated player ownership is not implemented yet. Keep Ledger disabled in production until requests are bound to a verified player capability; an operator setting alone is not a production authorization boundary.
- `Ledger:Provider=InMemory` is non-durable and is only for explicit local development and tests. After authenticated ownership is implemented, durable deployments must select PostgreSQL and provide a server-side connection string.
- PostgreSQL Ledger integration tests run only when `REDMIST_TEST_POSTGRES_CONNECTION_STRING` is configured. A build or an in-memory test run is not evidence that PostgreSQL integration passed.
