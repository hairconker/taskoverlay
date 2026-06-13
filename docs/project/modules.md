# Module Map

## Core

Path: `src/TaskOverlay.Core`

Owns domain models and app services:

- Task models, recurrence, tags, priorities.
- Goal models and Goal Library behavior.
- Planning models and Local Planning Algorithm.
- Service interfaces used by App, CLI, and Infrastructure.

## App

Path: `src/TaskOverlay.App`

Owns Windows desktop UI and runtime composition:

- Overlay window.
- Task Center.
- Settings UI.
- Local API host.
- Tray behavior.
- Startup and storage switching.

## CLI

Path: `src/TaskOverlay.Cli`

Owns command-line interaction:

- Human/script/agent commands.
- JSON/table/compact output.
- Date parsing and batch operations.
- Local API connection discovery.

## Infrastructure

Path: `src/TaskOverlay.Infrastructure`

Owns system and storage integrations:

- JSON task repository.
- JSON goal repository.
- Optional MySQL repository.
- Windows hotkey, click-through, startup, and topmost integration.

## Hotkey Bridge

Path: `src/TaskOverlay.HotkeyBridge`

Owns the companion listener for game/elevated-window scenarios:

- Reads TaskOverlay settings.
- Listens for configured hotkey.
- Calls local overlay API.
- Does not inject into games or bypass anti-cheat.

## Tests

Path: `tests/TaskOverlay.SmokeTests`, `tests/cli-e2e.ps1`

Owns broad regression checks:

- Repository and settings recovery.
- CLI/API behavior.
- Goal linking.
- Planning output.
- Single-instance lock.

## Project Management

Path: `docs/project`

Owns human/AI project coordination:

- Roadmap.
- Active sprint.
- Risk list.
- Nimbalyst visual map.
- Lightweight decision log.

