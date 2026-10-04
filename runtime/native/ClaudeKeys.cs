using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OCShell {
  public sealed class ClaudeKeyProfile {
    public string Id,Name,BaseUrl,AuthMode="bearer",Secret;
    public override string ToString(){return Name;}
  }
  public sealed class ClaudeKeyDocument {
    public int Version=1;
    public List<ClaudeKeyProfile> Profiles=new List<ClaudeKeyProfile>();
    // Snapshot the applied profile; editing a saved profile must not silently change the active key.
    public ClaudeKeyProfile Active;
  }
  public sealed class ClaudeConnection {
    public string BaseUrl,Key,AuthMode;
    public string Provider {get{Uri uri;return Uri.TryCreate(BaseUrl,UriKind.Absolute,out uri)&&uri.Host=="api.gpteamservices.com"?"GPTEAM":Uri.TryCreate(BaseUrl,UriKind.Absolute,out uri)&&uri.Host=="api.anthropic.com"?"Anthropic":"自定义 API";}}
  }
  public sealed class ClaudeKeyStore {
    public static readonly string[] CredentialNames={"ANTHROPIC_BASE_URL","ANTHROPIC_AUTH_TOKEN","ANTHROPIC_API_KEY"};
    static readonly byte[] entropy=Encoding.UTF8.GetBytes("OCShell.ClaudeKeys.v1");
    public readonly string Folder,SettingsPath;
    readonly string path,backup,mutexName;
    public static string DefaultFolder {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoleCatalog.ConfigurationNamespace,"claude-keys");}}
    public static string DefaultSettings {get{string folder=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");return Path.Combine(string.IsNullOrWhiteSpace(folder)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude"):folder,"settings.json");}}
    public ClaudeKeyStore(string folder=null,string settings=null){Folder=folder??DefaultFolder;SettingsPath=settings??DefaultSettings;Directory.CreateDirectory(Folder);path=Path.Combine(Folder,"profiles.json");backup=Path.Combine(Folder,"previous-auth.bin");mutexName="Local\\OCClaudeKeys-"+Hash(Folder.ToLowerInvariant()).Substring(0,20);}
    public static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value??""))).Replace("-","");}
    static T Locked<T>(string name,Func<T> action){using(var mutex=new Mutex(false,name)){bool owned=false;try{try{owned=mutex.WaitOne(5000);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new InvalidDataException("另一个密钥窗口正在保存，请稍后再试。");return action();}finally{if(owned)mutex.ReleaseMutex();}}}
    public ClaudeKeyDocument Read(){if(!File.Exists(path))return new ClaudeKeyDocument();try{var doc=Data.Json.Deserialize<ClaudeKeyDocument>(File.ReadAllText(path));if(doc==null||doc.Version!=1||doc.Profiles==null)throw new Exception();return doc;}catch{throw new InvalidDataException("密钥库无法读取，已保留原文件。请检查本机备份。");}}
    void Write(ClaudeKeyDocument doc){ChatStore.Atomic(path,Data.Json.Serialize(doc));}
    public static string Protect(string key){return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),entropy,DataProtectionScope.CurrentUser));}
    public static string Reveal(ClaudeKeyProfile profile){try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(profile.Secret),entropy,DataProtectionScope.CurrentUser));}catch{throw new InvalidDataException("该密钥无法由当前 Windows 用户解密。");}}
    public static string Mask(ClaudeKeyProfile profile){string key=Reveal(profile);return key.Length<=4?"••••":"•••• •••• "+key.Substring(key.Length-4);}
    public static string NormalizeUrl(string text){Uri uri;if(!Uri.TryCreate((text??"").Trim(),UriKind.Absolute,out uri)||(uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback))||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new InvalidDataException("API 地址应为 HTTPS；本地服务可使用回环 HTTP。不要在地址中填写密钥或查询参数。");return uri.AbsoluteUri.TrimEnd('/');}
    static void ValidateKey(string key){if(string.IsNullOrWhiteSpace(key)||key.Length>8192||key.Any(char.IsWhiteSpace)||key.Any(char.IsControl))throw new InvalidDataException("请填写完整密钥，不要包含空格、换行或控制字符。");}
    static ClaudeKeyProfile Copy(ClaudeKeyProfile p){return p==null?null:new ClaudeKeyProfile{Id=p.Id,Name=p.Name,BaseUrl=p.BaseUrl,AuthMode=p.AuthMode,Secret=p.Secret};}
    public ClaudeKeyProfile Save(string id,string name,string url,string mode,string key){
      name=(name??"").Trim();if(name.Length==0||name.Length>64||name.Any(char.IsControl))throw new InvalidDataException("配置名称需为 1–64 个字符。");url=NormalizeUrl(url);if(mode!="bearer"&&mode!="api-key")throw new InvalidDataException("请选择认证方式。");if(!string.IsNullOrEmpty(key))ValidateKey(key);
      return Locked(mutexName,delegate{var doc=Read();var item=doc.Profiles.FirstOrDefault(p=>p.Id==id);if(doc.Profiles.Any(p=>p.Id!=id&&string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("已有同名配置，请换一个名称。");if(item==null){if(string.IsNullOrEmpty(key))throw new InvalidDataException("新增配置需要填写 API Key。");item=new ClaudeKeyProfile{Id=Guid.NewGuid().ToString("N")};doc.Profiles.Add(item);}item.Name=name;item.BaseUrl=url;item.AuthMode=mode;if(!string.IsNullOrEmpty(key))item.Secret=Protect(key);Write(doc);return Copy(item);});
    }
    Dictionary<string,object> Settings(){if(!File.Exists(SettingsPath))return new Dictionary<string,object>();try{var doc=Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(SettingsPath));if(doc==null)throw new Exception();return doc;}catch{throw new InvalidDataException("Claude settings.json 不是有效的 JSON，未覆盖原文件。");}}
    static Dictionary<string,object> Env(Dictionary<string,object> doc){object value;if(!doc.TryGetValue("env",out value)){var env=new Dictionary<string,object>();doc["env"]=env;return env;}var existing=value as Dictionary<string,object>;if(existing==null)throw new InvalidDataException("Claude 设置中的 env 不是对象，未改动原文件。");return existing;}
    public static ClaudeConnection Resolve(string settingsPath=null){
      var source=new ClaudeKeyStore(settings:settingsPath);var doc=source.Settings();var env=Env(doc);object v;
      string url=env.TryGetValue("ANTHROPIC_BASE_URL",out v)?Convert.ToString(v):Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
      bool hasAuth=env.ContainsKey("ANTHROPIC_AUTH_TOKEN")||env.ContainsKey("ANTHROPIC_API_KEY");
      string token=env.TryGetValue("ANTHROPIC_AUTH_TOKEN",out v)?Convert.ToString(v):hasAuth?"":Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
      string apiKey=env.TryGetValue("ANTHROPIC_API_KEY",out v)?Convert.ToString(v):hasAuth?"":Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
      return new ClaudeConnection{BaseUrl=string.IsNullOrWhiteSpace(url)?"https://api.anthropic.com":url,Key=!string.IsNullOrWhiteSpace(token)?token:apiKey,AuthMode=!string.IsNullOrWhiteSpace(token)?"bearer":"api-key"};
    }
    public ClaudeKeyProfile ImportCurrent(){
      return Locked(mutexName,delegate{var connection=Resolve(SettingsPath);if(string.IsNullOrWhiteSpace(connection.Key))throw new InvalidDataException("当前 Claude 配置中没有 API Key。订阅登录请继续使用 Claude 的 /login。");var doc=Read();string url=NormalizeUrl(connection.BaseUrl);var same=doc.Profiles.FirstOrDefault(p=>p.BaseUrl==url&&p.AuthMode==connection.AuthMode&&Reveal(p)==connection.Key);if(same!=null)return Copy(same);string label=connection.Provider+" · 当前配置",name=label;int n=2;while(doc.Profiles.Any(p=>p.Name==name))name=label+" "+n++;var item=new ClaudeKeyProfile{Id=Guid.NewGuid().ToString("N"),Name=name,BaseUrl=url,AuthMode=connection.AuthMode,Secret=Protect(connection.Key)};doc.Profiles.Add(item);if(doc.Active==null)doc.Active=Copy(item);Write(doc);return Copy(item);});
    }
    public void Apply(string id){Locked(mutexName,delegate{
      var vault=Read();var item=vault.Profiles.FirstOrDefault(p=>p.Id==id);if(item==null)throw new InvalidDataException("配置已经被移除，请重新选择。");string key=Reveal(item);ValidateKey(key);var settings=Settings();bool envExisted=settings.ContainsKey("env");var env=Env(settings);
      var fields=new Dictionary<string,object>();foreach(string name in CredentialNames){object value;bool exists=env.TryGetValue(name,out value);fields[name]=new Dictionary<string,object>{{"exists",exists},{"value",value}};}
      var undo=new Dictionary<string,object>{{"settingsPath",SettingsPath},{"fields",fields},{"active",vault.Active},{"envExisted",envExisted}};
      ChatStore.Atomic(backup,Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Data.Json.Serialize(undo)),entropy,DataProtectionScope.CurrentUser)));
      env["ANTHROPIC_BASE_URL"]=item.BaseUrl;env["ANTHROPIC_AUTH_TOKEN"]=item.AuthMode=="bearer"?key:"";env["ANTHROPIC_API_KEY"]=item.AuthMode=="api-key"?key:"";
      // Claude reads the active credential from its own user settings; the profile library and undo stay encrypted.
      ChatStore.Atomic(SettingsPath,Data.Json.Serialize(settings));vault.Active=Copy(item);Write(vault);return true;
    });}
    public bool CanRestore {get{return File.Exists(backup);}}
    public void Restore(){Locked(mutexName,delegate{
      if(!File.Exists(backup))throw new InvalidDataException("还没有可恢复的上一次切换。");object undo;try{undo=Data.Json.DeserializeObject(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(backup)),entropy,DataProtectionScope.CurrentUser)));}catch{throw new InvalidDataException("切换备份无法解密，未改动 Claude 设置。");}
      if(!string.Equals(Convert.ToString(Data.At(undo,"settingsPath")),SettingsPath,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("备份属于另一个 Claude 配置目录，未写入。");var settings=Settings();var env=Env(settings);
      foreach(string name in CredentialNames){if(object.Equals(Data.At(undo,"fields",name,"exists"),true))env[name]=Data.At(undo,"fields",name,"value");else env.Remove(name);}
      if(env.Count==0&&!object.Equals(Data.At(undo,"envExisted"),true))settings.Remove("env");var vault=Read();object active=Data.At(undo,"active");vault.Active=active==null?null:Data.Json.Deserialize<ClaudeKeyProfile>(Data.Json.Serialize(active));ChatStore.Atomic(SettingsPath,Data.Json.Serialize(settings));Write(vault);return true;
    });}
    public void Delete(string id){Locked(mutexName,delegate{var doc=Read();if(doc.Active!=null&&doc.Active.Id==id)throw new InvalidDataException("请先切换到另一组密钥，再删除当前配置。");doc.Profiles.RemoveAll(p=>p.Id==id);Write(doc);return true;});}
    public static string Test(ClaudeConnection connection){
      string baseUrl=NormalizeUrl(connection.BaseUrl);ValidateKey(connection.Key);string endpoint=baseUrl+(baseUrl.EndsWith("/v1",StringComparison.OrdinalIgnoreCase)?"/models":"/v1/models");
      var request=(HttpWebRequest)WebRequest.Create(endpoint);request.Method="GET";request.AllowAutoRedirect=false;request.Timeout=request.ReadWriteTimeout=12000;request.Accept="application/json";request.Headers["anthropic-version"]="2023-06-01";
      if(connection.AuthMode=="bearer")request.Headers[HttpRequestHeader.Authorization]="Bearer "+connection.Key;else request.Headers["x-api-key"]=connection.Key;
      ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
      try{using(var response=(HttpWebResponse)request.GetResponse()){return response.StatusCode==HttpStatusCode.OK?"连接通过 · 模型列表接口响应 HTTP 200，未调用模型。":"服务已响应 · HTTP "+(int)response.StatusCode+"，密钥暂未验证。";}}
      catch(WebException error){using(var response=error.Response as HttpWebResponse){if(response==null)return "连接未完成，请检查地址或网络。";int code=(int)response.StatusCode;return code==401||code==403?"认证未通过 · HTTP "+code+"，请检查密钥及服务权限。":code==404||code==405?"该服务未提供模型列表接口，密钥暂未验证。":"服务返回 HTTP "+code+"，密钥暂未验证。";}}
    }
  }

  public sealed class ClaudeKeyWindow {
    public readonly Window Window;
    readonly ClaudeKeyStore store;readonly RoleVisual style;readonly Action applied;
    readonly ListBox profiles=new ListBox();readonly TextBox name=new TextBox(),url=new TextBox();readonly PasswordBox secret=new PasswordBox();readonly ComboBox auth=new ComboBox(),preset=new ComboBox();
    readonly TextBlock active=new TextBlock(),saved=new TextBlock(),status=new TextBlock();readonly Button test,apply,save;
    string selected;bool refreshing,busy,closed;
    Brush Paint(string color){return RoleVisual.Paint(color);}
    TextBlock Text(string value,double size=12){return new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};}
    Button Button(string label,Action action){var button=new Button{Content=label,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,7,7),Background=Paint(style.Paper),Foreground=Paint(style.Ink),BorderBrush=Paint(style.Line),Cursor=System.Windows.Input.Cursors.Hand};var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(14));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;button.Template=template;button.Click+=delegate{Guard(action);};return button;}
    void Field(Panel panel,string label,Control input){panel.Children.Add(Text(label));input.Padding=new Thickness(10,7,10,7);input.Margin=new Thickness(0,0,0,10);input.Background=Paint(style.Paper);input.Foreground=Paint(style.Ink);input.BorderBrush=Paint(style.Line);panel.Children.Add(input);}
    void Notify(string value){status.Text=value;}
    void Guard(Action action){try{action();}catch(Exception error){Notify(error is InvalidDataException?error.Message:"操作未完成，请检查文件权限后重试。");}}
    public ClaudeKeyWindow(Window owner,string role,FontFamily font,Action callback=null,ClaudeKeyStore storage=null){
      store=storage??new ClaudeKeyStore();style=RoleVisual.For(role);applied=callback;
      Window=new Window{Title="Claude Code · 密钥小管家",Width=780,Height=670,MinWidth=700,MinHeight=600,Background=Paint(style.Paper),Foreground=Paint(style.Ink),FontFamily=font,FontSize=13,ResizeMode=ResizeMode.CanResize,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,ShowInTaskbar=true};if(owner!=null)Window.Owner=owner;Window.Closed+=delegate{closed=true;};
      var shell=new DockPanel{Margin=new Thickness(24),Background=Paint(style.Paper)};Window.Content=shell;var header=new StackPanel();header.Children.Add(Text(style.Motif+"  Claude Code · 密钥小管家",24));active.FontSize=13;active.Foreground=Paint(style.Muted);active.Margin=new Thickness(0,0,0,18);header.Children.Add(active);DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
      var footer=new StackPanel{Margin=new Thickness(0,15,0,0)};status.TextWrapping=TextWrapping.Wrap;status.Foreground=Paint(style.Accent);footer.Children.Add(status);footer.Children.Add(Text("配置库与切换备份由当前 Windows 用户加密。应用时，当前 Key 会写入 Claude 的 settings.json，供 CLI 按现有认证机制读取；请勿分享该文件。新选择供下次启动 CLI 使用。",11));DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);
      var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(195)});columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});columns.ColumnDefinitions.Add(new ColumnDefinition());shell.Children.Add(columns);
      var left=new DockPanel();Grid.SetColumn(left,0);columns.Children.Add(left);var listActions=new StackPanel();listActions.Children.Add(Button("＋ 新增一组",New));listActions.Children.Add(Button("导入当前 CLI 配置",delegate{var item=store.ImportCurrent();Refresh(item.Id);Notify("已导入当前配置，密钥只显示末尾四位。");}));listActions.Children.Add(Button("恢复上一次切换",delegate{store.Restore();Refresh(null);Changed();Notify("已恢复上一次的认证配置；下次启动 CLI 使用。");}));DockPanel.SetDock(listActions,Dock.Bottom);left.Children.Add(listActions);profiles.Background=Paint(style.Soft);profiles.BorderBrush=Paint(style.Line);profiles.SelectionChanged+=delegate{if(!refreshing)Load(profiles.SelectedItem as ClaudeKeyProfile);};left.Children.Add(profiles);
      var form=new StackPanel();var scroll=new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,2);columns.Children.Add(scroll);
      Field(form,"配置名称",name);preset.ItemsSource=new[]{"自定义服务","GPTEAM","Anthropic 官方"};preset.SelectedIndex=0;preset.SelectionChanged+=delegate{if(refreshing)return;if(preset.SelectedIndex==1){url.Text="https://api.gpteamservices.com";auth.SelectedIndex=0;}else if(preset.SelectedIndex==2){url.Text="https://api.anthropic.com";auth.SelectedIndex=1;}};Field(form,"快捷填写服务地址",preset);Field(form,"API Base URL",url);
      auth.ItemsSource=new[]{"Bearer Token · ANTHROPIC_AUTH_TOKEN","API Key · X-Api-Key"};auth.SelectedIndex=0;Field(form,"认证方式",auth);
      saved.TextWrapping=TextWrapping.Wrap;saved.Foreground=Paint(style.Muted);form.Children.Add(saved);secret.ToolTip="编辑已有配置时留空保留密钥；不会回填完整密钥。";Field(form,"API Key / Token · 留空保留已有值",secret);
      var buttons=new WrapPanel();save=Button("保存这一组",delegate{Persist();Notify("已保存到本机加密密钥库，尚未应用。");});apply=Button("应用到 Claude",delegate{var item=Persist();store.Apply(item.Id);Refresh(item.Id);Changed();Notify("已应用「"+item.Name+"」。余额将重新查询，下次启动 CLI 使用这组配置。");});test=Button("测试连接",Test);buttons.Children.Add(save);buttons.Children.Add(apply);buttons.Children.Add(test);buttons.Children.Add(Button("删除这一组",delegate{if(selected==null)return;if(MessageBox.Show(Window,"删除选中的已保存配置？","密钥小管家",MessageBoxButton.OKCancel)==MessageBoxResult.OK){store.Delete(selected);Refresh(null);Notify("已删除所选配置。");}}));form.Children.Add(buttons);
      Guard(delegate{Refresh(null);});if(status.Text.Length==0)Notify("可以导入当前 CLI 配置，也可以新增一组密钥。连接测试只读取模型列表。");
    }
    void Changed(){if(applied!=null)applied();}
    public void Refresh(string id){refreshing=true;var document=store.Read();active.Text=document.Active==null?"当前：沿用 Claude 现有认证":"当前应用："+document.Active.Name+"  ·  "+document.Active.AuthMode;profiles.ItemsSource=document.Profiles;var item=document.Profiles.FirstOrDefault(p=>p.Id==(id??(document.Active==null?null:document.Active.Id)))??document.Profiles.FirstOrDefault();profiles.SelectedItem=item;refreshing=false;Load(item);}
    void Load(ClaudeKeyProfile item){refreshing=true;secret.Clear();selected=item==null?null:item.Id;name.Text=item==null?"":item.Name;url.Text=item==null?"https://api.gpteamservices.com":item.BaseUrl;auth.SelectedIndex=item!=null&&item.AuthMode=="api-key"?1:0;preset.SelectedIndex=0;saved.Text=item==null?"新增配置尚未保存。":"已保存密钥："+ClaudeKeyStore.Mask(item)+"\n输入新密钥可替换；留空保留。";saved.Margin=new Thickness(0,0,0,12);refreshing=false;}
    void New(){profiles.SelectedItem=null;Load(null);name.Focus();Notify("填好名称、地址和密钥后，可以保存或直接应用。");}
    ClaudeKeyProfile Persist(){var item=store.Save(selected,name.Text,url.Text,auth.SelectedIndex==1?"api-key":"bearer",secret.Password.Trim());selected=item.Id;secret.Clear();Refresh(item.Id);return item;}
    async void Test(){if(busy)return;ClaudeKeyProfile item;try{item=Persist();}catch(Exception error){Notify(error is InvalidDataException?error.Message:"配置未能保存。");return;}busy=true;test.IsEnabled=apply.IsEnabled=save.IsEnabled=false;Notify("正在测试「"+item.Name+"」的模型列表接口…");try{string result=await Task.Run(delegate{return ClaudeKeyStore.Test(new ClaudeConnection{BaseUrl=item.BaseUrl,Key=ClaudeKeyStore.Reveal(item),AuthMode=item.AuthMode});});if(!closed)Notify(item.Name+" · "+result);}catch{if(!closed)Notify("连接测试未完成，请检查地址或网络。");}finally{busy=false;if(!closed)test.IsEnabled=apply.IsEnabled=save.IsEnabled=true;}}
  }
}
