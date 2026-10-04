[CmdletBinding()]
param([string]$RuntimeRoot,[string]$OutputDirectory,[switch]$CodeOnly,[switch]$RenderPreview)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $repoRoot 'runtime/WorkflowTools.psm1') -Force -DisableNameChecking
if(-not$RuntimeRoot){$RuntimeRoot=Join-Path $repoRoot 'build/runtime/zhuangfangyi'}
$RuntimeRoot=[IO.Path]::GetFullPath($RuntimeRoot)
if(-not$OutputDirectory){$OutputDirectory=Join-Path $RuntimeRoot 'checks'}
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$contract=Read-CharacterContract (Join-Path $RuntimeRoot 'runtime-contract.json')
$build=Get-Content -LiteralPath (Join-Path $RuntimeRoot 'build-manifest.json') -Raw|ConvertFrom-Json
if(-not$build.assetsInjected){$CodeOnly=$true}
if($RenderPreview -and $CodeOnly){throw 'RenderPreview requires reviewed runtime assets'}
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
$binary=Join-Path $RuntimeRoot 'bin/OCCompanion.exe';[Reflection.Assembly]::LoadFrom($binary)|Out-Null
function Assert-Runtime([bool]$value,[string]$message){if(-not$value){throw $message}}
function Find-Buttons($node){if($node-is[Windows.Controls.Button]){$node};for($i=0;$i-lt[Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++){Find-Buttons ([Windows.Media.VisualTreeHelper]::GetChild($node,$i))}}
function Alpha-Count([Windows.Media.Imaging.BitmapSource]$image){$rgba=New-Object Windows.Media.Imaging.FormatConvertedBitmap -ArgumentList $image,([Windows.Media.PixelFormats]::Bgra32),$null,0;$data=New-Object byte[] ($rgba.PixelWidth*$rgba.PixelHeight*4);$rgba.CopyPixels($data,($rgba.PixelWidth*4),0);$count=0;for($i=3;$i-lt$data.Length;$i+=4){if($data[$i]-gt0){$count++}};return $count}
$report=[ordered]@{status='running';role=$contract.id;mode=$(if($CodeOnly){'code-only'}else{'reviewed-assets-offline'});companionSha256=(Get-FileHash -LiteralPath $binary).Hash;statusLineSha256=(Get-FileHash -LiteralPath (Join-Path $RuntimeRoot 'bin/OCStatusLine.exe')).Hash;modelRequests=0;liveCredentialsRead=$false;liveConfigurationWritten=$false;installationRun=$false;terminalStarted=$false;manualAppearanceAndAuditionReview='still required'}
$app=$null;$companion=$null
try{
    Assert-Runtime ([OCShell.RoleCatalog]::All.Length-eq1 -and [OCShell.RoleCatalog]::DefaultRole-eq$contract.id -and [OCShell.RoleCatalog]::Canonical('missing')-eq$contract.id) 'Generated role registration does not match contract'
    Assert-Runtime ([OCShell.RoleCatalog]::Name($contract.id)-eq$contract.displayName -and [OCShell.RoleCatalog]::ConfigurationNamespace-eq$contract.configurationNamespace) 'Generated display name or config namespace mismatch'
    $visual=[OCShell.RoleVisual]::For($contract.id)
    Assert-Runtime ($visual.Role-eq$contract.id -and $visual.Ink-eq$contract.visual.ink -and $visual.Room-eq$contract.visual.room) 'Visual palette still refers to an old character'
    Assert-Runtime ([OCShell.QwenVoice]::Direction($contract.id)-eq$contract.voice.direction -and [OCShell.RoleCatalog]::VoiceRate-eq$contract.voice.rate) 'Voice direction/rate did not register'
    $status=[OCShell.StatusProgram]::Format([OCShell.Data]::Json.DeserializeObject('{"model":{"display_name":"Offline QA"},"cwd":"C:/example/workspace"}'),$contract.id,$false)
    Assert-Runtime ($status.Contains($contract.id.ToUpperInvariant())) 'Status line selected a different role'
    $scripts=@(Get-ChildItem -LiteralPath $RuntimeRoot -File -Filter '*.ps1')+@(Get-ChildItem -LiteralPath $RuntimeRoot -File -Filter '*.psm1')
    foreach($file in $scripts){$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)|Out-Null;Assert-Runtime ($errors.Count-eq0) ('Rendered launcher parse failed: '+$file.Name);Assert-Runtime (-not([IO.File]::ReadAllText($file.FullName)-match'\{\{[A-Z_]+\}\}')) 'Unresolved runtime launcher token'}
    $defaults=New-Object OCShell.VoiceOptions;Assert-Runtime ($defaults.Model-eq$contract.voice.model -and $defaults.Voices.Count-eq1 -and $defaults.Voices.ContainsKey($contract.id) -and $defaults.Rates.ContainsKey($contract.id)) 'Default model or voice dictionaries contain a different role';
    $report.registeredRole=$true;$report.themeRegistered=$true;$report.voiceDirectionRegistered=$true;$report.statusLine=$true;$report.renderedScriptsParsed=$scripts.Count
    if(-not$CodeOnly){
        foreach($relative in $contract.requiredFiles){Assert-Runtime (Test-Path -LiteralPath (Join-Path $RuntimeRoot $relative)) ('Missing runtime resource: '+$relative)}
        $personas=Get-Content -LiteralPath (Join-Path $RuntimeRoot 'companion-personas.json') -Raw|ConvertFrom-Json
        $dialogue=Get-Content -LiteralPath (Join-Path $RuntimeRoot 'companion-dialogue.json') -Raw|ConvertFrom-Json
        Assert-Runtime ($null-ne$personas.PSObject.Properties[$contract.id] -and $null-ne$dialogue.characters.PSObject.Properties[$contract.id]) 'Data files lack the active role'
        Assert-Runtime (-not(($personas.($contract.id)|ConvertTo-Json -Depth 30)-match'TODO:')) 'Persona is still a draft skeleton'
        Assert-Runtime (-not(($dialogue.characters.($contract.id)|ConvertTo-Json -Depth 30)-match'TODO:')) 'Dialogue is still a draft skeleton'
        $bg=New-Object Windows.Media.Imaging.BitmapImage;$bg.BeginInit();$bg.CacheOption=[Windows.Media.Imaging.BitmapCacheOption]::OnLoad;$bg.UriSource=New-Object Uri (Join-Path $RuntimeRoot ('assets/backgrounds/'+$contract.backgroundFile));$bg.EndInit()
        Assert-Runtime ($bg.PixelWidth-eq3840 -and $bg.PixelHeight-eq2160) 'Background contract requires 3840x2160'
        $app=New-Object Windows.Application;$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
        $preferences=Join-Path $OutputDirectory ('isolated-'+[guid]::NewGuid().ToString('N')+'/preferences.json')
        $companion=New-Object OCShell.Companion -ArgumentList $RuntimeRoot,$contract.id,$preferences
        $companion.Balance.Stop();$companion.Audio.Voice.Options.Sfx=$false;$companion.Audio.Voice.Options.Reactions=$false;$companion.Audio.Voice.Options.Chat=$false
        Assert-Runtime (-not$companion.Audio.Voice.Options.EnableCloudSpeech) 'Fresh cloud speech must be disabled'
        Assert-Runtime ($companion.Appearance-eq$contract.defaultAppearance) 'Fresh appearance does not match contract'
        $appearances=@()
        foreach($style in @('bot','chibi')){
            $companion.BotJelly.Press((New-Object Windows.Point 30,30));Assert-Runtime ($companion.SetAppearance($style)) ('Missing appearance '+$style)
            Assert-Runtime (-not$companion.BotJelly.Running) 'Switch retains old animation state'
            $original=New-Object Windows.Media.Imaging.BitmapImage;$original.BeginInit();$original.DecodePixelWidth=480;$original.CacheOption=[Windows.Media.Imaging.BitmapCacheOption]::OnLoad;$original.UriSource=New-Object Uri (Join-Path $RuntimeRoot ('assets/appearances/'+$contract.id+'-'+$style+'.png'));$original.EndInit()
            $current=[Windows.Media.Imaging.BitmapSource]$companion.BotImage.Source
            Assert-Runtime ((Alpha-Count $original)-eq(Alpha-Count $current)) 'Visible artwork was cut by alpha-aware cropping'
            Assert-Runtime ([Math]::Abs($companion.BotImage.Height/$companion.BotImage.Width-$current.PixelHeight/$current.PixelWidth)-lt.00001) 'Character was stretched'
            $companion.Dock.Reset();$width=$companion.FitBotWidth(1000,700);$height=$width*$companion.BotRatio;$position=$companion.Dock.Position(1000,700,$width,$height)
            Assert-Runtime ([Math]::Abs($position.X+$width-1000)-lt.001 -and [Math]::Abs($position.Y+$height-700)-lt.001) 'Bottom-right dock has a gap'
            $persisted=New-Object OCShell.AppearancePreferences -ArgumentList $companion.Audio.Voice.Folder;Assert-Runtime ($persisted.Get($contract.id)-eq$style) 'Appearance did not persist'
            $appearances+=@{style=$style;ratioPreserved=$true;visiblePixelsRetained=$true;dockGap=0;persisted=$true;oldAnimationCleared=$true}
        }
        $rig=$companion.BubbleRig;$rig.Measure((New-Object Windows.Size $companion.BubbleWidth,$companion.BubbleHeight));$rig.Arrange((New-Object Windows.Rect 0,0,$companion.BubbleWidth,$companion.BubbleHeight));$rig.UpdateLayout()
        $buttons=@(Find-Buttons $rig|Where-Object Tag -eq 'bubble-action');Assert-Runtime ($buttons.Count-eq4) 'Four bubble actions are required'
        foreach($button in @($buttons)+@(Find-Buttons $rig|Where-Object Tag -eq 'bubble-close')){foreach($point in @((New-Object Windows.Point 8,8),(New-Object Windows.Point ($button.ActualWidth-8),8),(New-Object Windows.Point 8,($button.ActualHeight-8)),(New-Object Windows.Point ($button.ActualWidth-8),($button.ActualHeight-8)))){Assert-Runtime ($companion.BubbleContains($button.TranslatePoint($point,$rig))) 'Interactive button escapes authored bubble shape'}}
        $window=$companion.Bubble;$window.Left=-20000;$window.Top=-20000;$window.Show();$window.UpdateLayout();$handle=(New-Object Windows.Interop.WindowInteropHelper $window).Handle;$inside=0;$outside=0
        for($y=30;$y-lt$companion.BubbleHeight;$y+=36){for($x=30;$x-lt$companion.BubbleWidth;$x+=36){$point=New-Object Windows.Point $x,$y;$expected=$companion.BubbleContains($point);$screen=$rig.PointToScreen($point);$packed=(([long][Math]::Round($screen.X))-band65535)-bor((([long][Math]::Round($screen.Y))-band65535)-shl16);$actual=[OCShell.Native]::SendMessage($handle,0x84,[IntPtr]::Zero,([IntPtr]$packed)).ToInt64();Assert-Runtime ($actual-eq$(if($expected){1}else{-1})) 'Native hit test differs from bubble silhouette';if($expected){$inside++}else{$outside++}}}
        $window.Hide();Assert-Runtime ($inside-ge100 -and $outside-ge5) 'Bubble input coverage is incomplete'
        $lines=$companion.Audio.Interactions.Lines($contract.id);$ready=$companion.Audio.Interactions.ReadyCount($companion.Audio.Voice,$contract.id)
        Assert-Runtime ($lines.Count-eq$contract.voice.expectedOfflineClips -and $ready-eq$lines.Count) 'Offline voice count or PCM/hash/sidecar validation failed'
        foreach($sidecar in (Get-ChildItem -LiteralPath (Join-Path $RuntimeRoot ('voice-library/interaction-ja/'+$contract.id)) -Filter '*.wav.json')){$meta=Get-Content -LiteralPath $sidecar.FullName -Raw|ConvertFrom-Json;foreach($name in @('voiceId','key','apiKey','cache')){Assert-Runtime (-not$meta.PSObject.Properties[$name]) 'Sidecar contains private account/cache metadata'}}
        $shellPreview=Join-Path $OutputDirectory 'install-preview';. (Join-Path $RuntimeRoot 'Install-OCCharacterSkins.ps1') -PreviewDirectory $shellPreview -WorkingDirectory $RuntimeRoot
        $preview=Get-Content -LiteralPath (Join-Path $shellPreview 'profiles-preview.json') -Raw|ConvertFrom-Json
        Assert-Runtime ($preview.profiles.Count-eq2 -and $preview.schemes.Count-eq1) 'Installation preview includes other roles'
        $report.appearances=$appearances;$report.bubble=@{actionsComplete=$true;closeComplete=$true;bodyHitPoints=$inside;transparentHitPoints=$outside};$report.offlineVoices=@{lines=$lines.Count;ready=$ready;privateMetadataAbsent=$true};$report.installPreview=@{profiles=2;schemes=1;writesToLiveSettings=$false};$report.assetsValidated=$true
        if($RenderPreview){[OCShell.NewRolePreview]::Run($RuntimeRoot,(Join-Path $OutputDirectory 'design-preview'),$null);$report.designPreview='checks/design-preview'}
    }
    $report.status='passed'
}catch{$report.status='failed';$report.error=$_.Exception.Message;throw}
finally{
    if($companion){$companion.Shutdown();foreach($window in @($companion.Bot,$companion.Doll,$companion.Controls,$companion.Bubble)){$window.Close()}}
    if($app){$app.Shutdown()}
    Write-WorkflowJson (Join-Path $OutputDirectory 'validation-summary.json') $report
}
Write-Output ('Offline validation passed for '+$contract.id+' ('+$report.mode+'). No installation, terminal or model request.')
