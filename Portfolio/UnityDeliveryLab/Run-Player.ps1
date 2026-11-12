param(
 [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunName,
 [ValidateSet('Proof','Reload','Interactive')][string]$Scenario='Proof',
 [int]$Width=1280,[int]$Height=720
)
$ErrorActionPreference='Stop'
$labRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$labExe=Join-Path $labRoot ('Builds/'+$Revision+'/InventoryLab.exe')
if(-not(Test-Path -LiteralPath $labExe)){throw 'Requested lab build is missing'}
$labEvidence=Join-Path $labRoot ('Evidence/'+$Revision+'/'+$RunName)
$labReceiptPath=Join-Path $labEvidence 'player-run.json'
if(Test-Path -LiteralPath $labReceiptPath){throw 'Use a new RunName to preserve previous evidence'}
New-Item -ItemType Directory -Force -Path $labEvidence | Out-Null
$labSave=Join-Path $labRoot ('Evidence/'+$Revision+'/UserData')
$labArgs=@('-screen-width',$Width,'-screen-height',$Height,'-screen-fullscreen','0','-logFile',('"'+(Join-Path $labEvidence 'Player.log')+'"'),'-labSaveDir',('"'+$labSave+'"'),'-labProofDir',('"'+$labEvidence+'"'),'-labCaptureDir',('"'+$labEvidence+'"'))
if($Scenario -eq 'Proof'){$labArgs+='-labAutoProof'}
if($Scenario -eq 'Reload'){$labArgs+='-labVerifyReload'}
$labStarted=[DateTime]::UtcNow
$labProcess=Start-Process -FilePath $labExe -ArgumentList $labArgs -WorkingDirectory (Split-Path -Parent $labExe) -WindowStyle Normal -PassThru
$labReceipt=[ordered]@{scenario=$Scenario;assisted=($Scenario -ne 'Interactive');revision=$Revision;startedUtc=$labStarted.ToString('O');processId=$labProcess.Id;width=$Width;height=$Height;finishedUtc=$null;elapsedSeconds=$null;exitCode=$null;activeHumanHours=$null;externalApiCash=0;paidGpuCash=0;binarySha256=(Get-FileHash -LiteralPath $labExe -Algorithm SHA256).Hash}
$labReceipt|ConvertTo-Json|Set-Content -LiteralPath $labReceiptPath -Encoding utf8
if($Scenario -ne 'Interactive'){
 if(-not $labProcess.WaitForExit(120000)){$labProcess.CloseMainWindow()|Out-Null;if(-not $labProcess.WaitForExit(5000)){$labProcess.Kill()};$labReceipt['timeout']= $true}
 $labReceipt.finishedUtc=[DateTime]::UtcNow.ToString('O');$labReceipt.elapsedSeconds=([DateTime]::UtcNow-$labStarted).TotalSeconds;$labReceipt.exitCode=$labProcess.ExitCode
 $labReceipt|ConvertTo-Json|Set-Content -LiteralPath $labReceiptPath -Encoding utf8
}
$labReceipt|ConvertTo-Json
if($Scenario -ne 'Interactive' -and $labReceipt.exitCode -ne 0){exit 1}
