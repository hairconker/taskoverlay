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
- `POST /api/tasks/{id}/complete`
- `DELETE /api/tasks/{id}/delete`

除健康检查外，请在请求中发送 `Authorization: Bearer <令牌>`。AI 工具可以直接调用这些接口，也可以执行 CLI；建议始终先提交提案，再由用户确认。

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
