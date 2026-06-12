# TaskOverlay Context

TaskOverlay is a Windows desktop task overlay. It keeps daily task capture lightweight while preserving deliberate confirmation boundaries for tasks created by CLI, AI, or other local programs.

## Language

**Overlay**:
The small always-available desktop window for quick viewing and capture. It is not the full management surface.
_Avoid_: main window, popup, widget

**View Mode**:
The overlay state optimized for passive visibility. It stays on top and can allow mouse click-through.
_Avoid_: read-only mode

**Edit Mode**:
The overlay state for direct user interaction, including quick task entry and opening the Task Center.
_Avoid_: manage mode

**Task Center**:
The full management window for editing tasks, calendar planning, settings, data import/export, and external proposals.
_Avoid_: management UI, admin panel

**Task**:
A confirmed item in the user's task list. A task may have due time, reminder, priority, tags, recurrence, and completion state.
_Avoid_: todo item, proposal

**Daily Task**:
A task that recurs each day and tracks completion per date.
_Avoid_: repeating task when the recurrence is specifically daily

**External Proposal**:
A pending task suggestion submitted by CLI, AI, or another local program. It is not a Task until the user confirms it.
_Avoid_: AI task, external task, draft task

**Local API**:
The localhost-only HTTP API exposed by the desktop app. It requires the configured API token except for health checks.
_Avoid_: public API, cloud API

**CLI**:
The command-line control surface for humans, scripts, and agents. It can inspect, propose, confirm, update, complete, reopen, and delete tasks through the Local API.
_Avoid_: terminal UI

**Storage Backend**:
The persistence implementation selected by settings. JSON is the default backend; MySQL is optional and falls back to JSON on connection failure.
_Avoid_: database when referring to both JSON and MySQL

**Task Data**:
The portable JSON state containing tasks, daily completions, and task-related settings.
_Avoid_: cache when the data is required for recovery

**Runtime Artifact**:
A generated screenshot, log, build output, or copied runtime file kept for recovery or inspection. Runtime artifacts are not source code.
_Avoid_: source file

## Relationships

- An **External Proposal** can become a **Task** only after confirmation.
- The **CLI** talks to the **Local API**; it does not modify task files directly.
- The **Overlay** is optimized for quick use; the **Task Center** owns detailed management.
- **Task Data** must remain portable enough to survive reinstall or relocation.
- **Runtime Artifacts** may be committed for backup, but they should not redefine the source of truth.
