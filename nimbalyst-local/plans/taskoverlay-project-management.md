---
planStatus:
  planId: plan-taskoverlay-project-management-20260614
  title: TaskOverlay 项目结构管理
  status: ready-for-development
  planType: initiative
  priority: high
  owner: user
  stakeholders:
    - user
    - codex
  tags:
    - taskoverlay
    - nimbalyst
    - project-management
  created: "2026-06-14"
  updated: "2026-06-14T00:00:00.000Z"
  progress: 35
---

# TaskOverlay 项目结构管理

## 目标

把 TaskOverlay 管成一个可以持续推进的项目，而不是只靠临时聊天记录。Nimbalyst 负责项目结构、决策、计划、风险和验收记录；TaskOverlay 自身负责当天任务执行和悬浮提醒。

## 当前阶段

- V1：稳定悬浮窗、任务中心、配置保存、快捷键和基础 UAT。
- V2：CLI/API 控制任务、提案、确认和批量管理。
- V3：本地算法优先的今日/明日日程规划，AI 只做增强。
- V4：Nimbalyst 项目管理视图，把路线图、模块、风险、决策和 GitHub 备份固定下来。
- V5：AI 增强和 token 节省策略。

## 验收方式

- Nimbalyst 左侧 Types 中 Plans、Decisions、Bugs、Tasks、Ideas 不再是 0。
- 每个类型至少能看到一组 TaskOverlay 项目条目。
- GitHub 中保留 `nimbalyst-local/` 和 `docs/project/`，重装系统后可恢复。
- 后续每次大改前先提交并推送。
