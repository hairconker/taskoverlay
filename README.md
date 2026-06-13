# TaskOverlay

TaskOverlay 是一个 Windows 桌面悬浮待办工具。默认使用本地 JSON 文件保存任务，启动不依赖数据库；只有需要共享数据时才需要切换到 MySQL。

## 使用流程

1. 启动 `TaskOverlay.App.exe` 后，桌面会显示轻量悬浮窗。
2. 查看态默认置顶并允许鼠标穿透，不影响其他应用操作。
3. 按 `Ctrl+\`` 进入编辑态，可以快速添加今日任务、每日任务，或打开“任务中心”。
4. 在任务中心补充截止时间、提醒、标签和重复计划。
5. 使用“日历规划”查看指定日期安排，使用“数据管理”导出或导入 JSON 备份。
6. 在“设置”中查找或修改编辑模式快捷键、透明度、置顶、开机自启和存储方式。旧版 `~+1` 仍可兼容，但在游戏、管理员窗口或反作弊环境中可能失效，建议使用标准组合键。
7. CLI、AI 或其他本地程序提交的任务会进入“外部提案”，确认后才加入正式任务列表。
8. 在“明日规划”中选择任务列表模式或时间块模式，使用本地算法生成明日计划建议。
9. 在“目标库”中维护长期目标、优先级、每日投入和最近里程碑，明日规划会自动读取进行中的目标。

任务中心和系统托盘菜单都提供退出入口。应用使用单实例锁，重复启动不会创建第二个悬浮窗。

需要直接打开任务中心时，可以使用：

```powershell
TaskOverlay.App.exe --manage
```

## 数据文件

默认任务数据：

```text
程序所在目录\data\tasks.json
```

自动备份：

```text
程序所在目录\data\tasks.bak.json
```

本地设置：

```text
程序所在目录\data\settings.json
```

任务数据和设置默认保存在同一个 `data` 文件夹。首次使用新路径时，如果旧的 `%APPDATA%\TaskOverlay` 中已有数据，会自动复制到程序目录的 `data` 文件夹。设置文件使用原子写入并保留上一版本备份，损坏时会自动恢复，不会阻止应用启动。

自动化测试或便携运行可以显式覆盖目录：

```powershell
$env:TASKOVERLAY_DATA_DIR = 'D:\TaskOverlay\data'
$env:TASKOVERLAY_SETTINGS_DIR = 'D:\TaskOverlay\data'
```

## 构建与验证

环境要求：Windows 和 .NET 8 SDK。

```powershell
dotnet build TaskOverlay.sln
dotnet build TaskOverlay.sln -c Release
dotnet run --project tests\TaskOverlay.SmokeTests\TaskOverlay.SmokeTests.csproj
powershell -ExecutionPolicy Bypass -File tests\cli-e2e.ps1
```

## 本地 API 与 CLI

应用默认在 `http://127.0.0.1:43127/` 启动本地 API。接口仅监听本机，并要求使用“设置”页中的访问令牌。可在设置页修改端口、复制令牌或关闭 API。

先设置令牌：

```powershell
$env:TASKOVERLAY_TOKEN = '从设置页复制的令牌'
```

常用 CLI 命令：

```powershell
dotnet run --project src\TaskOverlay.Cli -- health
dotnet run --project src\TaskOverlay.Cli -- add "整理本周计划" --due "2026-06-07 18:00" --priority high --tags "工作,AI" --source ai
dotnet run --project src\TaskOverlay.Cli -- proposals
dotnet run --project src\TaskOverlay.Cli -- confirm <提案ID>
dotnet run --project src\TaskOverlay.Cli -- tasks --filter today
dotnet run --project src\TaskOverlay.Cli -- complete <任务ID>
```

CLI 同时支持完整分组命令、短参数、自然时间、批量 ID、JSON 文件/stdin 和多种输出格式：

```powershell
# 默认提交到提案箱
TaskOverlay.Cli.exe proposal add "整理会议纪要" -p high -d "tomorrow 18:00" -g AI -s ai
TaskOverlay.Cli.exe proposal add "推进目标拆分任务" --goal-id 1 --goal-title "提升 AI Agent 工程能力"

# 直接创建正式任务
TaskOverlay.Cli.exe task add "每周回顾" --repeat weekly --day 周五 --tags "工作,复盘"

# 更新、批量完成、恢复与删除
TaskOverlay.Cli.exe task update 12 --due "后天 09:30" --clear reminder,tags
TaskOverlay.Cli.exe task complete --ids 12,13,14
TaskOverlay.Cli.exe task reopen 12
TaskOverlay.Cli.exe task delete 12 --yes

# 批量确认所有提案，并以表格/ID 输出
TaskOverlay.Cli.exe proposal confirm --all
TaskOverlay.Cli.exe task list --filter today --output table
TaskOverlay.Cli.exe proposal list --output ids

# 从 JSON 对象或数组导入
TaskOverlay.Cli.exe proposal add --file proposals.json
Get-Content tasks.json -Raw | TaskOverlay.Cli.exe task add --stdin

# 长期目标库
TaskOverlay.Cli.exe goal add "提升 AI Agent 工程能力" --priority high --horizon long-term --daily-minutes 90 --tags "AI,学习" --milestone "完成 TaskOverlay 规划助手" --target "2026-06-20"
TaskOverlay.Cli.exe goal list --status active
TaskOverlay.Cli.exe goal link 1 --task-id 12 --note "关联已有任务"
TaskOverlay.Cli.exe goal unlink 1 --link-id 3

# 本地明日规划，不调用 AI
TaskOverlay.Cli.exe plan tomorrow --mode task-list --goal "推进 TaskOverlay"
TaskOverlay.Cli.exe plan tomorrow --mode time-block --window "09:00-11:30,14:00-17:30"
```

