using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OCShell {
  // Offline integration board: real skin assets with fixture text/balance, no terminal or model process.
  public static class NewRolePreview {
    static BitmapImage Bitmap(string path){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(path);b.EndInit();b.Freeze();return b;}
    static string Hash(string path){using(var sha=System.Security.Cryptography.SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");}
    static void Place(Canvas canvas,FrameworkElement element,double x,double y){Canvas.SetLeft(element,x);Canvas.SetTop(element,y);canvas.Children.Add(element);}
    static TextBlock Text(string text,int size,string color,FontFamily font=null){return new TextBlock{Text=text,FontSize=size,Foreground=RoleVisual.Paint(color),FontFamily=font??new FontFamily("Cascadia Mono, Microsoft YaHei UI"),LineHeight=size*1.65};}
    static void Save(FrameworkElement visual,string path){visual.Measure(new Size(1280,720));visual.Arrange(new Rect(0,0,1280,720));visual.UpdateLayout();var bitmap=new RenderTargetBitmap(1920,1080,144,144,PixelFormats.Pbgra32);bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);}
    public static void Run(string root,string output,string[] roles=null){
      Directory.CreateDirectory(output);var metadata=new List<object>();
      foreach(string role in roles??RoleCatalog.Newest){
        if(Array.IndexOf(RoleCatalog.Added,role)<0)throw new InvalidDataException("Unknown new-role preview ID: "+role);
        var style=RoleVisual.For(role);object[] schemes=(object[])Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(root,"oc-schemes.json")));object scheme=null;foreach(object item in schemes)if(Convert.ToString(Data.At(item,"name")).Equals("OC "+RoleCatalog.ProfileName,StringComparison.OrdinalIgnoreCase))scheme=item;
        if(scheme==null)throw new InvalidDataException("Missing scheme: "+role);string bg=Convert.ToString(Data.At(scheme,"background")),fg=Convert.ToString(Data.At(scheme,"foreground")),accent=RoleCatalog.StatusAccent(role);string path=Path.Combine(root,"assets","backgrounds",RoleCatalog.BackgroundFile(role));if(!File.Exists(path))throw new FileNotFoundException("Missing completed background",path);
        var scene=new Canvas{Width=1280,Height=720,Background=RoleVisual.Paint(bg),ClipToBounds=true};var background=new Image{Source=Bitmap(path),Width=1280,Height=680,Stretch=Stretch.UniformToFill,Opacity=.36};Place(scene,background,0,40);
        var bar=new Border{Width=1280,Height=40,Background=RoleVisual.Paint("#292D30")};Place(scene,bar,0,0);var tab=new Border{Width=230,Height=32,CornerRadius=new CornerRadius(10,10,0,0),Background=RoleVisual.Paint(accent),Padding=new Thickness(10,4,10,4)};var tabBody=new StackPanel{Orientation=Orientation.Horizontal};tabBody.Children.Add(new Image{Source=Bitmap(Path.Combine(root,"assets",role+"-tab-avatar.png")),Width=20,Height=20,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,8,0)});tabBody.Children.Add(Text("PowerShell · "+RoleCatalog.Name(role)+"     ×",12,bg));tab.Child=tabBody;Place(scene,tab,12,8);Place(scene,Text("＋   ⌄",15,"#D2D4D5"),256,7);Place(scene,Text("—      □      ×",15,"#D2D4D5"),1120,7);
        Place(scene,Text("PowerShell / Claude Code · "+RoleCatalog.Name(role),20,accent),32,76);Place(scene,Text("本地皮肤设计预览 · 下方余额与命令为示例",12,Convert.ToString(Data.At(scheme,"brightBlack"))),32,114);
        string sample=RoleCatalog.Motif(role)+" "+role.ToUpperInvariant()+" │ C:\\workspace\n❯ git status --short\n  M app/scene.ts\n  M styles/theme.css\n\n❯ npm run check\n  types       passed\n  layout      passed\n  interaction passed\n\n❯ claude\n  "+role.ToUpperInvariant()+" │ Claude Code │ ctx —\n\n准备好继续了。先把眼前的小事做好。";
        Place(scene,Text(sample,14,fg),34,162);var prompt=new Border{Width=448,Height=50,BorderBrush=RoleVisual.Paint(accent),BorderThickness=new Thickness(0,1,0,1),Padding=new Thickness(0,10,0,0),Child=Text("❯  _",17,fg)};Place(scene,prompt,32,600);Place(scene,Text("PREVIEW · 不包含实际 API Key 或真实余额",11,Convert.ToString(Data.At(scheme,"brightBlack"))),32,672);
        var companion=new Companion(root,role,Path.Combine(output,role,"preview-preferences.json"));try{
          companion.Audio.Voice.Options.Sfx=false;companion.Audio.Voice.Options.Reactions=false;companion.Audio.Voice.Options.Chat=false;companion.Balance.Stop();companion.Balance.Current=new BalanceSnapshot{Amount=12.34m,Provider="预览示例",Status="界面预览 · 12.34 USD 为演示值"};companion.Talk("greeting");companion.SetBotSize(182);
          // Render the exact baked rest texture in 2D: software/offscreen WPF saturates emissive 3D textures.
          // Runtime keeps its native spring mesh; this board is a static composition/contrast check.
          companion.BubbleGel.Visibility=Visibility.Collapsed;companion.BubbleRig.Children.Insert(0,new Image{Source=Bitmap(Path.Combine(root,"assets","materials",role+"-gel.png")),Width=companion.BubbleWidth,Height=companion.BubbleHeight,Stretch=Stretch.Fill,IsHitTestVisible=false});
          companion.Bot.Content=null;companion.Doll.Content=null;companion.Controls.Content=null;companion.Bubble.Content=null;
          Place(scene,companion.ControlPanel,1280-Companion.ControlWidth-20,60);Place(scene,companion.Rig,1060,124);Place(scene,companion.BubbleRig,520,200);Place(scene,companion.BotRig,1280-182-companion.BotPadding,720-182*companion.BotRatio-companion.BotPadding);
          Save(scene,Path.Combine(output,role+"-terminal-design-board.png"));metadata.Add(new{role=role,width=1920,height=1080,sourceBackground=path,sourceBackgroundSha256=Hash(path),sourceDollSha256=Hash(Path.Combine(root,"assets",role+"-doll.png")),sourceBotSha256=Hash(AppearanceCatalog.File(root,role,companion.Appearance)),sourceAvatarSha256=Hash(Path.Combine(root,"assets",role+"-tab-avatar.png")),sourceBubbleSha256=Hash(Path.Combine(root,"assets","materials",role+"-gel.png")),appearance=companion.Appearance,backgroundStretch="UniformToFill",backgroundOpacity=.36,realBot=true,realDoll=true,realTabAvatar=true,realControls=true,realBubble=true,balanceIsFixture=true,modelCalls=0,keysRead=false});
        }finally{companion.Shutdown();companion.Bot.Close();companion.Doll.Close();companion.Controls.Close();companion.Bubble.Close();}
      }
      ChatStore.Atomic(Path.Combine(output,"design-board-manifest.json"),Data.Json.Serialize(new{status="passed",roles=metadata,notes="Offline WPF integration renders; real artwork and controls, fixture shell text and balance. No actual terminal, model request or credentials."}));
    }
  }
}
