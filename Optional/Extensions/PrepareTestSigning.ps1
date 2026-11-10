param([Parameter(Mandatory=$true)][string]$Bundle)
# PowerShell 7; creates only project files. NEVER imports certificates or installs.
$ErrorActionPreference='Stop'
$Bundle=(Resolve-Path -LiteralPath $Bundle).Path
$private=Join-Path $PSScriptRoot 'private-signing'
New-Item -ItemType Directory -Path $private -Force|Out-Null
$acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'),[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))};Set-Acl -LiteralPath $private -AclObject $acl
$pfx=Join-Path $private 'ExtensionsTest.pfx';$secret=Join-Path $private 'password.dpapi'
if(!(Test-Path -LiteralPath $pfx)){
 $rsa=[Security.Cryptography.RSA]::Create(3072)
 try{$req=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=AirBridge Extensions Development Test',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $req.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true));$req.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
 $oids=[Security.Cryptography.OidCollection]::new();[void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'));$req.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids,$true))
 $cert=$req.CreateSelfSigned([DateTimeOffset]::Now.AddMinutes(-5),[DateTimeOffset]::Now.AddYears(1));$password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32));[IO.File]::WriteAllBytes($pfx,$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password));[IO.File]::WriteAllBytes($secret,[Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($password),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser));$cert.Dispose()}finally{$rsa.Dispose()}
}
$password=[Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($secret),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
$cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx,$password,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$packages=Join-Path $workspace '02_AirBridge运行版本\AirBridge2_49_1\VirtualAudio\packages'
$sign=Join-Path $packages 'microsoft.windows.sdk.cpp.10.0.26100.9457\c\bin\10.0.26100.0\x64\signtool.exe'
$inf2cat=Join-Path $packages 'Microsoft.Windows.WDK.x64.10.0.26100.6584\c\bin\10.0.26100.0\x86\Inf2Cat.exe'
$out=Join-Path $PSScriptRoot ('signed-candidates\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $out -Force|Out-Null
Copy-Item "$Bundle\*" $out -Recurse
[IO.File]::WriteAllBytes((Join-Path $out 'AirBridgeExtensionsTest.cer'),$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$manifest=Get-Content "$out\bundle.json" -Raw|ConvertFrom-Json
$verified=@()
foreach($c in $manifest.Components){$driver=Split-Path (Join-Path $out $c.Inf);$sys=Get-ChildItem $driver -Filter *.sys
 & $sign sign /f $pfx /p $password /fd SHA256 /ph $sys.FullName
 if($LASTEXITCODE -ne 0){throw 'SYS signing failed'}
 & $inf2cat "/driver:$driver" /os:10_NI_X64,10_GE_X64
 if($LASTEXITCODE -ne 0){throw 'CAT generation failed'}
 $cat=Get-ChildItem $driver -Filter *.cat
 & $sign sign /f $pfx /p $password /fd SHA256 $cat.FullName
 if($LASTEXITCODE -ne 0){throw 'CAT signing failed'}
 foreach($file in @($sys,$cat)){
  $data=[IO.File]::ReadAllBytes($file.FullName)
  if($file.Extension -eq '.sys'){$pe=[BitConverter]::ToInt32($data,60);$optional=$pe+24;if([BitConverter]::ToUInt16($data,$optional) -ne 0x20b){throw 'Expected PE32+'};$offset=[BitConverter]::ToInt32($data,$optional+112+32);$length=[BitConverter]::ToInt32($data,$offset)-8;$payload=[byte[]]::new($length);[Array]::Copy($data,$offset+8,$payload,0,$length)}else{$payload=$data}
  $cms=[Security.Cryptography.Pkcs.SignedCms]::new();$cms.Decode($payload);$cms.CheckSignature($true)
  if($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $cert.Thumbprint){throw 'Unexpected signer'}
  $log=Join-Path $out ($c.Id+'-'+$file.Name+'-kernel-policy.txt')
  & $sign verify /kp /v $file.FullName 2>&1 | Out-File -LiteralPath $log -Encoding UTF8
  $verified+=@{Component=$c.Id;File=$file.Name;CmsSignatureValid=$true;KernelPolicyExitCode=$LASTEXITCODE}
 }
}
$manifest.Files=@(Get-ChildItem $out -Recurse -File|Where-Object {$_.Name -notin 'bundle.json','preflight.json','signing.json'}|ForEach-Object {@{Path=$_.FullName.Substring($out.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$manifest|ConvertTo-Json -Depth 7|Set-Content "$out\bundle.json" -Encoding UTF8
@{TestSigned=$true;TrustImported=$false;Installed=$false;Timestamped=$false;Thumbprint=$cert.Thumbprint;Expires=$cert.NotAfter.ToString('o');Verification=$verified}|ConvertTo-Json -Depth 5|Set-Content "$out\signing.json" -Encoding UTF8
$password=$null;$cert.Dispose()
& "$PSScriptRoot\Preflight.ps1" -Bundle $out
Set-Content "$PSScriptRoot\latest-signed.txt" $out -Encoding UTF8
Write-Host "Prepared test-signed files only: $out"
