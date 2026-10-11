[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$issues = [Collections.Generic.List[string]]::new()
$count = 0
$totalBytes = 0L
$textExtensions = @('.cs', '.ps1', '.py', '.md', '.json', '.yml', '.yaml', '.txt', '.csv', '.xml', '.uxml', '.uss', '.shader', '.cginc', '.asmdef', '.gitignore', '.gitattributes', '.editorconfig')

Push-Location -LiteralPath $projectRoot
try {
    # Inspect tracked and eligible untracked files; never access credential stores.
    $paths = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed' }
    foreach ($relative in $paths) {
        $full = Join-Path $projectRoot $relative
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $file = Get-Item -LiteralPath $full
        $count++
        $totalBytes += $file.Length
        if ($file.Length -ge 50MB) { $issues.Add("Review large file: $relative") }
        if ($relative -match '(^|/)(Library|Temp|obj|bin|UserSettings|\.local|\.vs|\.idea)/') { $issues.Add("Generated or local directory: $relative") }
        if ($relative -match '(?i)(^|/)(\.env([.]|$)|hosts\.yml$|credentials([.]|$))|\.(pfx|p12|pem|key|ulf)$') { $issues.Add("Sensitive file type: $relative") }
        if ($relative -match '(?i)^Evidence/.*\.(log|dmp)$') { $issues.Add("Raw diagnostic data: $relative") }
        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        $baseName = [IO.Path]::GetFileName($relative)
        if (($textExtensions -contains $extension -or $textExtensions -contains $baseName) -and $file.Length -lt 5MB) {
            $body = [IO.File]::ReadAllText($full)
            # Report only file names. Never echo a matching credential value.
            if ($body -match '(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-proj-[A-Za-z0-9_-]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)') {
                $issues.Add("Possible credential in text: $relative")
            }
        }
    }
    [pscustomobject]@{
        inspectedFiles = $count
        totalMiB = [math]::Round($totalBytes / 1MB, 2)
        status = if ($issues.Count -eq 0) { 'PASS' } else { 'REVIEW' }
        issues = @($issues)
        scope = 'Tracked and eligible untracked working-copy files; index, commit and remote unchanged.'
    } | ConvertTo-Json -Depth 4
    if ($issues.Count -gt 0) { exit 1 }
}
finally { Pop-Location }
