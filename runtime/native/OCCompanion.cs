using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OCShell {
  public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] public struct ProcessEntry {
      public uint Size, Usage, Id; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string Name;
    }
    public delegate bool EnumProc(IntPtr handle, IntPtr data);
    public delegate void WinEventProc(IntPtr hook,uint eventType,IntPtr hwnd,int objectId,int childId,uint eventThread,uint eventTime);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref Point p);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int count);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int message,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll", EntryPoint="GetWindowLong")] public static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLong")] public static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("kernel32.dll")] public static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] public static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] public static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static string Title(IntPtr h) { var s=new StringBuilder(1024); GetWindowText(h,s,s.Capacity); return s.ToString(); }
    public static double Scale(IntPtr h) { try { return Math.Max(96,GetDpiForWindow(h))/96.0; } catch { return 1; } }
    public static HashSet<uint> Ancestors(int id) {
      var parents=new Dictionary<uint,uint>(); var snapshot=CreateToolhelp32Snapshot(2,0);
      var e=new ProcessEntry(); e.Size=(uint)Marshal.SizeOf(e);
      if(Process32First(snapshot,ref e)) do { parents[e.Id]=e.Parent; } while(Process32Next(snapshot,ref e));
      CloseHandle(snapshot); var ids=new HashSet<uint>(); uint current=(uint)id;
      while(current>0 && ids.Add(current) && parents.ContainsKey(current)) current=parents[current]; return ids;
    }
    public static IntPtr FindTerminal(int parent,string title) {
      var ancestors=Ancestors(parent); var matches=new List<IntPtr>();
      EnumWindows(delegate(IntPtr h,IntPtr unused) {
        uint pid; GetWindowThreadProcessId(h,out pid);
        if(IsWindowVisible(h) && ancestors.Contains(pid)) {
          try { if(Process.GetProcessById((int)pid).ProcessName.Equals("WindowsTerminal",StringComparison.OrdinalIgnoreCase)) matches.Add(h); } catch { }
        } return true;
      },IntPtr.Zero);
      foreach(var h in matches) if(Title(h).IndexOf(title,StringComparison.OrdinalIgnoreCase)>=0) return h;
      if(matches.Contains(GetForegroundWindow())) return GetForegroundWindow();
      return matches.Count==1?matches[0]:IntPtr.Zero;
    }
  }

  public static class Data {
    public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    public static object At(object value,params string[] path) {
      foreach(string key in path) { var map=value as Dictionary<string,object>; if(map==null || !map.TryGetValue(key,out value)) return null; } return value;
    }
    public static string Clean(object value,int length) {
      string s=Regex.Replace(Convert.ToString(value,CultureInfo.InvariantCulture)??"",@"[\x00-\x1f\x7f-\x9f]","");
      return s.Length>length?s.Substring(0,length-1)+"…":s;
    }
    public static string Character(string value) { return RoleCatalog.Canonical(value); }
    public static string Ansi(string hex) { return "\x1b[38;2;"+int.Parse(hex.Substring(1,2),NumberStyles.HexNumber)+";"+int.Parse(hex.Substring(3,2),NumberStyles.HexNumber)+";"+int.Parse(hex.Substring(5,2),NumberStyles.HexNumber)+"m"; }
  }

  public static class StatusProgram {
    static string GitBranch(string path) {
      try {
        if(!Directory.Exists(path)) return null;
        var start=new ProcessStartInfo("git","symbolic-ref --quiet --short HEAD") { WorkingDirectory=path,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        using(var p=Process.Start(start)) { if(!p.WaitForExit(180)) { p.Kill(); return null; } return p.ExitCode==0?Data.Clean(p.StandardOutput.ReadToEnd().Trim(),24):null; }
      } catch { return null; }
    }
    public static string Format(object data,string character,bool git) {
      string key=Data.Character(character); string accent=RoleCatalog.StatusAccent(key);
      string secondary=RoleCatalog.StatusSecondary(key);
      string symbol=RoleCatalog.Motif(key);
      string model=Data.Clean(Data.At(data,"model","display_name")??"Claude",26);
      string cwd=Convert.ToString(Data.At(data,"workspace","current_dir")??Data.At(data,"cwd"));
      string dir=""; try { dir=Data.Clean(new DirectoryInfo(cwd).Name,24); } catch { dir=Data.Clean(cwd,24); }
      string separator="\x1b[38;2;132;144;165m │ ";
      var parts=new List<string>(); parts.Add(Data.Ansi(accent)+symbol+" "+key.ToUpperInvariant()); parts.Add(Data.Ansi(secondary)+model);
      int columns; if(!int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"),out columns)) columns=120;
      if(dir.Length>0 && columns>=65) parts.Add("\x1b[38;2;228;237;250m"+dir);
      string branch=git && columns>=95?GitBranch(cwd):null; if(!string.IsNullOrEmpty(branch)) parts.Add(Data.Ansi(secondary)+"git:"+branch);
      object used=Data.At(data,"context_window","used_percentage"); double percentage;
      if(used!=null && double.TryParse(Convert.ToString(used,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out percentage)) {
        percentage=Math.Max(0,Math.Min(100,percentage));
        string color=percentage>=85?"#FF99B0":percentage>=65?"#EFD19D":accent;
        parts.Add(Data.Ansi(color)+"ctx "+Math.Round(percentage).ToString(CultureInfo.InvariantCulture)+"%");
      } else parts.Add("\x1b[38;2;132;144;165mctx —");
      return string.Join(separator,parts.ToArray())+"\x1b[0m";
    }
    public static int Main(string[] args) {
      Console.InputEncoding=new UTF8Encoding(false); Console.OutputEncoding=new UTF8Encoding(false);
      try {
        object data=Data.Json.DeserializeObject(Console.In.ReadToEnd());
        string key=Environment.GetEnvironmentVariable("OC_CLAUDE_CHARACTER");
        if(string.IsNullOrEmpty(key))key=RoleCatalog.DefaultRole;
        Console.WriteLine(Format(data,key,true)); return 0;
      } catch { Console.WriteLine("Claude"); return 0; }
    }
  }

  public static class OverlayProgram {
    static string root; static Companion companion;
    static readonly Dictionary<IntPtr,string> positions=new Dictionary<IntPtr,string>();
    static void Place(Window window,int x,int y,int width,int height) {
      var handle=new WindowInteropHelper(window).Handle;string rect=x+","+y+","+width+","+height;
      string previous;if(positions.TryGetValue(handle,out previous) && previous==rect)return;
      Native.SetWindowPos(handle,IntPtr.Zero,x,y,width,height,0x14);positions[handle]=rect;
    }
    static void Log(string message) { try { Directory.CreateDirectory(System.IO.Path.Combine(root,"runtime")); File.AppendAllText(System.IO.Path.Combine(root,"runtime","overlay.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine); } catch { } }
    static void ReplaceExisting(int parent){
      try{object state=Data.Json.DeserializeObject(File.ReadAllText(System.IO.Path.Combine(root,"runtime","state-"+parent+".json")));if(Convert.ToInt32(Data.At(state,"parent"))!=parent)return;int id=Convert.ToInt32(Data.At(state,"helperPid"));if(id==Process.GetCurrentProcess().Id)return;using(var old=Process.GetProcessById(id)){DateTime stamp=DateTime.Parse(Convert.ToString(Data.At(state,"updated")),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);if(!old.HasExited&&old.StartTime.ToUniversalTime()<=stamp.ToUniversalTime()&&string.Equals(old.MainModule.FileName,System.IO.Path.Combine(root,"bin","OCCompanion.exe"),StringComparison.OrdinalIgnoreCase)){old.Kill();old.WaitForExit(1000);}}}catch{}
    }
    [STAThread] public static int Main(string[] args) {
      root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); if(System.IO.Path.GetFileName(root)=="bin"||(!Directory.Exists(System.IO.Path.Combine(root,"assets"))&&Directory.Exists(System.IO.Path.Combine(Directory.GetParent(root).FullName,"assets"))))root=Directory.GetParent(root).FullName;
      if(args.Length>0&&args[0]=="--claude-keys-import"){try{var store=new ClaudeKeyStore();store.ImportCurrent();var document=store.Read();ChatStore.Atomic(System.IO.Path.Combine(root,"runtime","claude-key-install.json"),Data.Json.Serialize(new{imported=true,profileCount=document.Profiles.Count,encryptedProfileLibrary=true,settingsFile=store.SettingsPath,settingsChanged=false}));return 0;}catch{ChatStore.Atomic(System.IO.Path.Combine(root,"runtime","claude-key-install.json"),Data.Json.Serialize(new{imported=false,settingsChanged=false}));return 1;}}
      if(args.Length>0&&args[0]=="--claude-key-settings"){var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var manager=new ClaudeKeyWindow(null,args.Length>1?Data.Character(args[1]):RoleCatalog.DefaultRole,new FontFamily("Microsoft YaHei UI"));app.Run(manager.Window);return 0;}
      if(args.Length>0&&args[0]=="--new-role-preview"){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};try{string[] roles=args.Length>2?new string[args.Length-2]:null;if(roles!=null)Array.Copy(args,2,roles,0,roles.Length);NewRolePreview.Run(root,args.Length>1?args[1]:System.IO.Path.Combine(root,"native-qa-new-roles","design-boards"),roles);return 0;}catch(Exception error){Directory.CreateDirectory(System.IO.Path.Combine(root,"runtime"));File.WriteAllText(System.IO.Path.Combine(root,"runtime","new-role-preview-error.json"),Data.Json.Serialize(new{status="failed",error=error.Message}));return 1;}finally{app.Shutdown();}}
      if(args.Length==2&&args[0]=="--background"&&args[1]=="install"){var backgrounds=new TerminalBackground(root,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoleCatalog.ConfigurationNamespace));backgrounds.Apply(RoleCatalog.DefaultRole,true);return backgrounds.Error.Length==0?0:1;}
      if(args.Length>0&&(args[0]=="--voice-bake"||args[0]=="--voice-clone"))return System.Threading.Tasks.Task.Run(delegate{return VoiceBakeJob.Run(root,args[0]=="--voice-clone",args.Length>1?args[1]:null);}).GetAwaiter().GetResult();
      if(args.Length>0&&args[0]=="--voice-settings"){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};string role=args.Length>1?Data.Character(args[1]):RoleCatalog.DefaultRole;var sound=new CompanionAudio(root,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoleCatalog.ConfigurationNamespace));try{sound.Settings(null,role,new FontFamily("Microsoft YaHei UI, Yu Gothic UI"));}finally{sound.Close();app.Shutdown();}return 0;}
      string key=RoleCatalog.DefaultRole,title="",output="";int parent=0;bool bubble=false;
      for(int i=0;i<args.Length-1;i++) { if(args[i]=="--character")key=args[++i]; else if(args[i]=="--parent")parent=int.Parse(args[++i]); else if(args[i]=="--title")title=args[++i];else if(args[i]=="--test"||args[i]=="--self-test")output=args[++i];else if(args[i]=="--bubble")bubble=args[++i]=="open"; }
      try {
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        if(output.Length>0)throw new ArgumentException("Use scripts/Validate-Runtime.ps1 for isolated offline validation.");
        if(parent==0)throw new ArgumentException("Missing launcher process id");
        companion=new Companion(root,key);
        companion.BubbleWanted=bubble;
        companion.Backgrounds=new TerminalBackground(root,companion.Audio.Voice.Folder){TargetTitle=title};
        ReplaceExisting(parent);
        IntPtr terminal=IntPtr.Zero; int attempts=0;
        var follow=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        var moveFrames=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(16) };
        var balanceTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(60)};
        var hooks=new List<IntPtr>();Native.WinEventProc eventCallback=null;
        bool queued=false,moving=false;DateTime lastState=DateTime.MinValue;uint eventStamp=0;int lastLatency=0,maxLatency=0,eventCount=0;
        int rightGap=0,bottomGap=0;bool balanceStarted=false;
        Action position=null;Action request=null;
        Action<bool,bool,bool> saveState=delegate(bool visible,bool ownerVisible,bool focused) {
          if((DateTime.UtcNow-lastState).TotalMilliseconds<250)return;lastState=DateTime.UtcNow;
          var state=new Dictionary<string,object>{{"updated",DateTime.UtcNow.ToString("o")},{"helperPid",Process.GetCurrentProcess().Id},{"character",companion.Character},{"parent",parent},{"terminal",terminal.ToInt64()},{"visible",visible},{"ownerVisible",ownerVisible},{"focused",focused},{"persistent",companion.Persistent},{"botVisible",companion.Bot.IsVisible},{"dollVisible",companion.Doll.IsVisible},{"controlsVisible",companion.Controls.IsVisible},{"bubbleVisible",companion.Bubble.IsVisible},{"bubbleWanted",companion.BubbleWanted},{"dock",companion.Dock.Description},{"moving",moving},{"hookEvents",eventCount},{"lastLatencyMs",lastLatency},{"maxLatencyMs",maxLatency},{"rightGapPixels",rightGap},{"bottomGapPixels",bottomGap},{"botHandle",new WindowInteropHelper(companion.Bot).Handle.ToInt64()},{"dollHandle",new WindowInteropHelper(companion.Doll).Handle.ToInt64()},{"controlsHandle",new WindowInteropHelper(companion.Controls).Handle.ToInt64()},{"bubbleHandle",new WindowInteropHelper(companion.Bubble).Handle.ToInt64()},{"balanceStatus",companion.Balance.Current.Status},{"preferenceError",companion.PreferenceError}};
          state["claudeKeyManagerAvailable"]=true;
          state["appearance"]=companion.Appearance;state["appearanceError"]=companion.AppearanceError;
          state["version"]="0.8";state["targetTitle"]=title;state["backgroundError"]=companion.Backgrounds==null?"":companion.Backgrounds.Error;state["soundEffects"]=companion.Audio.Voice.Options.Sfx;state["qwenReplyVoice"]=companion.Audio.Voice.Options.Chat;
          state["qwenInteractionVoice"]=companion.Audio.Voice.Options.Reactions;state["voiceSettingsFolder"]=companion.Audio.Voice.Folder;state["voiceModel"]=companion.Audio.Voice.Options.Model;
          state["voiceModels"]=RoleCatalog.VoiceModels(companion.Audio.Voice.Options);state["voiceConfigurationError"]=companion.Audio.Voice.ConfigurationError;state["voicePlaying"]=companion.Audio.Playing;state["voiceLastEvent"]=companion.Audio.LastEvent;state["voiceLastStatus"]=companion.Audio.LastStatus;state["voiceOpenedCount"]=companion.Audio.OpenedCount;state["voiceEndedCount"]=companion.Audio.EndedCount;
          try{File.WriteAllText(System.IO.Path.Combine(root,"runtime","state-"+parent+".json"),Data.Json.Serialize(state));}catch{}
        };
        request=delegate { if(queued)return;queued=true;app.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate { queued=false;position(); })); };
        position=delegate {
          if(terminal==IntPtr.Zero) {
            terminal=Native.FindTerminal(parent,title);
            if(terminal==IntPtr.Zero) { if(++attempts>75){Log("No terminal window found for "+key);app.Shutdown();}return; }
            new WindowInteropHelper(companion.Bot).Owner=terminal; new WindowInteropHelper(companion.Doll).Owner=terminal;
            new WindowInteropHelper(companion.Controls).Owner=terminal;new WindowInteropHelper(companion.Bubble).Owner=terminal;
            Log("attached "+key+" hwnd="+terminal.ToInt64()+" parent="+parent);
            uint terminalProcess;Native.GetWindowThreadProcessId(terminal,out terminalProcess);
            eventCallback=delegate(IntPtr hook,uint eventType,IntPtr h,int objectId,int child,uint thread,uint time) {
              if(eventType!=3 && h!=terminal)return;
              if(eventType==0x800B && objectId!=0 && objectId!=-4)return;
              eventStamp=time;eventCount++;
              if(eventType==0xA){moving=true;moveFrames.Start();}else if(eventType==0xB){moving=false;moveFrames.Stop();}
              request();
            };
            hooks.Add(Native.SetWinEventHook(0x800B,0x800B,IntPtr.Zero,eventCallback,terminalProcess,0,2));
            hooks.Add(Native.SetWinEventHook(0xA,0xB,IntPtr.Zero,eventCallback,terminalProcess,0,2));
            hooks.Add(Native.SetWinEventHook(0x16,0x17,IntPtr.Zero,eventCallback,terminalProcess,0,2));
            hooks.Add(Native.SetWinEventHook(3,3,IntPtr.Zero,eventCallback,0,0,2));
            foreach(IntPtr hook in hooks)if(hook==IntPtr.Zero)Log("WinEvent hook unavailable; frame fallback remains enabled");
          }
          if(!Native.IsWindow(terminal)) { app.Shutdown();return; }
          companion.ReloadPreferences();bool focused=Native.GetForegroundWindow()==terminal||companion.ChatFocused;
          bool ownerVisible=!Native.IsIconic(terminal) && Native.IsWindowVisible(terminal) && Native.Title(terminal).IndexOf(title,StringComparison.OrdinalIgnoreCase)>=0;
          bool visible=CompanionPreferences.ShowCharacters(ownerVisible,focused,companion.MenuOpen,companion.Persistent);
          if(!ownerVisible) {companion.Bot.Hide();companion.Doll.Hide();companion.Controls.Hide();companion.Bubble.Hide();companion.PlaceChat(new Native.Point(),0,0,1,false);companion.Stop();saveState(false,false,focused);return;}
          Native.Rect r;Native.GetClientRect(terminal,out r);var origin=new Native.Point();Native.ClientToScreen(terminal,ref origin);
          double dpi=Native.Scale(terminal);int width=r.Right,height=r.Bottom;
          double botWidth=companion.FitBotWidth(width/dpi,height/dpi);double botHeight=botWidth*companion.BotRatio;
          companion.ClientWidth=width/dpi;companion.ClientHeight=height/dpi;companion.ClientScale=dpi;
          companion.SetBotSize(botWidth);companion.ArtworkPosition=companion.Dock.Position(width/dpi,height/dpi,botWidth,botHeight);companion.SetDockAppearance();
          int visualX=origin.X+(int)Math.Round(companion.ArtworkPosition.X*dpi),visualY=origin.Y+(int)Math.Round(companion.ArtworkPosition.Y*dpi);
          rightGap=origin.X+width-visualX-(int)Math.Round(botWidth*dpi);bottomGap=origin.Y+height-visualY-(int)Math.Round(botHeight*dpi);
          if(companion.BotEnabled && visible) {
            if(!companion.Bot.IsVisible)companion.Bot.Show();int bw=(int)((botWidth+companion.BotPadding)*dpi),bh=(int)((botHeight+companion.BotPadding)*dpi);
            Place(companion.Bot,visualX-(int)(companion.BotInsetX*dpi),visualY-(int)(companion.BotInsetY*dpi),bw,bh);
          } else companion.Bot.Hide();
          if(companion.DollEnabled && visible) {
            if(!companion.Doll.IsVisible)companion.Doll.Show();int dw=(int)(190*dpi),dh=(int)(190*dpi);
            companion.ToyPhysics.Bounds(width/dpi,height/dpi,144);
            int x=origin.X+(int)Math.Round((companion.ToyPhysics.Position.X-23)*dpi);
            int y=origin.Y+(int)Math.Round((companion.ToyPhysics.Position.Y-23)*dpi);
            Place(companion.Doll,x,y,dw,dh);
          } else companion.Doll.Hide();
          // The small switch bar remains available even when focus-only mode hides both characters.
          if(!companion.Controls.IsVisible)companion.Controls.Show();
          companion.SetControlClientBounds(width/dpi,height/dpi);int controlWidth=(int)Math.Ceiling(companion.ControlLayoutWidth*dpi),controlHeight=(int)Math.Ceiling(companion.ControlLayoutHeight*dpi);
          var controlPoint=companion.PanelPosition("controls",origin,width/dpi,height/dpi,dpi,companion.ControlLayoutWidth,companion.ControlLayoutHeight,new System.Windows.Point(width/dpi-companion.ControlLayoutWidth-20,64));
          Place(companion.Controls,origin.X+(int)Math.Round(controlPoint.X*dpi),origin.Y+(int)Math.Round(controlPoint.Y*dpi),controlWidth,controlHeight);
          if(companion.BubbleWanted && companion.BotEnabled && visible) {
            if(!companion.Bubble.IsVisible){companion.Bubble.Show();companion.AnimateBubble();}int bubbleWidth=(int)(companion.BubbleWidth*dpi),bubbleHeight=(int)(companion.BubbleHeight*dpi);
            int bx=companion.Dock.Horizontal<0?visualX+(int)(botWidth*dpi)-18:visualX-bubbleWidth+18;
            int by=visualY+(int)(botHeight*dpi)-bubbleHeight-26;
            var bubblePoint=companion.PanelPosition("bubble",origin,width/dpi,height/dpi,dpi,companion.BubbleWidth,companion.BubbleHeight,new System.Windows.Point((bx-origin.X)/dpi,(by-origin.Y)/dpi));
            bx=origin.X+(int)Math.Round(bubblePoint.X*dpi);by=origin.Y+(int)Math.Round(bubblePoint.Y*dpi);
            Place(companion.Bubble,bx,by,bubbleWidth,bubbleHeight);
          }else companion.Bubble.Hide();
          companion.PlaceChat(origin,width,height,dpi,visible);
          if(!visible)companion.Stop();
          if(!balanceStarted){balanceStarted=true;companion.Balance.Refresh();balanceTimer.Start();}
          if(eventStamp!=0){lastLatency=(int)unchecked((uint)Environment.TickCount-eventStamp);maxLatency=Math.Max(maxLatency,lastLatency);eventStamp=0;}
          saveState(visible,ownerVisible,focused);
        };
        companion.Refresh=position;moveFrames.Tick+=delegate{position();};
        balanceTimer.Tick+=delegate {if(Native.IsWindowVisible(terminal) && !Native.IsIconic(terminal))companion.Balance.Refresh();};
        follow.Tick+=delegate {
          try{using(var process=Process.GetProcessById(parent))if(process.HasExited){app.Shutdown();return;}}catch{app.Shutdown();return;}
          position();
        };follow.Start();
        app.Exit+=delegate { follow.Stop();moveFrames.Stop();balanceTimer.Stop();foreach(IntPtr hook in hooks)if(hook!=IntPtr.Zero)Native.UnhookWinEvent(hook);companion.Balance.Stop();companion.Shutdown();Log("closed "+companion.Character); };
        app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) { Log(e.Exception.ToString());e.Handled=true;app.Shutdown(1); };
        app.Run();return 0;
      } catch(Exception e) { Log(e.ToString());return 1; }
    }
    static void Save(FrameworkElement visual,int w,int h,string path) {
      visual.Measure(new Size(w,h));visual.Arrange(new System.Windows.Rect(0,0,w,h));visual.UpdateLayout();
      var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
      var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
    }
  }
}
