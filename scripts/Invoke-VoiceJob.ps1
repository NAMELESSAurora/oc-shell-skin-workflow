[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='Medium')]
param(
    [Parameter(Mandatory=$true)][string]$RuntimeRoot,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9][a-z0-9_]{0,39}$')][string]$Role,
    [ValidateSet('Clone','Bake')][string]$Operation = 'Clone',
    [switch]$EnableCloud,
    [switch]$ReferenceReviewed
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$taskMode = if ($Operation -eq 'Clone') { '--voice-clone' } else { '--voice-bake' }
$taskBinary = Join-Path $taskRoot 'bin/OCCompanion.exe'
# Dry runs never load settings, read a Key, create a process or call an API.
if (-not $EnableCloud -or $WhatIfPreference) {
    [pscustomobject]@{ mode='dry-run'; role=$Role; operation=$Operation; nativeArguments=@($taskMode,$Role); cloudEnabled=$false; actualCloudRequests=0; note='Use -EnableCloud -ReferenceReviewed only after reviewing your own settings, source permission and sample quality.' } | ConvertTo-Json -Depth 4
    return
}
if (-not $ReferenceReviewed) { throw 'Review reference speech, performer/source permission and natural endings first; use -ReferenceReviewed only when that review is complete.' }
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Use Windows PowerShell 5.1 for the .NET Framework native runtime.' }
if (-not (Test-Path -LiteralPath $taskBinary -PathType Leaf)) { throw 'Build this runtime first; bin/OCCompanion.exe is missing.' }
[Reflection.Assembly]::LoadFrom($taskBinary) | Out-Null
if ([Array]::IndexOf([OCShell.RoleCatalog]::All,$Role) -lt 0) { throw 'Role is not registered in this compiled runtime; regenerate its role contract and compile first.' }
$taskNamespace = [OCShell.RoleCatalog]::ConfigurationNamespace
if ([string]::IsNullOrWhiteSpace($taskNamespace) -or $taskNamespace -notmatch '^[A-Za-z0-9_-]+$') { throw 'Runtime configuration namespace is invalid.' }
$taskFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $taskNamespace
$taskSettings = Join-Path $taskFolder 'qwen-voice.json'
$taskEncryptedKey = Join-Path $taskFolder 'qwen-key.bin'
if (-not (Test-Path -LiteralPath $taskSettings -PathType Leaf) -or -not (Test-Path -LiteralPath $taskEncryptedKey -PathType Leaf)) { throw 'Configure this runtime with your own local settings and current-user encrypted Key first.' }
$taskVoice = New-Object OCShell.QwenVoice -ArgumentList @($taskFolder,$taskRoot)
if ($taskVoice.ConfigurationError.Length -gt 0 -or -not $taskVoice.Options.EnableCloudSpeech) { throw 'This runtime must explicitly enable cloud synthesis in readable local voice settings.' }
if ($taskVoice.Key().Length -eq 0) { throw 'The encrypted Key is unavailable to this Windows user. Enter your own Key in runtime settings; do not copy another user credential file.' }
$taskEffective = [OCShell.QwenVoice]::ForRole($taskVoice.Options,$Role)
if ($taskEffective.Provider -ne 'beijing' -or -not [OCShell.QwenVoice]::Modern($taskEffective)) { throw 'Native Qwen Audio cloning requires a configured Beijing Qwen Audio model.' }
[OCShell.QwenVoice]::EndpointFor($taskEffective) | Out-Null
$taskReference = $taskVoice.Options.References[$Role]
if (-not (Test-Path -LiteralPath $taskReference -PathType Leaf)) { throw 'The selected role reference WAV is missing.' }
[OCShell.WaveInfo]::Read([IO.File]::ReadAllBytes($taskReference),$true) | Out-Null
$taskBook = New-Object OCShell.InteractionVoices -ArgumentList $taskRoot
$taskLineCount = $taskBook.Lines($Role).Count
if ($Operation -eq 'Bake' -and $taskLineCount -eq 0) { throw 'No interaction lines are registered for the selected role.' }
if (-not $PSCmdlet.ShouldProcess("$Role ($taskLineCount lines, $($taskEffective.Model))",'Run the configured paid Qwen clone/verify and optional native bake job')) { return }
# Never print/upload Key, cloud voice IDs, Workspace ID, credentials or signed upload URLs.
$taskProcess = Start-Process -FilePath $taskBinary -ArgumentList @($taskMode,$Role) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru -Wait
[pscustomobject]@{ mode='executed'; role=$Role; operation=$Operation; model=$taskEffective.Model; lines=$taskLineCount; exitCode=$taskProcess.ExitCode; progressFile=(Join-Path $taskRoot 'voice-library/interaction-ja/bake-progress.json'); usageFolder=$taskFolder } | ConvertTo-Json -Depth 4
if ($taskProcess.ExitCode -ne 0) { throw 'Native voice job stopped. Inspect its sanitized progress file; successful audio remains available. No automatic second batch is started.' }
