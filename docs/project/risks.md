# Risks

## Runtime Stability

Risk: UI or background services crash during normal use.

Mitigation:

- Keep JSON as default storage.
- Catch background reminder/API failures without interrupting foreground use.
- Run smoke tests before major pushes.

## Data Loss During Reinstall

Risk: task data, settings, proposals, goals, or runtime artifacts are lost.

Mitigation:

- Commit tracked source and selected runtime artifacts.
- Keep Release `data/*.json` backed up while reinstall risk exists.
- Use Task Center export for portable JSON backups.

## Hotkey Behavior In Games

Risk: elevated games or anti-cheat environments block normal hotkey behavior.

Mitigation:

- Prefer running the main app as administrator when needed.
- Use Hotkey Bridge only when the main app cannot be elevated or should not directly listen.
- Do not run main app and bridge with the same hotkey at the same time.
- Do not attempt anti-cheat bypass.

## Planning Quality

Risk: generated planning items are too vague or duplicate existing goals.

Mitigation:

- Use Task Decomposition Templates before generic splits.
- Preserve Planning Item Hierarchy.
- Deduplicate Goal Library items against explicit goal summary input.

## UI Clipping

Risk: Task Center panels or action buttons become partially hidden at smaller sizes.

Mitigation:

- Prefer fixed action areas plus scrollable content.
- Add horizontal scroll as fallback for dense tables.
- Keep right-side editor width adjustable.

