using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace OCShell {
  public sealed class CompanionPreferences {
    readonly string path;
    DateTime stamp;
    public bool Persistent=true;
    public CompanionPreferences(string path) {this.path=path;Reload();}
    public bool Reload() {
      try {
        if(!File.Exists(path))return false;
        DateTime current=File.GetLastWriteTimeUtc(path);if(current==stamp)return false;
        object value=Data.At(Data.Json.DeserializeObject(File.ReadAllText(path)),"persistent");
        if(!(value is bool))return false;
        bool changed=Persistent!=(bool)value;Persistent=(bool)value;stamp=current;return changed;
      }catch{return false;}
    }
    public void Set(bool persistent) {
      // Replace a complete file so another companion cannot read a half-written preference.
      string temporary=path+"."+System.Diagnostics.Process.GetCurrentProcess().Id+".tmp";
      File.WriteAllText(temporary,Data.Json.Serialize(new {persistent=persistent}),new System.Text.UTF8Encoding(false));
      try {if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
      finally {if(File.Exists(temporary))File.Delete(temporary);}
      Persistent=persistent;stamp=File.GetLastWriteTimeUtc(path);
    }
    public static bool ShowCharacters(bool ownerVisible,bool focused,bool menuOpen,bool persistent) {
      return ownerVisible && (persistent || focused || menuOpen);
    }
  }

  // CompositionTarget.Rendering schedules one update per compositor frame.
  // The cached, display-sized bitmap is transformed rather than resampled from a 1K+ source.
  public sealed class Jelly {
    public readonly Image Image;
    readonly ScaleTransform scale=new ScaleTransform(1,1);
    readonly RotateTransform rotate=new RotateTransform();
    readonly TranslateTransform translate=new TranslateTransform();
    readonly bool corner;
    double q,v,angle,av,x,xv,y,yv,tq,ta,tx,ty,ix,iy,ivx,ivy;
    bool holding,active;DateTime started;TimeSpan lastFrame;
    Point start;
    public int Count;
    public Jelly(Image image,bool isCorner) {
      Image=image;corner=isCorner;
      var transform=new TransformGroup();transform.Children.Add(scale);transform.Children.Add(rotate);transform.Children.Add(translate);
      image.RenderTransform=transform;image.RenderTransformOrigin=isCorner?new Point(1,1):new Point(.5,.5);
      image.CacheMode=new BitmapCache(1.5);
      RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.LowQuality);
      image.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) {
        if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)return;
        Press(image.PointToScreen(e.GetPosition(image)));image.CaptureMouse();e.Handled=true;
      };
      image.MouseMove+=delegate(object s,MouseEventArgs e) {if(holding)Pull(image.PointToScreen(e.GetPosition(image)));};
      image.MouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e) {if(holding){Release();image.ReleaseMouseCapture();e.Handled=true;}};
      image.LostMouseCapture+=delegate {if(holding)Release();};
    }
    void Wake() {if(active)return;active=true;lastFrame=TimeSpan.Zero;CompositionTarget.Rendering+=Render;}
    void Render(object sender,EventArgs e) {
      var time=((RenderingEventArgs)e).RenderingTime;if(time==lastFrame)return;
      double dt=lastFrame==TimeSpan.Zero?1.0/60:Math.Max(.001,Math.Min(.032,(time-lastFrame).TotalSeconds));lastFrame=time;Tick(dt);
    }
    public void Press(Point p) {
      holding=true;start=p;started=DateTime.UtcNow;tq=.58;
      // Immediate visual response on pointer-down; no timer/startup delay.
      q=.42;v=0;Draw();Wake();
    }
    public void Pull(Point p) {
      double dx=p.X-start.X,dy=p.Y-start.Y;
      tx=Math.Max(-23,Math.Min(corner?0:23,dx*.28));ty=Math.Max(-20,Math.Min(corner?0:20,dy*.28));
      ta=Math.Max(-8,Math.Min(8,dx*.10));if(corner)ta=Math.Max(-4,Math.Min(4,ta));
      tq=Math.Min(.92,.58+Math.Sqrt(dx*dx+dy*dy)*.003);
      x+=(tx-x)*.6;y+=(ty-y)*.6;Draw();Wake();
    }
    public void Release() {if(!holding)return;holding=false;Count++;tq=tx=ty=ta=0;v-=2.0;Wake();}
    public void Impact(double nx,double ny,double speed) {
      if(!SystemParameters.ClientAreaAnimation)return;
      double amount=Math.Min(.23,Math.Max(.035,speed/2200));
      ix=nx!=0?-amount:amount*.55;iy=ny!=0?-amount:amount*.55;ivx=ivy=0;Draw();Wake();
    }
    public void Tick(double dt) {
      if(holding)tq=Math.Min(.92,Math.Max(tq,.58+(DateTime.UtcNow-started).TotalSeconds*.16));
      if(SystemParameters.ClientAreaAnimation) {
        v+=((tq-q)*265-v*18)*dt;q=Math.Max(-.16,Math.Min(1,q+v*dt));
        av+=((ta-angle)*120-av*13)*dt;angle+=av*dt;
        xv+=((tx-x)*220-xv*16)*dt;x+=xv*dt;
        yv+=((ty-y)*220-yv*16)*dt;y+=yv*dt;
        ivx+=(-ix*230-ivx*17)*dt;ix+=ivx*dt;ivy+=(-iy*230-ivy*17)*dt;iy+=ivy*dt;
      }else{q=tq;x=tx;y=ty;angle=0;v=xv=yv=av=0;}
      Draw();double energy=Math.Abs(q-tq)+Math.Abs(v)+Math.Abs(x)+Math.Abs(xv)+Math.Abs(y)+Math.Abs(yv)+Math.Abs(angle)+Math.Abs(av)+Math.Abs(ix)+Math.Abs(iy)+Math.Abs(ivx)+Math.Abs(ivy);
      if(!holding && energy<.01)Stop();
    }
    void Draw() {
      scale.ScaleX=1+q*(corner?.20:.30)+ix;scale.ScaleY=1-q*(corner?.24:.26)+iy;
      rotate.Angle=angle;translate.X=x;translate.Y=y;
    }
    public void Freeze(bool squeezed) {Stop();q=squeezed?.7:0;angle=squeezed?-5:0;Draw();}
    public void Stop() {
      holding=false;if(active){CompositionTarget.Rendering-=Render;active=false;}Image.ReleaseMouseCapture();
      q=v=angle=av=x=xv=y=yv=tq=ta=tx=ty=ix=iy=ivx=ivy=0;Draw();
    }
    public double Compression {get{return q;}}
    public bool Running {get{return active;}}
  }

  public sealed class Companion {
    public readonly Window Bot,Doll,Controls,Bubble;
    public readonly Canvas Rig,BotRig;
    public readonly Image Toy,BotImage;
    public readonly Jelly DollJelly,BotJelly;
    public readonly Border ControlPanel;
    public readonly Button CharacterButton,ModeButton;
    public readonly Button AudioButton,AppearanceButton;
    readonly TextBlock controlMotif;
    readonly Border controlDivider;
    public readonly PanelPlacement Panels;
    readonly PanelDrag controlDrag,bubbleDrag;
    public readonly CompanionAudio Audio;
    public const double ControlWidth=330,ControlHeight=46;
    public double ControlScale {get;private set;}
    public double ControlLayoutWidth {get{return 4+(ControlWidth-4)*ControlScale;}}
    public double ControlLayoutHeight {get{return 4+(ControlHeight-4)*ControlScale;}}
    public readonly Border BubblePanel;
    public readonly Grid BubbleRig;
    public readonly System.Windows.Shapes.Path BubbleHitSurface;
    readonly Border wallet;
    readonly Border speechSurface;
    readonly TextBlock accountTitle,accountSubtitle;
    readonly List<Button> softButtons=new List<Button>();
    readonly System.Windows.Shapes.Path bubbleCloud;
    readonly System.Windows.Shapes.Path bubbleShadow,bubbleOutline;
    readonly Image bubbleMaterial;
    public readonly GelSurface BubbleGel;
    readonly FontFamily uiFont;
    readonly object bubbleShapes;
    readonly Dictionary<string,BitmapImage> materials=new Dictionary<string,BitmapImage>();
    readonly TextBlock bubbleSubtitle,bubbleMotif;
    readonly Image bubblePortrait;
    readonly System.Windows.Shapes.Ellipse tailA,tailB;
    readonly DialogueBook dialogue;
    public readonly BalanceService Balance;
    public readonly BotDock Dock=new BotDock();
    public readonly ToyMotion ToyPhysics=new ToyMotion();
    bool toyMoving,physicsActive,toyPressed,toyDragAnnounced,toyPinching;Point toyPointer,toyOrigin;TimeSpan physicsFrame;DateTime toyDown;
    ChatPane chat;
    ClaudeKeyWindow keyManager;
    readonly TextBlock bubbleTitle,bubbleLine,balanceAmount,balanceStatus;
    readonly Button balanceRefresh;
    public bool BubbleWanted;
    public double ClientWidth,ClientHeight,ClientScale=1;
    public Point ArtworkPosition;
    public string CurrentSpeech {get;private set;}
    bool manualBalance;

    bool botPressed,botMoving,botShift;
    Point botPointer,botOrigin;
    DateTime botDown;
    readonly TextBlock characterLabel,modeLabel;
    readonly Image characterIcon;
    readonly ContextMenu characterMenu;
    readonly CompanionPreferences preferences;
    public readonly AppearancePreferences Appearances;
    readonly Dictionary<string,BitmapSource> appearanceImages=new Dictionary<string,BitmapSource>();
    readonly List<MenuItem> appearanceItems=new List<MenuItem>();
    readonly ContextMenu appearanceMenu=new ContextMenu();
    string appearance;
    public string Appearance {get{return appearance;}}
    public string AppearanceError="";
    public double BotRatio {get;private set;}
    public string Character {get{return key;}}
    public FontFamily RoundedUiFont {get{return uiFont;}}
    public bool ChatFocused {get{return chat!=null&&chat.Focused;}}
    public double BubbleWidth {get{return Convert.ToDouble(Data.At(bubbleShapes,key,"width")??420);}}
    public double BubbleHeight {get{return Convert.ToDouble(Data.At(bubbleShapes,key,"height")??420);}}
    public bool Persistent {get{return preferences.Persistent;}}
    public string PreferenceError="";
    public bool BotEnabled=true,DollEnabled=true;
    public double OffsetX,OffsetY;
    public Action Refresh;
    public TerminalBackground Backgrounds;
    const double breathing=48;
    readonly string root;
    string key;
    public Companion(string root,string character,string preferencesPath=null) {
      this.root=root;key=Data.Character(character);uiFont=LoadRoundedFont(root);bubbleShapes=Data.Json.DeserializeObject(File.ReadAllText(System.IO.Path.Combine(root,"materials","bubble-shapes.json")));string localFolder=preferencesPath==null?System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoleCatalog.ConfigurationNamespace):System.IO.Path.Combine(System.IO.Path.GetDirectoryName(preferencesPath),"companion-test-data");Panels=new PanelPlacement(localFolder);Audio=new CompanionAudio(root,localFolder);
      Appearances=new AppearancePreferences(localFolder);appearance=Appearances.Get(key);
      dialogue=new DialogueBook(root);CurrentSpeech=dialogue.Say(key,"greeting");
      BitmapSource botBitmap;try{botBitmap=LoadAppearance(key,appearance);}catch(FileNotFoundException){appearance=AppearanceCatalog.Default(key);botBitmap=LoadAppearance(key,appearance);AppearanceError="保存的外观暂未安装，先保留原造型。";}BotRatio=(double)botBitmap.PixelHeight/botBitmap.PixelWidth;
      Bot=MakeWindow("OC Bot · "+key,210+breathing,210*BotRatio+breathing);
      BotRig=new Canvas();Bot.Content=BotRig;
      BotImage=new Image {Source=botBitmap,Stretch=Stretch.Fill,Cursor=Cursors.Hand,ToolTip="轻点展开气泡 · 按住 Q 弹 · 拖动后贴边吸附 · Ctrl + 拖动捏拉"};
      Canvas.SetLeft(BotImage,breathing);Canvas.SetTop(BotImage,breathing);BotRig.Children.Add(BotImage);SetBotSize(210);
      BotJelly=new Jelly(BotImage,true);
      BotImage.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
        botPressed=true;botMoving=false;botDown=DateTime.UtcNow;botPointer=BotImage.PointToScreen(e.GetPosition(BotImage));botOrigin=ArtworkPosition;
        Audio.Sound(key,"press");
        botShift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;if(botShift)BotImage.CaptureMouse();
      };
      BotImage.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {
        if(!botPressed || (Keyboard.Modifiers&ModifierKeys.Control)!=0)return;
        Point current=BotImage.PointToScreen(e.GetPosition(BotImage));Vector delta=current-botPointer;
        if(delta.Length>10){if(!botMoving)Audio.Touch(key,Audio.Interactions.Pick(key,"drag"));botMoving=true;}
        if(botMoving){Dock.Move(botOrigin.X+delta.X/ClientScale,botOrigin.Y+delta.Y/ClientScale,ClientWidth,ClientHeight,BotImage.Width,BotImage.Height);if(Refresh!=null)Refresh();}
      };
      BotImage.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
        if(!botPressed)return;
        bool click=!botMoving && !botShift && (DateTime.UtcNow-botDown).TotalMilliseconds<500 && (Keyboard.Modifiers&ModifierKeys.Control)==0;
        bool squeezed=!botMoving && !botShift && !click;
        bool moved=botMoving;Point hit=e.GetPosition(BotImage);EndBotGesture();Audio.Sound(key,"release");if(click){Talk(hit.Y<BotImage.Height*.35?"headPat":hit.Y<BotImage.Height*.7?"cheek":"touch");}else if(squeezed)Talk("squeeze");else if(moved)Talk("release");if(botShift)BotImage.ReleaseMouseCapture();
      };
      BotImage.LostMouseCapture+=delegate {if(botPressed)EndBotGesture();};
      Doll=MakeWindow("OC Plushie · "+key,190,190);Rig=new Canvas {Width=190,Height=190};Doll.Content=Rig;
      Toy=new Image {Width=144,Height=144,Source=Load(key+"-doll.png",360),Stretch=Stretch.Uniform,Cursor=Cursors.Hand,ToolTip="按住捏一捏 · 直接拖动搬家 · 松手滑动，碰壁 Q 弹 · Ctrl + 拖动只捏拉"};
      Canvas.SetLeft(Toy,23);Canvas.SetTop(Toy,23);Rig.Children.Add(Toy);DollJelly=new Jelly(Toy,false);
      ToyPhysics.Impact=delegate(double nx,double ny,double speed){DollJelly.Impact(nx,ny,speed);Audio.Sound(key,"impact");TouchLine line=Audio.Interactions.Pick(key,"impact");Audio.Touch(key,line);};
      Toy.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
        BeginDollGesture(Toy.PointToScreen(e.GetPosition(Toy)),(Keyboard.Modifiers&ModifierKeys.Control)!=0);
      };
      Toy.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {
        if(toyPressed)MoveDollGesture(Toy.PointToScreen(e.GetPosition(Toy)));
      };
      Toy.PreviewMouseLeftButtonUp+=delegate {EndDollGesture(DateTime.UtcNow);};
      Toy.LostMouseCapture+=delegate {toyPressed=toyDragAnnounced=toyPinching=false;ReleaseToy();};
      var menu=new ContextMenu();
      AddMenu(menu,"人物显示 / 隐藏",delegate {BotEnabled=!BotEnabled;if(Refresh!=null)Refresh();});
      AddMenu(menu,"娃娃归位",delegate {StopToyPhysics();ToyPhysics.Reset();DollJelly.Stop();if(Refresh!=null)Refresh();});
      AddMenu(menu,"收起娃娃",delegate {DollEnabled=false;if(Refresh!=null)Refresh();});
      AddMenu(menu,"Claude Code 密钥管理…",delegate {OpenKeyManager();});
      AddMenu(menu,"关闭本窗口装饰",delegate {Application.Current.Shutdown();});
      Doll.ContextMenu=menu;
      var botMenu=new ContextMenu();
      AddAppearanceChoices(botMenu);botMenu.Items.Add(new Separator());
      AddMenu(botMenu,"娃娃显示 / 隐藏",delegate {DollEnabled=!DollEnabled;if(Refresh!=null)Refresh();});
      AddMenu(botMenu,"人物归位（右下角）",delegate {Dock.Reset();BotJelly.Stop();if(Refresh!=null)Refresh();});
      AddMenu(botMenu,"查看余额 / 对话气泡",delegate {ToggleBubble();});
      AddMenu(botMenu,"Claude Code 密钥管理…",delegate {OpenKeyManager();});
      AddMenu(botMenu,"声音与 Qwen 语音",delegate {Audio.Settings(Bot,key,uiFont);});
      AddMenu(botMenu,"气泡归位",delegate {Panels.Reset(key,"bubble");if(Refresh!=null)Refresh();});
      AddMenu(botMenu,"关闭本窗口装饰",delegate {Application.Current.Shutdown();});
      Bot.ContextMenu=botMenu;
      BindInput(Bot,BotImage);BindInput(Doll,Toy);
      preferences=new CompanionPreferences(preferencesPath??System.IO.Path.Combine(localFolder,"companion-settings.json"));
      ControlScale=1;Controls=MakeWindow("OC Controls",ControlWidth,ControlHeight);Controls.FontFamily=uiFont;
      ControlPanel=new Border {CornerRadius=new CornerRadius(22),Margin=new Thickness(2),Padding=new Thickness(6,5,6,5),BorderThickness=new Thickness(1)};
      var buttons=new StackPanel {Orientation=Orientation.Horizontal};ControlPanel.Child=buttons;Controls.Content=ControlPanel;
      controlMotif=new TextBlock {Width=24,FontSize=18,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center};buttons.Children.Add(controlMotif);controlDrag=new PanelDrag(Controls,controlMotif,Panels,"controls",delegate{return key;},delegate{if(Refresh!=null)Refresh();});
      CharacterButton=MakeButton(92);CharacterButton.ToolTip=RoleCatalog.Name(key)+" · 显示、外观与背景设置";
      var label=new StackPanel {Orientation=Orientation.Horizontal};
      characterIcon=new Image {Width=20,Height=20,Stretch=Stretch.Uniform};characterLabel=new TextBlock {Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Center};
      label.Children.Add(characterIcon);label.Children.Add(characterLabel);CharacterButton.Content=label;buttons.Children.Add(CharacterButton);
      controlDivider=new Border {Width=1,Height=14,VerticalAlignment=VerticalAlignment.Center};buttons.Children.Add(controlDivider);
      ModeButton=MakeButton(74);modeLabel=new TextBlock {VerticalAlignment=VerticalAlignment.Center};ModeButton.Content=modeLabel;buttons.Children.Add(ModeButton);
      AppearanceButton=MakeButton(76);AppearanceButton.FontWeight=FontWeights.SemiBold;AppearanceButton.Click+=delegate{SetAppearance(appearance=="bot"?"chibi":"bot");};AddAppearanceChoices(appearanceMenu);AppearanceButton.ContextMenu=appearanceMenu;buttons.Children.Add(AppearanceButton);
      AudioButton=MakeButton(36);AudioButton.Content="♫";AudioButton.ToolTip="音效开关与 Qwen 语音设置";AudioButton.Click+=delegate{Audio.Settings(Controls,key,uiFont);UpdateControls();};buttons.Children.Add(AudioButton);
      ModeButton.Click+=delegate {SetPersistent(!Persistent);};
      characterMenu=new ContextMenu {Background=Brush("#28212E"),Foreground=Brush("#F2E6EE"),BorderBrush=Brush("#68586D"),FontSize=12};
      foreach(string role in RoleCatalog.All) {
        string selected=role;var item=new MenuItem {Header=RoleName(role),Icon=new Image {Source=Load(role+"-tab-avatar.png",64),Width=24,Height=24},Tag=role,IsCheckable=true};
        item.Click+=delegate {SetCharacter(selected);};characterMenu.Items.Add(item);
      }
      characterMenu.Items.Add(new Separator());AddMenu(characterMenu,"显示娃娃与 Bot",delegate {BotEnabled=DollEnabled=true;if(Refresh!=null)Refresh();});
      AddMenu(characterMenu,"Claude Code 密钥管理…",delegate {OpenKeyManager();});
      AddMenu(characterMenu,"更换终端背景图片…",delegate {if(Backgrounds==null)return;var picker=new Microsoft.Win32.OpenFileDialog{Title="选择终端背景图片",Filter="图片|*.png;*.jpg;*.jpeg",CheckFileExists=true};if(picker.ShowDialog(Controls)==true){try{Backgrounds.Choose(key,picker.FileName);}catch(Exception error){MessageBox.Show(Controls,error is InvalidDataException?error.Message:"图片未能应用。","背景图片");}}});
      AddMenu(characterMenu,"恢复人物背景",delegate {if(Backgrounds!=null)Backgrounds.Restore(key);});
      AddMenu(characterMenu,"选项栏归位",delegate{Panels.Reset(key,"controls");if(Refresh!=null)Refresh();});
      CharacterButton.ContextMenu=characterMenu;
      CharacterButton.Click+=delegate {characterMenu.PlacementTarget=CharacterButton;characterMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;characterMenu.IsOpen=true;};
      BindPassiveWindow(Controls);UpdateControls();
      Bubble=MakeWindow("OC Dialogue",BubbleWidth,BubbleHeight);Bubble.FontFamily=uiFont;
      BubbleRig=new Grid();Bubble.Content=BubbleRig;
      var cloud=Geometry.Empty;
      bubbleShadow=new System.Windows.Shapes.Path {Data=cloud,Stretch=Stretch.Fill,Margin=new Thickness(6,9,8,18),Fill=Brush("#2020182E"),IsHitTestVisible=false};BubbleRig.Children.Add(bubbleShadow);
      bubbleOutline=new System.Windows.Shapes.Path {Data=cloud,Stretch=Stretch.Fill,Margin=new Thickness(3,3,11,23),Stroke=Brush("#16FFFFFF"),StrokeThickness=1,IsHitTestVisible=false};BubbleRig.Children.Add(bubbleOutline);
      bubbleCloud=new System.Windows.Shapes.Path {Data=cloud,Stretch=Stretch.Fill,Margin=new Thickness(3,3,11,23),StrokeThickness=.3,IsHitTestVisible=false};BubbleRig.Children.Add(bubbleCloud);
      BubbleGel=new GelSurface();BubbleRig.Children.Add(BubbleGel);
      bubbleMaterial=new Image {Stretch=Stretch.Fill,Opacity=.001,IsHitTestVisible=false};BubbleRig.Children.Add(bubbleMaterial);
      BubbleHitSurface=new System.Windows.Shapes.Path{Fill=Brush("#03FFFFFF"),Stretch=Stretch.None};BubbleRig.Children.Add(BubbleHitSurface);
      BubbleRig.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(ButtonSource(e.OriginalSource as DependencyObject))BubbleGel.Nudge(e.GetPosition(BubbleRig));};
      BubbleRig.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.Handled)return;if(ButtonSource(e.OriginalSource as DependencyObject)){BubbleGel.Nudge(e.GetPosition(BubbleRig));return;}BubbleGel.Press(e.GetPosition(BubbleRig));BubbleRig.CaptureMouse();e.Handled=true;};
      BubbleRig.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){if(BubbleRig.IsMouseCaptured && (bubbleDrag==null||!bubbleDrag.Active))BubbleGel.Pull(e.GetPosition(BubbleRig));};
      BubbleRig.PreviewMouseLeftButtonUp+=delegate {if(BubbleRig.IsMouseCaptured && (bubbleDrag==null||!bubbleDrag.Active)){BubbleGel.Release();BubbleRig.ReleaseMouseCapture();}};
      BubbleRig.LostMouseCapture+=delegate {BubbleGel.Release();};
      BubblePanel=new Border {CornerRadius=new CornerRadius(24),Margin=new Thickness(30,24,30,30),Background=Brushes.Transparent,CacheMode=new BitmapCache(1.5)};
      TextOptions.SetTextRenderingMode(BubblePanel,TextRenderingMode.Grayscale);
      BubbleRig.Children.Add(BubblePanel);var bubbleBody=new StackPanel();BubblePanel.Child=bubbleBody;
      tailA=new System.Windows.Shapes.Ellipse {Width=12,Height=12,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,10,7),StrokeThickness=1,IsHitTestVisible=false};
      tailB=new System.Windows.Shapes.Ellipse {Width=6,Height=6,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,1,0),StrokeThickness=1,IsHitTestVisible=false};BubbleRig.Children.Add(tailA);BubbleRig.Children.Add(tailB);
      var header=new DockPanel();var close=MakeButton(22,true);close.Tag="bubble-close";close.Content="×";close.ToolTip="收起气泡";DockPanel.SetDock(close,System.Windows.Controls.Dock.Right);header.Children.Add(close);close.Click+=delegate {BubbleWanted=false;if(Refresh!=null)Refresh();};
      bubblePortrait=new Image {Width=44,Height=44,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,9,0)};DockPanel.SetDock(bubblePortrait,System.Windows.Controls.Dock.Left);header.Children.Add(bubblePortrait);
      var heading=new StackPanel {VerticalAlignment=VerticalAlignment.Center};bubbleTitle=new TextBlock {FontSize=15,FontWeight=FontWeights.Bold,FontFamily=uiFont};bubbleSubtitle=new TextBlock {FontSize=11,Margin=new Thickness(0,4,0,0)};heading.Children.Add(bubbleTitle);heading.Children.Add(bubbleSubtitle);header.Children.Add(heading);bubbleBody.Children.Add(header);
      bubbleDrag=new PanelDrag(Bubble,BubbleRig,Panels,"bubble",delegate{return key;},delegate{if(Refresh!=null)Refresh();});bubbleDrag.Allow=delegate{return (Keyboard.Modifiers&ModifierKeys.Control)==0;};bubbleDrag.Press=BubbleGel.Press;bubbleDrag.Pull=BubbleGel.Pull;bubbleDrag.Release=BubbleGel.Release;BubbleRig.ToolTip="直接拖动气泡 · Ctrl + 拖动原地捏拉 · 双击归位";bubbleSubtitle.ToolTip=BubbleRig.ToolTip;
      speechSurface=new Border {CornerRadius=new CornerRadius(20),Height=70,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,12,0,14),Background=Brush("#62352A44")};bubbleLine=new TextBlock {FontSize=16,FontFamily=uiFont,TextWrapping=TextWrapping.Wrap,LineHeight=25};speechSurface.Child=bubbleLine;bubbleBody.Children.Add(speechSurface);
      wallet=new Border {CornerRadius=new CornerRadius(24),Padding=new Thickness(13,6,13,6),Height=48,BorderThickness=new Thickness(1)};var money=new DockPanel();wallet.Child=money;
      balanceAmount=new TextBlock {Text="—",FontSize=20,FontFamily=uiFont,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(balanceAmount,System.Windows.Controls.Dock.Right);money.Children.Add(balanceAmount);
      var account=new StackPanel {VerticalAlignment=VerticalAlignment.Center};accountTitle=new TextBlock {Text="小账本 · 可用额度",FontSize=12,Foreground=Brush("#FFF0F9")};accountSubtitle=new TextBlock {Text="GPTEAM · 当前密钥",FontSize=10,Foreground=Brush("#DECDE0"),Margin=new Thickness(0,2,0,0)};account.Children.Add(accountTitle);account.Children.Add(accountSubtitle);money.Children.Add(account);bubbleBody.Children.Add(wallet);
      balanceStatus=new TextBlock {FontSize=10,Height=28,Margin=new Thickness(2,6,0,8),TextWrapping=TextWrapping.Wrap,Foreground=Brush("#DECDE0")};bubbleBody.Children.Add(balanceStatus);
      var actions=new DockPanel {HorizontalAlignment=HorizontalAlignment.Center};bubbleMotif=new TextBlock {FontSize=19,Width=24,Visibility=Visibility.Collapsed};
      var actionButtons=new Grid();actionButtons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});actionButtons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(8)});actionButtons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});actionButtons.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});actionButtons.RowDefinitions.Add(new RowDefinition{Height=new GridLength(4)});actionButtons.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});
      var next=MakeButton(120,true);next.Tag="bubble-action";next.Content="聊一会儿 ♡";next.Click+=delegate {OpenChat();};actionButtons.Children.Add(next);
      balanceRefresh=MakeButton(120,true);balanceRefresh.Tag="bubble-action";balanceRefresh.Content="小账本 ↻";balanceRefresh.Click+=delegate {manualBalance=Balance.Refresh();};Grid.SetColumn(balanceRefresh,2);actionButtons.Children.Add(balanceRefresh);
      var manageKey=MakeButton(120,true);manageKey.Tag="bubble-action";manageKey.Content="密钥管理";manageKey.ToolTip="管理 Claude Code 的 API Key 与服务地址";manageKey.Click+=delegate{OpenKeyManager();};Grid.SetRow(manageKey,2);actionButtons.Children.Add(manageKey);
      var recharge=MakeButton(120,true);recharge.Tag="bubble-action";recharge.Content="充值 ↗";recharge.ToolTip="打开 GPTEAM 计价官网，可前往充值";recharge.Click+=delegate {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://gpteamservices.com/"){UseShellExecute=true});};Grid.SetRow(recharge,2);Grid.SetColumn(recharge,2);actionButtons.Children.Add(recharge);actions.Children.Add(actionButtons);bubbleBody.Children.Add(actions);
      Balance=new BalanceService(System.Windows.Threading.Dispatcher.CurrentDispatcher);Balance.Changed=delegate {
        if(manualBalance && !Balance.Busy){manualBalance=false;CurrentSpeech=dialogue.Say(key,Balance.Current.Status!="已更新"?"error":Balance.Current.Amount.HasValue && Balance.Current.Amount.Value<1?"lowBalance":"balance");}
        UpdateBubble();
      };BindBubbleInput();UpdateBubble();
    }
    void ApplyBubbleShape() {
      string outline=Convert.ToString(Data.At(bubbleShapes,key,"path"));
      Geometry shape=Geometry.Parse(outline);shape.Freeze();bubbleCloud.Data=bubbleShadow.Data=bubbleOutline.Data=BubbleHitSurface.Data=shape;
      BubbleRig.Width=BubbleWidth;BubbleRig.Height=BubbleHeight;
      var margin=(object[])Data.At(bubbleShapes,key,"margin");BubblePanel.Margin=new Thickness(Convert.ToDouble(margin[0]),Convert.ToDouble(margin[1]),Convert.ToDouble(margin[2]),Convert.ToDouble(margin[3]));
      // The authored alpha layer is the single silhouette; extra path strokes caused doubled edges.
      bubbleShadow.Visibility=bubbleOutline.Visibility=bubbleCloud.Visibility=Visibility.Collapsed;
      bubbleMotif.Visibility=Visibility.Collapsed;
      BitmapImage material;
      if(!materials.TryGetValue(key,out material)) {
        string path=System.IO.Path.Combine(root,"assets","materials",key+"-gel.png");
        if(File.Exists(path)){material=new BitmapImage();material.BeginInit();material.CacheOption=BitmapCacheOption.OnLoad;material.UriSource=new Uri(path);material.DecodePixelWidth=960;material.EndInit();material.Freeze();materials[key]=material;}
      }
      bubbleMaterial.Source=material;BubbleGel.SetMaterial(material,BubbleWidth,BubbleHeight);
    }
    static bool ButtonSource(DependencyObject node){while(node!=null){if(node is Button)return true;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return false;}
    public void BeginDollGesture(Point screen,bool pinching){toyPressed=true;toyDragAnnounced=false;toyPinching=pinching;toyDown=DateTime.UtcNow;toyPointer=screen;toyOrigin=ToyPhysics.Position;Audio.Sound(key,"press");if(!pinching){toyMoving=true;ToyPhysics.Grab();}}
    public void MoveDollGesture(Point screen){if(!toyPressed||toyPinching||!toyMoving)return;Vector delta=(screen-toyPointer)/Math.Max(.01,ClientScale);if(delta.Length<=5)return;if(!toyDragAnnounced){toyDragAnnounced=true;Audio.Touch(key,Audio.Interactions.Pick(key,"drag"));}ToyPhysics.Drag(toyOrigin+delta);if(!Toy.IsMouseCaptured)Toy.CaptureMouse();if(Refresh!=null)Refresh();}
    public string EndDollGesture(DateTime now){if(!toyPressed)return null;string context=toyDragAnnounced?"release":toyPinching||(now-toyDown).TotalMilliseconds>=500?"squeeze":"dollTouch";toyPressed=toyDragAnnounced=toyPinching=false;ReleaseToy();Audio.Sound(key,"release");Talk(context);return context;}
    void ReleaseToy() {if(!toyMoving)return;toyMoving=false;ToyPhysics.Release();if(SystemParameters.ClientAreaAnimation && ToyPhysics.Velocity.Length>=3)WakeToyPhysics();else ToyPhysics.Suspend();}
    void WakeToyPhysics(){if(physicsActive)return;physicsActive=true;physicsFrame=TimeSpan.Zero;CompositionTarget.Rendering+=RenderToyPhysics;}
    void RenderToyPhysics(object sender,EventArgs e){var now=((RenderingEventArgs)e).RenderingTime;double dt=physicsFrame==TimeSpan.Zero?1.0/60:Math.Max(.001,Math.Min(.032,(now-physicsFrame).TotalSeconds));physicsFrame=now;if(!ToyPhysics.Tick(dt))StopToyPhysics();if(Refresh!=null)Refresh();}
    void StopToyPhysics(){if(physicsActive){CompositionTarget.Rendering-=RenderToyPhysics;physicsActive=false;}ToyPhysics.Suspend();toyMoving=toyPressed=toyDragAnnounced=toyPinching=false;}
    public bool ToyPhysicsRunning {get{return physicsActive;}}
    public void OpenChat() {
      if(chat==null)chat=new ChatPane(root,key,uiFont,delegate(string role,string reply){if(role!=key)return;CurrentSpeech=reply.Length>32?reply.Substring(0,31)+"…":reply;BubbleWanted=false;UpdateBubble();Audio.Reply(role,reply);if(Refresh!=null)Refresh();},Audio.Voice.Folder,Panels,Audio);
      BubbleWanted=false;
      chat.SetCharacter(key);chat.ShowFor(Bot);if(Refresh!=null)Refresh();
    }
    public void PlaceChat(Native.Point origin,int width,int height,double dpi,bool visible){if(chat==null)return;chat.Follow(origin,width,height,dpi,visible);}
    public void OpenKeyManager(){
      if(keyManager!=null&&keyManager.Window.IsVisible){keyManager.Window.Activate();return;}
      keyManager=new ClaudeKeyWindow(Bubble.IsVisible?Bubble:Bot,key,uiFont,delegate{manualBalance=false;Balance.ConfigurationChanged();UpdateBubble();});
      keyManager.Window.Show();keyManager.Window.Activate();
    }
    public void Shutdown(){Stop();Audio.Close();if(chat!=null)chat.Shutdown();if(keyManager!=null)keyManager.Window.Close();}
    void EndBotGesture() {
      if(botMoving){Dock.Snap(ClientWidth,ClientHeight,BotImage.Width,BotImage.Height);if(BubbleWanted){CurrentSpeech=dialogue.Say(key,"dock");UpdateBubble();}}
      botPressed=botMoving=false;if(Refresh!=null)Refresh();
    }
    public void ToggleBubble() {BubbleWanted=!BubbleWanted;if(BubbleWanted){TouchLine line=Audio.Interactions.Pick(key,"greeting");CurrentSpeech=line==null?dialogue.Say(key,"greeting"):line.Chinese;UpdateBubble();Balance.Refresh();Audio.Touch(key,line);}if(Refresh!=null)Refresh();}
    public void Talk(string context) {
      BubbleWanted=true;TouchLine line=Audio.Interactions.Pick(key,context);CurrentSpeech=line==null?dialogue.Say(key,context):line.Chinese;if(line==null)Audio.Reaction(key,CurrentSpeech);else Audio.Touch(key,line);UpdateBubble();if(Refresh!=null)Refresh();
    }
    public void AnimateBubble() {
      if(!SystemParameters.ClientAreaAnimation)return;
      var transform=new ScaleTransform(1,1);BubbleRig.RenderTransform=transform;BubbleRig.RenderTransformOrigin=new Point(Dock.Horizontal<0?.1:.9,.95);
      var animation=new DoubleAnimation(.94,1,TimeSpan.FromMilliseconds(160)) {EasingFunction=new BackEase {Amplitude=.6,EasingMode=EasingMode.EaseOut}};
      transform.BeginAnimation(ScaleTransform.ScaleXProperty,animation);transform.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
    }
    Brush Theme(string field,string fallback) {try{return Brush(dialogue.Text(key,field,fallback));}catch{return Brush(fallback);}}
    void UpdateBubble() {
      if(bubbleTitle==null)return;
      ApplyBubbleShape();
      bubbleTitle.Text=dialogue.Text(key,"name",RoleName(key));bubbleSubtitle.Text=dialogue.Text(key,"subtitle","");bubbleLine.Text=CurrentSpeech;
      bubblePortrait.Source=Load(key+"-tab-avatar.png",64);bubbleMotif.Text=dialogue.Text(key,"motif","✿");
      Brush accent=Theme("nameColor","#9D607E");bubbleTitle.Foreground=balanceAmount.Foreground=bubbleMotif.Foreground=accent;bubbleSubtitle.Foreground=accent;bubbleSubtitle.Opacity=.8;bubbleLine.Foreground=Theme("ink","#765568");
      accountTitle.Foreground=Theme("ink","#FFF0F9");accountSubtitle.Foreground=balanceStatus.Foreground=Theme("mutedInk","#DECDE0");speechSurface.Background=Theme("speechSurface","#62352A44");
      if(Balance!=null)accountSubtitle.Text=Balance.Current.Provider+" · 当前密钥";
      foreach(Button button in softButtons){button.Foreground=Theme("buttonInk","#FFF0FA");button.Background=Theme("buttonSurface","#48312A46");button.BorderBrush=Theme("border","#86EEE0F2");}
      var top=(SolidColorBrush)Theme("paper","#FFFFFCFA");var bottom=(SolidColorBrush)Theme("paperBottom","#FBE7EF");
      var paper=new LinearGradientBrush(top.Color,bottom.Color,new Point(0,0),new Point(.1,1));paper.Freeze();bubbleCloud.Fill=paper;
      tailA.Fill=tailB.Fill=bottom;bubbleCloud.Stroke=wallet.BorderBrush=tailA.Stroke=tailB.Stroke=Theme("border","#E3BECD");wallet.Background=Theme("wallet","#FFF8FB");
      if(Balance==null)return;BalanceSnapshot value=Balance.Current;balanceAmount.Text=value.Display;
      balanceStatus.Text=Balance.Busy?"正在刷新…":value.Status;
      if(value.Updated!=DateTime.MinValue)balanceStatus.Text+=(value.Stale?" · 上次结果":"")+"\n"+value.Updated.ToLocalTime().ToString("HH:mm:ss")+" · 每分钟翻一次小账本";
      balanceRefresh.IsEnabled=!Balance.Busy;
    }
    bool mirrored;
    public void SetDockAppearance() {
      bool left=Dock.Horizontal<0,top=Dock.Vertical<0;
      Canvas.SetLeft(BotImage,left?0:breathing);Canvas.SetTop(BotImage,top?0:breathing);
      if(left!=mirrored){mirrored=left;BotImage.LayoutTransform=new ScaleTransform(left?-1:1,1);}
      BotImage.RenderTransformOrigin=new Point(left?0:1,top?0:1);
      tailA.HorizontalAlignment=tailB.HorizontalAlignment=left?HorizontalAlignment.Left:HorizontalAlignment.Right;
      tailA.Margin=new Thickness(left?10:0,0,left?0:10,7);tailB.Margin=new Thickness(left?1:0,0,left?0:1,0);
      BubblePanel.CornerRadius=left?new CornerRadius(28,28,28,14):new CornerRadius(28,28,14,28);
    }
    public double BotInsetX {get{return Dock.Horizontal<0?0:breathing;}}
    public double BotInsetY {get{return Dock.Vertical<0?0:breathing;}}
    static Brush Brush(string color) {var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(color);brush.Freeze();return brush;}
    static FontFamily LoadRoundedFont(string root) {
      string folder=System.IO.Path.Combine(root,"assets","fonts");
      if(File.Exists(System.IO.Path.Combine(folder,"ChillRoundFRegular.ttf")))return new FontFamily(new Uri(folder+System.IO.Path.DirectorySeparatorChar),"./#ChillRoundF");
      return new FontFamily("幼圆, Microsoft YaHei UI");
    }
    Button MakeButton(double width,bool soft=false) {
      var button=new Button {Width=width,Height=soft?34:28,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=Brush(soft?"#FFF0FA":"#F2E6EE"),Cursor=Cursors.Hand,Focusable=false,FontFamily=uiFont,FontSize=soft?13:12};
      var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border),"surface");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(13));
      border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Button.BackgroundProperty));if(soft){softButtons.Add(button);border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Button.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));}
      var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;
      var hover=new Trigger {Property=Button.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(UIElement.OpacityProperty,.78));template.Triggers.Add(hover);
      var pressed=new Trigger {Property=Button.IsPressedProperty,Value=true};pressed.Setters.Add(new Setter(UIElement.OpacityProperty,.6));template.Triggers.Add(pressed);button.Template=template;return button;
    }
    static string RoleName(string role) {return RoleCatalog.Name(role);}
    void AddAppearanceChoices(ContextMenu menu){foreach(string style in AppearanceCatalog.Styles){string selected=style;var item=new MenuItem{Header=AppearanceCatalog.Name(style),Tag=style,IsCheckable=true};item.Click+=delegate{SetAppearance(selected);};menu.Items.Add(item);appearanceItems.Add(item);}}
    void UpdateAppearanceControls(){if(AppearanceButton==null)return;RoleVisual style=RoleVisual.For(key);AppearanceButton.Content=appearance=="bot"?"圆眼 ⇄":"Q 版 ⇄";AppearanceButton.Foreground=Brush(style.Ink);AppearanceButton.Background=Brush(style.Soft);AppearanceButton.ToolTip=AppearanceError.Length>0?AppearanceError:"当前："+AppearanceCatalog.Name(appearance)+" · 点击切换圆眼 Bot / 精致 Q 版；右键选择。每位角色单独记住。";appearanceMenu.Background=Brush(style.Paper);appearanceMenu.Foreground=Brush(style.Ink);appearanceMenu.BorderBrush=Brush(style.Line);foreach(MenuItem item in appearanceItems)item.IsChecked=(string)item.Tag==appearance;}
    void UpdateControls() {
      RoleVisual style=RoleVisual.For(key);ControlPanel.CornerRadius=style.StripCorners;ControlPanel.Background=style.Glow;ControlPanel.BorderBrush=Brush(style.Line);controlDivider.Background=Brush(style.Line);controlMotif.Text=style.Motif;controlMotif.Foreground=Brush(style.Accent);ModeButton.Foreground=AudioButton.Foreground=Brush(style.Ink);characterMenu.Background=Brush(style.Paper);characterMenu.Foreground=Brush(style.Ink);characterMenu.BorderBrush=Brush(style.Line);
      characterLabel.Text=RoleName(key)+" ▾";characterIcon.Source=Load(key+"-tab-avatar.png",64);
      characterLabel.Foreground=Brush(style.Ink);
      modeLabel.Text=Persistent?"常驻 ●":"仅聚焦 ○";
      ModeButton.ToolTip=Persistent?"当前为常驻：终端失去焦点仍保留人物。点击改为仅聚焦。":"当前仅聚焦时显示人物。点击改为常驻。";
      foreach(object entry in characterMenu.Items) {var item=entry as MenuItem;if(item!=null && item.Tag!=null)item.IsChecked=(string)item.Tag==key;}
      UpdateAppearanceControls();
    }
    public void SetPersistent(bool value) {
      try {preferences.Set(value);PreferenceError="";}catch(Exception error){preferences.Persistent=value;PreferenceError=error.Message;}
      UpdateControls();if(Refresh!=null)Refresh();
    }
    public void ReloadPreferences() {if(preferences.Reload())UpdateControls();if(Appearances.Reload()){string selected=Appearances.Get(key);if(selected!=appearance)SetAppearance(selected,false);else{AppearanceError="";UpdateAppearanceControls();}}if(Appearances.Error.Length>0){AppearanceError=Appearances.Error;UpdateAppearanceControls();}Audio.Voice.ReloadIfChanged();Audio.Interactions.ReloadIfChanged();AudioButton.ToolTip=Audio.LastStatus.Length>0?Audio.LastStatus:Audio.Voice.ConfigurationError.Length>0?Audio.Voice.ConfigurationError:"音效开关与 Qwen 语音设置 · 点击语音会完整说完后接下一句";}
    BitmapSource LoadAppearance(string role,string style){string path=AppearanceCatalog.File(root,role,style);BitmapSource image;if(!appearanceImages.TryGetValue(path,out image)){image=CropTransparent(LoadPath(path,480));appearanceImages[path]=image;}return image;}
    void ApplyAppearance(BitmapSource image,string style){botPressed=botMoving=botShift=false;BotJelly.Stop();BotImage.Source=image;BotRatio=(double)image.PixelHeight/image.PixelWidth;appearance=style;double width=ClientWidth>0&&ClientHeight>0?FitBotWidth(ClientWidth,ClientHeight):BotImage.Width;SetBotSize(width);if(ClientWidth>0&&ClientHeight>0)ArtworkPosition=Dock.Position(ClientWidth,ClientHeight,BotImage.Width,BotImage.Height);SetDockAppearance();}
    public bool SetAppearance(string style,bool persist=true){try{if(!AppearanceCatalog.Valid(style))throw new InvalidDataException("请选择圆眼 Bot 或精致 Q 版。");if(style==appearance){if(persist)Appearances.Set(key,style);AppearanceError="";UpdateAppearanceControls();return true;}var image=LoadAppearance(key,style);if(persist)Appearances.Set(key,style);ApplyAppearance(image,style);AppearanceError="";UpdateAppearanceControls();if(Refresh!=null)Refresh();return true;}catch(Exception error){AppearanceError=error is FileNotFoundException?"这款外观图片暂未安装；当前造型不变。":error is InvalidDataException?error.Message:"外观选择未能保存；当前造型不变。";UpdateAppearanceControls();return false;}}
    public void SetCharacter(string character) {
      Audio.StopVoice();
      string next=Data.Character(character);
      // Load both assets first; a missing file must not leave the pair mismatched.
      string nextAppearance=Appearances.Get(next);var bot=LoadAppearance(next,nextAppearance);var doll=Load(next+"-doll.png",360);
      Stop();key=next;BotImage.Source=bot;Toy.Source=doll;BotRatio=(double)bot.PixelHeight/bot.PixelWidth;
      appearance=nextAppearance;AppearanceError="";double width=ClientWidth>0&&ClientHeight>0?FitBotWidth(ClientWidth,ClientHeight):BotImage.Width;SetBotSize(width);if(ClientWidth>0&&ClientHeight>0)ArtworkPosition=Dock.Position(ClientWidth,ClientHeight,BotImage.Width,BotImage.Height);SetDockAppearance();
      Bot.Title="OC Bot · "+key;Doll.Title="OC Plushie · "+key;CurrentSpeech=dialogue.Say(key,"greeting");if(chat!=null)chat.SetCharacter(key);UpdateControls();UpdateBubble();if(Backgrounds!=null)Backgrounds.Apply(key,false);if(Refresh!=null)Refresh();
    }
    public bool MenuOpen {get{return Doll.ContextMenu.IsOpen || Bot.ContextMenu.IsOpen || characterMenu.IsOpen||appearanceMenu.IsOpen||Audio.PanelOpen||(keyManager!=null&&keyManager.Window.IsVisible);}}
    public Point PanelPosition(string panel,Native.Point origin,double width,double height,double dpi,double pw,double ph,Point fallback){if(panel=="controls")controlDrag.Bounds(origin,width,height,dpi,pw,ph);if(panel=="bubble")bubbleDrag.Bounds(origin,width,height,dpi,pw,ph);return Panels.Position(key,panel,width,height,pw,ph,fallback);}
    public void SetControlClientBounds(double width,double height){Rect safe=PanelPlacement.Safe(width,height);double scale=Math.Max(.01,Math.Min(1,Math.Min((safe.Width-4)/(ControlWidth-4),(safe.Height-4)/(ControlHeight-4))));if(Math.Abs(ControlScale-scale)<.00001)return;ControlScale=scale;ControlPanel.LayoutTransform=scale==1?Transform.Identity:new ScaleTransform(scale,scale);}
    public void SetBotSize(double width) {
      if(BotImage!=null && Math.Abs(BotImage.Width-width)<.01&&Math.Abs(BotImage.Height-width*BotRatio)<.01)return;
      BotImage.Width=width;BotImage.Height=width*BotRatio;BotRig.Width=width+breathing;BotRig.Height=width*BotRatio+breathing;
    }
    public double FitBotWidth(double width,double height){double ratio=Math.Max(.01,BotRatio);double preferred=Math.Max(110,Math.Min(210,Math.Min(width*.22,height*.45/ratio)));return Math.Min(preferred,Math.Max(1,Math.Min(width,height/ratio)));}
    public double BotPadding {get{return breathing;}}
    static Window MakeWindow(string title,double width,double height) {return new Window {Title=title,Width=width,Height=height,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Focusable=false,Topmost=false};}
    BitmapSource Load(string file,int width) {return LoadPath(System.IO.Path.Combine(root,"assets",file),width);}
    BitmapSource LoadPath(string path,int width) {
      var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=width;
      bitmap.UriSource=new Uri(path);bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    static BitmapSource CropTransparent(BitmapSource bitmap) {
      var rgba=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);int w=rgba.PixelWidth,h=rgba.PixelHeight;var bytes=new byte[w*h*4];rgba.CopyPixels(bytes,w*4,0);
      int left=w,top=h,right=0,bottom=0;for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(bytes[(y*w+x)*4+3]>0){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
      if(left>=w)return bitmap;var cropped=new CroppedBitmap(bitmap,new Int32Rect(left,top,right-left+1,bottom-top+1));cropped.Freeze();return cropped;
    }
    static void BindInput(Window window,Image image) {
      BitmapSource source=null;int w=0,h=0;byte[] bytes=null;
      window.SourceInitialized+=delegate {
        var host=(HwndSource)PresentationSource.FromVisual(window);
        Native.SetWindowLong(host.Handle,-20,Native.GetWindowLong(host.Handle,-20)|0x08000000|0x80);
        host.AddHook(delegate(IntPtr handle,int message,IntPtr wp,IntPtr lp,ref bool handled) {
          if(message==0x21){handled=true;return new IntPtr(3);}
          if(message==0x84 && !image.IsMouseCaptured && !window.IsMouseCaptureWithin) {
            if(source!=image.Source) {
              source=(BitmapSource)image.Source;var rgba=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);w=rgba.PixelWidth;h=rgba.PixelHeight;
              bytes=new byte[w*h*4];rgba.CopyPixels(bytes,w*4,0);
            }
            long v=lp.ToInt64();var p=image.PointFromScreen(new Point((short)(v&65535),(short)((v>>16)&65535)));
            double sx=image.ActualWidth/w,sy=image.ActualHeight/h;
            if(image.Stretch==Stretch.Uniform)sx=sy=Math.Min(sx,sy);
            int px=(int)Math.Floor((p.X-(image.ActualWidth-w*sx)/2)/sx),py=(int)Math.Floor((p.Y-(image.ActualHeight-h*sy)/2)/sy);
            if(px<0 || py<0 || px>=w || py>=h || bytes[(py*w+px)*4+3]<=32){handled=true;return new IntPtr(-1);}
          }return IntPtr.Zero;
        });
      };
    }
    static void BindPassiveWindow(Window window) {
      window.SourceInitialized+=delegate {
        var host=(HwndSource)PresentationSource.FromVisual(window);
        Native.SetWindowLong(host.Handle,-20,Native.GetWindowLong(host.Handle,-20)|0x08000000|0x80);
        host.AddHook(delegate(IntPtr handle,int message,IntPtr wp,IntPtr lp,ref bool handled) {
          if(message==0x21){handled=true;return new IntPtr(3);}
          if(message==0x84){long v=lp.ToInt64();var point=window.PointFromScreen(new Point((short)(v&65535),(short)((v>>16)&65535)));var hit=window.InputHitTest(point);if(hit==null || hit==window || hit==window.Content){handled=true;return new IntPtr(-1);}}
          return IntPtr.Zero;
        });
      };
    }
    public bool BubbleContains(Point p){return BubbleHitSurface.Data!=null&&BubbleHitSurface.Data.FillContains(p);}
    void BindBubbleInput(){
      Bubble.SourceInitialized+=delegate{
        var host=HwndSource.FromHwnd(new WindowInteropHelper(Bubble).Handle);Native.SetWindowLong(host.Handle,-20,Native.GetWindowLong(host.Handle,-20)|0x08000000|0x80);
        host.AddHook(delegate(IntPtr h,int message,IntPtr wp,IntPtr lp,ref bool handled){
          if(message==0x21){handled=true;return new IntPtr(3);}
          if(message==0x84){handled=true;if(Bubble.IsMouseCaptureWithin)return new IntPtr(1);long v=lp.ToInt64();var p=BubbleRig.PointFromScreen(new Point((short)(v&65535),(short)((v>>16)&65535)));return new IntPtr(BubbleContains(p)?1:-1);}
          return IntPtr.Zero;
        });
      };
    }
    static void AddMenu(ContextMenu menu,string label,Action action){var item=new MenuItem {Header=label};item.Click+=delegate{action();};menu.Items.Add(item);}
    public void BeginSqueeze(Point p){DollJelly.Press(p);}
    public void DragSqueeze(Point p){DollJelly.Pull(p);}
    public void EndSqueeze(){DollJelly.Release();}
    public void Tick(double dt){DollJelly.Tick(dt);}
    public int SqueezeCount {get{return DollJelly.Count;}}
    public double Compression {get{return DollJelly.Compression;}}
    public bool SpringRunning {get{return DollJelly.Running;}}
    public void FreezeForTest(bool squeezed){DollJelly.Freeze(squeezed);}
    public void Stop(){botPressed=botMoving=false;StopToyPhysics();DollJelly.Stop();BotJelly.Stop();BubbleGel.Stop();}
  }
}
