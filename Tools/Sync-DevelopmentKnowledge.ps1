[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Entry)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$entryPath = [IO.Path]::GetFullPath((Join-Path $root $Entry))
if (-not $entryPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar)) { throw 'Entry must belong to this project.' }
$body = [IO.File]::ReadAllText($entryPath).Trim()
$heading = ($body -split "`n")[0].Trim()
$vault = 'E:\Krea_tren_AI\Knowledge'
$dateMatch = [regex]::Match($heading, '^##\s+(\d{4}-\d{2}-\d{2})\b')
if (-not $dateMatch.Success) { throw 'Entry heading must begin with ## YYYY-MM-DD, in Europe/Moscow.' }
$journalDate = [DateTime]::ParseExact($dateMatch.Groups[1].Value, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$journal = Join-Path $vault ('07_Journal\' + $journalDate.ToString('yyyy-MM-dd') + '.md')
$projectNote = Join-Path $vault '01_Project\World_Genesis.md'
if (-not (Test-Path -LiteralPath $projectNote)) { throw 'Established World Genesis project card missing.' }
if (-not (Test-Path -LiteralPath $journal)) { [IO.File]::WriteAllText($journal, '# ' + $journalDate.ToString('yyyy-MM-dd') + "`n", [Text.UTF8Encoding]::new($false)) }
$paths = @($projectNote, $journal)
foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Expected established note missing: $path" }
    $existing = [IO.File]::ReadAllText($path)
    if (-not $existing.Contains($heading)) { [IO.File]::AppendAllText($path, "`n`n" + $body + "`n", [Text.UTF8Encoding]::new($false)) }
    if (-not [IO.File]::ReadAllText($path).Contains($body)) { throw "Readback mismatch: $path" }
}
$receipt = Join-Path $root ('Evidence\Development\' + [IO.Path]::GetFileNameWithoutExtension($entryPath) + '-obsidian.json')
New-Item -ItemType Directory -Path (Split-Path $receipt) -Force | Out-Null
[ordered]@{ utc=[DateTime]::UtcNow.ToString('o'); entry=$Entry; status='written-and-read-back'; notes=$paths } | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
Write-Output 'Project and journal appended and verified by readback.'
