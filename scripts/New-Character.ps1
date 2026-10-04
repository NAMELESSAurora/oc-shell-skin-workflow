[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Id,[Parameter(Mandatory=$true)][string]$ProfileName,[Parameter(Mandatory=$true)][string]$DisplayName,[string]$Destination,[string]$TemplatePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $repoRoot 'runtime/WorkflowTools.psm1') -Force -DisableNameChecking
if(-not$TemplatePath){$TemplatePath=Join-Path $repoRoot 'templates/character-runtime-contract.json'}
$base=Read-CharacterContract $TemplatePath
if(-not$Destination){$Destination=Join-Path $repoRoot ('workspaces/'+$Id)}
$Destination=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $Destination){if(@(Get-ChildItem -LiteralPath $Destination -Force).Count){throw 'Destination is not empty; existing character work is preserved.'}}
$json=$base|ConvertTo-Json -Depth 60
$json=$json.Replace($base.id,$Id).Replace($base.profileName,$ProfileName)
$character=$json|ConvertFrom-Json
$character.id=$Id;$character.profileName=$ProfileName;$character.displayName=$DisplayName;$character.familiarName=$DisplayName
$character.configurationNamespace='OCShellSkinWorkflow-'+$ProfileName;$character.fragmentNamespace=$character.configurationNamespace
$character.reviewStatus='draft';$character.firstChat='TODO: write an original first greeting after persona review.'
$character.visual.room='TODO: character room title'
$character.profileGuids.claude='{'+[guid]::NewGuid().ToString()+'}';$character.profileGuids.powershell='{'+[guid]::NewGuid().ToString()+'}'
$character.voice.direction='TODO: reviewed voice direction; use a normal conversational pace.'
# Validate the generated contract before writing any character workspace.
$null=Read-CharacterContract -Contract $character
Write-WorkflowJson (Join-Path $Destination 'runtime-contract.json') $character
$shape=[ordered]@{};$shape[$Id]=$character.bubble;Write-WorkflowJson (Join-Path $Destination 'materials/bubble-shapes.json') $shape
$personas=[ordered]@{source='Draft workspace; canonical source research and persona review are still required.';shared='Reply naturally to the actual user message. Do not invent terminal actions, account balances or a relationship with the user.'}
$personas[$Id]=[ordered]@{name=$DisplayName;voice='TODO: canon-grounded persona and human dialogue behavior';temperature=.7;ttsRate=$character.voice.rate;ttsVoice='own-voice-unconfigured'}
Write-WorkflowJson (Join-Path $Destination 'companion-personas.json') $personas
$entry=[ordered]@{name=$DisplayName;accent=$character.status.accent;ink=$character.visual.ink;paper=$character.visual.paper;paperBottom=$character.visual.soft;nameColor=$character.visual.accent;background=$character.visual.paper;border=$character.visual.line;wallet=$character.visual.paper;speechSurface=$character.visual.soft;mutedInk=$character.visual.muted;buttonSurface=$character.visual.paper;buttonInk=$character.visual.ink;motif=$character.status.motif;subtitle='TODO: original subtitle'}
foreach($context in @('greeting','touch','headPat','cheek','dollTouch','squeeze','drag','release','impact','repeatTouch','dock','busy','idle','balance','late')){$entry[$context]=@('TODO: review original character dialogue')}
$characters=[ordered]@{};$characters[$Id]=$entry;Write-WorkflowJson (Join-Path $Destination 'companion-dialogue.json') ([ordered]@{source='Original dialogue draft, awaiting review';characters=$characters})
$lines=[ordered]@{};$lines[$Id]=@();Write-WorkflowJson (Join-Path $Destination 'interaction-lines-ja.json') ([ordered]@{Characters=$lines})
Write-WorkflowJson (Join-Path $Destination 'AUTHORING-TODO.json') ([ordered]@{status='draft';role=$Id;assetsGenerated=$false;voicesGenerated=0;installationReady=$false;actions=@('Research canonical identity and write persona/dialogue','Create and review true-RGBA doll/chibi/bot/avatar assets','Author shape and bake plush material; review hit areas','Compose 4K background without stretching character','Import cleaned references and author/bake/hash-check Japanese interaction lines','Provide terminal scheme and Claude theme','Mark runtime contract approved only after reviewing all materials','Build with AssetRoot then run full offline Validate-Runtime')})
Write-Output ('Created a draft character skeleton at '+$Destination+'. No image, voice, API, build, install or deployment was performed.')
