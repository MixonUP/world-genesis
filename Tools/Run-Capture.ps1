[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9-]+$')][string]$Name='overview',
    [int]$Width=1920, [int]$Height=1080, [int]$Seconds=8,
    [ValidateSet(1,2,5,10)][int]$Speed=1,
    [switch]$FocusSettlement, [int]$Zoom=18,
    [ValidateSet('', 'Fire', 'Rain', 'Lightning', 'Meteor', 'Heal', 'Bless', 'Plague')][string]$Power='',
    [int]$ProfileSeconds=0, [int]$StressPopulation=0,
    [string]$PlayerRelativePath='Builds/Windows-0.2.0/WorldGenesis.exe',
    [switch]$InspectCitizen, [switch]$InspectResourceWorker,
    [ValidateSet('', 'Woman', 'Man', 'Child')][string]$Demographic='',
    [ValidateSet('', 'Tree', 'Stone', 'IronOre', 'CopperOre', 'GoldOre', 'SilverOre', 'Coal', 'Clay', 'Sand', 'Limestone', 'Tin')][string]$Resource='',
    [string]$WorldFile='', [switch]$InspectCaravan
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$player=[IO.Path]::GetFullPath((Join-Path $root $PlayerRelativePath))
if (-not $player.StartsWith((Join-Path $root 'Builds')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Capture Player must be inside project Builds.' }
if (-not (Test-Path -LiteralPath $player)) { throw 'Build the Windows player first.' }
if ($Width -lt 800 -or $Height -lt 600 -or $Seconds -lt 1) { throw 'Invalid capture dimensions or duration.' }
$stamp=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3)).ToString('yyyyMMdd-HHmmss')
$directory=Join-Path $root 'Evidence\Captures'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$base=Join-Path $directory ($stamp+'-'+$Name)
$image=$base+'.png'; $log=$base+'.log'; $profile=$base+'-profile.json'
function Quote([string]$value) { if ($value.Contains('"')) { throw 'Invalid quote in path' }; return '"'+$value+'"' }
$arguments=@('-screen-width',"$Width",'-screen-height',"$Height",'-screen-fullscreen','0','-logFile',(Quote $log),'--capture',(Quote $image),'--capture-delay',"$Seconds",'--quit-after-capture')
$arguments+=@('--simulation-speed',"$Speed")
if ($FocusSettlement) { $arguments+=@('--focus-settlement','--zoom',"$Zoom") }
if ($InspectCitizen) { $arguments+='--inspect-citizen' }
if ($InspectCaravan) { $arguments+='--inspect-caravan' }
if ($InspectResourceWorker) { $arguments+='--inspect-resource-worker' }
if ($Demographic) { $arguments+=@('--inspect-demographic',$Demographic) }
if ($Resource) { $arguments+=@('--inspect-resource',$Resource) }
if ($WorldFile) {
    $captureSource=[IO.Path]::GetFullPath((Join-Path $root $WorldFile))
    if (-not $captureSource.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $captureSource)) { throw 'Capture world must be an existing file inside this project.' }
    $arguments+=@('--load-capture-world',(Quote $captureSource))
}
if ($Power) { $arguments+=@('--demo-power',$Power) }
if ($ProfileSeconds -gt 0) {
    if ($Seconds -le $ProfileSeconds+4) { throw 'Capture delay must exceed profile duration + 4 seconds.' }
    $arguments+=@('--profile-report',(Quote $profile),'--profile-seconds',"$ProfileSeconds",'--stress-population',"$StressPopulation")
}
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'WorldGenesis\Assets\_Project') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object {
    [ordered]@{file=$_.FullName.Substring($root.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
# This command intentionally opens the interactive game window for native visual
# review. Hidden/minimized Windows players can produce a black backbuffer capture.
$process=Start-Process -FilePath $player -ArgumentList $arguments -PassThru
Write-Output "Native Player PID=$($process.Id); capture=$image"
$process.WaitForExit()
$exit=$process.ExitCode
if ($exit -ne 0) { throw "Player exited $exit; inspect $log" }
if (-not (Test-Path -LiteralPath $image)) { throw 'Native image missing.' }
$body=Get-Content -LiteralPath $log -Raw
if ($body -match 'NullReferenceException|IndexOutOfRangeException|ArgumentException|InvalidOperationException|Shader.*not supported') { throw 'Player log contains a runtime error.' }
if ($ProfileSeconds -gt 0 -and -not (Test-Path -LiteralPath $profile)) { throw 'Native profile report missing.' }
[ordered]@{
    utc=[DateTime]::UtcNow.ToString('o');playerExitCode=$exit;image=[IO.Path]::GetFileName($image);
    playerSHA256=(Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash;
    imageSHA256=(Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash;
    scope='Native Unity Windows Player. Visual content still requires image inspection.';
    automatedCamera=[bool]$FocusSettlement;automatedPower=$Power;stressPopulation=$StressPopulation;workspaceSourcesAtLaunch=$sources;
    loadedWorld=$WorldFile;loadedWorldSHA256=$(if($WorldFile){(Get-FileHash -LiteralPath $captureSource -Algorithm SHA256).Hash}else{''});
    runtimeAssemblySHA256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $player) 'WorldGenesis_Data/Managed/WorldGenesis.Runtime.dll') -Algorithm SHA256).Hash;
    simulationAssemblySHA256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $player) 'WorldGenesis_Data/Managed/WorldGenesis.Simulation.dll') -Algorithm SHA256).Hash
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($base+'-run.json') -Encoding UTF8
Write-Output "Player exited successfully; native image and source receipt recorded: $image"
