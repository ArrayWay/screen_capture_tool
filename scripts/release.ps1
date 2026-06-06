param(
    [string]$Version,

    [switch]$Trial,

    [switch]$InitChangelog,

    [string[]]$Added = @(),

    [string[]]$Fixed = @(),

    [string[]]$Changed = @(),

    [string]$CommitMessage,

    [switch]$SkipPushMain,

    [switch]$OpenWeb,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Fail {
    param([string]$Message)
    throw $Message
}

function Ensure-GitCommand {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Fail "Git 未安装或未加入 PATH。"
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $output = & git @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if (-not $AllowFailure -and $exitCode -ne 0) {
        $text = if ($output) { ($output | Out-String).Trim() } else { "git $($Arguments -join ' ') 执行失败。" }
        Fail $text
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = ($output | Out-String).Trim()
    }
}

function Test-WorkingTreeClean {
    $status = Invoke-Git -Arguments @('status', '--short')
    return [string]::IsNullOrWhiteSpace($status.Output)
}

function Test-RemoteBranchExists {
    param([string]$BranchName)

    $result = Invoke-Git -Arguments @('ls-remote', '--heads', 'origin', $BranchName) -AllowFailure
    return -not [string]::IsNullOrWhiteSpace($result.Output)
}

function Test-RemoteTagExists {
    param([string]$TagName)

    $result = Invoke-Git -Arguments @('ls-remote', '--tags', 'origin', $TagName) -AllowFailure
    return -not [string]::IsNullOrWhiteSpace($result.Output)
}

function Test-LocalTagExists {
    param([string]$TagName)

    $result = Invoke-Git -Arguments @('tag', '--list', $TagName)
    return $result.Output -eq $TagName
}

function Test-ChangelogEntry {
    param(
        [string]$ChangelogPath,
        [string]$TagName
    )

    if (-not (Test-Path -LiteralPath $ChangelogPath)) {
        Fail "未找到 $ChangelogPath。"
    }

    $content = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8
    $escapedTag = [regex]::Escape($TagName)
    $pattern = "(?ms)^## \[$escapedTag\].*?(?=^## \[|\z)"
    return [regex]::IsMatch($content, $pattern)
}

function New-ChangelogBulletBlock {
    param(
        [string]$Title,
        [string[]]$Items
    )

    $lines = @("### $Title")
    if ($Items -and $Items.Count -gt 0) {
        $lines += $Items | ForEach-Object { "- $_" }
    }
    else {
        $lines += '- 暂无'
    }

    return ($lines -join "`r`n")
}

function Add-ChangelogEntry {
    param(
        [string]$ChangelogPath,
        [string]$TagName,
        [string]$DisplayVersion,
        [bool]$IsTrial,
        [string[]]$AddedItems,
        [string[]]$FixedItems,
        [string[]]$ChangedItems
    )

    if (-not (Test-Path -LiteralPath $ChangelogPath)) {
        Fail "未找到 $ChangelogPath。"
    }

    if (Test-ChangelogEntry -ChangelogPath $ChangelogPath -TagName $TagName) {
        return $false
    }

    $content = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8
    $title = if ($IsTrial) { "试用版 V$DisplayVersion" } else { "正式版 V$DisplayVersion" }

    $sectionLines = @(
        "## [$TagName] - $title",
        '',
        (New-ChangelogBulletBlock -Title '新增' -Items $AddedItems),
        '',
        (New-ChangelogBulletBlock -Title '修复' -Items $FixedItems),
        '',
        (New-ChangelogBulletBlock -Title '调整' -Items $ChangedItems),
        ''
    )
    $newSection = ($sectionLines -join "`r`n")

    $unreleasedPattern = '(?s)(## \[未发布\]\s*.*?)(?=\r?\n## \[|\z)'
    if ([regex]::IsMatch($content, $unreleasedPattern)) {
        $content = [regex]::Replace(
            $content,
            $unreleasedPattern,
            {
                param($match)
                $match.Value.TrimEnd() + "`r`n`r`n" + $newSection
            },
            1
        )
    }
    else {
        $content = $content.TrimEnd() + "`r`n`r`n" + $newSection
    }

    Set-Content -LiteralPath $ChangelogPath -Value $content -Encoding UTF8
    return $true
}

function Read-YesNo {
    param(
        [string]$Prompt,
        [bool]$Default = $false
    )

    $suffix = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $inputValue = Read-Host "$Prompt $suffix"

    if ([string]::IsNullOrWhiteSpace($inputValue)) {
        return $Default
    }

    switch ($inputValue.Trim().ToLowerInvariant()) {
        'y' { return $true }
        'yes' { return $true }
        'n' { return $false }
        'no' { return $false }
        default { Fail "无法识别输入：[$inputValue]，请输入 y 或 n。" }
    }
}

function Read-ListInput {
    param([string]$Prompt)

    $raw = Read-Host $Prompt
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @()
    }

    return $raw.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Convert-RemoteToWebUrl {
    param([string]$RemoteUrl)

    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
        return $null
    }

    $url = $RemoteUrl.Trim()

    if ($url -match '^https://github\.com/(?<path>.+?)(?:\.git)?/?$') {
        return "https://github.com/$($Matches.path)"
    }

    if ($url -match '^git@github\.com:(?<path>.+?)(?:\.git)?$') {
        return "https://github.com/$($Matches.path)"
    }

    return $null
}

