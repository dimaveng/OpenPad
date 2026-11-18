param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Split-Path $PSScriptRoot
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Split-Path $source}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$staging=Join-Path ([IO.Path]::GetTempPath()) ('OpenPad-Portable-Build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
 $app=Join-Path $staging 'app'
 dotnet publish (Join-Path $source 'OpenPad.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:RestoreLockedMode=true -o $app
 if($LASTEXITCODE) {throw 'Portable publish failed'}
 foreach($name in @('README.md','README.en.md','README.uk.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md','CONTRIBUTING.md')) {Copy-Item (Join-Path $source $name) (Join-Path $app $name)}
 Copy-Item (Join-Path $source 'licenses') (Join-Path $app 'licenses') -Recurse
 $packages=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'}
 $config=Get-Content (Join-Path $app 'OpenPad.runtimeconfig.json') -Raw | ConvertFrom-Json
 $runtimeVersion=($config.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
 if(!$runtimeVersion){throw 'Cannot determine bundled runtime version'}
 $core=Join-Path $packages "microsoft.netcore.app.runtime.win-x64/$runtimeVersion"
 $desktop=Join-Path $packages "microsoft.windowsdesktop.app.runtime.win-x64/$runtimeVersion"
 Copy-Item (Join-Path $core 'LICENSE.TXT') (Join-Path $app 'licenses/dotnet-runtime-LICENSE.txt')
 Copy-Item (Join-Path $core 'THIRD-PARTY-NOTICES.TXT') (Join-Path $app 'licenses/dotnet-runtime-NOTICES.txt')
 Copy-Item (Join-Path $desktop 'LICENSE') (Join-Path $app 'licenses/dotnet-desktop-LICENSE.txt')
 Copy-Item (Join-Path $source 'docs') (Join-Path $app 'docs') -Recurse
 Copy-Item (Join-Path $PSScriptRoot 'README.txt') (Join-Path $app 'PORTABLE-README.txt')
 [IO.File]::WriteAllText((Join-Path $app 'portable.marker'),'OpenPad portable win-x64')
 [IO.File]::WriteAllText((Join-Path $app 'VB-CABLE.url'),"[InternetShortcut]`r`nURL=https://vb-audio.com/Cable/`r`n")
 foreach($required in @('OpenPad.exe','coreclr.dll','hostfxr.dll','PresentationFramework.dll','portable.marker')) {if(!(Test-Path (Join-Path $app $required))){throw "Incomplete portable package: $required"}}
 if(Test-Path (Join-Path $app 'Data')) {throw 'Portable payload must not include user Data'}
 $zip=Join-Path $output 'OpenPad-portable-win-x64.zip'
 $temporary=Join-Path $staging 'portable.zip'
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($app,$temporary,[IO.Compression.CompressionLevel]::Optimal,$false)
 Move-Item -LiteralPath $temporary -Destination $zip -Force
 Write-Output "Portable ZIP: $zip"
} finally {
 $absolute=[IO.Path]::GetFullPath($staging)
 if(!$absolute.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected staging path'}
 Remove-Item -LiteralPath $absolute -Recurse -Force
}
