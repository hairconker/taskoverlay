# Active Sprint

## Focus

Make TaskOverlay easier to manage as a project while preserving current runtime stability.

## Current Work Items

- Establish `docs/project/` as the durable project-management context.
- Mirror this structure into Nimbalyst as a visual project map.
- Keep GitHub push as the default safety checkpoint before large changes.
- Continue improving planning templates and UI visibility issues.

## Recently Completed

- Task Center action column layout fixed.
- Visual settings auto-save added.
- Game hotkey bridge documented and usage clarified.
- Planning templates added for CCD camera check and game automation prototype.
- Release data files for goals/proposals/tasks/settings saved to GitHub.

## Next Candidate Tasks

- Add a Task Center view for planning item children.
- Add a stale/demo-task cleanup workflow.
- Add a Nimbalyst import/checklist document for manually building the visual map.
- Run full smoke and CLI E2E tests after closing the currently running TaskOverlay instance.

## Validation Checklist

- `dotnet build TaskOverlay.sln`
- `dotnet run --project tests\TaskOverlay.SmokeTests\TaskOverlay.SmokeTests.csproj`
- `powershell -ExecutionPolicy Bypass -File tests\cli-e2e.ps1`
- Manual UI check: Task Center task list, editor panel, settings, planning page.

