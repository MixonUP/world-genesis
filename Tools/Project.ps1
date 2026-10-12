[CmdletBinding()]
param(
    [ValidateSet('Doctor', 'Validate', 'Import', 'Prepare', 'Build', 'Open', 'EditMode', 'PlayMode')]
    [string]$Action = 'Doctor',
    [switch]$DryRun,
    [string]$VerificationProject = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolchainPath = Join-Path $PSScriptRoot 'toolchain.json'
$toolchain = Get-Content -LiteralPath $toolchainPath -Raw -Encoding UTF8 | ConvertFrom-Json
$unityProjectPath = Join-Path $projectRoot $toolchain.projectRelativePath
if ($VerificationProject) {
    $candidate = [IO.Path]::GetFullPath($VerificationProject)
    $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Builds')) + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path (Join-Path $candidate 'Assets'))) { throw 'Verification project must be an existing snapshot inside Builds.' }
    if ($Action -in @('Open','Doctor','Validate')) { throw 'VerificationProject is for Unity batch checks only.' }
    $unityProjectPath = $candidate
}
$evidenceRoot = Join-Path $projectRoot 'Evidence'
$moscowNow = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(3))

if ($Action -eq 'Doctor') {
    [pscustomobject]@{
        Project = $unityProjectPath
        UnityVersion = $toolchain.unityVersion
        UnityExecutable = $toolchain.unityExecutable
        EditorExists = Test-Path -LiteralPath $toolchain.unityExecutable
        WindowsModuleExists = Test-Path -LiteralPath $toolchain.windowsModule
        EditorLaunchVerified = $toolchain.editorLaunchVerified
        ProjectImportVerified = $toolchain.projectImportVerified
        PreviousLaunchExit = $toolchain.lastObservedLaunchExitHex
        Note = 'Presence of files is not proof of successful editor launch.'
    } | Format-List
    return
}

