# CLAUDE.md — Unity / C# Conventions

**Always respond in Korean (한국어로 답변할 것).**

Before starting any task, ask up to 3 clarifying questions if anything is unclear.

## Architecture
- Shared systems (state, sound, UI) go in Manager classes.
- Singletons for Managers only (`GameManager.Instance`). Never for Player, Monster, etc.
- One script, one responsibility. Split `Player` into `Movement`, `Health`, `Inventory`.
- Any hierarchy object that needs managing is parented under its Manager. No loose managed objects at the scene root.

## Communication
- Decouple with C# `event` / `Action`. Player raises "damaged"; UI subscribes and updates itself.

## References
- Prefer `GetComponent` over inspector assignment.
- Never use `GameObject.Find` or any hierarchy search — runtime cost.
- Never compare by string (tags, names) — slow and error-prone.

## Assets
- Dynamic/repeated objects must be prefabs. Edit originals in Prefab Mode, never in the scene.
- Never leave prefab instances in the hierarchy. Spawn them from the prefab at runtime.
- Prefixes: `P_` prefab, `T_` texture, `M_` material.
- Store prefabs under `@Resources/`, foldered by type, so they are easy to swap.

## Performance
- No allocations in `Update` or other per-frame code.

## Planning
`Plan.md` holds the spec; this file holds the rules. Read both, then summarize the core loop in 3 lines — no code until asked.
