param([string]$Destination, [string]$ProgramsDirectory, [switch]$SkipCable, [switch]$NoLaunch)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
if(!$Destination) { $Destination=Join-Path $env:LOCALAPPDATA 'Programs\OpenPad' }
if(!$ProgramsDirectory) { $ProgramsDirectory=[Environment]::GetFolderPath('Programs') }
try {
 if(![Environment]::Is64BitOperatingSystem) { throw 'OpenPad requires 64-bit Windows.' }
 Write-Output 'Checking .NET Desktop Runtime 10...'
 $dotnet=Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
 $runtimeOk=(Test-Path $dotnet) -and ((& $dotnet --list-runtimes) -match 'Microsoft.WindowsDesktop.App 10\.')
 if(!$runtimeOk) {
  $releases=Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
  $release=$releases.releases | Where-Object { $_.'release-version' -eq $releases.'latest-release' } | Select-Object -First 1
  $asset=$release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } | Select-Object -First 1
  if(!$asset -or ([uri]$asset.url).Host -notin @('builds.dotnet.microsoft.com','download.visualstudio.microsoft.com')) { throw 'No official runtime installer found.' }
  $runtime=Join-Path $PSScriptRoot 'dotnet-desktop.exe'
  Write-Output 'Downloading Microsoft .NET Desktop Runtime...'
  Invoke-WebRequest $asset.url -OutFile $runtime -UseBasicParsing
  if((Get-FileHash $runtime -Algorithm SHA512).Hash -ne $asset.hash) { throw 'Runtime checksum mismatch.' }
  Write-Output 'Installing .NET. Confirm the Windows administrator prompt.'
  $p=Start-Process -FilePath $runtime -ArgumentList '/install /quiet /norestart' -Verb RunAs -Wait -PassThru
  if($p.ExitCode -notin @(0,3010)) { throw "Runtime installation failed: $($p.ExitCode)" }
  if(!((& $dotnet --list-runtimes) -match 'Microsoft.WindowsDesktop.App 10\.')) { throw 'Runtime not detected after installation.' }
 }
 Write-Output 'Installing OpenPad...'
 $running=Get-Process OpenPad -ErrorAction SilentlyContinue
 if($running) { throw 'Close OpenPad before installing an update.' }
 $Destination=[IO.Path]::GetFullPath($Destination)
 $stage=Join-Path $PSScriptRoot ('stage-'+[guid]::NewGuid().ToString('N'))
 $backup=Join-Path $PSScriptRoot ('backup-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Force -Path $Destination,$stage,$backup | Out-Null
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $archive=[IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot 'OpenPad.zip'))
 try {
  foreach($entry in $archive.Entries) {
   $resolved=[IO.Path]::GetFullPath((Join-Path $stage $entry.FullName))
   if(!$resolved.StartsWith($stage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $entry.FullName -match '^(?i:Data)[/\\]') { throw 'Unsafe installer payload path.' }
  }
 } finally { $archive.Dispose() }
 Expand-Archive -LiteralPath (Join-Path $PSScriptRoot 'OpenPad.zip') -DestinationPath $stage
 if(!(Test-Path (Join-Path $stage 'OpenPad.exe'))) { throw 'OpenPad.exe is missing from the payload.' }
 $written=New-Object 'System.Collections.Generic.List[string]'
 try {
  foreach($file in (Get-ChildItem -LiteralPath $stage -File -Recurse)) {
   $relative=$file.FullName.Substring($stage.Length+1)
   $target=Join-Path $Destination $relative
   if(Test-Path -LiteralPath $target) {
    $saved=Join-Path $backup $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $saved) | Out-Null
    Copy-Item -LiteralPath $target -Destination $saved
   }
   New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
   $written.Add($relative)
   Copy-Item -LiteralPath $file.FullName -Destination $target -Force
  }
 } catch {
  foreach($relative in $written) {
   $target=Join-Path $Destination $relative; $saved=Join-Path $backup $relative
   if(Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target -Force }
   elseif(Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
  }
  throw
 }
 New-Item -ItemType Directory -Force -Path $ProgramsDirectory | Out-Null
 $shell=New-Object -ComObject WScript.Shell
 $shortcut=$shell.CreateShortcut((Join-Path $ProgramsDirectory 'OpenPad.lnk'))
 $shortcut.TargetPath=Join-Path $Destination 'OpenPad.exe'
 $shortcut.WorkingDirectory=$Destination
 $shortcut.IconLocation=(Join-Path $Destination 'OpenPad.exe')+',0'
 $shortcut.Description='OpenPad — sounds and microphone for Discord'
 $shortcut.Save()
 Write-Output 'OpenPad added to Start menu.'
 if(!$SkipCable) {
  $cable=Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'VB-Audio Virtual Cable|CABLE Input|CABLE Output' }
  if(!$cable) {
   Write-Output 'Downloading VB-CABLE from vb-audio.com...'
   $zip=Join-Path $PSScriptRoot 'VB-CABLE.zip'
   Invoke-WebRequest 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip' -OutFile $zip -UseBasicParsing
   if((Get-FileHash $zip -Algorithm SHA256).Hash -ne 'BC44E8974191A5A2427D6C979E2DAB08DBB09925F3F435DFA2F0BA3E45B6EB90') { throw 'VB-CABLE checksum mismatch.' }
   $driver=Join-Path $PSScriptRoot 'VB-CABLE'
   Expand-Archive -LiteralPath $zip -DestinationPath $driver -Force
   Write-Output 'In the VB-CABLE window click Install Driver. Confirm the administrator prompt.'
   Start-Process -FilePath (Join-Path $driver 'VBCABLE_Setup_x64.exe') -Verb RunAs -Wait
   $cable=Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'VB-Audio Virtual Cable|CABLE Input|CABLE Output' }
   if(!$cable) { Write-Output 'OpenPad is installed. VB-CABLE is not detected yet: finish driver installation and restart Windows.' }
   else { Write-Output 'VB-CABLE detected. Restart Windows if requested by its installer.' }
  } else { Write-Output 'VB-CABLE is already installed.' }
 }
 Write-Output 'Installation complete.'
 if(!$NoLaunch) { Start-Process -FilePath (Join-Path $Destination 'OpenPad.exe') -WorkingDirectory $Destination }
 exit 0
} catch { Write-Output ('ERROR: '+$_.Exception.Message); exit 1 }