if ($Action -eq 'Validate') {
    $checks = [Collections.Generic.List[object]]::new()
    function Add-Check([string]$Id, [bool]$Passed, [string]$Detail) {
        $checks.Add([pscustomobject]@{ id = $Id; passed = $Passed; detail = $Detail })
    }
    $required = @(
        'AGENTS.md', 'README.md', '.gitignore', '.editorconfig', '.gitattributes',
        'Docs/00_INDEX.md', 'Docs/STATE.md', 'Docs/MASTER_BRIEF.md', 'Docs/KICKOFF.md',
        'Docs/REFERENCES.md', 'Docs/FEATURE_MAP.md', 'Docs/ARCHITECTURE.md',
        'Docs/WORKFLOW.md', 'Docs/ROADMAP.md', 'Docs/ACCEPTANCE.md',
        'Docs/ART_AND_AUDIO.md', 'Docs/DECISIONS.md', 'Tools/README.md',
        'Tools/toolchain.json', 'Evidence/README.md', 'SourceAssets/manifest.csv',
        'WorldGenesis/ProjectSettings/ProjectVersion.txt', 'WorldGenesis/Packages/manifest.json'
    )
    foreach ($relative in $required) {
        Add-Check "file:$relative" (Test-Path -LiteralPath (Join-Path $projectRoot $relative) -PathType Leaf) $relative
    }
    $versionFile = Join-Path $unityProjectPath 'ProjectSettings/ProjectVersion.txt'
    $version = Get-Content -LiteralPath $versionFile -Raw
    $versionMatch = [regex]::Match($version, '(?m)^m_EditorVersion:\s*(\S+)')
    Add-Check 'unity-version' ($versionMatch.Success -and $versionMatch.Groups[1].Value -eq $toolchain.unityVersion) $version.Trim()

    $assemblies = @{}
    $assemblyFiles = @(Get-ChildItem -LiteralPath (Join-Path $unityProjectPath 'Assets/_Project') -Recurse -Filter '*.asmdef')
    foreach ($file in $assemblyFiles) {
        try {
            $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            Add-Check "assembly-unique:$($definition.name)" (-not $assemblies.ContainsKey($definition.name)) $file.FullName
            $assemblies[$definition.name] = $definition
        } catch { Add-Check "assembly-json:$($file.Name)" $false $_.Exception.Message }
    }
    Add-Check 'assembly-count' ($assemblies.Count -eq 5) "Found $($assemblies.Count), expected 5"
    $externalAssemblies = @('UnityEngine.UI')
    foreach ($name in $assemblies.Keys) {
        foreach ($reference in $assemblies[$name].references) {
            Add-Check "reference:$name->$reference" ($assemblies.ContainsKey($reference) -or $reference -in $externalAssemblies) $reference
        }
    }
    $visited = @{}
    $visiting = @{}
    function Visit-Assembly([string]$Name) {
        if ($visiting.ContainsKey($Name)) { throw "Cyclic assembly dependency: $Name" }
        if ($visited.ContainsKey($Name)) { return }
        $visiting[$Name] = $true
        foreach ($reference in $assemblies[$Name].references) {
            if ($assemblies.ContainsKey($reference)) { Visit-Assembly $reference }
        }
        $visiting.Remove($Name)
        $visited[$Name] = $true
    }
    try {
        foreach ($name in $assemblies.Keys) { Visit-Assembly $name }
        Add-Check 'assembly-acyclic' $true 'No dependency cycle'
    } catch { Add-Check 'assembly-acyclic' $false $_.Exception.Message }
    if ($assemblies.ContainsKey('WorldGenesis.Simulation')) {
        Add-Check 'simulation-engine-independent' ($assemblies['WorldGenesis.Simulation'].noEngineReferences -eq $true) 'noEngineReferences'
    }
    if ($assemblies.ContainsKey('WorldGenesis.Editor')) {
        Add-Check 'editor-only' ('Editor' -in $assemblies['WorldGenesis.Editor'].includePlatforms) 'Editor platform'
    }
    foreach ($name in @('WorldGenesis.EditModeTests', 'WorldGenesis.PlayModeTests')) {
        if ($assemblies.ContainsKey($name)) {
            Add-Check "test-assembly:$name" ('TestAssemblies' -in $assemblies[$name].optionalUnityReferences) 'Test assembly flag'
        }
    }
    $manifestPath = Join-Path $unityProjectPath 'Packages/manifest.json'
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Add-Check 'package-manifest-json' $true 'Parsed'
        foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
            $packageJson = Join-Path (Join-Path $toolchain.builtInPackages $dependency.Name) 'package.json'
            if (Test-Path -LiteralPath $packageJson) {
                $localPackage = Get-Content -LiteralPath $packageJson -Raw | ConvertFrom-Json
                Add-Check "local-package:$($dependency.Name)" ($localPackage.version -eq $dependency.Value) "Requested $($dependency.Value); local $($localPackage.version)"
            } else {
                Add-Check "local-package:$($dependency.Name)" $false 'Not found in pinned editor'
            }
        }
    } catch { Add-Check 'package-manifest-json' $false $_.Exception.Message }
    $uiAssembly = Join-Path $toolchain.builtInPackages 'com.unity.ugui/Runtime/UGUI/UnityEngine.UI.asmdef'
    Add-Check 'ugui-reference-observed' (Test-Path -LiteralPath $uiAssembly) $uiAssembly

    $markdown = @((Get-Item -LiteralPath (Join-Path $projectRoot 'README.md')))
    $markdown += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Docs') -Recurse -Filter '*.md')
    $markdown += @(Get-Item -LiteralPath (Join-Path $projectRoot 'Tools/README.md'))
    foreach ($file in $markdown) {
        $body = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        foreach ($match in [regex]::Matches($body, '(?<!!)\[[^\]]+\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value.Trim('<', '>')
            if ($target -match '^(https?://|#)') { continue }
            $target = [Uri]::UnescapeDataString(($target -split '#')[0])
            if ([IO.Path]::IsPathRooted($target)) { $resolved = $target }
            else { $resolved = Join-Path $file.DirectoryName $target }
            Add-Check "link:$($file.Name):$target" (Test-Path -LiteralPath $resolved) $resolved
        }
    }

    $failed = @($checks | Where-Object { -not $_.passed })
    $report = [ordered]@{
        schemaVersion = 1
        timestamp = $moscowNow.ToString('o')
        scope = 'Project organization and file scaffold only'
        passed = ($failed.Count -eq 0)
        checkCount = $checks.Count
        failures = $failed
        checks = $checks
        unityImport = 'not-run'
        gameplayTests = 'not-run'
        playerBuild = 'not-run'
        userVisualAcceptance = 'not-reviewed'
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'project-setup-report.json') -Encoding UTF8
    Write-Output "Organization checks: $($checks.Count); failures: $($failed.Count). Gameplay: not-run."
    if ($failed.Count -gt 0) { $failed | Format-Table; throw 'Project organization validation failed.' }
    return
}

if (-not (Test-Path -LiteralPath $toolchain.unityExecutable -PathType Leaf)) { throw 'Pinned Unity editor not found.' }
if (-not (Test-Path -LiteralPath $unityProjectPath -PathType Container)) { throw 'Project directory not found.' }
if (Test-Path -LiteralPath (Join-Path $unityProjectPath 'Temp/UnityLockfile')) {
    if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Unity is running and this project has a lock. Inspect its owner before another editor launch.' }
    $staleLock = [IO.Path]::GetFullPath((Join-Path $unityProjectPath 'Temp/UnityLockfile'))
    if (-not $staleLock.StartsWith($unityProjectPath + [IO.Path]::DirectorySeparatorChar)) { throw 'Unexpected lock path.' }
    # Compilation failures can leave the file behind. Require both absence of Unity
    # processes and an exclusive open before removing this one known temporary file.
    $lockProbe = [IO.File]::Open($staleLock, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $lockProbe.Dispose()
    Remove-Item -LiteralPath $staleLock
    Write-Output 'Removed stale Unity lock: no Unity process and exclusive access verified.'
}

function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { throw 'An argument contains an unsupported quote.' }
    return '"' + $Value + '"'
}

