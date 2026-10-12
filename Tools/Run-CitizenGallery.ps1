[CmdletBinding()]
param(
    [ValidateSet('families','wardrobe','actions')][string]$Mode='families',
    [ValidateSet('male','female')][string]$Sex='male',
    [ValidateRange(0,2)][int]$ActionPage=0,
    [ValidateRange(1,20)][int]$Seconds=6,
    [Parameter(Mandatory=$true)][string]$PlayerRelativePath,
    [string]$FFmpegPath='C:/FFmpeg_exe/ffmpeg.exe'
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$player=[IO.Path]::GetFullPath((Join-Path $root $PlayerRelativePath))
$buildRoot=(Join-Path $root 'Builds')+[IO.Path]::DirectorySeparatorChar
if(-not $player.StartsWith($buildRoot,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $player -PathType Leaf)) { throw 'Player must exist inside project Builds. Build current sources first.' }
if(-not(Test-Path -LiteralPath $FFmpegPath -PathType Leaf)) { throw 'FFmpeg encoder not found.' }
$stamp=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3)).ToString('yyyyMMdd-HHmmss-fff')
$name="citizen-gallery-$Mode-$Sex-p$ActionPage"
$frames=Join-Path $root "Builds/CaptureFrames/$stamp-$name"
$destination=Join-Path $root 'Evidence/Captures'
$output=Join-Path $destination "$stamp-$name.mp4"
New-Item -ItemType Directory -Path $frames -Force | Out-Null
New-Item -ItemType Directory -Path $destination -Force | Out-Null

