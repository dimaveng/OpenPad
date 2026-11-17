param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(!$OutputDirectory){$OutputDirectory=Join-Path ([IO.Path]::GetTempPath()) ('OpenPad-Installer-Test-'+[guid]::NewGuid().ToString('N'))}
$root=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$source=Join-Path $PSScriptRoot '../Installer/Install.ps1'
$log=[Collections.Generic.List[string]]::new()
function Run-Case($name,$entries,$expected,$locked=$false) {
 $case=Join-Path $root $name
 New-Item -ItemType Directory -Force -Path $case | Out-Null
 Copy-Item -LiteralPath $source -Destination (Join-Path $case 'Install.ps1')
 $destination=Join-Path $case 'app'; $programs=Join-Path $case 'programs'
 New-Item -ItemType Directory -Force -Path (Join-Path $destination 'Data') | Out-Null
 $library=Join-Path $destination 'Data/library.json'; [IO.File]::WriteAllText($library,'{"Clips":[],"Language":"uk","Voice":true}')
 [IO.File]::WriteAllText((Join-Path $destination 'OpenPad.exe'),'old exe')
 [IO.File]::WriteAllText((Join-Path $destination 'z-locked.txt'),'old locked')
 $hash=(Get-FileHash $library).Hash
 $zip=[IO.Compression.ZipFile]::Open((Join-Path $case 'OpenPad.zip'),'Create')
 try { foreach($entry in $entries.GetEnumerator()) { $e=$zip.CreateEntry($entry.Key); $w=[IO.StreamWriter]::new($e.Open()); try {$w.Write($entry.Value)} finally {$w.Dispose()} } } finally {$zip.Dispose()}
 $lock=$null
 try {
  if($locked) {$lock=[IO.File]::Open((Join-Path $destination 'z-locked.txt'),'Open','ReadWrite','None')}
  $output=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $case 'Install.ps1') -Destination $destination -ProgramsDirectory $programs -SkipCable -NoLaunch 2>&1
  $code=$LASTEXITCODE
 } finally {if($lock){$lock.Dispose()}}
 $output | Set-Content (Join-Path $case 'install-log.txt')
 if($code -ne $expected){throw "$name returned $code (expected $expected): $output"}
 if((Get-FileHash $library).Hash -ne $hash){throw "$name changed the library"}
 if($expected -eq 0) {
  if([IO.File]::ReadAllText((Join-Path $destination 'OpenPad.exe')) -ne 'new exe'){throw "$name did not update exe"}
  if(!(Test-Path (Join-Path $programs 'OpenPad.lnk'))){throw "$name did not create shortcut"}
  $output=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $case 'Install.ps1') -Destination $destination -ProgramsDirectory $programs -SkipCable -NoLaunch 2>&1
  if($LASTEXITCODE -ne 0 -or (Get-FileHash $library).Hash -ne $hash){throw 'Reinstall failed or changed Data'}
 } else {
  if([IO.File]::ReadAllText((Join-Path $destination 'OpenPad.exe')) -ne 'old exe'){throw "$name did not preserve/restore exe"}
 }
 if(Test-Path (Join-Path $case 'escape.txt')){throw 'Traversal escaped staging'}
 $log.Add("PASS: $name; user library unchanged.")
}
Run-Case 'update-and-reinstall' ([ordered]@{'OpenPad.exe'='new exe';'a.txt'='new'}) 0
Run-Case 'missing-executable' ([ordered]@{'a.txt'='new'}) 1
Run-Case 'traversal-rejected' ([ordered]@{'OpenPad.exe'='new exe';'../escape.txt'='bad'}) 1
Run-Case 'data-payload-rejected' ([ordered]@{'OpenPad.exe'='new exe';'Data/library.json'='bad'}) 1
Run-Case 'locked-file-rollback' ([ordered]@{'OpenPad.exe'='new exe';'z-locked.txt'='new locked'}) 1 $true
$log | Set-Content (Join-Path $root 'results.txt')
$log
