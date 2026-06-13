# Nimbalyst Language Notes

## Current Finding

The repository contains local Nimbalyst-related permission entries in `.claude/settings.local.json`, including:

- `mcp__nimbalyst-settings__ai_set_preferred_language`
- `mcp__nimbalyst-settings__settings_get_overview`
- `mcp__nimbalyst-settings__ai_set_default_model`
- `mcp__nimbalyst-settings__features_toggle`

However, this Codex session does not currently expose callable Nimbalyst settings tools, and the local repository does not contain an obvious Nimbalyst UI language configuration file.

## Practical Steps To Try In Nimbalyst

1. Open Nimbalyst settings.
2. Look for `Language`, `Locale`, `Appearance`, `General`, or `AI preferred language`.
3. If there is no UI language option, set AI output language to Chinese if available.
4. If a Nimbalyst settings MCP becomes available, use `ai_set_preferred_language` with Chinese.

## Fallback

Keep project-management documents in Chinese-facing form inside this repo. Even if the Nimbalyst UI cannot switch language, project content can still be managed in Chinese through:

- `docs/project/`
- `CONTEXT.md`
- `.codex/skills/taskoverlay-grill-with-docs/SKILL.md`
- `nimbalyst-local/ai-actions.md`

## Recommended Default

Use Chinese for user-facing planning, confirmations, sprint notes, and visual board labels. English is acceptable for stable technical context when it improves AI interoperability.