Windows PowerShell 5 会处理原生命令参数中的双引号，因此复杂 JSON 推荐使用 `--file` 或 `--stdin`；使用 `--json` 时需要将内部双引号转义为 `\"`。

运行 `TaskOverlay.Cli.exe help` 可查看完整命令。CLI 会优先读取 `--url`/`--token`，其次读取环境变量，最后自动发现 `TASKOVERLAY_SETTINGS_DIR` 或应用数据目录中的 `settings.json`。

可用接口：

- `GET /health`
- `GET /api/tasks?filter=all&search=`
- `GET /api/tasks/{id}`
- `POST /api/tasks`
- `PUT /api/tasks/{id}`
- `GET /api/proposals`
- `GET /api/proposals/{id}`
- `POST /api/proposals`
- `POST /api/proposals/{id}/confirm`
- `DELETE /api/proposals/{id}/reject`
- `GET /api/goals?status=active`
- `GET /api/goals/{id}`
- `POST /api/goals`
- `PUT /api/goals/{id}`
- `DELETE /api/goals/{id}`
- `POST /api/goals/{id}/links`
- `DELETE /api/goals/{id}/links/{linkId}`
- `POST /api/overlay/toggle-edit`
- `POST /api/overlay/show`
- `POST /api/overlay/hide`
- `POST /api/tasks/{id}/complete`
- `DELETE /api/tasks/{id}/delete`
- `GET /api/plans/tomorrow?mode=taskList&windows=09:00-11:30`
- `POST /api/plans/tomorrow`

除健康检查外，请在请求中发送 `Authorization: Bearer <令牌>`。AI 工具可以直接调用这些接口，也可以执行 CLI；建议始终先提交提案，再由用户确认。

## 游戏内快捷键桥接

如果游戏内普通快捷键监听失效，可以先启动主程序，再以管理员身份运行桥接程序：

```text
src\TaskOverlay.HotkeyBridge\bin\Release\net8.0-windows\TaskOverlay.HotkeyBridge.exe
```

桥接程序会读取 `settings.json` 中的 API 端口、令牌和快捷键，监听到快捷键后调用 `POST /api/overlay/toggle-edit`。它只监听并触发，不吞掉按键。若主程序设置文件不在默认位置，可用：

```powershell
TaskOverlay.HotkeyBridge.exe --settings-dir "E:\work\to-dolist\src\TaskOverlay.App\bin\Release\net8.0-windows\data"
TaskOverlay.HotkeyBridge.exe --hotkey "Ctrl+Shift+F12" --url "http://127.0.0.1:43127/" --token "<设置页令牌>"
```

如果游戏或反作弊系统主动屏蔽外部键盘监听，桥接程序也可能无法生效；不要尝试绕过反作弊规则。

## 明日规划 V1

明日规划使用本地算法，运行时不依赖 AI。它会读取进行中的长期目标、今天、明天、过期和未来任务，生成待确认的规划建议。

- `任务列表模式`：输出按优先级排序的明日任务建议。
- `时间块模式`：按可用时间段安排任务，并在需要时保留父子层级拆分。
- 新增建议可提交到“外部提案”；确认后才会变成正式任务。
- 已有任务调整只作为建议展示，不会自动修改正式任务。
- 从目标库生成的规划项会保留 `goalId` 和 `goalTitle`，进入提案箱时备注会显示关联目标，确认后会写入目标的任务链接。

## 长期目标库 V2

目标库保存到程序数据目录中的 `goals.json`，和任务数据分开，避免影响 `tasks.json` 的导入导出。

- `Goal`：长期目标，包含标题、描述、优先级、状态、时间范围、每日建议投入时间和标签。
- `Milestone`：阶段目标，包含目标日期和状态。
- `Task Link`：预留的任务关联结构，后续用于把正式任务或提案关联到目标。

目标可以通过 Task Center 的“目标库”页、CLI `goal` 命令或本地 API 管理。本地规划算法会读取 `active` 目标，把高优先级目标和最近阶段目标转换成明日建议。已有正式任务可以通过 `goal link` 或目标链接 API 关联到目标；解除链接不会删除正式任务。

Release 可执行文件：

```text
src\TaskOverlay.App\bin\Release\net8.0-windows\TaskOverlay.App.exe
```

停止测试实例：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\stop-task-overlay.ps1
```

## 存储后端

- `本地 JSON`：默认模式，适合单机日常使用。
- `MySQL`：可选模式，适合明确需要共享数据库的场景。连接失败时应用会自动回退到本地 JSON。
