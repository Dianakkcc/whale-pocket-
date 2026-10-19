param([string]$Node='node.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root '编译.ps1')
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$results=Join-Path $root ('test-results\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $results 'data') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'bin') | Copy-Item -Destination $results -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\demo-balances.json') -Destination (Join-Path $results 'data\balances.json')
[IO.File]::WriteAllText((Join-Path $results 'config.json'),'{"refreshMinutes":10,"accounts":[]}',[Text.UTF8Encoding]::new($false))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler '/nologo' '/platform:x86' '/utf8output' "/out:$results\VerifyVisibility.exe" '/reference:System.dll' '/reference:System.Core.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Web.Extensions.dll' (Join-Path $PSScriptRoot 'VerifyVisibility.cs')
if($LASTEXITCODE -ne 0){throw 'Visibility test build failed'}
& (Join-Path $results 'VerifyVisibility.exe')
if($LASTEXITCODE -ne 0){throw 'Visibility test failed'}
$settings=Start-Process -FilePath (Join-Path $results 'WhaleSettings.exe') -ArgumentList '--verify','--settings-preview','--preview',('"'+(Join-Path $results 'settings.png')+'"') -PassThru -WindowStyle Hidden
if(-not $settings.WaitForExit(20000)){throw 'Settings verification timed out'}
if($settings.ExitCode -ne 0){throw 'Settings verification failed'}
$report=Get-Content -LiteralPath (Join-Path $results 'data\ui-verification.json') -Raw | ConvertFrom-Json
if(-not $report.allHidden -or -not $report.visibilityPersisted -or $report.dynamicAccounts -ne 3){throw 'Settings toggle verification failed'}
Write-Output 'PASS: WPF settings toggles and persistence (offline fixtures)'
& $Node (Join-Path $PSScriptRoot 'accounts.test.mjs')
if($LASTEXITCODE -ne 0){throw 'Account adapter tests failed'}
& $Node (Join-Path $PSScriptRoot 'recharge.test.mjs')
if($LASTEXITCODE -ne 0){throw 'Recharge tests failed'}
Write-Output "Verification OK: $results"
