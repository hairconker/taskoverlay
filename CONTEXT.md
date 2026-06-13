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

**Planning Assistant**:
The assistant behavior that analyzes goals and existing tasks, then produces a user-confirmed plan. It can suggest tasks and schedule changes, but it does not directly mutate confirmed Tasks without user approval.
_Avoid_: autopilot, AI scheduler

**Goal Library**:
The user's durable collection of long-term goals, priorities, and planning preferences. It guides planning but is not itself a task list.
_Avoid_: backlog, task archive

**Local Planning Algorithm**:
The deterministic planning logic that works without AI. It ranks goals and tasks, proposes a today or tomorrow schedule plan, and provides a baseline that AI can refine instead of replacing.
_Avoid_: AI planner when no model is involved

**User-Facing Planning Review**:
The Chinese-language review artifact shown to the user before implementing or applying a plan. Project context may stay in English, but confirmation material should be presented in Chinese.
_Avoid_: English-only confirmation

**Planning Mode**:
The user-selected output shape for a planning run. Task List Mode produces an ordered list of suggested tasks; Time Block Mode assigns suggested work to specific time ranges.
_Avoid_: fixed schedule when the user has not selected time blocks

**Planning Item Hierarchy**:
The parent-child structure inside a planning result. When the assistant splits a broad task or time block into smaller items, the child items stay grouped under the original parent item.
_Avoid_: flat split tasks when the parent context matters

**Task Decomposition Template**:
A reusable planning pattern for common task types. It turns a broad goal into concrete child planning items with preparation, execution, verification, and delivery steps while preserving Planning Item Hierarchy.
_Avoid_: vague prepare/execute splits when a known task type has a better template

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
- The **Planning Assistant** reads the **Goal Library** and existing **Task Data** to produce suggestions.
- The **Local Planning Algorithm** must work without AI; AI may refine its output but should not be required for basic planning.
- A **User-Facing Planning Review** is shown in Chinese before implementation or plan application.
- Each planning run has a user-selected **Planning Mode**: Task List Mode or Time Block Mode.
- Split planning suggestions preserve **Planning Item Hierarchy** so child items remain under the broad parent task or time block.
- A **Task Decomposition Template** should be used before generic splitting when the task type is recognizable, such as CCD shooting/checking or automation prototype work.
- The **CLI** talks to the **Local API**; it does not modify task files directly.
- The **Overlay** is optimized for quick use; the **Task Center** owns detailed management.
- **Task Data** must remain portable enough to survive reinstall or relocation.
- **Runtime Artifacts** may be committed for backup, but they should not redefine the source of truth.
