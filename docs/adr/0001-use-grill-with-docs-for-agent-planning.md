# Use grill-with-docs for agent planning

TaskOverlay is now controlled by both humans and AI agents through CLI/API surfaces, so ambiguous terms like "add task", "proposal", "cache", and "management window" can easily produce incorrect implementations. We will use a lightweight `grill-with-docs` workflow for non-trivial changes: inspect code and `CONTEXT.md`, ask one focused question at a time when the answer is not discoverable, keep domain terms in `CONTEXT.md`, and create ADRs only for decisions that are hard to reverse, surprising, and based on real trade-offs.
