using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OCShell {
  public static class VoiceBakeJob {
    static object Ready(QwenVoice voice,InteractionVoices book){var result=new System.Collections.Generic.Dictionary<string,int>();foreach(string role in QwenVoice.Roles)result[role]=book.ReadyCount(voice,role);return result;}
    public static async Task BakeRoles(QwenVoice voice,InteractionVoices book,string[] roles,CancellationToken cancel,Action<string> report){
      System.Net.ServicePointManager.DefaultConnectionLimit=Math.Max(6,System.Net.ServicePointManager.DefaultConnectionLimit);
      using(var batch=CancellationTokenSource.CreateLinkedTokenSource(cancel)){var work=new System.Collections.Generic.List<Task>();Exception failure=null;var gate=new object();
        foreach(string role in roles){string current=role;work.Add(Task.Run(async delegate{try{await book.Bake(voice,new[]{current},batch.Token,delegate(string message){lock(gate)report(current+" · "+message);});}catch(Exception e){lock(gate){if(failure==null&&!(e is OperationCanceledException))failure=e;}batch.Cancel();throw;}},batch.Token));}
        try{await Task.WhenAll(work);}catch{if(failure!=null)throw failure;throw;}
      }
    }
    public static async Task<int> Run(string root,bool cloneOnly,string onlyRole=null){
      if(onlyRole!=null&&Array.IndexOf(QwenVoice.Roles,onlyRole)<0)throw new InvalidDataException("未知烘焙角色");string[] roles=onlyRole==null?RoleCatalog.All:new[]{onlyRole};
      var voice=new QwenVoice(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoleCatalog.ConfigurationNamespace),root);
      var book=new InteractionVoices(root);Directory.CreateDirectory(book.ExportFolder);string state=Path.Combine(book.ExportFolder,"bake-progress.json");
      string phase="连接";Action<string> report=delegate(string message){phase=message;ChatStore.Atomic(state,QwenCodec.Encode(new{updated=DateTime.UtcNow.ToString("o"),state="running",message=message,model=voice.Options.Model,ready=Ready(voice,book)}));};
      try{
        var options=voice.Options;if(!options.EnableCloudSpeech)throw new InvalidOperationException("EnableCloudSpeech must be enabled explicitly before clone or bake.");voice.Save(options,"");book.Inventory(voice);report("正在连接北京百炼，保留各角色的模型与音色绑定");
        foreach(string role in roles){var record=await voice.Clone(role,CancellationToken.None,report);if(record.Status!="OK"&&!await voice.WaitClone(role,CancellationToken.None,report))throw new InvalidOperationException(role+" 的音色仍在准备，ID 已保存；稍后重试将继续查询");report(role+" 音色已就绪");}
        if(!cloneOnly){await BakeRoles(voice,book,roles,CancellationToken.None,report);voice.Options.Reactions=true;voice.Save(voice.Options,"");}
        ChatStore.Atomic(state,QwenCodec.Encode(new{updated=DateTime.UtcNow.ToString("o"),state="complete",message=cloneOnly?(onlyRole==null?"当前角色音色已克隆并绑定":onlyRole+" 音色已绑定"):(onlyRole==null?"当前角色触碰语音已烘焙并启用":onlyRole+" 的 "+book.Lines(onlyRole).Count+" 条日语触碰语音已更新并启用"),model=voice.Options.Model,ready=Ready(voice,book)}));book.Inventory(voice);return 0;
      }catch(Exception e){ChatStore.Atomic(state,QwenCodec.Encode(new{updated=DateTime.UtcNow.ToString("o"),state="error",phase=phase,message=e is InvalidOperationException||e is InvalidDataException?e.Message:"语音任务未完成（"+e.GetType().Name+"）；已有音色与文件保留",model=voice.Options.Model,ready=Ready(voice,book)}));book.Inventory(voice);return 1;}
    }
  }
}
