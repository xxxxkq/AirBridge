param([string]$Bundle=$PSScriptRoot)
# Deliberately read-only: this is NOT an installer and never opens audio devices.
$ErrorActionPreference='Stop'
$Bundle=(Resolve-Path -LiteralPath $Bundle).Path
$manifest=Get-Content -LiteralPath (Join-Path $Bundle 'bundle.json') -Raw|ConvertFrom-Json
$checks=@();$bad=@()
foreach($file in $manifest.Files){$path=[IO.Path]::GetFullPath((Join-Path $Bundle $file.Path));if(-not $path.StartsWith($Bundle+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escapes bundle'};if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.SHA256){$bad+=$file.Path}}
foreach($c in $manifest.Components){$driver=Split-Path (Join-Path $Bundle $c.Inf);foreach($f in Get-ChildItem -LiteralPath $driver -File|Where-Object {$_.Extension -in '.sys','.cat'}){$sig=Get-AuthenticodeSignature -LiteralPath $f.FullName;$checks+=@{Component=$c.Id;File=$f.Name;Authenticode=$sig.Status.ToString();Signer=if($sig.SignerCertificate){$sig.SignerCertificate.Thumbprint}else{$null}}}}
$report=@{ReadOnly=$true;CheckedUtc=[DateTime]::UtcNow.ToString('o');HashFailures=$bad;Signatures=$checks;ReadyToInstall=$false;BlockingReasons=@('Installation approval is not granted','Microsoft/kernel-policy signature and current machine security checks are not validated','Hardware test needs a fresh elevated security check and explicit approval','Physical driver loading/cleanup testing not run','Real output recovery writer and broker not validated; default-output takeover prohibited')}
$report|ConvertTo-Json -Depth 6|Set-Content (Join-Path $Bundle 'preflight.json') -Encoding UTF8
if($bad.Count){throw 'Bundle integrity failed'}
Write-Host 'Integrity OK. Installation BLOCKED; see preflight.json. No settings changed.'
