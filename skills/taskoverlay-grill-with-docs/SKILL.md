---
name: taskoverlay-grill-with-docs
description: Plan and stress-test TaskOverlay changes with the grill-with-docs workflow. Use when the user wants to add or redesign TaskOverlay features, CLI/API behavior, AI task submission, proposal confirmation, persistence, backup/recovery, overlay UI, or any change where domain terms and project decisions should be clarified before editing code.
---

# TaskOverlay Grill With Docs

Use this skill to align TaskOverlay changes before implementation. The goal is to ask fewer but sharper questions, ground answers in the codebase, and keep project language stable.

## Workflow

1. Read the local project context first:
   - `README.md`
   - `CONTEXT.md`
   - `docs/adr/*.md` when present
   - the relevant code paths under `src/`, `tests/`, or `scripts/`
2. If a question can be answered from code or docs, answer it yourself and cite the file.
3. If the answer is not discoverable, ask one focused question at a time and include a recommended answer.
4. Challenge vague language against `CONTEXT.md`.
   - If the user says "AI task", prefer **External Proposal** unless they explicitly mean a confirmed **Task**.
   - If the user says "cache", distinguish **Runtime Artifact** from **Task Data**.
   - If the user says "window", distinguish **Overlay** from **Task Center**.
5. Update `CONTEXT.md` only when a stable TaskOverlay domain term is resolved.
6. Offer an ADR only when all three are true:
   - the decision is hard to reverse;
   - future maintainers would wonder why it was done this way;
   - there was a real trade-off between viable options.
7. After alignment, produce a concise implementation plan or proceed with code changes if the user asked for execution.

## Documentation Rules

- `CONTEXT.md` is a glossary, not a PRD, not a changelog, and not a scratchpad.
- Keep definitions to one or two sentences.
- Do not add general programming terms.
- ADRs live in `docs/adr/` and use sequential names like `0001-short-slug.md`.
- Keep ADRs short; one paragraph is enough when that captures the context and decision.

## TaskOverlay Boundaries

Read `references/taskoverlay-boundaries.md` when planning changes across UI, CLI/API, proposals, storage, or backup behavior.