$runName = $moscowNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + $Action.ToLowerInvariant()
$runPath = Join-Path $evidenceRoot $runName
$logPath = Join-Path $runPath 'unity.log'
$resultPath = Join-Path $runPath 'tests.xml'
$unityArguments = @('-projectPath', (Quote-Argument $unityProjectPath), '-logFile', (Quote-Argument $logPath))
if ($Action -ne 'Open') { $unityArguments += '-batchmode' }
if ($Action -eq 'Import') { $unityArguments += @('-nographics', '-quit') }
if ($Action -eq 'Prepare') { $unityArguments += @('-nographics', '-quit', '-executeMethod', 'WorldGenesis.Editor.ProjectSetup.Prepare') }
if ($Action -eq 'Build') { $unityArguments += @('-nographics', '-quit', '-executeMethod', 'WorldGenesis.Editor.ProjectSetup.BuildWindows', '--artifact-root', (Quote-Argument $projectRoot)) }
if ($Action -in @('EditMode', 'PlayMode')) {
    $unityArguments += @('-runTests', '-testPlatform', $Action, '-testResults', (Quote-Argument $resultPath))
}
if ($DryRun) {
    Write-Output ('Executable: ' + $toolchain.unityExecutable)
    Write-Output ('Arguments: ' + ($unityArguments -join ' '))
    Write-Output 'Dry run only; Unity was not started.'
    return
}

New-Item -ItemType Directory -Path $runPath -Force | Out-Null
if ($Action -eq 'Open') {
    # Explicit Open is an interactive user command, not a background helper.
    $editorProcess = Start-Process -FilePath $toolchain.unityExecutable -ArgumentList $unityArguments -PassThru
    Write-Output "Editor requested. PID: $($editorProcess.Id). Verify UI and log; launch is not import verification."
    return
}
$editorProcess = Start-Process -FilePath $toolchain.unityExecutable -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
Write-Output "Unity $Action started. PID: $($editorProcess.Id). Log: $logPath"
$editorProcess.WaitForExit()
$editorExitCode = $editorProcess.ExitCode
if ($editorExitCode -ne 0) { throw "Unity exited with $editorExitCode. Inspect $runPath; a missing log means startup was not verified." }
if (-not (Test-Path -LiteralPath $logPath)) { throw 'Unity log missing; cannot verify import/test run.' }
$log = Get-Content -LiteralPath $logPath -Raw
if ($log -match '(?m)error CS\d+|Scripts have compiler errors|Aborting batchmode due to failure') {
    throw "Unity reported compilation/batch errors. See $logPath"
}
if ($Action -in @('EditMode', 'PlayMode')) {
    if (-not (Test-Path -LiteralPath $resultPath)) { throw 'NUnit results missing; tests are not verified.' }
    [xml]$testResults = Get-Content -LiteralPath $resultPath -Raw
    $testRun = $testResults.SelectSingleNode('/test-run')
    if ($null -eq $testRun) { throw 'NUnit test-run element missing.' }
    $total = [int]$testRun.GetAttribute('total')
    $failedTests = [int]$testRun.GetAttribute('failed')
    $overall = $testRun.GetAttribute('result')
    if ($total -le 0 -or $failedTests -gt 0 -or $overall -ne 'Passed') {
        throw "Tests did not pass: total=$total failed=$failedTests result=$overall"
    }
    Write-Output "Tests passed: $total. Results: $resultPath"
} elseif ($Action -eq 'Build') {
    $buildReceiptPath = Join-Path $evidenceRoot 'build-report.json'
    if (-not (Test-Path -LiteralPath $buildReceiptPath)) { throw 'Build report missing.' }
    $buildReceipt = Get-Content -LiteralPath $buildReceiptPath -Raw | ConvertFrom-Json
    if ($buildReceipt.result -ne 'Succeeded' -or $buildReceipt.errors -ne 0 -or $log -notmatch 'WORLD_GENESIS_BUILD_SUCCEEDED') { throw 'Windows build was not confirmed.' }
    Write-Output "Windows build succeeded: $($buildReceipt.output); errors=$($buildReceipt.errors); warnings=$($buildReceipt.warnings). Player startup remains a separate check."
} elseif ($Action -eq 'Prepare') {
    if ($log -notmatch 'WORLD_GENESIS_PREPARED') { throw 'Project preparation marker missing.' }
    Write-Output 'Unity compilation and scene preparation succeeded. Player startup remains a separate check.'
} else {
    Write-Output "Batch import completed without detected compiler errors. This is not gameplay verification. Log: $logPath"
}
