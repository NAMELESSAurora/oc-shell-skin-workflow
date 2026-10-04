# Shared build helpers. These functions never load personal key/settings folders.
function Write-WorkflowJson([string]$Path,$Value){[IO.Directory]::CreateDirectory((Split-Path $Path -Parent))|Out-Null;[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 60)+"`n",(New-Object Text.UTF8Encoding($false)))}
function Read-CharacterContract([string]$Path,$Contract=$null){
    $c=if($Contract){$Contract}else{Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}
    if($c.schemaVersion-ne1){throw 'Unsupported runtime contract schemaVersion'}
    if($c.id-notmatch'^[a-z0-9][a-z0-9_]{1,31}$' -or $c.profileName-notmatch'^[A-Za-z0-9][A-Za-z0-9_-]{1,31}$'){throw 'id/profileName must be safe short identifiers'}
    if(-not$c.displayName -or $c.displayName-match'[\x00-\x1f"''`$]'){throw 'displayName must be plain text without script quotation/control characters'}
    foreach($name in @('configurationNamespace','fragmentNamespace')){if($c.$name-notmatch'^[A-Za-z0-9][A-Za-z0-9_-]{1,70}$'){throw "Invalid $name"}}
    foreach($guid in @($c.profileGuids.claude,$c.profileGuids.powershell)){if($guid-notmatch'^\{[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}$'){throw 'Profile GUID is missing or malformed'}}
    if($c.profileGuids.claude-eq$c.profileGuids.powershell){throw 'Two distinct terminal GUIDs are required'}
    if($c.defaultAppearance-notin@('bot','chibi')){throw 'defaultAppearance must be bot or chibi'}
    if($c.backgroundFile-notmatch('^'+[regex]::Escape($c.id)+'-terminal-[A-Za-z0-9_-]+\.png$')){throw 'backgroundFile must be a role-prefixed PNG filename'}
    foreach($color in @($c.status.accent,$c.status.secondary,$c.visual.ink,$c.visual.muted,$c.visual.accent,$c.visual.paper,$c.visual.soft,$c.visual.line)){if($color-notmatch'^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$'){throw 'Invalid theme color'}}
    if($c.status.motif.Length-ne1 -or [char]::IsSurrogate($c.status.motif[0])){throw 'motif must be one BMP character'}
    if($c.voice.rate-lt.5 -or $c.voice.rate-gt2 -or $c.status.soundPitch-lt100 -or $c.status.soundPitch-gt2000){throw 'Voice rate or sound pitch outside supported limits'}
    foreach($radii in @($c.visual.corners,$c.visual.stripCorners)){if($radii.Count-ne4 -or @($radii|Where-Object {$_-lt0 -or $_-gt100}).Count){throw 'Corner radii require four values from 0 to 100'}}
    if($c.bubble.width-lt400 -or $c.bubble.height-lt400 -or $c.bubble.margin.Count-ne4 -or -not$c.bubble.path){throw 'Bubble needs authored path, size and four margins'}
    foreach($relative in @($c.requiredFiles)+@($c.optionalFiles)){
        if([IO.Path]::IsPathRooted($relative) -or $relative-match'(^|[/\\])\.\.([/\\]|$)' -or $relative.Contains(':')){throw 'Asset contract paths must stay relative to their root'}
    }
    return $c
}
function ConvertTo-CSharpString([string]$Value){return (ConvertTo-Json -InputObject $Value -Compress)}
function New-RoleCatalogSource($c){
    $s=@{};foreach($field in @('id','profileName','displayName','familiarName','configurationNamespace','fragmentNamespace','defaultAppearance','backgroundFile','firstChat')){$s[$field]=ConvertTo-CSharpString ([string]$c.$field)}
    $accent=ConvertTo-CSharpString $c.status.accent;$secondary=ConvertTo-CSharpString $c.status.secondary;$motif=ConvertTo-CSharpString $c.status.motif;$direction=ConvertTo-CSharpString $c.voice.direction;$model=ConvertTo-CSharpString $c.voice.model
    $v=@{};foreach($field in @('ink','muted','accent','paper','soft','line','room')){$v[$field]=ConvertTo-CSharpString ([string]$c.visual.$field)}
    $corners=($c.visual.corners|ForEach-Object {([double]$_).ToString([Globalization.CultureInfo]::InvariantCulture)})-join','
    $strip=($c.visual.stripCorners|ForEach-Object {([double]$_).ToString([Globalization.CultureInfo]::InvariantCulture)})-join','
    $rate=([double]$c.voice.rate).ToString([Globalization.CultureInfo]::InvariantCulture);$pitch=([double]$c.status.soundPitch).ToString([Globalization.CultureInfo]::InvariantCulture)
    return @"
using System;
using System.Collections.Generic;
using System.Windows;
namespace OCShell {
  // Generated from a reviewed single-character contract by Build-Runtime.ps1.
  public static class RoleCatalog {
    public const string DefaultRole=$($s.id),ProfileName=$($s.profileName),ConfigurationNamespace=$($s.configurationNamespace),FragmentNamespace=$($s.fragmentNamespace);
    public const string DefaultAppearance=$($s.defaultAppearance),VoiceDirection=$direction,DefaultVoiceModel=$model;
    public const double VoiceRate=$rate;
    public static readonly string[] All={DefaultRole},Added={DefaultRole},Latest={},Newest={DefaultRole};
    public static string Canonical(string value){return DefaultRole;}
    public static string Name(string role){return $($s.displayName);}
    public static string FamiliarName(string role){return $($s.familiarName);}
    public static string StatusAccent(string role){return $accent;}
    public static string StatusSecondary(string role){return $secondary;}
    public static string Motif(string role){return $motif;}
    public static string FirstChat(string role){return $($s.firstChat);}
    public static string BackgroundFile(string role){return $($s.backgroundFile);}
    public static double SoundPitch(string role){return $pitch;}
    public static RoleVisual Visual(string role){return new RoleVisual{Role=DefaultRole,Ink=$($v.ink),Muted=$($v.muted),Accent=$($v.accent),Paper=$($v.paper),Soft=$($v.soft),Line=$($v.line),Motif=$motif,Room=$($v.room),Corners=new CornerRadius($corners),StripCorners=new CornerRadius($strip)};}
    public static Dictionary<string,string> VoiceModels(VoiceOptions options){var models=new Dictionary<string,string>();models[DefaultRole]=QwenVoice.Model(options,DefaultRole);return models;}
  }
}
"@
}
function Get-RoleTemplateTokens($c){return [ordered]@{ROLE_ID=$c.id;PROFILE_NAME=$c.profileName;DISPLAY_NAME=$c.displayName;CONFIG_NAMESPACE=$c.configurationNamespace;FRAGMENT_NAMESPACE=$c.fragmentNamespace;CLAUDE_GUID=$c.profileGuids.claude;POWERSHELL_GUID=$c.profileGuids.powershell;BACKGROUND_FILE=$c.backgroundFile;MOTIF_CODEPOINT=([int][char]$c.status.motif[0]).ToString()}}
function Render-RoleTemplate([string]$Text,$c){foreach($entry in (Get-RoleTemplateTokens $c).GetEnumerator()){$Text=$Text.Replace('{{'+$entry.Key+'}}',[string]$entry.Value)};if($Text-match'\{\{[A-Z_]+\}\}'){throw 'Unresolved launcher template token'};return $Text}
Export-ModuleMember -Function Write-WorkflowJson,Read-CharacterContract,New-RoleCatalogSource,Render-RoleTemplate
