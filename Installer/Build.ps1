param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Split-Path $PSScriptRoot
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Split-Path $source}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$staging=Join-Path $env:TEMP ('OpenPad-Build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
 dotnet publish (Join-Path $source 'OpenPad.csproj') -c Release -o (Join-Path $staging 'app')
 if($LASTEXITCODE) { throw 'Publish failed' }
 foreach($name in @('README.md','README.en.md','README.uk.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md','CONTRIBUTING.md')) { Copy-Item (Join-Path $source $name) (Join-Path $staging ('app\'+$name)) }
 Copy-Item (Join-Path $source 'licenses') (Join-Path $staging 'app\licenses') -Recurse
 Copy-Item (Join-Path $source 'docs') (Join-Path $staging 'app\docs') -Recurse
 Compress-Archive -Path (Join-Path $staging 'app\*') -DestinationPath (Join-Path $staging 'OpenPad.zip')
 Copy-Item (Join-Path $staging 'OpenPad.zip') (Join-Path $output 'OpenPad-update-win-x64.zip') -Force
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 & $compiler /nologo /target:winexe "/out:$output\OpenPad-Setup.exe" "/win32icon:$source\OpenPad.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/resource:$staging\OpenPad.zip,OpenPad.zip" "/resource:$PSScriptRoot\Install.ps1,Install.ps1" "$PSScriptRoot\Setup.cs"
 if($LASTEXITCODE) { throw 'Installer build failed' }
} finally {
 if([IO.Path]::GetFullPath($staging).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
