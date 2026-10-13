[CmdletBinding()]
param([ValidatePattern('^[a-z0-9-]+$')][string]$Name='citizens', [int]$Seconds=6,
    [string]$PlayerRelativePath='Builds/Windows-0.2.0/WorldGenesis.exe',
    [int]$Delay=3, [switch]$InspectResourceWorker, [switch]$LogDelivery, [string]$WorldFile='', [switch]$InspectCaravan,
    [int]$InspectX=-1, [int]$InspectY=-1, [int]$Zoom=7, [switch]$WeatherTab, [int]$InspectShip=0)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$player=[IO.Path]::GetFullPath((Join-Path $root $PlayerRelativePath))
if(-not $player.StartsWith((Join-Path $root 'Builds')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Motion player must be inside project Builds.' }
if(-not(Test-Path -LiteralPath $player)) { throw 'Build the player before capturing.' }
$stamp=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3)).ToString('yyyyMMdd-HHmmss')
$frames=Join-Path $root ('Builds/CaptureFrames/'+$stamp+'-'+$Name)
$output=Join-Path $root ('Evidence/Captures/'+$stamp+'-'+$Name+'.mp4')
New-Item -ItemType Directory -Path $frames -Force | Out-Null
function Quote([string]$v) { if($v.Contains('"')) { throw 'Invalid quote' }; return '"'+$v+'"' }
$arguments=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',(Quote (Join-Path $frames 'player.log')),'--focus-settlement','--zoom',"$Zoom",'--capture-motion',(Quote $frames),'--motion-seconds',"$Seconds",'--quit-after-motion')
if ($InspectX -ge 0 -and $InspectY -ge 0) { $arguments+=@('--inspect-cell',"$InspectX","$InspectY") }
elseif ($InspectX -ge 0 -or $InspectY -ge 0) { throw 'Cell inspection needs both coordinates.' }
else { $arguments+='--inspect-citizen' }
$arguments+=@('--motion-delay',"$Delay")
if($WeatherTab) { $arguments+='--weather-tab' }
if($InspectResourceWorker) { $arguments+='--inspect-resource-worker' }
if($LogDelivery) { $arguments+='--observe-log-delivery' }
if($InspectCaravan) { $arguments+='--inspect-caravan' }
if($InspectShip -gt 0) { $arguments+=@('--inspect-ship',"$InspectShip") }
if($WorldFile) {
    $captureWorld=[IO.Path]::GetFullPath((Join-Path $root $WorldFile))
    if(-not $captureWorld.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $captureWorld)) { throw 'Capture world must exist inside this project.' }
    $arguments+=@('--load-capture-world',(Quote $captureWorld))
}
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
