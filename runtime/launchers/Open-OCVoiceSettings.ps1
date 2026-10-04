param([ValidateSet('{{PROFILE_NAME}}')][string]$Character='{{ROLE_ID}}')
$ErrorActionPreference='Stop'
Start-Process -FilePath (Join-Path $PSScriptRoot 'bin/OCCompanion.exe') -ArgumentList '--voice-settings',$Character -WindowStyle Hidden
