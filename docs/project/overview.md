# Project Overview

TaskOverlay is a Windows desktop task overlay for lightweight task capture, local planning, and controlled external task submission.

## Current Product Shape

- Overlay: quick desktop surface for viewing and adding tasks.
- Task Center: full task, goal, proposal, planning, settings, backup, and calendar surface.
- Local API: localhost-only integration surface with token checks.
- CLI: script/agent/human control surface over the Local API.
- Goal Library: durable long-term goals used by local planning.
- Planning Assistant: local algorithm first; AI can refine suggestions but should not be required.

## Current Priorities

1. Keep runtime stable and avoid crashes during normal use.
2. Preserve task data and runtime artifacts before reinstall or major refactors.
3. Make UI behavior obvious: visual settings auto-save; risky or validated settings use explicit save.
4. Improve planning quality with reusable task decomposition templates.
5. Keep CLI/API flexible enough for AI-controlled workflows while preserving user confirmation.

## Operating Rules

- AI-generated task changes default to External Proposals.
- Direct Task creation is allowed only when the user explicitly asks for it.
- JSON remains the default storage source of truth.
- MySQL is optional and must not block startup.
- Game-related input automation must stay framed as compliant testability/prototype work, not anti-cheat bypass.

