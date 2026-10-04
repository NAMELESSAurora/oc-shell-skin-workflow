[CmdletBinding()]
param(
    [ValidateSet('{{PROFILE_NAME}}')][string[]]$Characters=@('{{PROFILE_NAME}}'),
    [string]$PreviewDirectory,
    [string]$TerminalSettingsPath,
    [string]$WorkingDirectory=[Environment]::GetFolderPath('UserProfile')
)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
. (Join-Path $taskRoot 'PortableSkinTools.ps1')
function Set-OCSkinProperty($Object,$Name,$Value){$Object|Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force}
function Merge-OCSkinProfiles($Current,$Added){
    $items=@($Current)
    foreach($profile in $Added){
        $existing=$items|Where-Object guid -eq $profile.guid|Select-Object -First 1
        if($existing){foreach($name in $profile.Keys){Set-OCSkinProperty $existing $name $profile[$name]}}
        else{$items+=([pscustomobject]$profile)}
    }
    return $items
}
if(-not(Test-Path -LiteralPath $WorkingDirectory -PathType Container)){throw '工作目录不存在，请用 -WorkingDirectory 指定现有文件夹。'}
$WorkingDirectory=(Resolve-Path -LiteralPath $WorkingDirectory).ProviderPath
$schemes=Get-Content -LiteralPath (Join-Path $taskRoot 'oc-schemes.json') -Raw|ConvertFrom-Json
$selectedSchemes=@($schemes|Where-Object name -eq 'OC {{PROFILE_NAME}}')
if($selectedSchemes.Count-ne1){throw 'Missing single-role terminal scheme'}
$scheme=$selectedSchemes[0]
$powershell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$background=Join-Path $taskRoot 'assets/backgrounds/{{BACKGROUND_FILE}}'
$required=@('bin/OCCompanion.exe','bin/OCStatusLine.exe','assets/{{ROLE_ID}}-tab-avatar.png','assets/{{ROLE_ID}}-doll.png','assets/{{ROLE_ID}}-corner-complete.png','assets/appearances/{{ROLE_ID}}-chibi.png','assets/appearances/{{ROLE_ID}}-bot.png','assets/materials/{{ROLE_ID}}-gel.png','assets/backgrounds/{{BACKGROUND_FILE}}','claude/oc-{{ROLE_ID}}.theme.json')
foreach($relative in $required){if(-not(Test-Path -LiteralPath (Join-Path $taskRoot $relative))){throw ('Missing package asset: '+$relative)}}
$profiles=@()
foreach($kind in @('Claude','PowerShell')){
    $guid=if($kind-eq'Claude'){'{{CLAUDE_GUID}}'}else{'{{POWERSHELL_GUID}}'}
    $launcher=if($kind-eq'Claude'){'Start-OCClaude.ps1'}else{'Start-OCPowerShell.ps1'}
    $profiles+=[ordered]@{guid=$guid;name="$kind - {{PROFILE_NAME}}";tabTitle="$kind - {{PROFILE_NAME}}";suppressApplicationTitle=$true;commandline='"'+$powershell+'" -NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit -File "'+(Join-Path $taskRoot $launcher)+'" -Character {{PROFILE_NAME}}';startingDirectory=$WorkingDirectory;icon=(Join-Path $taskRoot 'assets/{{ROLE_ID}}-tab-avatar.png');colorScheme='OC {{PROFILE_NAME}}';background=$scheme.background;foreground=$scheme.foreground;tabColor=$scheme.cursorColor;cursorShape='bar';font=[ordered]@{face='Cascadia Mono';size=12};padding='14, 10, 14, 12';useAcrylic=$false;opacity=100;hidden=$false;backgroundImage=$background;backgroundImageAlignment='bottomLeft';backgroundImageStretchMode='uniformToFill';backgroundImageOpacity=.36}
}
$settings=New-OCPortableClaudeSettings
if($PreviewDirectory){
    Write-OCPortableJson (Join-Path $PreviewDirectory 'profiles-preview.json') ([ordered]@{profiles=$profiles;schemes=$selectedSchemes})
    Write-OCPortableJson (Join-Path $PreviewDirectory 'claude-settings-preview.json') $settings
    Write-OCPortableJson (Join-Path $PreviewDirectory 'install-preview.json') ([ordered]@{characters=@('{{PROFILE_NAME}}');profiles=2;packageRoot=$taskRoot;workingDirectory=$WorkingDirectory;globalClaudeSettingsChanged=$false;defaultTerminalProfileChanged=$false;previewOnly=$true})
    Write-Output 'Exported relocated single-role installation preview; user configuration unchanged.'
    return
}
# Resolve prerequisites before writing configuration. No Claude key is inspected.
$terminalPath=if($TerminalSettingsPath){[IO.Path]::GetFullPath($TerminalSettingsPath)}else{Get-OCPortableTerminalSettings}
$terminalExecutable=Get-OCPortableTerminalExecutable
$terminal=Read-OCPortableJson $terminalPath
if(-not$terminal.profiles -or -not$terminal.profiles.PSObject.Properties['list']){throw 'Terminal profile list missing; no changes applied.'}
$fragmentDirectory=Join-Path $env:LOCALAPPDATA 'Microsoft/Windows Terminal/Fragments/{{FRAGMENT_NAMESPACE}}'
$fragmentPath=Join-Path $fragmentDirectory 'skin.json'
$fragment=if(Test-Path -LiteralPath $fragmentPath){Read-OCPortableJson $fragmentPath}else{[pscustomobject]@{profiles=@();schemes=@()}}
$backupDirectory=Join-Path $taskRoot ('backups/portable-install-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backupDirectory -Force|Out-Null
Copy-Item -LiteralPath $terminalPath -Destination (Join-Path $backupDirectory 'terminal-settings.json')
if(Test-Path -LiteralPath $fragmentPath){Copy-Item -LiteralPath $fragmentPath -Destination (Join-Path $backupDirectory 'portable-fragment.json')}
if(Test-Path -LiteralPath (Join-Path $taskRoot 'claude/{{ROLE_ID}}.json')){Copy-Item -LiteralPath (Join-Path $taskRoot 'claude/{{ROLE_ID}}.json') -Destination (Join-Path $backupDirectory 'package-claude-skin.json')}
# Only these two stable GUIDs and one scheme are updated. Unknown fields survive.
Set-OCSkinProperty $terminal.profiles 'list' @(Merge-OCSkinProfiles $terminal.profiles.list $profiles)
Set-OCSkinProperty $terminal 'schemes' (@($terminal.schemes|Where-Object name -ne 'OC {{PROFILE_NAME}}')+@($selectedSchemes))
Set-OCSkinProperty $fragment 'profiles' @(Merge-OCSkinProfiles $fragment.profiles $profiles)
Set-OCSkinProperty $fragment 'schemes' (@($fragment.schemes|Where-Object name -ne 'OC {{PROFILE_NAME}}')+@($selectedSchemes))
$themeDirectory=Join-Path $env:USERPROFILE '.claude/themes'
New-Item -ItemType Directory -Path $themeDirectory,$fragmentDirectory -Force|Out-Null
$themePath=Join-Path $themeDirectory 'oc-{{ROLE_ID}}.json'
if(Test-Path -LiteralPath $themePath){Copy-Item -LiteralPath $themePath -Destination (Join-Path $backupDirectory 'previous-{{ROLE_ID}}-theme.json')}
Copy-Item -LiteralPath (Join-Path $taskRoot 'claude/oc-{{ROLE_ID}}.theme.json') -Destination $themePath -Force
Write-OCPortableJson (Join-Path $taskRoot 'claude/{{ROLE_ID}}.json') $settings
Write-OCPortableJson $fragmentPath $fragment
Write-OCPortableJson $terminalPath $terminal
$shell=New-Object -ComObject WScript.Shell
$shortcutDirectory=Join-Path ([Environment]::GetFolderPath('Desktop')) '{{PROFILE_NAME}} Skin'
New-Item -ItemType Directory -Path $shortcutDirectory -Force|Out-Null
foreach($kind in @('Claude','PowerShell')){
    $shortcut=$shell.CreateShortcut((Join-Path $shortcutDirectory "$kind - {{PROFILE_NAME}}.lnk"))
    $shortcut.TargetPath=$terminalExecutable
    $shortcut.Arguments='-w new new-tab -p "'+$kind+' - {{PROFILE_NAME}}" -d "'+$WorkingDirectory+'"'
    $shortcut.WorkingDirectory=$WorkingDirectory;$shortcut.Description="$kind · {{DISPLAY_NAME}}";$shortcut.Save()
}
Write-OCPortableJson (Join-Path $taskRoot 'runtime/portable-install.json') ([ordered]@{installedAt=(Get-Date).ToString('o');packageRoot=$taskRoot;characters=@('{{PROFILE_NAME}}');profiles=2;terminalSettings=$terminalPath;fragmentPath=$fragmentPath;backupDirectory=$backupDirectory;globalClaudeSettingsChanged=$false;defaultTerminalProfileChanged=$false;runningSessionsRestarted=$false})
Write-Output 'Installed two {{PROFILE_NAME}} profiles. Existing profiles with these two GUIDs now point to this package; other roles, default profile, credentials and running sessions are preserved.'
