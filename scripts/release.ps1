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
$script:GitCommand = $null

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Fail {
    param([string]$Message)
    throw $Message
}

function Ensure-GitCommand {
    $gitCmd = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $gitCmd) {
        $gitCmd = Get-Command git -ErrorAction SilentlyContinue
    }

    if ($gitCmd) {
        $script:GitCommand = $gitCmd.Source
        return
    }

    $candidatePaths = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "$env:ProgramFiles\Git\bin\git.exe",
        "$env:ProgramFiles(x86)\Git\cmd\git.exe",
        "$env:ProgramFiles(x86)\Git\bin\git.exe",
        "$env:LocalAppData\Programs\Git\cmd\git.exe",
        "$env:LocalAppData\Programs\Git\bin\git.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($path in $candidatePaths) {
        if (Test-Path -LiteralPath $path) {
            $script:GitCommand = $path
            $gitDir = Split-Path -Path $path -Parent
            if ($env:PATH -notlike "*$gitDir*") {
                $env:PATH = "$gitDir;$env:PATH"
            }
            return
        }
    }

    Fail "Git is not installed or not available in PATH. Install Git or add git.exe to PATH."
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    if (-not $script:GitCommand) {
        Ensure-GitCommand
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:GitCommand
    $psi.WorkingDirectory = (Get-Location).Path
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    foreach ($argument in $Arguments) {
        [void]$psi.ArgumentList.Add($argument)
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    $exitCode = $process.ExitCode
    $combinedOutput = (@($stdout, $stderr) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
    $combinedOutput = $combinedOutput.Trim()

    if (-not $AllowFailure -and $exitCode -ne 0) {
        $text = if ($combinedOutput) { $combinedOutput } else { "git $($Arguments -join ' ') failed." }
        Fail $text
    }

    if ($stderr -and $exitCode -eq 0) {
        $stderr.Trim().Split("`r?`n") | ForEach-Object {
            if (-not [string]::IsNullOrWhiteSpace($_)) {
                Write-Warning $_.Trim()
            }
        }
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = $combinedOutput
    }
}

function Test-WorkingTreeClean {
    $status = Invoke-Git -Arguments @('status', '--short')
    return [string]::IsNullOrWhiteSpace($status.Output)
}

function Get-CurrentBranchName {
    $symbolicRef = Invoke-Git -Arguments @('symbolic-ref', '--short', 'HEAD') -AllowFailure
    if ($symbolicRef.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($symbolicRef.Output)) {
        return $symbolicRef.Output.Trim()
    }

    $nameRev = Invoke-Git -Arguments @('rev-parse', '--abbrev-ref', 'HEAD') -AllowFailure
    if ($nameRev.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($nameRev.Output) -and $nameRev.Output.Trim() -ne 'HEAD') {
        return $nameRev.Output.Trim()
    }

    Fail 'Unable to determine current Git branch name.'
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
        Fail "Cannot find $ChangelogPath."
    }

    $content = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8
    $escapedTag = [regex]::Escape($TagName)
    $pattern = "(?ms)^## \[$escapedTag\].*?(?=^## \[|\z)"
    return [regex]::IsMatch($content, $pattern)
}

function Get-PreferredNewLine {
    param([string]$Content)

    if ($Content -match "`r`n") {
        return "`r`n"
    }

    return "`n"
}

function New-ChangelogBulletBlock {
    param(
        [string]$Title,
        [string[]]$Items,
        [string]$NewLine = "`r`n"
    )

    $lines = @("### $Title")
    if ($Items -and $Items.Count -gt 0) {
        $lines += $Items | ForEach-Object { "- $_" }
    }
    else {
        $lines += '- TBD'
    }

    return ($lines -join $NewLine)
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
        Fail "Cannot find $ChangelogPath."
    }

    if (Test-ChangelogEntry -ChangelogPath $ChangelogPath -TagName $TagName) {
        return $false
    }

    $content = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8
    $newLine = Get-PreferredNewLine -Content $content
    $title = if ($IsTrial) { "Trial V$DisplayVersion" } else { "Release V$DisplayVersion" }

    $sectionLines = @(
        "## [$TagName] - $title",
        '',
        (New-ChangelogBulletBlock -Title 'Added' -Items $AddedItems -NewLine $newLine),
        '',
        (New-ChangelogBulletBlock -Title 'Fixed' -Items $FixedItems -NewLine $newLine),
        '',
        (New-ChangelogBulletBlock -Title 'Changed' -Items $ChangedItems -NewLine $newLine),
        ''
    )
    $newSection = ($sectionLines -join $newLine)

    $unreleasedPattern = '(?s)(## \[未发布\]\s*.*?)(?=\r?\n## \[|\z)'
    if ([regex]::IsMatch($content, $unreleasedPattern)) {
        $content = [regex]::Replace(
            $content,
            $unreleasedPattern,
            {
                param($match)
                $match.Value.TrimEnd() + $newLine + $newLine + $newSection
            },
            1
        )
    }
    else {
        $content = $content.TrimEnd() + $newLine + $newLine + $newSection
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

    while ($true) {
        $inputValue = Read-Host "$Prompt $suffix"

        if ([string]::IsNullOrWhiteSpace($inputValue)) {
            return $Default
        }

        switch ($inputValue.Trim().ToLowerInvariant()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
            default {
                Write-Host "Unrecognized input: [$inputValue]. Please enter y or n." -ForegroundColor Yellow
            }
        }
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

    Write-Host "`n==== Release confirmation summary ====" -ForegroundColor Yellow
    Write-Host "Branch           : $BranchName"
    Write-Host "Tag              : $TagName"
    Write-Host "Release type     : $(if ($IsTrial) { 'Trial' } else { 'Formal' })"
    Write-Host "Init changelog   : $(if ($WillInitChangelog) { 'Yes' } else { 'No' })"
    Write-Host "Push branch      : $(if ($WillPushBranch) { 'Yes' } else { 'No' })"
    Write-Host "Open GitHub pages: $(if ($WillOpenWeb) { 'Yes' } else { 'No' })"

    if ($WillInitChangelog) {
        $addedText = if ($AddedItems.Count -gt 0) { $AddedItems -join '; ' } else { 'TBD' }
        $fixedText = if ($FixedItems.Count -gt 0) { $FixedItems -join '; ' } else { 'TBD' }
        $changedText = if ($ChangedItems.Count -gt 0) { $ChangedItems -join '; ' } else { 'TBD' }

        Write-Host "Added            : $addedText"
        Write-Host "Fixed            : $fixedText"
        Write-Host "Changed          : $changedText"
    }

    if (-not (Read-YesNo -Prompt 'Continue release?' -Default $false)) {
        Fail 'Release cancelled.'
    }
}

Ensure-GitCommand

$repoRoot = (Invoke-Git -Arguments @('rev-parse', '--show-toplevel')).Output
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Fail "Current directory is not a Git repository."
}

Set-Location -LiteralPath $repoRoot

if (-not $Force) {
    Write-Step "Checking whether the working tree is clean"
    if (-not (Test-WorkingTreeClean)) {
        Fail "Working tree has uncommitted changes. Commit or clean them first, or use -Force if you really want to continue."
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "No -Version provided. Entering interactive release mode." -ForegroundColor Yellow
    $Version = Read-Host 'Enter version (for example: 1.2 or 2.0.3)'

    if ([string]::IsNullOrWhiteSpace($Version)) {
        Fail 'Version cannot be empty.'
    }

    if (-not $PSBoundParameters.ContainsKey('Trial')) {
        $Trial = Read-YesNo -Prompt 'Release as trial version (tag will include -trial)?' -Default $false
    }

    if (-not $PSBoundParameters.ContainsKey('InitChangelog')) {
        $InitChangelog = Read-YesNo -Prompt 'If CHANGELOG.md does not contain this version, initialize it automatically?' -Default $true
    }

    if ($InitChangelog) {
        if ($Added.Count -eq 0) {
            $Added = Read-ListInput -Prompt 'Enter Added items, separated by commas. Leave empty to use TBD.'
        }
        if ($Fixed.Count -eq 0) {
            $Fixed = Read-ListInput -Prompt 'Enter Fixed items, separated by commas. Leave empty to use TBD.'
        }
        if ($Changed.Count -eq 0) {
            $Changed = Read-ListInput -Prompt 'Enter Changed items, separated by commas. Leave empty to use TBD.'
        }
    }

    if (-not $PSBoundParameters.ContainsKey('OpenWeb')) {
        $OpenWeb = Read-YesNo -Prompt 'Open GitHub Releases / Actions pages after success?' -Default $true
    }
}

$normalizedVersion = $Version.Trim()
if ($normalizedVersion -notmatch '^\d+(?:\.\d+)*$') {
    Fail "Invalid version format: [$Version]. Use values like 1.1, 1.2, or 2.0.3."
}

$tag = if ($Trial) { "v$normalizedVersion-trial" } else { "v$normalizedVersion" }
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$currentBranch = Get-CurrentBranchName

Write-Host "Repository root: $repoRoot"
Write-Host "Current branch : $currentBranch"
Write-Host "Target tag     : $tag"

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

Write-Step "Checking whether CHANGELOG.md contains the target version section"
$changelogCreated = $false
if (-not (Test-ChangelogEntry -ChangelogPath $changelogPath -TagName $tag)) {
    if (-not $InitChangelog) {
        Fail "CHANGELOG.md does not contain section [$tag]. Use -InitChangelog to create it automatically."
    }

    Write-Step "Initializing CHANGELOG.md version section"
    $changelogCreated = Add-ChangelogEntry `
        -ChangelogPath $changelogPath `
        -TagName $tag `
        -DisplayVersion $normalizedVersion `
        -IsTrial ([bool]$Trial) `
        -AddedItems $Added `
        -FixedItems $Fixed `
        -ChangedItems $Changed

    if (-not $changelogCreated) {
        Fail "Failed to initialize CHANGELOG.md."
    }

    $defaultCommitMessage = if ($CommitMessage) { $CommitMessage } else { "init changelog for $tag" }

    Write-Step "Committing generated CHANGELOG.md"
    Invoke-Git -Arguments @('add', 'CHANGELOG.md') | Out-Null
    Invoke-Git -Arguments @('commit', '-m', $defaultCommitMessage) | Out-Null
}

Write-Step "Checking whether local or remote tag already exists"
if (Test-LocalTagExists -TagName $tag) {
    Fail "Local tag [$tag] already exists. Delete it first: git tag -d $tag"
}
if (Test-RemoteTagExists -TagName $tag) {
    Fail "Remote tag [$tag] already exists. Delete it first: git push origin :refs/tags/$tag"
}

if (-not $SkipPushMain) {
    Write-Step "Pushing current branch to origin/$currentBranch"

    if (-not (Test-RemoteBranchExists -BranchName $currentBranch)) {
        Invoke-Git -Arguments @('push', '-u', 'origin', $currentBranch) | Out-Null
    }
    else {
        Invoke-Git -Arguments @('push', 'origin', $currentBranch) | Out-Null
    }
}
else {
    Write-Step "Skipping branch push"
}

Write-Step "Creating local tag"
Invoke-Git -Arguments @('tag', $tag) | Out-Null

try {
    Write-Step "Pushing tag to remote"
    Invoke-Git -Arguments @('push', 'origin', $tag) | Out-Null
}
catch {
    Write-Warning "Failed to push tag. Rolling back local tag [$tag]..."
    Invoke-Git -Arguments @('tag', '-d', $tag) -AllowFailure | Out-Null
    throw
}

Write-Step "Verifying remote tag"
if (-not (Test-RemoteTagExists -TagName $tag)) {
    Fail "Remote tag [$tag] was not detected. You can verify manually with: git ls-remote --tags origin $tag"
}

$remoteUrl = (Invoke-Git -Arguments @('remote', 'get-url', 'origin')).Output
$repoWebUrl = Convert-RemoteToWebUrl -RemoteUrl $remoteUrl
$releasesWebUrl = if ($repoWebUrl) { "$repoWebUrl/releases" } else { $null }
$actionsWebUrl = if ($repoWebUrl) { "$repoWebUrl/actions" } else { $null }

Write-Host "`nRelease completed." -ForegroundColor Green
Write-Host "Tag pushed      : $tag" -ForegroundColor Green
Write-Host "GitHub Actions should now start the release workflow." -ForegroundColor Green
Write-Host "Verify command  : git ls-remote --tags origin $tag"
Write-Host "Remote URL      : $remoteUrl"
if ($releasesWebUrl) {
    Write-Host "Releases page   : $releasesWebUrl"
}
if ($actionsWebUrl) {
    Write-Host "Actions page    : $actionsWebUrl"
}

if ($OpenWeb -and $repoWebUrl) {
    Write-Step "Opening GitHub Releases / Actions pages"
    Open-Url -Url $releasesWebUrl
    Open-Url -Url $actionsWebUrl
}

Write-Host ""
Write-Host "Examples:"
Write-Host "  ./scripts/release.ps1 -Version 1.2"
Write-Host "  ./scripts/release.ps1 -Version 1.2 -Trial"
Write-Host "  ./scripts/release.ps1 -Version 1.2 -InitChangelog -Added 'Feature A' -Changed 'UI text update'"
Write-Host "  ./scripts/release.ps1 -OpenWeb"
Write-Host "  ./scripts/release.ps1   # interactive mode"
