param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw '.NET Framework C# compiler was not found.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$shared=@('/nologo','/target:winexe','/platform:x86','/optimize+','/debug-','/utf8output')
& $compiler @shared "/out:$OutputDirectory\WhaleWidget.exe" '/reference:System.dll' '/reference:System.Core.dll' '/reference:System.Drawing.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Security.dll' "$PSScriptRoot\AccountPreferences.cs" "$PSScriptRoot\WhaleWidget.cs"
if($LASTEXITCODE -ne 0){throw 'Widget build failed'}
& $compiler @shared "/out:$OutputDirectory\WhaleSettings.exe" '/reference:System.dll' '/reference:System.Core.dll' '/reference:System.Drawing.dll' '/reference:System.Web.Extensions.dll' '/reference:System.Security.dll' '/reference:System.Xml.dll' '/reference:System.Xaml.dll' "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\PresentationFramework.dll" "/reference:$framework\WPF\WindowsBase.dll" "$PSScriptRoot\AccountPreferences.cs" "$PSScriptRoot\WhalePocket.cs" "$PSScriptRoot\SettingsEntry.cs"
if($LASTEXITCODE -ne 0){throw 'Settings build failed'}
$runtime=@('界面.xaml','config.example.json','accounts.mjs','auto-poller.mjs','recharge.mjs','whale-portrait.png','whale-cheer.png','whale-think.png','启动.cmd','README.md','LICENSE','THIRD_PARTY_NOTICES.md')
foreach($name in $runtime){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $OutputDirectory $name)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
Write-Output "Build OK: $OutputDirectory"
