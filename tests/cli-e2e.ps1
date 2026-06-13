param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "src\TaskOverlay.App\bin\$Configuration\net8.0-windows\TaskOverlay.App.exe"
$cli = Join-Path $root "src\TaskOverlay.Cli\bin\$Configuration\net8.0\TaskOverlay.Cli.exe"
$testDir = Join-Path ([IO.Path]::GetTempPath()) ("TaskOverlay.CliE2E\" + [guid]::NewGuid().ToString("N"))
$listener = [System.Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$token = "cli-e2e-" + [guid]::NewGuid().ToString("N")
$process = $null
$passed = 0

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw "ASSERT FAILED: $message"
    }
    $script:passed++
}

function Invoke-Cli([string[]]$Arguments, [Nullable[bool]]$ExpectSuccess = $true) {
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $script:cli @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorAction
    if ($ExpectSuccess.HasValue -and $ExpectSuccess.Value -and $exitCode -ne 0) {
        throw "CLI failed ($exitCode): $($Arguments -join ' ')`n$($output -join "`n")"
    }
    if ($ExpectSuccess.HasValue -and -not $ExpectSuccess.Value -and $exitCode -eq 0) {
        throw "CLI unexpectedly succeeded: $($Arguments -join ' ')"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Text = ($output -join "`n") }
}

function Invoke-CliJson([string[]]$Arguments) {
    $result = Invoke-Cli $Arguments
    try {
        $value = $result.Text | ConvertFrom-Json
    }
    catch {
        throw "Invalid CLI JSON for '$($Arguments -join ' ')': $($result.Text)"
    }
    if ($value -is [Array]) {
        foreach ($item in $value) {
            Write-Output $item
        }
        return
    }
    return $value
}

try {
    if (Get-Process TaskOverlay.App -ErrorAction SilentlyContinue) {
        throw "TaskOverlay.App is already running. Stop it before CLI E2E testing."
    }
    if (-not (Test-Path $app) -or -not (Test-Path $cli)) {
        throw "Release binaries not found. Build the solution first."
    }

    New-Item -ItemType Directory -Path $testDir | Out-Null
    @{
        apiEnabled = $true
        apiPort = $port
        apiToken = $token
        hotkey = "Ctrl+Shift+F12"
    } | ConvertTo-Json | Set-Content (Join-Path $testDir "settings.json")
    @{
        nextTaskId = 1
        nextCompletionId = 1
        tasks = @()
        dailyCompletions = @()
        settings = @{}
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $testDir "tasks.json")

    $env:TASKOVERLAY_DATA_DIR = $testDir
    $env:TASKOVERLAY_SETTINGS_DIR = $testDir
    Remove-Item Env:TASKOVERLAY_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:TASKOVERLAY_URL -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $app -PassThru -WindowStyle Hidden

    for ($i = 0; $i -lt 40; $i++) {
        $health = Invoke-Cli @("status", "--output=compact") $null
        if ($health.ExitCode -eq 0) { break }
        Start-Sleep -Milliseconds 250
    }
    Assert-True ($health.ExitCode -eq 0) "status alias and settings auto-discovery should reach the API"

    $overlayResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "http://127.0.0.1:$port/api/overlay/toggle-edit" `
        -Headers @{ Authorization = "Bearer $token" }
    Assert-True ($overlayResponse.accepted -eq $true -and $overlayResponse.overlay -eq "toggle-edit") "overlay toggle API should accept authenticated requests"

    $shortConnection = Invoke-Cli @("health", "-u", "http://127.0.0.1:$port/", "-t", $token, "-o", "compact")
    Assert-True ($shortConnection.Text -match '"status":"ok"') "short connection and output options should work"

    $config = Invoke-CliJson @("config")
    Assert-True ($config.url -eq "http://127.0.0.1:$port/") "config should discover API port from settings"
    Assert-True ($config.token -ne $token) "config should mask token by default"

    $proposal = Invoke-CliJson @(
        "proposal", "add", "natural date and short options",
        "-n", "CLI flexible test",
        "-p", "high",
        "-d", "tomorrow 18:00",
        "-g", "AI",
        "--tag=automation",
        "--repeat", "weekly",
        "--day", "Monday",
        "-s", "ai"
    )
    Assert-True ($proposal.title -eq "natural date and short options") "proposal add should preserve title"
    Assert-True ($proposal.priority -eq "high") "short priority option should work"
    Assert-True ($proposal.tags.Count -eq 2) "repeated tag options should work"
    Assert-True ($proposal.recurrence.kind -eq "weekly") "weekly recurrence should be created"

    $shownProposal = Invoke-CliJson @("proposal", "show", $proposal.id)
    Assert-True ($shownProposal.id -eq $proposal.id) "proposal show should return the requested proposal"

    $legacy = Invoke-CliJson @("add", "legacy command compatibility", "--due=+2h")
    Assert-True ($legacy.title -eq "legacy command compatibility") "legacy add alias should work"

    $proposalJsonPath = Join-Path $testDir "proposal-batch.json"
    @(
        @{ title = "batch proposal A"; source = "json-file" },
        @{ title = "batch proposal B"; source = "json-file"; isDaily = $true }
    ) | ConvertTo-Json | Set-Content $proposalJsonPath
    $batchProposals = @(Invoke-CliJson @("proposal", "add", "--file", $proposalJsonPath))
    Assert-True ($batchProposals.Count -eq 2) "proposal add --file should accept JSON arrays; actual=$($batchProposals.Count)"

    $inlineJson = '{"title":"inline-json-proposal","source":"inline"}'.Replace('"', '\"')
    $inlineProposal = Invoke-CliJson @("proposal", "add", "--json", $inlineJson)
    Assert-True ($inlineProposal.source -eq "inline" -and $inlineProposal.title -eq "inline-json-proposal") "proposal add --json should accept inline JSON"

    $proposalIds = (Invoke-Cli @("proposal", "list", "--output", "ids")).Text -split "\r?\n"
    Assert-True ($proposalIds.Count -eq 5) "ids output should emit one proposal id per line"

    $confirmResult = @(Invoke-CliJson @("proposal", "confirm", $proposal.id, $legacy.id))
    Assert-True ($confirmResult.Count -eq 2) "proposal confirm should accept multiple IDs"
    Assert-True ($confirmResult[0].recurrence.kind -eq "weekly") "proposal confirmation should preserve recurrence"

    $direct = Invoke-CliJson @(
        "task", "add", "direct official task",
        "--priority", "urgent",
        "--repeat", "3d",
        "--reminder", "+30m",
        "--tags", "direct,test"
    )
    Assert-True ($direct.recurrence.kind -eq "customDays") "task add should support custom-day recurrence"

    $taskJsonPath = Join-Path $testDir "task-batch.json"
    @(
        @{ title = "JSON official task A"; priority = "low" },
        @{ title = "JSON official task B"; priority = "normal" }
    ) | ConvertTo-Json | Set-Content $taskJsonPath
    $batchTasks = @(Invoke-CliJson @("task", "add", "--file", $taskJsonPath))
    Assert-True ($batchTasks.Count -eq 2) "task add --file should accept JSON arrays"

    $stdinText = '{"title":"stdin official task","priority":"high"}' | & $cli task add --stdin 2>&1
    Assert-True ($LASTEXITCODE -eq 0) "task add --stdin should accept piped JSON"
    $stdinTask = ($stdinText -join "`n") | ConvertFrom-Json
    Assert-True ($stdinTask.title -eq "stdin official task") "stdin task should be created"

    $quiet = Invoke-Cli @("task", "list", "--quiet")
    Assert-True ([string]::IsNullOrEmpty($quiet.Text)) "--quiet should suppress successful output"

    $searched = @(Invoke-CliJson @("task", "list", "--search", "direct official"))
    Assert-True ($searched.Count -eq 1 -and $searched[0].id -eq $direct.id) "task search should find the direct task"

    $updated = Invoke-CliJson @(
        "task", "update", $direct.id,
        "--title", "updated official task",
        "--notes", "updated",
        "--due", "tomorrow 09:30",
        "--clear", "reminder,tags,repeat"
    )
    Assert-True ($updated.title -eq "updated official task") "task update should change title"
    Assert-True ($null -eq $updated.reminderAt -and $updated.tags.Count -eq 0 -and $null -eq $updated.recurrence) "task update --clear should clear fields"

    $completeIds = "$($direct.id),$($batchTasks[0].id)"
    $null = Invoke-CliJson @("task", "complete", "--ids", $completeIds)
    $completed = @(Invoke-CliJson @("task", "list", "--filter", "completed"))
    Assert-True (@($completed | Where-Object id -eq $direct.id).Count -eq 1) "batch complete should mark tasks completed"

    $null = Invoke-CliJson @("task", "reopen", $direct.id)
    $completedAfterReopen = @(Invoke-CliJson @("task", "list", "--filter", "completed"))
    Assert-True (@($completedAfterReopen | Where-Object id -eq $direct.id).Count -eq 0) "reopen should restore a task"

    $deleteWithoutYes = Invoke-Cli @("task", "delete", $direct.id) $false
    Assert-True ($deleteWithoutYes.Text -match "--yes") "delete should require explicit --yes"
    $null = Invoke-CliJson @("task", "delete", $direct.id, "--yes")
    $missing = Invoke-Cli @("task", "show", $direct.id) $false
    Assert-True ($missing.ExitCode -ne 0) "deleted task should no longer exist"

    $wrongToken = Invoke-Cli @("task", "list", "--token", "wrong-token") $false
    Assert-True ($wrongToken.Text -match "error") "wrong token should be rejected"

    $remainingProposals = @(Invoke-CliJson @("proposal", "list"))
    Assert-True ($remainingProposals.Count -eq 3) "three proposals should remain before reject all"
    $null = Invoke-CliJson @("proposal", "reject", "--all", "--yes")
    $afterReject = @(Invoke-CliJson @("proposal", "list"))
    Assert-True ($afterReject.Count -eq 0) "proposal reject --all --yes should clear pending proposals"

    $table = Invoke-Cli @("task", "list", "--output", "table")
    Assert-True ($table.Text -match "JSON official task") "table output should contain readable task titles"

    $goal = Invoke-CliJson @(
        "goal", "add", "提升 AI Agent 工程能力",
        "--description", "长期目标库 e2e",
        "--priority", "high",
        "--horizon", "long-term",
        "--daily-minutes", "90",
        "--tags", "AI,规划",
        "--milestone", "完成 TaskOverlay 规划 V2",
        "--target", "tomorrow"
    )
    Assert-True ($goal.id -gt 0 -and $goal.milestones.Count -eq 1) "goal add should create a goal with milestone"
    $goals = @(Invoke-CliJson @("goal", "list", "--status", "active"))
    Assert-True (@($goals | Where-Object id -eq $goal.id).Count -eq 1) "goal list should include active goal"

    $manualLinkedGoal = Invoke-CliJson @("goal", "link", $goal.id, "--task-id", $stdinTask.id, "--note", "manual cli link")
    $manualLink = @($manualLinkedGoal.taskLinks | Where-Object { $_.taskId -eq $stdinTask.id -and $_.note -eq "manual cli link" })[0]
    Assert-True ($manualLink.id -gt 0) "goal link should attach an existing task"
    $unlinkResult = Invoke-CliJson @("goal", "unlink", $goal.id, "--link-id", $manualLink.id)
    Assert-True ($unlinkResult.removed -eq $true) "goal unlink should remove a task link"
    $goalAfterManualUnlink = Invoke-CliJson @("goal", "show", $goal.id)
    Assert-True (@($goalAfterManualUnlink.taskLinks | Where-Object id -eq $manualLink.id).Count -eq 0) "goal unlink should not leave the link in goal details"

    $goalProposal = Invoke-CliJson @(
        "proposal", "add", "goal-linked proposal",
        "--goal-id", $goal.id,
        "--goal-title", $goal.title,
        "--source", "ai"
    )
    Assert-True ($goalProposal.goalId -eq $goal.id -and $goalProposal.goalTitle -eq $goal.title) "proposal add should preserve goal metadata"
    $goalProposalTask = @(Invoke-CliJson @("proposal", "confirm", $goalProposal.id))[0]
    $goalAfterLink = Invoke-CliJson @("goal", "show", $goal.id)
    Assert-True (@($goalAfterLink.taskLinks | Where-Object { $_.taskId -eq $goalProposalTask.id -and $_.proposalId -eq $goalProposal.id }).Count -eq 1) "confirming a goal proposal should link the task back to the goal"

    $taskListPlan = Invoke-CliJson @("plan", "tomorrow", "--mode", "task-list", "--goal", "推进 TaskOverlay", "--max", "20")
    Assert-True ($taskListPlan.mode -eq "taskList") "plan tomorrow should support task-list mode"
    Assert-True ($taskListPlan.items.Count -gt 0) "task-list plan should include planning items"
    Assert-True (@($taskListPlan.items | Where-Object { $_.title -match "AI Agent" }).Count -gt 0) "task-list plan should include active goal suggestions"
    Assert-True (@($taskListPlan.items | Where-Object { $_.goalId -eq $goal.id -and $_.goalTitle -eq $goal.title }).Count -gt 0) "goal-derived plan items should include goal source"

    $todayPlan = Invoke-CliJson @("plan", "today", "--mode", "time-block", "--window", "09:00-10:00", "--goal", "今天白天完成一个可验收任务")
    Assert-True ($todayPlan.targetDate -eq (Get-Date).ToString("yyyy-MM-dd")) "plan today should target the current date"
    Assert-True ($todayPlan.items[0].timeBlock -eq "09:00-10:00") "plan today should support requested time blocks"

    $timeBlockPlan = Invoke-CliJson @("plan", "tomorrow", "--mode", "time-block", "--window", "08:00-09:00")
    Assert-True ($timeBlockPlan.mode -eq "timeBlock") "plan tomorrow should support time-block mode"
    Assert-True ($timeBlockPlan.items[0].timeBlock -eq "08:00-09:00") "time-block plan should use requested windows"
    Assert-True ($timeBlockPlan.items[0].children.Count -gt 0) "time-block plan should preserve split hierarchy"

    $null = Invoke-CliJson @("task", "complete", "--all", "--filter", "all")
    $completedAll = @(Invoke-CliJson @("task", "list", "--filter", "completed"))
    Assert-True ($completedAll.Count -ge 3) "task complete --all should complete all matching tasks"
    $null = Invoke-CliJson @("task", "reopen", "--all", "--filter", "completed")
    $completedNone = @(Invoke-CliJson @("task", "list", "--filter", "completed"))
    Assert-True ($completedNone.Count -eq 0) "task reopen --all should reopen all completed tasks"

    $missingComplete = Invoke-Cli @("task", "complete", "99999999") $false
    Assert-True ($missingComplete.Text -match "error") "operations on missing tasks should fail"

    Write-Host "PASS: $passed CLI E2E assertions passed."
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
    Remove-Item Env:TASKOVERLAY_DATA_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:TASKOVERLAY_SETTINGS_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:TASKOVERLAY_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:TASKOVERLAY_URL -ErrorAction SilentlyContinue
    if (Test-Path $testDir) {
        $resolved = (Resolve-Path $testDir).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe cleanup path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
