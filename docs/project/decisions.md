# Decision Log

This file is for lightweight decisions. Promote only durable architectural tradeoffs to `docs/adr/`.

## 2026-06-14 - Use GitHub As Safety Checkpoint

Before large Nimbalyst/project-management changes, commit and push the current repository state to GitHub.

Reason: the user is preparing for system reinstall and wants recovery safety before local experimentation.

## 2026-06-14 - Store Project Management Context In Repo

Use `docs/project/` as the durable project-management source of truth, then mirror it into Nimbalyst.

Reason: Nimbalyst is useful as a visual management surface, but project context must survive outside a single local app.

## 2026-06-14 - Keep Nimbalyst Visual, GitHub Authoritative

Nimbalyst should organize project thinking, roadmap, and workflows. GitHub remains authoritative for source, runtime backups, and history.

Reason: this avoids losing direction when local app state changes and keeps code review/recovery straightforward.

