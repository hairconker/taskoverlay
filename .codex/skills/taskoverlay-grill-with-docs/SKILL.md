---
name: taskoverlay-grill-with-docs
description: Plan and stress-test TaskOverlay changes with the grill-with-docs workflow. Use when adding or redesigning TaskOverlay features, CLI/API behavior, AI task submission, proposal confirmation, persistence, backup/recovery, overlay UI, or planning behavior.
---

# TaskOverlay Grill With Docs

Use this skill to align TaskOverlay changes before implementation. Read local project context first:

- `README.md`
- `CONTEXT.md`
- relevant files under `src/`, `tests/`, or `scripts/`

Keep TaskOverlay language stable:

- AI-created work should default to **External Proposal** unless the user explicitly asks for direct task creation.
- Distinguish **Task Data** from **Runtime Artifact**.
- Distinguish **Overlay** from **Task Center**.
- Preserve **Planning Item Hierarchy** when splitting broad planning items.

## Planning Template Rules

When planning TaskOverlay work or operating the Planning Assistant, prefer a concrete **Task Decomposition Template** before generic "准备/推进" splits. Keep child items under the parent planning item.

### CCD / Camera Effect Check

Use when the goal mentions CCD, camera checking, sample image, effect image, shooting, or delivery to a classmate/customer.

Child steps:

1. Confirm what effect points need to be checked and what file/result should be delivered.
2. Prepare CCD, lens, cable, power, storage, software, and a stable shooting scene.
3. Capture sample images and record lighting, distance, settings, and anomalies.
4. Check focus, noise, color, vignetting, bad pixels, and whether the image answers the test goal.
5. Export the effect image and send the result plus a short conclusion.

### Game Script Automation Prototype

Use when the goal mentions game script automation, human-like control, input automation, or a playable automation prototype.

Child steps:

1. Limit the next work session to one verifiable scenario and define input, exit, and failure conditions.
2. Build the input action sequence prototype: movement, click, key press, and wait.
3. Add human-like timing: random delay, small offset, and action interval.
4. Record logs/screenshots and verify stable reproduction.
5. List the next step: visual recognition, state judgment, and safe stop.

Do not plan anti-cheat bypass, stealth, or rule evasion. Keep the prototype framed as compliant personal automation and testability work.

