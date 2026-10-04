using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OCShell {
  public sealed class TouchLine {public string Id,Context,Japanese,Chinese,Delivery;}
  public sealed class TouchScript {public Dictionary<string,List<TouchLine>> Characters=new Dictionary<string,List<TouchLine>>();}
  public sealed class InteractionVoices {
    readonly string root,exportOverride;readonly object inventoryLock=new object();TouchScript script;DateTime scriptStamp=DateTime.MinValue;readonly Random random=new Random();readonly Dictionary<string,string> previous=new Dictionary<string,string>();
    readonly Dictionary<string,DateTime> touched=new Dictionary<string,DateTime>();readonly Dictionary<string,int> streaks=new Dictionary<string,int>();
    public InteractionVoices(string root,string exportFolder=null){this.root=root;exportOverride=string.IsNullOrWhiteSpace(exportFolder)?null:exportFolder;script=new TouchScript();ReloadIfChanged();}
    public void ReloadIfChanged(){string path=Path.Combine(root,"interaction-lines-ja.json");try{DateTime stamp=File.GetLastWriteTimeUtc(path);if(stamp==scriptStamp)return;var loaded=QwenCodec.Read<TouchScript>(File.ReadAllText(path));if(loaded==null||loaded.Characters==null)return;script=loaded;scriptStamp=stamp;previous.Clear();streaks.Clear();}catch{}}
    public List<TouchLine> Lines(string role){List<TouchLine> lines;return script.Characters.TryGetValue(role,out lines)?lines:new List<TouchLine>();}
    public TouchLine Pick(string role,string context){
      // The latest set has seven eight-line pools; retain the old six role mappings.
      if(Array.IndexOf(RoleCatalog.All,role)>=0)if(context=="headPat"||context=="cheek"||context=="greeting"||context=="dock")context="touch";
      if(context=="touch"||context=="headPat"||context=="cheek"||context=="dollTouch"){DateTime time;int count;bool repeat=touched.TryGetValue(role,out time)&&(DateTime.UtcNow-time).TotalSeconds<4;streaks.TryGetValue(role,out count);streaks[role]=repeat?count+1:1;touched[role]=DateTime.UtcNow;if(streaks[role]>=4)context="repeatTouch";}
      var choices=Lines(role).FindAll(delegate(TouchLine line){return line.Context==context;});if(choices.Count==0)return null;string key=role+":"+context,last;previous.TryGetValue(key,out last);if(choices.Count>1)choices.RemoveAll(delegate(TouchLine line){return line.Id==last;});TouchLine selected=choices[random.Next(choices.Count)];previous[key]=selected.Id;return selected;
    }
    public string ExportFolder {get{return exportOverride??Path.Combine(root,"voice-library","interaction-ja");}}
    public string ExportPath(string role,TouchLine line){return Path.Combine(ExportFolder,Data.Character(role),line.Id+".wav");}
    public string Direction(TouchLine line){return line.Id.StartsWith("viola_")?line.Delivery+"使用自然日语日常对话，只说台词。":"使用自然日语。"+line.Delivery+"只朗读台词，不读出表演说明，不加音乐或额外文字。";}
    public string Exported(string role,TouchLine line){
      try{if(line==null||role!=Data.Character(role)||!line.Id.StartsWith(role+"_",StringComparison.Ordinal)||!System.Text.RegularExpressions.Regex.IsMatch(line.Id,"^[A-Za-z0-9_]+$"))return null;string path=Path.GetFullPath(ExportPath(role,line)),basePath=Path.GetFullPath(ExportFolder)+Path.DirectorySeparatorChar;if(!path.StartsWith(basePath,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||!File.Exists(path+".json"))return null;var meta=QwenCodec.Decode(File.ReadAllText(path+".json"));if(Convert.ToString(Data.At(meta,"role"))!=role||Convert.ToString(Data.At(meta,"id"))!=line.Id||Convert.ToString(Data.At(meta,"ja"))!=line.Japanese)return null;byte[] bytes=File.ReadAllBytes(path);var info=WaveInfo.Read(bytes,false);if(info.Rate!=24000||info.Channels!=1||info.Bits!=16||info.Seconds<.25||info.Seconds>30||info.Hash!=Convert.ToString(Data.At(meta,"sha256")))return null;return path;}catch{return null;}
    }
    public string CloudCached(QwenVoice voice,string role,TouchLine line){try{string cache=voice.CachePath(voice.Options,role,line.Japanese,Direction(line),"ja");return File.Exists(cache)&&QwenVoice.Wave(File.ReadAllBytes(cache))?cache:null;}catch{return null;}}
    public string Cached(QwenVoice voice,string role,TouchLine line){return Exported(role,line)??CloudCached(voice,role,line);}

    public int ReadyCount(QwenVoice voice,string role){int n=0;foreach(var line in Lines(role))if(Cached(voice,role,line)!=null)n++;return n;}
    async Task<string> BakeRetry(QwenVoice voice,string role,TouchLine line,CancellationToken cancel,Action<string> progress){for(int attempt=0;;attempt++){try{return await voice.Bake(role,line.Japanese,cancel,Direction(line),"ja");}catch(InvalidOperationException e){bool transient=e.Message.Contains("过于频繁")||e.Message.Contains("连接中断")||e.Message.Contains("超时")||e.Message.Contains("HTTP 5");if(!transient||attempt>=2)throw;progress(role+" · "+line.Id+" 网络暂忙，保留进度后重试 "+(attempt+1));}await Task.Delay(attempt==0?4000:12000,cancel);}}
    public void Inventory(QwenVoice voice){lock(inventoryLock){Directory.CreateDirectory(ExportFolder);var records=new List<object>();foreach(string role in QwenVoice.Roles)foreach(var line in Lines(role)){string cache=Cached(voice,role,line);records.Add(new{role=role,model=QwenVoice.Model(voice.Options,role),id=line.Id,context=line.Context,ja=line.Japanese,zh=line.Chinese,delivery=line.Delivery,status=cache==null?"pending":"ready",wav=cache==null?null:Data.Character(role)+"/"+line.Id+".wav",sha256=cache==null?null:QwenVoice.Hash(File.ReadAllBytes(cache))});}ChatStore.Atomic(Path.Combine(ExportFolder,"manifest.json"),QwenCodec.Encode(new{updated=DateTime.UtcNow.ToString("o"),model=voice.Options.Model,clips=records}));}}
    public async Task<int> Bake(QwenVoice voice,IEnumerable<string> roles,CancellationToken cancel,Action<string> progress){
      int count=0;Directory.CreateDirectory(ExportFolder);
      try{foreach(string role in roles){foreach(var line in Lines(role)){
        cancel.ThrowIfCancellationRequested();string cached=CloudCached(voice,role,line);bool fresh=cached==null;progress(role+" · "+line.Id+" · "+(fresh?"合成中":"复用缓存"));
        string file=cached??await BakeRetry(voice,role,line,cancel,progress);string target=ExportPath(role,line);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
        ChatStore.Atomic(target+".json",QwenCodec.Encode(new{role=role,id=line.Id,ja=line.Japanese,zh=line.Chinese,delivery=line.Delivery,model=QwenVoice.Model(voice.Options,role),sha256=QwenVoice.Hash(File.ReadAllBytes(file))}));count++;progress("已保存 "+count+" 条 · "+role+" / "+line.Id);if(fresh)await Task.Delay(400,cancel);
      }voice.Options.Reactions=true;voice.Save(voice.Options,"");}}finally{Inventory(voice);}return count;
    }
  }
}
