param([Parameter(Mandatory=$true)][string]$ZipPath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$zip=(Resolve-Path -LiteralPath $ZipPath).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path ([IO.Path]::GetTempPath()) ('OpenPad-Portable-Verify-'+[guid]::NewGuid().ToString('N'))}
$root=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$first=Join-Path $root 'first folder'; $moved=Join-Path $root 'перенесена папка'
if((Test-Path -LiteralPath $first) -or (Test-Path -LiteralPath $moved)){throw 'Use a fresh verification directory'}
$logs=[Collections.Generic.List[string]]::new()
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip,$first)
foreach($name in @('OpenPad.exe','coreclr.dll','hostfxr.dll','portable.marker','PORTABLE-README.txt','VB-CABLE.url')){if(!(Test-Path (Join-Path $first $name))){throw "Missing $name"}}
if(Test-Path (Join-Path $first 'Data')){throw 'Shipped ZIP contains user Data'}
$saved=@{}
foreach($name in @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_MULTILEVEL_LOOKUP','SOUNDPAD_DATA','SOUNDPAD_CAPTURE')){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}
function Run-App($folder,$arguments) {
 $p=Start-Process (Join-Path $folder 'OpenPad.exe') -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(60000)){$p.Kill();throw "Portable app timed out: $arguments"}
 if($p.ExitCode -ne 0){throw "Portable app failed: $arguments ($($p.ExitCode))"}
}
try {
 $env:DOTNET_ROOT=Join-Path $root 'no-installed-dotnet'; $env:DOTNET_ROOT_X64=$env:DOTNET_ROOT; $env:DOTNET_MULTILEVEL_LOOKUP='0'
 Run-App $first '--portable-test'
 $logs.Add('PASS: self-contained executable uses its bundled runtime; audio/library relocation unit checks passed.')
 $data=Join-Path $first 'Data'; New-Item -ItemType Directory -Force -Path (Join-Path $data 'Sounds') | Out-Null
 $writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $data 'Sounds/fixture.wav')))
 try {
  $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int]8856);$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$writer.Write([int]16);$writer.Write([short]1);$writer.Write([short]1);$writer.Write([int]44100);$writer.Write([int]88200);$writer.Write([short]2);$writer.Write([short]16);$writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]8820);$writer.Write([byte[]]::new(8820))
 } finally {$writer.Dispose()}
 [IO.File]::WriteAllText((Join-Path $data 'library.json'),'{"Clips":[{"Path":"Sounds/fixture.wav","Name":"Portable fixture","Gain":210,"Bass":12,"Favorite":true}],"Language":"uk","Master":325,"CheckUpdates":false}')
 $env:SOUNDPAD_DATA=$data; $env:SOUNDPAD_CAPTURE=Join-Path $root 'before-move.png'
 Run-App $first '--capture-portable'
 if(!(Test-Path $env:SOUNDPAD_CAPTURE)){throw 'First portable window did not render'}
 # Both resolved move paths must remain inside this disposable verification root.
 foreach($path in @($first,$moved)){if(![IO.Path]::GetFullPath($path).StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Move escaped verification root'}}
 [IO.Directory]::Move($first,$moved)
 $data=Join-Path $moved 'Data';$env:SOUNDPAD_DATA=$data;$env:SOUNDPAD_CAPTURE=Join-Path $root 'after-move.png'
 Run-App $moved '--capture-portable'
 $library=Join-Path $data 'library.json';$prefs=Get-Content -LiteralPath $library -Raw | ConvertFrom-Json
 if($prefs.Clips.Count -ne 1 -or $prefs.Clips[0].Gain -ne 210 -or $prefs.Language -ne 'uk' -or $prefs.CheckUpdates -or [IO.Path]::IsPathRooted($prefs.Clips[0].Path) -or !(Test-Path -LiteralPath (Join-Path $data $prefs.Clips[0].Path))){throw 'Moved library lost audio/settings or stored an absolute internal path'}
 $logs.Add('PASS: actual WPF window starts before/after moving a folder with spaces and Unicode; settings and relative audio path survive.')
 $before=(Get-FileHash -LiteralPath $library).Hash
 $sleeper=Start-Process powershell.exe -ArgumentList '-NoProfile -Command "Start-Sleep -Seconds 3"' -WindowStyle Hidden -PassThru
 $helper=Join-Path $root 'Apply-Update.ps1'; Copy-Item (Join-Path $PSScriptRoot '../Installer/Install.ps1') $helper
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -Destination $moved -Payload $zip -WaitForProcessId $sleeper.Id -Update -Portable -SkipCable -NoLaunch | Set-Content (Join-Path $root 'update-log.txt')
 if($LASTEXITCODE -ne 0 -or !$sleeper.HasExited){throw 'Portable update failed or did not wait for the parent'}
 if((Get-FileHash -LiteralPath $library).Hash -ne $before){throw 'Portable update changed Data'}
 Run-App $moved '--portable-test'
 $logs.Add('PASS: real portable ZIP applies after waiting for parent exit; Data hash unchanged; updated self-contained executable starts.')
 $logs | Set-Content (Join-Path $root 'results.txt');$logs
} finally {foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
exit 0
