# Roadmap

## Phase 1 - Stable Overlay Core

Status: mostly complete.

- Desktop Overlay with view/edit mode.
- Click-through view mode.
- Hotkey support with configurable gesture.
- Task Center for detailed management.
- JSON persistence and recovery.

## Phase 2 - Local Control Surfaces

Status: mostly complete.

- Local API over localhost with token checks.
- CLI for task, proposal, goal, and planning operations.
- External Proposal confirmation boundary.
- Game hotkey bridge as a best-effort companion process.

## Phase 3 - Planning Assistant V1/V2

Status: in progress.

- Goal Library.
- Local tomorrow planning.
- Task List Mode and Time Block Mode.
- Planning Item Hierarchy.
- Task Decomposition Templates for common workflows.

Next work:

- Show template-derived child steps more clearly in Task Center.
- Add UI controls to confirm selected planning items into proposals.
- Add better cleanup/triage flow for stale demo tasks.

## Phase 4 - Project Management Integration

Status: starting.

- Use `docs/project/` as the source of truth for Nimbalyst project management.
- Mirror roadmap, sprint, module map, and risk log into Nimbalyst.
- Keep GitHub as the recovery and history source.
- Add project-level planning prompts/actions.

## Phase 5 - Automation And AI Refinement

Status: planned.

- AI can read project context and propose work through CLI.
- Local algorithm provides baseline planning to reduce token use.
- AI refines, checks conflicts, and proposes better task decomposition.
- User remains the final confirmer.