function Quote-Argument([string]$Value) {
    if($Value.Contains('"') -or $Value.EndsWith('\')) { throw 'Unsupported quote or trailing slash in argument.' }
    return '"'+$Value+'"'
}
function Hash-File([string]$Path) {
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required provenance file is missing: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
$binaryHashes=[ordered]@{
    player=(Hash-File $player)
    runtime=(Hash-File (Join-Path (Split-Path $player) 'WorldGenesis_Data/Managed/WorldGenesis.Runtime.dll'))
    simulation=(Hash-File (Join-Path (Split-Path $player) 'WorldGenesis_Data/Managed/WorldGenesis.Simulation.dll'))
}
$sourceHashes=[ordered]@{}
$sourceFiles=@(
    'WorldGenesis/Assets/_Project/Scripts/Runtime/NativeCitizenGallery.cs',
    'WorldGenesis/Assets/_Project/Scripts/Runtime/Rendering/CitizenSprites.cs',
    'WorldGenesis/Assets/_Project/Scripts/Runtime/Rendering/OriginalCitizenAtlas.cs',
    'WorldGenesis/Assets/_Project/Scripts/Runtime/Rendering/CitizenWardrobe.cs',
    'WorldGenesis/Assets/_Project/Scripts/Runtime/Rendering/OriginalCitizenTools.cs',
    'Tools/Run-CitizenGallery.ps1'
)
foreach($relative in $sourceFiles) { $sourceHashes[$relative]=Hash-File (Join-Path $root $relative) }
foreach($race in @('Human','Elf','Dwarf','Orc')) {
    $relative="WorldGenesis/Assets/_Project/Art/Units/Resources/OriginalCitizens/$race.png"
    $sourceHashes[$relative]=Hash-File (Join-Path $root $relative)
}
$startedUtc=[DateTime]::UtcNow.ToString('o')
$arguments=@('-screen-width','1920','-screen-height','1080','-screen-fullscreen','0',
    '-logFile',(Quote-Argument (Join-Path $frames 'player.log')),
    '--citizen-gallery',$Mode,'--gallery-sex',$Sex,'--gallery-page',"$ActionPage",
    '--gallery-output',(Quote-Argument $frames),'--gallery-seconds',"$Seconds",'--quit-after-gallery')
# This is deliberately a visible native capture Player, not a background helper.
# The isolated overlay identifies prepared fixtures; no gameplay save is loaded or written.
$process=Start-Process -FilePath $player -ArgumentList $arguments -PassThru
Write-Output "Native citizen preview PID=$($process.Id); mode=$Mode; sex=$Sex; page=$ActionPage"
if(-not $process.WaitForExit(90000)) { throw "Gallery did not finish within 90 seconds; inspect Player PID $($process.Id). No process was killed." }
if($process.ExitCode -ne 0) { throw "Citizen gallery Player exited with code $($process.ExitCode). Inspect $frames/player.log" }
$receiptPath=Join-Path $frames 'gallery.json'
if(-not(Test-Path -LiteralPath $receiptPath)) { throw 'Gallery receipt is absent. The selected Player may predate NativeCitizenGallery.' }
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if($receipt.mode -ne $Mode -or $receipt.sex -ne $Sex -or [int]$receipt.page -ne $ActionPage -or $receipt.gameplay -ne $false) { throw 'Native gallery receipt does not match requested isolated fixture.' }
if($receipt.frames.Count -lt 8 -or $receipt.frames[-1].tick -le $receipt.frames[0].tick) { throw 'Insufficient animated native frames or non-advancing simulation clock.' }
$expectedCount=if($Mode -eq 'families'){64}elseif($Mode -eq 'wardrobe'){112}else{32}
if($receipt.subjects.Count -ne $expectedCount) { throw 'Unexpected gallery subject coverage.' }
$lines=[Collections.Generic.List[string]]::new()
$frameHashes=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt $receipt.frames.Count;$i++) {
    $sample=$receipt.frames[$i]
    $frame=Join-Path $frames ('frame-'+([int]$sample.index).ToString('D4')+'.png')
    if(-not(Test-Path -LiteralPath $frame) -or (Get-Item -LiteralPath $frame).Length -lt 100) { throw "Missing or empty native PNG $i" }
    if($sample.atlasFrames.Count -ne $expectedCount -or $sample.directions.Count -ne $expectedCount) { throw "Incomplete pose receipt $i" }
    $escaped=$frame.Replace('\','/').Replace("'","'\''")
    $lines.Add("file '$escaped'")
    $span=if($i+1 -lt $receipt.frames.Count){[double]$receipt.frames[$i+1].wallSeconds-[double]$sample.wallSeconds}else{.1}
    if($span -le 0 -or [double]::IsNaN($span) -or [double]::IsInfinity($span)) { throw 'Non-monotonic native sample timestamps.' }
    $lines.Add('duration '+$span.ToString('0.000000',[Globalization.CultureInfo]::InvariantCulture))
    $frameHashes.Add([ordered]@{index=[int]$sample.index;sha256=(Hash-File $frame)})
}
# Repeat the last file so the concat demuxer honors its final duration.
$lines.Add($lines[$lines.Count-2])
$concat=Join-Path $frames 'frames.txt'
[IO.File]::WriteAllLines($concat,$lines,[Text.UTF8Encoding]::new($false))
& $FFmpegPath -hide_banner -loglevel error -f concat -safe 0 -i $concat -r 30 -c:v libx264 -crf 18 -pix_fmt yuv420p -movflags +faststart $output
if($LASTEXITCODE -ne 0) { throw 'Native gallery encoding failed.' }
$poster=$output+'.png'
Copy-Item -LiteralPath (Join-Path $frames 'frame-0000.png') -Destination $poster
Copy-Item -LiteralPath $receiptPath -Destination ($output+'.json')
$changedSources=[Collections.Generic.List[string]]::new()
foreach($relative in $sourceHashes.Keys) { if((Hash-File (Join-Path $root $relative)) -ne $sourceHashes[$relative]) { $changedSources.Add($relative) } }
$log=Get-Content -LiteralPath (Join-Path $frames 'player.log') -Raw
$runtimeErrors=$log -match 'Exception|Shader.*not supported|Citizen gallery preparation failed'
[ordered]@{
    startedUtc=$startedUtc;finishedUtc=[DateTime]::UtcNow.ToString('o');timezone='Europe/Moscow'
    kind='isolated native Unity preview';gameplay=$false;imageGeneration=$false;artisticAcceptance='pending'
    player=$PlayerRelativePath;mode=$Mode;sex=$Sex;page=$ActionPage;frames=$receipt.frames.Count
    sourceHashes=$sourceHashes;binaryHashes=$binaryHashes;sourceToBinaryMatch='not established by this capture; use the integrator build snapshot receipt'
    sourceChangedDuringCapture=$changedSources.ToArray();sampledFrameHashes=$frameHashes.ToArray()
    runtimeErrors=$runtimeErrors;clipSHA256=(Hash-File $output);posterSHA256=(Hash-File $poster)
    encoding='Original native PNG samples at approximately 10 Hz; actual wall-time intervals retained, MP4 encoded at 30 fps without optical flow'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ($output+'.build.json') -Encoding UTF8
if($runtimeErrors) { throw 'Native gallery log contains a runtime error. Evidence retained, not accepted.' }
if($changedSources.Count -gt 0) { throw 'Workspace sources changed during capture; use recorded binary hashes and recapture from a frozen build.' }
Write-Output "Native preview PNG, MP4, pose timestamps and provenance: $output"
