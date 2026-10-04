using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace OCShell {
  public static class AppearanceCatalog {
    public static readonly string[] Styles={"bot","chibi"};
    public static string Default(string role){string key=RoleCatalog.Canonical(role);return RoleCatalog.DefaultAppearance;}
    public static bool Valid(string style){return style=="bot"||style=="chibi";}
    public static string Name(string style){return style=="chibi"?"精致 Q 版":"圆眼 Bot";}
    public static string File(string root,string role,string style){if(!Valid(style))throw new InvalidDataException("请选择圆眼 Bot 或精致 Q 版。");string key=RoleCatalog.Canonical(role),path=Path.Combine(root,"assets","appearances",key+"-"+style+".png");if(System.IO.File.Exists(path))return path;if(style==Default(key))return Path.Combine(root,"assets",key+"-corner-complete.png");throw new FileNotFoundException("这款外观图片暂未安装。",path);}
  }
  // Each helper merges only its changed role under a per-file cross-process mutex.
  // Unknown document/role fields survive; a corrupt document is never replaced with defaults.
  public sealed class AppearancePreferences {
    public readonly string Path;
    readonly string mutexName;
    Dictionary<string,object> values=new Dictionary<string,object>();
    DateTime stamp=DateTime.MinValue;long length=-1;
    public string Error="";
    public AppearancePreferences(string folder){System.IO.Directory.CreateDirectory(folder);Path=System.IO.Path.Combine(folder,"appearance-settings.json");using(var hash=SHA256.Create())mutexName="Local\\OCShellAppearance-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(Path).ToUpperInvariant()))).Replace("-","").Substring(0,24);Reload();}
    static Dictionary<string,object> Read(string path){if(!System.IO.File.Exists(path))return new Dictionary<string,object>();var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(System.IO.File.ReadAllText(path));object roles,node;if(data==null||(data.TryGetValue("characters",out roles)&&Object(roles)==null))throw new InvalidDataException("外观设置格式无效。");if(data.TryGetValue("characters",out roles)){var characters=Object(roles);foreach(string role in RoleCatalog.All)if(characters.TryGetValue(role,out node)&&Object(node)==null)throw new InvalidDataException("角色外观设置格式无效。");}return data;}
    static Dictionary<string,object> Object(object value){return value as Dictionary<string,object>;}
    public string Get(string role){object roles,node,style;Dictionary<string,object> characters,entry;if(values.TryGetValue("characters",out roles)&&(characters=Object(roles))!=null&&characters.TryGetValue(RoleCatalog.Canonical(role),out node)&&(entry=Object(node))!=null&&entry.TryGetValue("style",out style)&&AppearanceCatalog.Valid(style as string))return (string)style;return AppearanceCatalog.Default(role);}
    public bool Reload(){try{var info=new FileInfo(Path);DateTime now=info.Exists?info.LastWriteTimeUtc:DateTime.MinValue;long size=info.Exists?info.Length:-1;if(now==stamp&&size==length&&Error.Length==0)return false;values=Read(Path);stamp=now;length=size;Error="";return true;}catch{Error="外观设置暂时无法读取；保留当前造型。";return false;}}
    public void Set(string role,string style){if(!AppearanceCatalog.Valid(style))throw new InvalidDataException("请选择圆眼 Bot 或精致 Q 版。");using(var mutex=new Mutex(false,mutexName)){bool owned=false;try{try{owned=mutex.WaitOne(2000);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new IOException("外观设置正在保存，请稍后重试。");var current=Read(Path);object raw;Dictionary<string,object> characters;if(current.TryGetValue("characters",out raw)){characters=Object(raw);if(characters==null)throw new InvalidDataException("外观角色设置格式无效。");}else{characters=new Dictionary<string,object>();current["characters"]=characters;}string key=RoleCatalog.Canonical(role);Dictionary<string,object> node;if(characters.TryGetValue(key,out raw)){node=Object(raw);if(node==null)throw new InvalidDataException("角色外观设置格式无效。");}else{node=new Dictionary<string,object>();characters[key]=node;}node["style"]=style;if(!current.ContainsKey("version"))current["version"]=1;ChatStore.Atomic(Path,new JavaScriptSerializer().Serialize(current));values=current;var info=new FileInfo(Path);stamp=info.LastWriteTimeUtc;length=info.Length;Error="";}catch{Error="外观选择未能保存；当前造型已保留。";throw;}finally{if(owned)mutex.ReleaseMutex();}}}
  }
}