function Open-Url {
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return
    }

    Start-Process $Url | Out-Null
}

function Confirm-ReleasePlan {
    param(
        [string]$BranchName,
        [string]$TagName,
        [bool]$IsTrial,
        [bool]$WillInitChangelog,
        [bool]$WillPushBranch,
        [bool]$WillOpenWeb,
        [string[]]$AddedItems,
        [string[]]$FixedItems,
        [string[]]$ChangedItems
    )

    Write-Host "`n==== 发版确认摘要 ====" -ForegroundColor Yellow
    Write-Host "分支            : $BranchName"
    Write-Host "Tag             : $TagName"
    Write-Host "发布类型        : $(if ($IsTrial) { '试用版' } else { '正式版' })"
    Write-Host "自动初始化日志  : $(if ($WillInitChangelog) { '是' } else { '否' })"
    Write-Host "推送当前分支    : $(if ($WillPushBranch) { '是' } else { '否' })"
    Write-Host "打开 GitHub 页面: $(if ($WillOpenWeb) { '是' } else { '否' })"

    if ($WillInitChangelog) {
        $addedText = if ($AddedItems.Count -gt 0) { $AddedItems -join '；' } else { '暂无' }
        $fixedText = if ($FixedItems.Count -gt 0) { $FixedItems -join '；' } else { '暂无' }
        $changedText = if ($ChangedItems.Count -gt 0) { $ChangedItems -join '；' } else { '暂无' }

        Write-Host "新增            : $addedText"
        Write-Host "修复            : $fixedText"
        Write-Host "调整            : $changedText"
    }

    if (-not (Read-YesNo -Prompt '确认继续执行发版？' -Default $false)) {
        Fail '已取消发版。'
    }
}

Ensure-GitCommand

$repoRoot = (Invoke-Git -Arguments @('rev-parse', '--show-toplevel')).Output
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Fail "当前目录不是 Git 仓库。"
}

Set-Location -LiteralPath $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "未传入 -Version，进入交互式发版模式。" -ForegroundColor Yellow
    $Version = Read-Host '请输入版本号（例如 1.2 或 2.0.3）'

    if ([string]::IsNullOrWhiteSpace($Version)) {
        Fail '版本号不能为空。'
    }

    if (-not $PSBoundParameters.ContainsKey('Trial')) {
        $Trial = Read-YesNo -Prompt '是否发布为试用版（tag 将带 -trial）？' -Default $false
    }

    if (-not $PSBoundParameters.ContainsKey('InitChangelog')) {
        $InitChangelog = Read-YesNo -Prompt '若 CHANGELOG.md 缺少该版本，是否自动初始化？' -Default $true
    }

    if ($InitChangelog) {
        if ($Added.Count -eq 0) {
            $Added = Read-ListInput -Prompt '请输入“新增”条目，多个用英文逗号分隔，留空则写入暂无'
        }
        if ($Fixed.Count -eq 0) {
            $Fixed = Read-ListInput -Prompt '请输入“修复”条目，多个用英文逗号分隔，留空则写入暂无'
        }
        if ($Changed.Count -eq 0) {
            $Changed = Read-ListInput -Prompt '请输入“调整”条目，多个用英文逗号分隔，留空则写入暂无'
        }
    }

    if (-not $PSBoundParameters.ContainsKey('OpenWeb')) {
        $OpenWeb = Read-YesNo -Prompt '发版成功后是否自动打开 GitHub Releases / Actions 页面？' -Default $true
    }
}

$normalizedVersion = $Version.Trim()
if ($normalizedVersion -notmatch '^\d+(?:\.\d+)*$') {
    Fail "版本号格式无效：[$Version]。请输入类似 1.1、1.2、2.0.3 的版本号。"
}

$tag = if ($Trial) { "v$normalizedVersion-trial" } else { "v$normalizedVersion" }
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$currentBranch = (Invoke-Git -Arguments @('branch', '--show-current')).Output

Write-Host "仓库目录: $repoRoot"
Write-Host "当前分支: $currentBranch"
Write-Host "目标 Tag : $tag"

$willInitChangelog = $InitChangelog -and (-not (Test-ChangelogEntry -ChangelogPath $changelogPath -TagName $tag))
$willPushBranch = -not $SkipPushMain
$willOpenWeb = [bool]$OpenWeb

