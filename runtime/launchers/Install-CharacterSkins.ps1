[CmdletBinding()]
param([string]$PreviewDirectory,[string]$TerminalSettingsPath,[string]$WorkingDirectory=[Environment]::GetFolderPath('UserProfile'))
$ErrorActionPreference='Stop'
# This generated entry is scoped to the active character; the shared installer owns
# backups and additive profile/scheme merging.
& (Join-Path $PSScriptRoot 'Install-OCCharacterSkins.ps1') -Characters {{PROFILE_NAME}} -PreviewDirectory $PreviewDirectory -TerminalSettingsPath $TerminalSettingsPath -WorkingDirectory $WorkingDirectory
