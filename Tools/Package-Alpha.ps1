[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+-alpha$')][string]$Version='0.2.0-alpha', [string]$BuildFolder='Windows-0.2.0')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build=[IO.Path]::GetFullPath((Join-Path $root ('Builds/'+$BuildFolder)))
if(-not $build.StartsWith((Join-Path $root 'Builds')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Build folder must be inside Builds.' }
$archive=Join-Path $root ('Builds/WorldGenesis-v'+$Version+'-win64.zip')
foreach ($required in @('WorldGenesis.exe','UnityPlayer.dll','WorldGenesis_Data\Managed\WorldGenesis.Runtime.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $build $required))) { throw "Missing build component: $required" }
}
Copy-Item -LiteralPath (Join-Path $root 'Docs\Development\PLAY_GUIDE.md') -Destination (Join-Path $build 'PLAY_GUIDE-RU.md')
$report=if($Version -eq '0.1.0-alpha') {'ALPHA_REPORT.md'} elseif($Version -eq '0.2.0-alpha') {'CITIZENS_REPORT.md'} elseif($Version -eq '0.3.0-alpha') {'RESOURCES_REPORT.md'} elseif($Version -eq '0.4.0-alpha') {'SOCIETY_REPORT.md'} elseif($Version -eq '0.5.1-alpha') {'SUBSISTENCE_REPORT.md'} elseif($Version -eq '0.6.0-alpha') {'ORIGINAL_CITIZENS_REPORT.md'} else {'TRANSPORT_REPORT.md'}
if ($Version -eq '0.6.1-alpha') { $report = 'FISHING_RIVERS_REPORT.md' }
if ($Version -eq '0.7.0-alpha') { $report = 'PRODUCTION_REPORT.md' }
if ($Version -eq '0.8.0-alpha') { $report = 'MARITIME_REPORT.md' }
if ($Version -eq '0.9.0-alpha') { $report = 'SPEED_FAUNA_REPORT.md' }
Copy-Item -LiteralPath (Join-Path $root ('Docs/Development/'+$report)) -Destination (Join-Path $build $report)
if ($Version -eq '0.9.0-alpha') {
    Copy-Item -LiteralPath (Join-Path $root 'SourceAssets/Music/PROVENANCE.md') -Destination (Join-Path $build 'BACKGROUND-MUSIC.md')
}
if ($Version -in @('0.6.0-alpha','0.6.1-alpha','0.7.0-alpha','0.8.0-alpha','0.9.0-alpha')) {
    Copy-Item -LiteralPath (Join-Path $root 'SourceAssets/OriginalCitizens/PROVENANCE.md') -Destination (Join-Path $build 'ORIGINAL-CITIZENS-ART.md')
}
if ($Version -ne '0.1.0-alpha') {
    Copy-Item -LiteralPath (Join-Path $root 'SourceAssets/ThirdParty/TinySwordsCC0/PROVENANCE.md') -Destination (Join-Path $build 'THIRD-PARTY-ART.md')
}
$files=@(Get-ChildItem -LiteralPath $build -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($build.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
if (Test-Path -LiteralPath $archive) { throw 'Archive already exists; preserve the prior release and choose a new version.' }
$writer=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer,(Join-Path $build $file.path),$file.path,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $writer.Dispose() }
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
    if ($zip.Entries.Count -ne $files.Count) { throw 'ZIP entry count differs from build.' }
    foreach ($file in $files) {
        $entry=$zip.GetEntry($file.path)
        if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw "ZIP entry mismatch: $($file.path)" }
        $stream=$entry.Open(); $algorithm=[Security.Cryptography.SHA256]::Create()
        try { $hash=([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-','') }
        finally { $stream.Dispose(); $algorithm.Dispose() }
        if ($hash -ne $file.sha256) { throw "ZIP content mismatch: $($file.path)" }
    }
}
finally { $zip.Dispose() }
$archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[ordered]@{utc=[DateTime]::UtcNow.ToString('o');version=$Version;archive=[IO.Path]::GetFileName($archive);archiveBytes=(Get-Item -LiteralPath $archive).Length;archiveSHA256=$archiveHash;verification='Every ZIP file read back and SHA256 compared with native build.';files=$files} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root ('Evidence/Development/package-'+$Version+'.json')) -Encoding UTF8
Set-Content -LiteralPath ($archive+'.sha256') -Value ($archiveHash+'  '+[IO.Path]::GetFileName($archive)) -Encoding ASCII
Write-Output "Verified complete Windows archive: $archive"
