# TaskOverlay Decisions

- 性能和稳定性优先于功能堆叠，任何新功能都不能让运行中的悬浮窗更容易崩溃 #[decision][id:dec_20260614_stability_first status:to-do priority:critical created:2026-06-14]
- 任务规划采用本地算法优先、AI 增强的结构；无 AI 时必须能生成可用计划 #[decision][id:dec_20260614_local_first_planning status:to-do priority:high created:2026-06-14]
- AI 或 CLI 添加任务必须先进入提案，除非用户明确授权自动确认 #[decision][id:dec_20260614_proposal_before_task status:to-do priority:high created:2026-06-14]
- 拆分任务必须保留父子层级，子任务显示在被拆分任务下面 #[decision][id:dec_20260614_hierarchical_tasks status:to-do priority:high created:2026-06-14]
- 重装系统前必须将源码、文档、运行数据和关键配置推送到 GitHub #[decision][id:dec_20260614_backup_to_github status:to-do priority:critical created:2026-06-14]
