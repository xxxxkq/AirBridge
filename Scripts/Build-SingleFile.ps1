param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,[string]$Version='3.0.65',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$runtime=(Resolve-Path -LiteralPath $RuntimeDirectory).Path
$info=Get-Content -LiteralPath (Join-Path $runtime 'build-info.json') -Raw | ConvertFrom-Json
if($info.Configuration -ne 'Release' -or $info.Version -ne $Version){throw 'Single-file payload must be a matching Release build'}
if((Get-Item -LiteralPath (Join-Path $runtime 'AirBridge.exe')).VersionInfo.FileVersion -ne ($Version+'.0')){throw 'Runtime assembly version differs from package version'}
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $repo 'artifacts/single-file'}
$null=New-Item -ItemType Directory -Path $output -Force
$zip=Join-Path $repo 'build/single-file/payload.zip'
$null=New-Item -ItemType Directory -Path (Split-Path $zip) -Force
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# An allowlist prevents test binaries, logs and user data leaking from a reused build folder.
$files=@('AirBridge.exe','AirBridge.exe.config','AirBridge.Core.dll','AirBridge.Routing.dll','AirBridge.UI.dll','AirBridge.Capture.dll','NAudio.Core.dll','NAudio.Wasapi.dll','UI/Shell.xaml','Assets/AirBridge.ico','build-info.json','README.txt','THIRD-PARTY-NOTICES.txt','LICENSE','RTP-L16-example.sdp')
$stream=[IO.File]::Open($zip,[IO.FileMode]::Create)
try{$archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
 try{foreach($file in $files){$path=Join-Path $runtime $file;if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing payload file: $file"};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$path,$file,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
}finally{$stream.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $output 'AirBridge.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ('/win32manifest:'+(Join-Path $repo 'app.manifest')) ('/win32icon:'+(Join-Path $repo 'Assets/AirBridge.ico')) ('/resource:'+$zip+',AirBridge.Payload.zip') ('/out:'+$exe) (Join-Path $repo 'Packaging/SingleFileHost.cs') (Join-Path $repo 'Shared/DataLocation.cs') (Join-Path $repo 'Desktop/AssemblyInfo.cs')
if($LASTEXITCODE -ne 0){throw 'Single-file host compilation failed'}
Write-Output "Single-file Release: $exe"
