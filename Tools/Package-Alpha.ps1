[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build=Join-Path $root 'Builds\Windows'
$archive=Join-Path $root 'Builds\WorldGenesis-v0.1.0-alpha-win64.zip'
foreach ($required in @('WorldGenesis.exe','UnityPlayer.dll','WorldGenesis_Data\Managed\WorldGenesis.Runtime.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $build $required))) { throw "Missing build component: $required" }
}
Copy-Item -LiteralPath (Join-Path $root 'Docs\Development\PLAY_GUIDE.md') -Destination (Join-Path $build 'PLAY_GUIDE-RU.md')
Copy-Item -LiteralPath (Join-Path $root 'Docs\Development\ALPHA_REPORT.md') -Destination (Join-Path $build 'ALPHA_REPORT.md')
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
[ordered]@{utc=[DateTime]::UtcNow.ToString('o');version='0.1.0-alpha';archive=[IO.Path]::GetFileName($archive);archiveBytes=(Get-Item -LiteralPath $archive).Length;archiveSHA256=$archiveHash;verification='Every ZIP file read back and SHA256 compared with native build.';files=$files} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'Evidence\Development\alpha-package.json') -Encoding UTF8
Set-Content -LiteralPath ($archive+'.sha256') -Value ($archiveHash+'  '+[IO.Path]::GetFileName($archive)) -Encoding ASCII
Write-Output "Verified complete Windows archive: $archive"
