[CmdletBinding()]
param([ValidatePattern('^[a-z0-9-]+$')][string]$Name='citizens', [int]$Seconds=6)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$player=Join-Path $root 'Builds/Windows-0.2.0/WorldGenesis.exe'
$stamp=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3)).ToString('yyyyMMdd-HHmmss')
$frames=Join-Path $root ('Builds/CaptureFrames/'+$stamp+'-'+$Name)
$output=Join-Path $root ('Evidence/Captures/'+$stamp+'-'+$Name+'.mp4')
New-Item -ItemType Directory -Path $frames -Force | Out-Null
function Quote([string]$v) { if($v.Contains('"')) { throw 'Invalid quote' }; return '"'+$v+'"' }
$arguments=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',(Quote (Join-Path $frames 'player.log')),'--focus-settlement','--zoom','7','--inspect-citizen','--capture-motion',(Quote $frames),'--motion-seconds',"$Seconds",'--quit-after-motion')
# Deliberately visible interactive Player: hidden Windows backbuffers can be black.
$process=Start-Process -FilePath $player -ArgumentList $arguments -PassThru
Write-Output "Native motion Player PID=$($process.Id)"
$process.WaitForExit()
if($process.ExitCode -ne 0) { throw 'Motion Player failed.' }
$receiptPath=Join-Path $frames 'motion.json'
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if($receipt.frames.Count -lt 10) { throw 'Insufficient native frames.' }
$lines=[Collections.Generic.List[string]]::new()
for($i=0;$i -lt $receipt.frames.Count;$i++) {
    $frame=Join-Path $frames ('frame-'+$receipt.frames[$i].index.ToString('D4')+'.png')
    if(-not(Test-Path -LiteralPath $frame)) { throw "Missing frame $i" }
    $escaped=$frame.Replace('\','/').Replace("'","'\''")
    $lines.Add("file '$escaped'")
    $duration=if($i+1 -lt $receipt.frames.Count) {$receipt.frames[$i+1].wallSeconds-$receipt.frames[$i].wallSeconds} else {.1}
    $lines.Add('duration '+$duration.ToString('0.000000',[Globalization.CultureInfo]::InvariantCulture))
}
$concat=Join-Path $frames 'frames.txt'
[IO.File]::WriteAllLines($concat,$lines,[Text.UTF8Encoding]::new($false))
& 'C:/FFmpeg_exe/ffmpeg.exe' -hide_banner -loglevel error -f concat -safe 0 -i $concat -r 30 -c:v libx264 -crf 20 -pix_fmt yuv420p -movflags +faststart $output
if($LASTEXITCODE -ne 0) { throw 'Motion encoding failed.' }
Copy-Item -LiteralPath $receiptPath -Destination ($output+'.json')
[ordered]@{utc=[DateTime]::UtcNow.ToString('o');nativeFrames=$receipt.frames.Count;assistedCamera=$true;assistedInspection=$true;imageGeneration=$false;runtimeAssemblySHA256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $player) 'WorldGenesis_Data/Managed/WorldGenesis.Runtime.dll') -Algorithm SHA256).Hash;simulationAssemblySHA256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $player) 'WorldGenesis_Data/Managed/WorldGenesis.Simulation.dll') -Algorithm SHA256).Hash;clipSHA256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath ($output+'.build.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $frames 'frame-0000.png') -Destination ($output+'.png')
$log=Get-Content -LiteralPath (Join-Path $frames 'player.log') -Raw
if($log -match 'Exception|Shader.*not supported') { throw 'Inspect motion log: runtime error.' }
Write-Output "Native motion, poster and timestamps recorded: $output"
