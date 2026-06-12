# TaskOverlay Boundaries

Use these project-specific boundaries while grilling plans.

## Core Surfaces

- **Overlay**: quick visible desktop surface. Avoid adding complex management flows here.
- **Task Center**: detailed user management surface. Settings, proposals, calendar, import/export, and full editing belong here.
- **Local API**: localhost-only integration surface. It should enforce token checks except health.
- **CLI**: human/script/agent control surface. It should prefer safe defaults and support explicit destructive flags.

## Confirmation Boundary

AI, CLI, or other external programs should default to **External Proposal** creation. Direct **Task** creation is acceptable only when the user or command explicitly requests it.

When planning a feature, ask:

1. Is this a user-confirmed task, or only a suggested task?
2. Which surface owns the action: Overlay, Task Center, CLI, or API?
3. Is the operation reversible?
4. Does the action need batch support?
5. Should the CLI default be safe or direct?

## Persistence Boundary

- **Task Data** is required for recovery and portability.
- **Runtime Artifacts** help restore state after reinstall but are not source of truth.
- JSON remains the default storage backend.
- MySQL is optional and must not block local app startup.

## ADR Candidates

Consider an ADR for:

- changing the default confirmation boundary;
- changing the storage source of truth;
- exposing the API beyond localhost;
- removing JSON fallback;
- committing or excluding runtime artifacts as a project policy.

Skip ADRs for routine UI polish, small command aliases, or reversible refactors.
