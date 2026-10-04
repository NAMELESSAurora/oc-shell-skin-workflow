using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;

namespace OCShell {
  // Windows Terminal owns the background underneath its text; the companion only updates appearance fields.
  public sealed class TerminalBackground {
    readonly string root,settingsPath,fragmentPath,folder;
    public string TargetTitle="";
    public string Error="";
    public TerminalBackground(string root,string folder,string settingsPath=null,string fragmentPath=null){this.root=root;this.folder=folder;this.settingsPath=settingsPath??SettingsFile();this.fragmentPath=fragmentPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","Windows Terminal","Fragments",RoleCatalog.FragmentNamespace,"skin.json");}
    public static string SettingsFile(){string folder=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);string[] files={Path.Combine(folder,"Packages","Microsoft.WindowsTerminal_8wekyb3d8bbwe","LocalState","settings.json"),Path.Combine(folder,"Microsoft","Windows Terminal","settings.json"),Path.Combine(folder,"Packages","Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe","LocalState","settings.json")};foreach(string file in files)if(File.Exists(file))return file;return files[0];}
    public string Default(string role){return Path.Combine(root,"assets","backgrounds",RoleCatalog.BackgroundFile(role));}
    public string Selected(string role){try{object config=Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(folder,"terminal-backgrounds.json")));string path=Convert.ToString(Data.At(config,Data.Character(role)));if(File.Exists(path))return path;}catch{}return Default(role);}
    public static void Validate(string path){if(!File.Exists(path)||new FileInfo(path).Length>30000000)throw new InvalidDataException("请选择 30 MB 以内的 PNG 或 JPG 图片");string ext=Path.GetExtension(path).ToLowerInvariant();if(ext!=".png"&&ext!=".jpg"&&ext!=".jpeg")throw new InvalidDataException("背景支持 PNG 和 JPG");using(var file=File.OpenRead(path)){var b=BitmapDecoder.Create(file,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnLoad);if(b.Frames.Count==0||b.Frames[0].PixelWidth<32||b.Frames[0].PixelHeight<32||b.Frames[0].PixelWidth>16000||b.Frames[0].PixelHeight>16000)throw new InvalidDataException("图片尺寸无效");}}
    public void Choose(string role,string path){Validate(path);Directory.CreateDirectory(folder);Dictionary<string,object> choices;try{choices=Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"terminal-backgrounds.json")));}catch{choices=new Dictionary<string,object>();}choices[Data.Character(role)]=Path.GetFullPath(path);ChatStore.Atomic(Path.Combine(folder,"terminal-backgrounds.json"),Data.Json.Serialize(choices));Apply(role,false);}
    public void Restore(string role){Choose(role,Default(role));}
    public void Apply(string role,bool all){
      Error="";try{using(var mutex=new Mutex(false,"Local\\OCShellTerminalBackground")){bool owned=false;try{try{owned=mutex.WaitOne(1500);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new IOException();Update(settingsPath,true,role,all);Update(fragmentPath,false,role,all);}finally{if(owned)mutex.ReleaseMutex();}}}catch{Error="终端背景未能更新，已有配置保留。";}
    }
    static string ProfileRole(Dictionary<string,object> profile){string name=Convert.ToString(Data.At(profile,"name"));string scheme=Convert.ToString(Data.At(profile,"colorScheme"));foreach(string role in RoleCatalog.All)if(name.Equals("PowerShell - "+role,StringComparison.OrdinalIgnoreCase)||name.Equals("Claude - "+role,StringComparison.OrdinalIgnoreCase))return role;return null;}
    void Update(string file,bool userSettings,string role,bool all){
      if(!File.Exists(file))return;var document=(Dictionary<string,object>)Data.Json.DeserializeObject(File.ReadAllText(file));object[] profiles=(userSettings?Data.At(document,"profiles","list"):Data.At(document,"profiles")) as object[];if(profiles==null)throw new InvalidDataException("Terminal profile list is missing");bool changed=false;
      foreach(object entry in profiles){var profile=entry as Dictionary<string,object>;if(profile==null)continue;string originalRole=ProfileRole(profile);if(originalRole==null)continue;string name=Convert.ToString(Data.At(profile,"name"));string tab=Convert.ToString(Data.At(profile,"tabTitle"));bool target=TargetTitle.Length>0&&(name.Equals(TargetTitle,StringComparison.OrdinalIgnoreCase)||tab.Equals(TargetTitle,StringComparison.OrdinalIgnoreCase));if(!all&&originalRole!=role&&!target)continue;string selectedRole=all?originalRole:role;string path=Selected(selectedRole);Validate(path);
        if(userSettings)Backup(profile,file);
        profile["backgroundImage"]=path;profile["backgroundImageAlignment"]="bottomLeft";profile["backgroundImageStretchMode"]="uniformToFill";profile["backgroundImageOpacity"]=.36;if(userSettings)Applied(profile);changed=true;
      }
      if(changed)ChatStore.Atomic(file,Data.Json.Serialize(document));
    }
    void Backup(Dictionary<string,object> profile,string configPath){string statePath=Path.Combine(root,"background-installation-state.json");Dictionary<string,object> state;try{state=Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(statePath));}catch{state=new Dictionary<string,object>();}string id=Convert.ToString(Data.At(profile,"guid"));if(state.ContainsKey(id))return;var fields=new Dictionary<string,object>();foreach(string key in new[]{"backgroundImage","backgroundImageAlignment","backgroundImageStretchMode","backgroundImageOpacity"})fields[key]=new{present=profile.ContainsKey(key),value=Data.At(profile,key)};state[id]=new{configPath=configPath,fields=fields};ChatStore.Atomic(statePath,Data.Json.Serialize(state));}
    void Applied(Dictionary<string,object> profile){string path=Path.Combine(root,"background-installation-state.json");var state=Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));var entry=(Dictionary<string,object>)state[Convert.ToString(Data.At(profile,"guid"))];var applied=new Dictionary<string,object>();foreach(string key in new[]{"backgroundImage","backgroundImageAlignment","backgroundImageStretchMode","backgroundImageOpacity"})applied[key]=profile[key];entry["applied"]=applied;ChatStore.Atomic(path,Data.Json.Serialize(state));}
  }
}
