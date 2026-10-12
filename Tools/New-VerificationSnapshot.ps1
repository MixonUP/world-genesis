[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9-]+$')][string]$Name)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=Join-Path $root 'WorldGenesis'
$target=Join-Path $root ('Builds/Verification/'+$Name+'/WorldGenesis')
if(Test-Path -LiteralPath $target) { throw 'Use a fresh snapshot name; an earlier verification is preserved.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
$files=[Collections.Generic.List[object]]::new()
foreach($folder in @('Assets','Packages','ProjectSettings')) {
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $source $folder) -Recurse -File) {
        $relative=$file.FullName.Substring($source.Length+1)
        $destination=Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw "Snapshot changed during copy: $relative" }
        $files.Add([ordered]@{path=$relative.Replace('\','/');sha256=$hash})
    }
}
[ordered]@{utc=[DateTime]::UtcNow.ToString('o');name=$Name;purpose='Batch verification of a source snapshot while the interactive project remains open';source='WorldGenesis';snapshot=$target.Substring($root.Length+1).Replace('\','/');files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root ('Evidence/Development/snapshot-'+$Name+'.json')) -Encoding UTF8
Write-Output $target
