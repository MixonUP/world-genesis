param([ValidateSet('EditMode','Build')][string]$Action,[Parameter(Mandatory=$true)][string]$Revision)
$ErrorActionPreference='Stop'
$labRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$labProject = Join-Path $labRoot 'Project'
$labEvidence = Join-Path $labRoot ('Evidence/'+$Revision)
New-Item -ItemType Directory -Force -Path $labEvidence | Out-Null
$labUnity = 'C:/Program Files/Unity/Hub/Editor/6000.0.45f1/Editor/Unity.exe'
$labArgs = @('-batchmode','-nographics','-projectPath',('"'+$labProject+'"'),'-logFile',('"'+(Join-Path $labEvidence ($Action+'.log'))+'"'))
if ($Action -eq 'EditMode') { $labArgs += @('-runTests','-testPlatform','EditMode','-testResults',('"'+(Join-Path $labEvidence 'editmode-results.xml')+'"')) }
else { $labArgs += @('-quit','-executeMethod','DeliveryLab.EditorTools.BuildEntry.Build','-labBuildPath',('"'+(Join-Path $labRoot ('Builds/'+$Revision+'/InventoryLab.exe'))+'"')) }
$labStarted = [DateTime]::UtcNow
$labProcess = Start-Process -FilePath $labUnity -ArgumentList $labArgs -WindowStyle Hidden -PassThru
$labReceipt = [ordered]@{action=$Action;revision=$Revision;startedUtc=$labStarted.ToString('O');processId=$labProcess.Id;finishedUtc=$null;elapsedSeconds=$null;exitCode=$null;activeHumanHours=$null;externalApiCash=0;paidGpuCash=0}
$labReceiptPath=Join-Path $labEvidence ($Action+'-run.json')
$labReceipt | ConvertTo-Json | Set-Content -LiteralPath $labReceiptPath -Encoding utf8
$labProcess.WaitForExit()
$labReceipt.finishedUtc=[DateTime]::UtcNow.ToString('O');$labReceipt.elapsedSeconds=([DateTime]::UtcNow-$labStarted).TotalSeconds;$labReceipt.exitCode=$labProcess.ExitCode
$labReceipt | ConvertTo-Json | Set-Content -LiteralPath $labReceiptPath -Encoding utf8
$labReceipt | ConvertTo-Json
if ($labProcess.ExitCode -ne 0) { exit 1 }
