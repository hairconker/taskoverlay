# TaskOverlay Project Management

This folder is the durable project-management layer for TaskOverlay. Use it as the source context for Nimbalyst, Codex, GitHub, and future planning sessions.

## Files

- `overview.md`: project purpose, current state, and operating rules.
- `roadmap.md`: phased development plan.
- `modules.md`: module map and ownership boundaries.
- `active-sprint.md`: current short-term focus.
- `risks.md`: known risks, mitigations, and test focus.
- `decisions.md`: lightweight decision log before an ADR is needed.
- `nimbalyst-map.md`: suggested Nimbalyst visual project map.
- `nimbalyst-language.md`: Nimbalyst language-setting notes and fallback.

## Routine

1. Before a large change, commit and push the current state.
2. Update `active-sprint.md` with the current work target.
3. Use `modules.md` to decide where the change belongs.
4. Put uncertain tradeoffs in `decisions.md`; promote only durable architecture choices to `docs/adr/`.
5. After implementation, run the available tests and update this folder if project direction changed.

