param([ValidateSet('{{PROFILE_NAME}}')][string]$Character='{{PROFILE_NAME}}')
$ErrorActionPreference='Stop'
Start-Process -FilePath (Join-Path $PSScriptRoot 'bin/OCCompanion.exe') -ArgumentList @('--claude-key-settings',$Character.ToLowerInvariant()) -WindowStyle Hidden
