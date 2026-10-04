using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OCShell {
  public sealed class ChatLine {public string role,content,time,id,state;}
  public sealed class ChatSettings {
    public readonly string Folder;
    public string Model="deepseek-flash";
    public ChatSettings(string folder=null){Folder=folder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoleCatalog.ConfigurationNamespace);Directory.CreateDirectory(Folder);try{Model=Convert.ToString(Data.At(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Folder,"deepseek.json"))),"model")??Model);}catch{}if(Model!="deepseek-flash"&&Model!="deepseek-v4-pro")Model="deepseek-flash";}
    public string Key(){string env=Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");if(!string.IsNullOrWhiteSpace(env))return env.Trim();try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Folder,"deepseek-key.bin")),null,DataProtectionScope.CurrentUser));}catch{return "";}}
    public void Save(string model,string key){Model=model=="deepseek-v4-pro"?model:"deepseek-flash";ChatStore.Atomic(Path.Combine(Folder,"deepseek.json"),Data.Json.Serialize(new{model=Model,endpoint="https://api.deepseek.com/chat/completions"}));if(!string.IsNullOrWhiteSpace(key)){byte[] encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()),null,DataProtectionScope.CurrentUser);File.WriteAllBytes(Path.Combine(Folder,"deepseek-key.bin"),encrypted);}}
  }
  public sealed class ChatStore {
    readonly string folder;
    public ChatStore(string folder){this.folder=folder;Directory.CreateDirectory(folder);}
    string RoleFolder(string role){string path=Path.Combine(folder,"chat",Data.Character(role));Directory.CreateDirectory(path);return path;}
    public List<ChatLine> Read(string role){var result=new List<ChatLine>();string path=Path.Combine(RoleFolder(role),"history.jsonl");if(!File.Exists(path))return result;foreach(string line in File.ReadLines(path)){try{var item=Data.Json.Deserialize<ChatLine>(line);if(item!=null&&(item.role=="user"||item.role=="assistant")&&!string.IsNullOrWhiteSpace(item.content))result.Add(item);}catch{}}return result;}
    public string Append(string role,string speaker,string content,string state="complete"){string id=Guid.NewGuid().ToString("N");Locked(role,delegate{File.AppendAllText(Path.Combine(RoleFolder(role),"history.jsonl"),Data.Json.Serialize(new ChatLine{role=speaker,content=content,time=DateTime.UtcNow.ToString("o"),id=id,state=state})+Environment.NewLine,new UTF8Encoding(false));});return id;}
    void Locked(string role,Action action){string path=RoleFolder(role);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-","").Substring(0,24);using(var mutex=new Mutex(false,"Local\\OCShellChat-"+hash)){bool owned=false;try{try{owned=mutex.WaitOne(1000);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new IOException("本机记录正在写入，请稍后重试。");action();}finally{if(owned)mutex.ReleaseMutex();}}}
    public void SetState(string role,string id,string state){Locked(role,delegate{var lines=Read(role);var output=new StringBuilder();foreach(var line in lines){if(line.id==id)line.state=state;output.AppendLine(Data.Json.Serialize(line));}Atomic(Path.Combine(RoleFolder(role),"history.jsonl"),output.ToString());});}
    public string Memory(string role){string path=Path.Combine(RoleFolder(role),"memory.md");return File.Exists(path)?File.ReadAllText(path):"";}
    public void SaveMemory(string role,string text){Atomic(Path.Combine(RoleFolder(role),"memory.md"),text);}
    public void Archive(string role){string path=Path.Combine(RoleFolder(role),"history.jsonl");if(File.Exists(path))File.Move(path,Path.Combine(RoleFolder(role),"history-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".jsonl"));}
    public static void Atomic(string path,string text){string temporary=path+"."+System.Diagnostics.Process.GetCurrentProcess().Id+".tmp";File.WriteAllText(temporary,text,new UTF8Encoding(false));try{if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
    public List<object> Context(string role,string system,string pendingId=null){var history=Read(role);var selected=new List<ChatLine>();int remaining=18000;for(int i=history.Count-1;i>=0&&selected.Count<24;i--){var line=history[i];if(line.state!=null&&line.state!="complete"&&line.id!=pendingId)continue;string text=line.content.Length>4000?line.content.Substring(0,4000):line.content;if(text.Length>remaining&&selected.Count>0)break;remaining-=text.Length;selected.Insert(0,new ChatLine{role=line.role,content=text});}while(selected.Count>0&&selected[0].role=="assistant")selected.RemoveAt(0);var messages=new List<object>{new{role="system",content=system}};foreach(var line in selected)messages.Add(new{role=line.role,content=line.content});return messages;}
  }
  public sealed class DeepSeekChat {
    HttpWebRequest active;
    public string FinishReason;
    public void Cancel(){var request=active;if(request!=null)request.Abort();}
    public static string ReadStream(Stream input,Action<string> delta,CancellationToken cancel,Action<string> finish=null) {
      var text=new StringBuilder();bool done=false,finished=false;int bytes=0;
      using(var reader=new StreamReader(input,Encoding.UTF8,true,4096,true)) {
        string line;while((line=reader.ReadLine())!=null){cancel.ThrowIfCancellationRequested();bytes+=line.Length;if(bytes>1048576)throw new InvalidDataException("回复超过接收上限");if(!line.StartsWith("data:"))continue;string json=line.Substring(5).Trim();if(json=="[DONE]"){done=true;break;}if(json.Length==0)continue;object chunk=Data.Json.DeserializeObject(json);if(Data.At(chunk,"error")!=null)throw new InvalidDataException("服务端中断了回复");var choices=Data.At(chunk,"choices") as object[];if(choices==null||choices.Length==0)continue;string part=Convert.ToString(Data.At(choices[0],"delta","content"));if(part.Length>0){text.Append(part);if(text.Length>12000)throw new InvalidDataException("回复过长");delta(text.ToString());}if(Data.At(choices[0],"finish_reason")!=null){string reason=Convert.ToString(Data.At(choices[0],"finish_reason"));if(reason!="stop"&&reason!="length")throw new InvalidDataException("这次回复被服务端中断，请稍后再试。");finished=true;if(finish!=null)finish(reason);}}
      }
      if((!done&&!finished)||text.Length==0)throw new InvalidDataException("没有收到完整回复，可以再试一次");return text.ToString();
    }
    public Task<string> Send(ChatSettings settings,List<object> messages,double temperature,Action<string> delta,CancellationToken cancel) {
      string key=settings.Key();if(key.Length==0)throw new InvalidOperationException("先在‘设置’里填入 DeepSeek 官方 API Key。");FinishReason=null;
      var body=new{model=settings.Model,messages=messages,stream=true,thinking=new{type="disabled"},max_tokens=512,temperature=temperature};
      return Task.Run(delegate {
        var request=(HttpWebRequest)WebRequest.Create("https://api.deepseek.com/chat/completions");active=request;
        request.Method="POST";request.ContentType="application/json";request.Accept="text/event-stream";request.AllowAutoRedirect=false;request.Timeout=60000;request.ReadWriteTimeout=60000;request.Headers[HttpRequestHeader.Authorization]="Bearer "+key;ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        using(cancel.Register(delegate{request.Abort();}))using(var deadline=new Timer(delegate{request.Abort();},null,150000,Timeout.Infinite)) {
          try{byte[] data=Encoding.UTF8.GetBytes(Data.Json.Serialize(body));request.ContentLength=data.Length;using(var stream=request.GetRequestStream())stream.Write(data,0,data.Length);
            using(var response=(HttpWebResponse)request.GetResponse()){if((response.ContentType??"").IndexOf("text/event-stream",StringComparison.OrdinalIgnoreCase)<0)throw new InvalidDataException("接口没有返回流式对话");using(var stream=response.GetResponseStream())return ReadStream(stream,delta,cancel,delegate(string reason){FinishReason=reason;});}
          }catch(WebException e){cancel.ThrowIfCancellationRequested();var response=e.Response as HttpWebResponse;if(response!=null){int status=(int)response.StatusCode;response.Close();throw new InvalidOperationException(status==401?"DeepSeek Key 未通过验证，请检查设置。":status==402?"DeepSeek 官方账户余额不足。GPTEAM 的余额不用于此对话。":status==429?"DeepSeek 暂时限流，稍等一会儿再发。":"DeepSeek 请求失败（HTTP "+status+"），可以稍后重试。");}throw new InvalidOperationException("网络连接中断或超时，请稍后重试。");}
          finally{if(active==request)active=null;}
        }
      },cancel);
    }
  }
  public sealed class SpeechBridge {
    readonly string folder,root;
    public SpeechBridge(string root,string folder){this.root=root;this.folder=folder;}
    // A future speech worker can subscribe to this event or consume the opt-in local outbox.
    public event Action<string,string,string> Ready;
    public void Reply(string role,string text){var callback=Ready;if(callback!=null)callback(role,text,"complete");string settings=Path.Combine(folder,"tts-settings.json");if(!File.Exists(settings))return;object config;try{config=Data.Json.DeserializeObject(File.ReadAllText(settings));}catch{return;}if(!object.Equals(Data.At(config,"enabled"),true))return;object personas=Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(root,"companion-personas.json")));var job=new{id=Guid.NewGuid().ToString("N"),character=role,text=text,voice=Data.At(personas,role,"ttsVoice"),rate=Data.At(personas,role,"ttsRate"),format="wav",status="pending",created=DateTime.UtcNow.ToString("o")};Directory.CreateDirectory(Path.Combine(folder,"tts"));File.AppendAllText(Path.Combine(folder,"tts","outbox.jsonl"),Data.Json.Serialize(job)+Environment.NewLine,new UTF8Encoding(false));}
  }
  public sealed class ChatPane {
    public readonly Window Window;
    public readonly Border Surface;
    readonly string root;
    readonly ChatSettings settings;
    readonly ChatStore store;
    readonly SpeechBridge speech;
    readonly DeepSeekChat transport=new DeepSeekChat();
    readonly Action<string,string> reply;
    readonly TextBlock title,status;
    readonly StackPanel transcript;
    readonly ScrollViewer scroll;
    readonly TextBox input;
    readonly Button send;
    readonly FontFamily font;
    readonly PanelPlacement panels;
    readonly CompanionAudio audio;
    readonly PanelDrag drag;
    readonly TextBlock room;
    readonly Image portrait;
    readonly Border field;
    readonly List<Button> buttons=new List<Button>();
    Native.Point followOrigin;int followWidth,followHeight;double followDpi=1;bool followVisible;
    CancellationTokenSource cancel;
    bool busy,wanted,closed;
    string role;
    public ChatPane(string root,string role,FontFamily font,Action<string,string> reply,string dataFolder=null,PanelPlacement panels=null,CompanionAudio audio=null) {
      this.root=root;this.role=role;this.font=font;this.reply=reply;settings=new ChatSettings(dataFolder);store=new ChatStore(settings.Folder);speech=new SpeechBridge(root,settings.Folder);this.panels=panels??new PanelPlacement(settings.Folder);this.audio=audio;
      Window=new Window{Title="OC 小角落",Width=420,Height=482,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,FontFamily=font};
      Surface=new Border{CornerRadius=new CornerRadius(28),Padding=new Thickness(20),Margin=new Thickness(4),BorderThickness=new Thickness(1.5)};Window.Content=Surface;
      var layout=new Grid();for(int i=0;i<4;i++)layout.RowDefinitions.Add(new RowDefinition{Height=i==1?new GridLength(1,GridUnitType.Star):GridLength.Auto});Surface.Child=layout;
      var header=new DockPanel{Margin=new Thickness(0,0,0,14)};Grid.SetRow(header,0);layout.Children.Add(header);
      var close=Pill("×",30);DockPanel.SetDock(close,Dock.Right);close.Click+=delegate{wanted=false;Window.Hide();};header.Children.Add(close);
      var headerBody=new StackPanel();header.Children.Add(headerBody);var identity=new StackPanel{Orientation=Orientation.Horizontal};headerBody.Children.Add(identity);
      portrait=new Image{Width=36,Height=36,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,8,0)};identity.Children.Add(portrait);
      var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center};identity.Children.Add(names);title=new TextBlock{FontSize=17,FontWeight=FontWeights.Bold};names.Children.Add(title);room=new TextBlock{FontSize=11,Margin=new Thickness(0,2,0,0)};names.Children.Add(room);
      var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(40,9,0,0)};headerBody.Children.Add(tools);
      var memory=Pill("记忆",60);memory.Click+=delegate{MemoryPanel();};tools.Children.Add(memory);
      var options=Pill("设置",60);options.Click+=delegate{SettingsPanel();};tools.Children.Add(options);
      var voice=Pill("声音 ♪",72);voice.Click+=delegate{if(this.audio!=null)this.audio.Settings(Window,this.role,font);else status.Text="声音入口可从终端选项栏打开。";};tools.Children.Add(voice);
      drag=new PanelDrag(Window,header,this.panels,"chat",delegate{return this.role;},delegate{Follow(followOrigin,followWidth,followHeight,followDpi,followVisible);});
      transcript=new StackPanel();scroll=new ScrollViewer{Content=transcript,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
      status=new TextBlock{FontSize=11,Foreground=Paint("#DFC8DD"),Margin=new Thickness(0,10,0,8),TextWrapping=TextWrapping.Wrap};Grid.SetRow(status,2);layout.Children.Add(status);
      var composer=new Grid();composer.ColumnDefinitions.Add(new ColumnDefinition());composer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetRow(composer,3);layout.Children.Add(composer);
      field=new Border{CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),Padding=new Thickness(12,8,12,8)};composer.Children.Add(field);
      input=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=35,MaxHeight=70,FontSize=14,FontFamily=font,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=Paint("#FFF0FA"),CaretBrush=Paint("#FFCFEB"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};field.Child=input;input.ToolTip="Enter 发送 · Shift + Enter 换行 · /记住 内容 保存记忆";
      input.PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Enter&&(Keyboard.Modifiers&ModifierKeys.Shift)==0){e.Handled=true;Send();}};
      send=Pill("发送 ♡",72);send.Margin=new Thickness(8,0,0,0);Grid.SetColumn(send,1);composer.Children.Add(send);send.Click+=delegate{if(busy)Cancel();else Send();};
      Window.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!closed){e.Cancel=true;wanted=false;Window.Hide();}};if(audio!=null)audio.Status=delegate(string text){if(!closed)status.Text=text;};SetCharacter(role);
    }
    static Brush Paint(string color){return (SolidColorBrush)new BrushConverter().ConvertFromString(color);}
    Button Pill(string text,double width){var style=RoleVisual.For(role);var b=new Button{Content=text,Width=width,Height=32,FontFamily=font,FontSize=12,Foreground=Paint(style.Ink),Background=Paint(style.Paper),BorderBrush=Paint(style.Line),Cursor=Cursors.Hand,Margin=new Thickness(3,0,0,0)};var t=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(16));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Button.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Button.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));var c=new FrameworkElementFactory(typeof(ContentPresenter));c.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);c.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(c);t.VisualTree=border;b.Template=t;b.MouseEnter+=delegate{b.Opacity=.78;};b.MouseLeave+=delegate{b.Opacity=1;};buttons.Add(b);return b;}
    string Name(){return RoleCatalog.FamiliarName(role)+"的小角落";}
    BitmapSource Avatar(){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.DecodePixelWidth=96;b.UriSource=new Uri(Path.Combine(root,"assets",role+"-tab-avatar.png"));b.EndInit();b.Freeze();return b;}
    public void SetCharacter(string selected,bool reload=false){selected=Data.Character(selected);if(role==selected&&title.Text.Length>0&&!reload)return;if(role!=selected){Cancel();if(audio!=null)audio.StopVoice();}role=selected;var style=RoleVisual.For(role);Surface.CornerRadius=style.Corners;Surface.Background=style.Glow;Surface.BorderBrush=Paint(style.Line);title.Foreground=Paint(style.Ink);title.Text=Name();room.Foreground=Paint(style.Muted);room.Text=style.Motif+" "+style.Room+" · 拖动这里摆放";portrait.Source=Avatar();status.Foreground=Paint(style.Muted);input.Foreground=Paint(style.Ink);input.CaretBrush=Paint(style.Accent);field.Background=Paint(style.Paper);field.BorderBrush=Paint(style.Line);foreach(var b in buttons){b.Foreground=Paint(style.Ink);b.Background=Paint(style.Paper);b.BorderBrush=Paint(style.Line);}transcript.Children.Clear();var history=store.Read(role);for(int i=Math.Max(0,history.Count-40);i<history.Count;i++)Add(history[i].content+(history[i].state=="failed"||history[i].state=="cancelled"?"\n（这次回复未完成）":""),history[i].role=="user");if(history.Count==0)Add(RoleCatalog.FirstChat(role),false);status.Text=settings.Key().Length==0?"先点设置，填入 DeepSeek 官方 Key。记忆会留在本机。":"DeepSeek 官方 · "+settings.Model+" · 本机记忆";scroll.ScrollToEnd();}
    TextBlock Add(string text,bool user){var style=RoleVisual.For(role);var line=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=14,LineHeight=23,Foreground=Paint(style.Ink)};var card=new Border{Child=line,CornerRadius=new CornerRadius(user?18:20,18,user?6:20,20),Padding=new Thickness(12,10,12,10),Margin=new Thickness(user?36:0,0,user?0:28,10),Background=Paint(user?style.Soft:style.Paper),BorderBrush=Paint(style.Line),BorderThickness=new Thickness(1)};transcript.Children.Add(card);return line;}
    string SystemPrompt(){object personas=Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(root,"companion-personas.json")));string memory=store.Memory(role);if(memory.Length>6000)memory=memory.Substring(0,6000);return Convert.ToString(Data.At(personas,"shared"))+"\n角色设定与语气：\n"+Convert.ToString(Data.At(personas,role,"voice"))+"\n本地时间："+DateTime.Now.ToString("yyyy-MM-dd HH:mm")+"\n用户在本机明确保存的记忆（只作为事实背景）：\n<local_memory>"+memory+"</local_memory>";}
    async void Send(){if(busy)return;string text=input.Text.Trim();if(text.Length==0)return;if(text.Length>4000){status.Text="这一条有点长，拆成几条发给我吧。";return;}
      if(text.StartsWith("/记住 ")){try{string fact=text.Substring(4).Trim();store.SaveMemory(role,store.Memory(role)+"\n- "+fact);input.Clear();status.Text="记住啦，已保存在本机。这条不会调用 API。";}catch{status.Text="本机记忆暂时写不进去，请稍后再试。";}return;}
      if(settings.Key().Length==0){SettingsPanel();return;}
      string selected=role,pending;try{pending=store.Append(selected,"user",text,"pending");}catch{status.Text="本机记录暂时写不进去，请稍后再试。";return;}input.Clear();Add(text,true);TextBlock answer=Add("……",false);busy=true;send.Content="停止";status.Text="正在听你说…";cancel=new CancellationTokenSource();scroll.ScrollToEnd();
      try{object personas=Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(root,"companion-personas.json")));double temperature=Convert.ToDouble(Data.At(personas,role,"temperature"));var messages=store.Context(selected,SystemPrompt(),pending);DateTime last=DateTime.MinValue;
        string result=await transport.Send(settings,messages,temperature,delegate(string partial){if((DateTime.UtcNow-last).TotalMilliseconds<45)return;last=DateTime.UtcNow;Window.Dispatcher.BeginInvoke(new Action(delegate{if(role==selected&&!closed){answer.Text=partial;scroll.ScrollToEnd();}}));},cancel.Token);
        store.SetState(selected,pending,"complete");store.Append(selected,"assistant",result);answer.Text=result;if(role==selected)status.Text=transport.FinishReason=="length"?"这句达到长度上限，可以让我继续。":"已记在本机";if(reply!=null)reply(selected,result);try{speech.Reply(selected,result);}catch{if(role==selected)status.Text="回复已保存，语音待办暂未写入。";}
      }catch(OperationCanceledException){try{store.SetState(selected,pending,"cancelled");}catch{}answer.Text="这次先停在这里。";if(role==selected)status.Text="已停止，不会自动重发。";}catch(Exception error){try{store.SetState(selected,pending,"failed");}catch{}answer.Text="这一句没能完整接住。";if(role==selected)status.Text=error is InvalidOperationException||error is InvalidDataException?error.Message:"对话暂时中断，可以稍后重试。";}
      finally{busy=false;send.Content="发送 ♡";if(cancel!=null){cancel.Dispose();cancel=null;}scroll.ScrollToEnd();}
    }
    void Cancel(){if(cancel!=null)cancel.Cancel();transport.Cancel();if(audio!=null)audio.StopVoice();}
    Window Editor(string name,double height){var style=RoleVisual.For(role);return new Window{Title=name,Width=390,Height=height,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Paint(style.Paper),Foreground=Paint(style.Ink),FontFamily=font,ShowInTaskbar=false};}
    void SettingsPanel(){var window=Editor("DeepSeek 官方设置",270);var box=new StackPanel{Margin=new Thickness(20)};window.Content=box;box.Children.Add(new TextBlock{Text="DeepSeek 官方 API",FontSize=19,FontWeight=FontWeights.Bold});box.Children.Add(new TextBlock{Text="https://api.deepseek.com\nKey 在本机用 Windows 用户加密保存。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10),FontSize=12});var key=new PasswordBox{Height=30,FontSize=14};key.ToolTip="填新 Key；留空保留现有 Key";box.Children.Add(key);var model=new ComboBox{ItemsSource=new[]{"deepseek-flash","deepseek-v4-pro"},SelectedItem=settings.Model,Margin=new Thickness(0,10,0,10)};box.Children.Add(model);var save=Pill("保存 ♡",110);save.Click+=delegate{try{settings.Save(Convert.ToString(model.SelectedItem),key.Password);key.Clear();status.Text=settings.Key().Length>0?"设置已保存，可以开始聊啦。":"已保存模型；还需要填入 API Key。";window.Close();}catch{status.Text="设置未能保存，请检查本地目录。";}};box.Children.Add(save);window.ShowDialog();}
    void MemoryPanel(){var window=Editor("本机记忆 · "+Name(),390);var box=new DockPanel{Margin=new Thickness(18)};window.Content=box;var hint=new TextBlock{Text="写下希望她长期记住的事。每位角色分开保存。\n这些记忆会随你发送的聊天带给 DeepSeek。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(hint,Dock.Top);box.Children.Add(hint);var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(actions,Dock.Bottom);box.Children.Add(actions);var edit=new TextBox{Text=store.Memory(role),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontFamily=font,FontSize=14,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};box.Children.Add(edit);var save=Pill("记住这些 ♡",120);save.Click+=delegate{try{store.SaveMemory(role,edit.Text);status.Text="本机记忆更新了。";window.Close();}catch{status.Text="本机记忆未能保存。";}};actions.Children.Add(save);var fresh=Pill("另开一段对话",130);fresh.Click+=delegate{if(busy){status.Text="先停止当前回复，再另开对话。";return;}try{store.Archive(role);SetCharacter(role,true);window.Close();}catch{status.Text="旧对话暂未归档，请稍后重试。";}};actions.Children.Add(fresh);window.ShowDialog();}
    public void ShowFor(Window bot){if(closed)return;var owner=new WindowInteropHelper(bot).Owner;if(owner!=IntPtr.Zero && !Window.IsVisible)new WindowInteropHelper(Window).Owner=owner;wanted=true;Window.Show();if(followWidth>0)Follow(followOrigin,followWidth,followHeight,followDpi,followVisible);Window.Activate();input.Focus();}
    public bool Focused {get{if(Window.IsActive)return true;foreach(Window owned in Window.OwnedWindows)if(owned.IsActive)return true;return false;}}
    public void Follow(Native.Point origin,int width,int height,double dpi,bool visible){followOrigin=origin;followWidth=width;followHeight=height;followDpi=dpi;followVisible=visible;if(!wanted||closed)return;if(!visible){Window.Hide();return;}if(!Window.IsVisible)Window.Show();int w=(int)(420*dpi),h=(int)(482*dpi);drag.Bounds(origin,width/dpi,height/dpi,dpi,420,482);var p=panels.Position(role,"chat",width/dpi,height/dpi,420,482,new Point(width/dpi-640,height/dpi-502));int x=origin.X+(int)Math.Round(p.X*dpi),y=origin.Y+(int)Math.Round(p.Y*dpi);IntPtr handle=new WindowInteropHelper(Window).Handle;Native.Rect old;if(Native.GetWindowRect(handle,out old)&&(old.Left!=x||old.Top!=y||old.Right-old.Left!=w||old.Bottom-old.Top!=h))Native.SetWindowPos(handle,IntPtr.Zero,x,y,w,h,0x14);}
    public void Shutdown(){closed=true;Cancel();Window.Close();}
  }
}