if (-not $Force) {
    Confirm-ReleasePlan `
        -BranchName $currentBranch `
        -TagName $tag `
        -IsTrial ([bool]$Trial) `
        -WillInitChangelog $willInitChangelog `
        -WillPushBranch $willPushBranch `
        -WillOpenWeb $willOpenWeb `
        -AddedItems $Added `
        -FixedItems $Fixed `
        -ChangedItems $Changed
}

if (-not $Force) {
    Write-Step "检查工作区是否干净"
    if (-not (Test-WorkingTreeClean)) {
        Fail "工作区存在未提交变更。请先提交或清理后再发版；如确认继续，可追加 -Force。"
    }
}

Write-Step "检查 CHANGELOG.md 中是否存在对应版本区块"
$changelogCreated = $false
if (-not (Test-ChangelogEntry -ChangelogPath $changelogPath -TagName $tag)) {
    if (-not $InitChangelog) {
        Fail "CHANGELOG.md 中未找到对应区块 [$tag]。如需自动初始化，可追加 -InitChangelog。"
    }

    Write-Step "自动初始化 CHANGELOG.md 版本区块"
    $changelogCreated = Add-ChangelogEntry `
        -ChangelogPath $changelogPath `
        -TagName $tag `
        -DisplayVersion $normalizedVersion `
        -IsTrial ([bool]$Trial) `
        -AddedItems $Added `
        -FixedItems $Fixed `
        -ChangedItems $Changed

    if (-not $changelogCreated) {
        Fail "CHANGELOG.md 初始化失败。"
    }

    $defaultCommitMessage = if ($CommitMessage) { $CommitMessage } else { "init changelog for $tag" }

    Write-Step "提交自动生成的 CHANGELOG.md"
    Invoke-Git -Arguments @('add', 'CHANGELOG.md') | Out-Null
    Invoke-Git -Arguments @('commit', '-m', $defaultCommitMessage) | Out-Null
}

Write-Step "检查本地与远端 Tag 是否已存在"
if (Test-LocalTagExists -TagName $tag) {
    Fail "本地已存在 Tag [$tag]。如需重发，请先删除本地 Tag：git tag -d $tag"
}
if (Test-RemoteTagExists -TagName $tag) {
    Fail "远端已存在 Tag [$tag]。如需重发，请先删除远端 Tag：git push origin :refs/tags/$tag"
}

if (-not $SkipPushMain) {
    Write-Step "推送当前分支到 origin/$currentBranch"

    if (-not (Test-RemoteBranchExists -BranchName $currentBranch)) {
        Invoke-Git -Arguments @('push', '-u', 'origin', $currentBranch) | Out-Null
    }
    else {
        Invoke-Git -Arguments @('push', 'origin', $currentBranch) | Out-Null
    }
}
else {
    Write-Step "已跳过 push main/当前分支"
}

Write-Step "创建本地 Tag"
Invoke-Git -Arguments @('tag', $tag) | Out-Null

try {
    Write-Step "推送 Tag 到远端"
    Invoke-Git -Arguments @('push', 'origin', $tag) | Out-Null
}
catch {
    Write-Warning "推送 Tag 失败，正在回滚本地 Tag [$tag]..."
    Invoke-Git -Arguments @('tag', '-d', $tag) -AllowFailure | Out-Null
    throw
}

Write-Step "校验远端 Tag 是否存在"
if (-not (Test-RemoteTagExists -TagName $tag)) {
    Fail "远端未检测到 Tag [$tag]，请稍后手动执行：git ls-remote --tags origin $tag"
}

$remoteUrl = (Invoke-Git -Arguments @('remote', 'get-url', 'origin')).Output
$repoWebUrl = Convert-RemoteToWebUrl -RemoteUrl $remoteUrl
$releasesWebUrl = if ($repoWebUrl) { "$repoWebUrl/releases" } else { $null }
$actionsWebUrl = if ($repoWebUrl) { "$repoWebUrl/actions" } else { $null }

Write-Host "`n发版完成。" -ForegroundColor Green
Write-Host "Tag 已推送: $tag" -ForegroundColor Green
Write-Host "GitHub Actions 将自动触发 Release 工作流。" -ForegroundColor Green
Write-Host "可检查命令: git ls-remote --tags origin $tag"
Write-Host "远端地址: $remoteUrl"
if ($releasesWebUrl) {
    Write-Host "Releases 页面: $releasesWebUrl"
}
if ($actionsWebUrl) {
    Write-Host "Actions 页面 : $actionsWebUrl"
}

if ($OpenWeb -and $repoWebUrl) {
    Write-Step "打开 GitHub Releases / Actions 页面"
    Open-Url -Url $releasesWebUrl
    Open-Url -Url $actionsWebUrl
}

Write-Host ""
Write-Host "常用示例:"
Write-Host "  ./scripts/release.ps1 -Version 1.2"
Write-Host "  ./scripts/release.ps1 -Version 1.2 -Trial"
Write-Host "  ./scripts/release.ps1 -Version 1.2 -InitChangelog -Added '新增功能 A' -Changed '界面文案调整'"
Write-Host "  ./scripts/release.ps1 -OpenWeb"
Write-Host "  ./scripts/release.ps1   # 不带参数时进入交互模式"
