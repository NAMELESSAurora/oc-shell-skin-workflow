using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OCShell {
  public static class QwenCodec {
    public static string Encode(object value){return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);}
    public static object Decode(string value){return new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(value);}
    public static T Read<T>(string value){return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(value);}
  }
  public sealed class CloneRecord {
    public string VoiceId="",Model="",Workspace="",ReferenceHash="",Status="",Created="";
  }
  public sealed class VoiceOptions {
    public bool Sfx=true,Reactions=true,Chat=false;
    public bool EnableCloudSpeech=false;
    public double Volume=.38;
    public string Provider="beijing",Endpoint="http://127.0.0.1:8000/v1/audio/speech",Model=RoleCatalog.DefaultVoiceModel,WorkspaceId="";
    public Dictionary<string,string> Voices=new Dictionary<string,string>{{RoleCatalog.DefaultRole,""}};
    public Dictionary<string,string> References=new Dictionary<string,string>();
    public Dictionary<string,string> ReferenceUrls=new Dictionary<string,string>();
    public Dictionary<string,CloneRecord> Clones=new Dictionary<string,CloneRecord>();
    public Dictionary<string,string> Directions=new Dictionary<string,string>();
    public Dictionary<string,double> Rates=new Dictionary<string,double>{{RoleCatalog.DefaultRole,RoleCatalog.VoiceRate}};
    public Dictionary<string,string> Models=new Dictionary<string,string>();
    public Dictionary<string,bool> EmotionEnhancement=new Dictionary<string,bool>();
  }
  public sealed class WaveInfo {
    public int Rate,Channels,Bits;public double Seconds;public string Hash;
    // Qwen can return a finished HTTP download with an open-ended streaming WAV header.
    // Finalize only this specific sentinel form; ordinary truncated WAVs remain invalid.
    public static byte[] CompleteProviderWave(byte[] bytes){
      if(bytes.Length<44||Encoding.ASCII.GetString(bytes,0,4)!="RIFF"||Encoding.ASCII.GetString(bytes,8,4)!="WAVE")return bytes;
      uint riff=BitConverter.ToUInt32(bytes,4);if(!OpenLength(riff))return bytes;
      int channels=0,bits=0,format=0;
      for(int pos=12;pos+8<=bytes.Length;){string tag=Encoding.ASCII.GetString(bytes,pos,4);uint length=BitConverter.ToUInt32(bytes,pos+4);
        if(tag=="data"&&OpenLength(length)){int available=bytes.Length-pos-8;if(format!=1||bits!=16||channels<1||channels>2||available==0||available%(channels*2)!=0)throw new InvalidDataException("流式 WAV 的 PCM 数据不完整");var completed=(byte[])bytes.Clone();Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length-8),0,completed,4,4);Buffer.BlockCopy(BitConverter.GetBytes(available),0,completed,pos+4,4);Read(completed,false);return completed;}
        if(length>int.MaxValue||pos+8L+length>bytes.Length)break;int n=(int)length;if(tag=="fmt "&&n>=16){format=BitConverter.ToUInt16(bytes,pos+8);channels=BitConverter.ToUInt16(bytes,pos+10);bits=BitConverter.ToUInt16(bytes,pos+22);}pos+=8+n+n%2;
      }throw new InvalidDataException("服务返回的流式 WAV 无法完成封装");
    }
    static bool OpenLength(uint value){return value==uint.MaxValue||(value>=0x7fff0000&&value<=0x7fffffff);}
    public static WaveInfo Read(byte[] bytes,bool reference){
      if(bytes.Length<44||Encoding.ASCII.GetString(bytes,0,4)!="RIFF"||Encoding.ASCII.GetString(bytes,8,4)!="WAVE")throw new InvalidDataException("请选择真正的 PCM WAV 音频");
      int format=0,rate=0,channels=0,bits=0,size=0;bool found=false;
      for(int pos=12;pos+8<=bytes.Length;){string tag=Encoding.ASCII.GetString(bytes,pos,4);uint length=BitConverter.ToUInt32(bytes,pos+4);if(length>int.MaxValue||pos+8L+length>bytes.Length)throw new InvalidDataException("WAV 文件不完整");int n=(int)length;
        if(tag=="fmt "&&n>=16){format=BitConverter.ToUInt16(bytes,pos+8);channels=BitConverter.ToUInt16(bytes,pos+10);rate=BitConverter.ToInt32(bytes,pos+12);bits=BitConverter.ToUInt16(bytes,pos+22);}
        if(tag=="data"){size+=n;found=true;}pos+=(int)(8L+n+(n%2));
      }
      if(!found||size==0||format!=1||channels<1||channels>2||rate<8000||bits!=16)throw new InvalidDataException("需要非空、16 bit PCM WAV（单声道或双声道）");
      double seconds=size/(double)(rate*channels*2);
      if(reference&&(bytes.Length>10*1024*1024||rate<16000||seconds<5||seconds>60))throw new InvalidDataException("参考音需为 5–60 秒、至少 16 kHz、10 MB 以内的 16 bit WAV；建议 10–20 秒");
      return new WaveInfo{Rate=rate,Channels=channels,Bits=bits,Seconds=seconds,Hash=QwenVoice.Hash(bytes)};
    }
  }
  // Network results are never logged: they may contain signed OSS URLs or upload credentials.
  public class QwenTransport {
    public virtual byte[] Send(Uri uri,string method,string key,string contentType,byte[] body,bool resolve,CancellationToken cancel,int limit){
      cancel.ThrowIfCancellationRequested();ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
      var request=(HttpWebRequest)WebRequest.Create(uri);request.Method=method;request.AllowAutoRedirect=false;request.Timeout=request.ReadWriteTimeout=60000;request.KeepAlive=false;
      if(contentType.Length>0)request.ContentType=contentType;if(key.Length>0)request.Headers[HttpRequestHeader.Authorization]="Bearer "+key;if(resolve)request.Headers["X-DashScope-OssResourceResolve"]="enable";
      try{using(cancel.Register(delegate{request.Abort();})){
        if(body!=null){request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);}
        using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var memory=new MemoryStream()){
          byte[] buffer=new byte[8192];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();if(memory.Length+count>limit)throw new InvalidDataException("Qwen 响应超出大小限制");memory.Write(buffer,0,count);}return memory.ToArray();
        }
      }}catch(WebException e){cancel.ThrowIfCancellationRequested();var response=e.Response as HttpWebResponse;int status=response==null?0:(int)response.StatusCode;string code="";if(response!=null){try{using(var error=response.GetResponseStream())using(var reader=new StreamReader(error)){string text=reader.ReadToEnd();if(text.Length<262144){if(text.TrimStart().StartsWith("{"))code=Convert.ToString(Data.At(QwenCodec.Decode(text),"code"));else{var match=Regex.Match(text,"<Code>([A-Za-z0-9_.-]+)</Code>");if(match.Success)code=match.Groups[1].Value;}}}}catch{}response.Close();}if(!Regex.IsMatch(code,"^[A-Za-z0-9_.-]{0,100}$"))code="";
        if(code=="AllocationQuota.FreeTierOnly")throw new InvalidOperationException("百炼已拦截本次调用（AllocationQuota.FreeTierOnly）：当前模型没有可用免费额度，任务已停止；已有音色、台词和音频保留。不会自动改为付费调用。");
        if(code=="Arrearage"||code=="ArrearageOverdue")throw new InvalidOperationException("百炼账户欠费拦截（HTTP "+status+" · "+code+"）：新 Key 已鉴权，但账户当前无法调用。免费额度也不能绕过账户限制；任务已停止，已有内容保留。");
        string operation=uri.AbsolutePath.EndsWith("/uploads")?"获取上传凭证":contentType.StartsWith("multipart/")?"上传参考音":uri.AbsolutePath.EndsWith("/customization")?"创建或查询音色":"合成或下载语音";
        throw new InvalidOperationException(status==429?"Qwen 请求过于频繁，稍后可继续已有进度":status==0?"Qwen 连接中断或超时；创建音色超时后先查询状态，避免重复上传":"Qwen "+operation+"失败（HTTP "+status+(code.Length>0?" · "+code:"")+"）");
      }
    }
    public object Json(Uri uri,string method,string key,object body,bool resolve,CancellationToken cancel){return QwenCodec.Decode(Encoding.UTF8.GetString(Send(uri,method,key,"application/json",body==null?null:Encoding.UTF8.GetBytes(QwenCodec.Encode(body)),resolve,cancel,262144)));}
  }
  public sealed class QwenVoice {
    public const string PortableFolderName=RoleCatalog.ConfigurationNamespace;
    public static readonly string[] Roles=RoleCatalog.All;
    public static string RoleName(string role){return RoleCatalog.Name(role);}
    public readonly string Folder;readonly string root;readonly QwenTransport transport;readonly QwenRealtime realtime;readonly object usageLock=new object(),settingsLock=new object();
    public VoiceOptions Options;public string ConfigurationError="";DateTime stamp=DateTime.MinValue,retryAt=DateTime.MinValue;
    public QwenVoice(string folder,string projectRoot=null,QwenTransport client=null,QwenRealtime realtime=null){Folder=folder;root=projectRoot;transport=client??new QwenTransport();this.realtime=realtime??new QwenRealtime();Directory.CreateDirectory(folder);Reload();}
    public void Reload(){string path=Path.Combine(Folder,"qwen-voice.json");try{if(!File.Exists(path))throw new FileNotFoundException();var loaded=QwenCodec.Read<VoiceOptions>(File.ReadAllText(path));if(loaded==null)throw new InvalidDataException();Normalize(loaded);Options=loaded;stamp=File.GetLastWriteTimeUtc(path);ConfigurationError="";retryAt=DateTime.MinValue;}catch(Exception e){if(Options==null){Options=new VoiceOptions();Normalize(Options);}ConfigurationError=File.Exists(path)?"声音设置暂时无法读取（"+e.GetType().Name+"）；保留上次设置并稍后重试":"离线互动语音可用；Qwen联网合成尚未配置";stamp=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;retryAt=DateTime.UtcNow.AddSeconds(1);}}
    void Normalize(VoiceOptions o){if(o.Voices==null)o.Voices=new VoiceOptions().Voices;if(o.References==null)o.References=new Dictionary<string,string>();if(o.ReferenceUrls==null)o.ReferenceUrls=new Dictionary<string,string>();if(o.Clones==null)o.Clones=new Dictionary<string,CloneRecord>();if(o.Directions==null)o.Directions=new Dictionary<string,string>();if(o.Rates==null)o.Rates=new Dictionary<string,double>();if(o.Models==null)o.Models=new Dictionary<string,string>();if(o.EmotionEnhancement==null)o.EmotionEnhancement=new Dictionary<string,bool>();foreach(string role in Roles){if(!o.Voices.ContainsKey(role))o.Voices[role]="";if(!o.References.ContainsKey(role))o.References[role]=root==null?"":Path.Combine(root,"assets","voice-references",role+".wav");if(!o.ReferenceUrls.ContainsKey(role))o.ReferenceUrls[role]="";}o.Volume=Math.Max(0,Math.Min(1,o.Volume));}
    public void ReloadIfChanged(){string path=Path.Combine(Folder,"qwen-voice.json");DateTime now=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;if(now!=stamp||(ConfigurationError.Length>0&&DateTime.UtcNow>=retryAt))Reload();}
    public string Key(){try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Folder,"qwen-key.bin")),null,DataProtectionScope.CurrentUser));}catch{return "";}}
    public void Save(VoiceOptions options,string key){lock(settingsLock){if(options.Volume<0||options.Volume>1)throw new InvalidDataException("音量需在 0 到 1 之间");if(options.Provider=="local")EndpointFor(options);if(!string.IsNullOrWhiteSpace(options.WorkspaceId))Workspace(options);Normalize(options);Options=options;ChatStore.Atomic(Path.Combine(Folder,"qwen-voice.json"),QwenCodec.Encode(options));if(!string.IsNullOrWhiteSpace(key))File.WriteAllBytes(Path.Combine(Folder,"qwen-key.bin"),ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()),null,DataProtectionScope.CurrentUser));stamp=File.GetLastWriteTimeUtc(Path.Combine(Folder,"qwen-voice.json"));ConfigurationError="";retryAt=DateTime.MinValue;}}
    public static VoiceOptions ForRole(VoiceOptions o,string role){string model;if(o.Models==null||!o.Models.TryGetValue(role,out model)||string.IsNullOrWhiteSpace(model)||model==o.Model)return o;var copy=QwenCodec.Read<VoiceOptions>(QwenCodec.Encode(o));copy.Model=model.Trim();return copy;}
    public static string Model(VoiceOptions o,string role){return ForRole(o,role).Model;}
    public static bool Realtime(VoiceOptions o){return o.Provider!="local"&&o.Model=="qwen-audio-3.1-realtime-plus";}
    public static bool Modern(VoiceOptions o){return o.Model.StartsWith("qwen-audio-",StringComparison.Ordinal);}
    static string Workspace(VoiceOptions o){string id=(o.WorkspaceId??"").Trim();if(id.Length==0)throw new InvalidDataException("请填写北京百炼业务空间 ID（Workspace ID）");if(!Regex.IsMatch(id,"^[A-Za-z0-9][A-Za-z0-9-]{0,62}$"))throw new InvalidDataException("业务空间 ID 格式不正确");return id;}
    public static Uri CloudEndpoint(VoiceOptions o,string service){if(o.Provider!="beijing")throw new InvalidDataException("Qwen Audio 3.1 请使用北京百炼地域");string host=string.IsNullOrWhiteSpace(o.WorkspaceId)?"dashscope.aliyuncs.com":Workspace(o)+".cn-beijing.maas.aliyuncs.com";return new Uri("https://"+host+"/api/v1/services/audio/tts/"+service);}
    public static Uri EndpointFor(VoiceOptions o){if(o.Provider=="local"){Uri uri;if(!Uri.TryCreate(o.Endpoint,UriKind.Absolute,out uri)||!uri.IsLoopback||(uri.Scheme!="http"&&uri.Scheme!="https")||uri.UserInfo.Length>0)throw new InvalidDataException("本地 Qwen 接口需使用 localhost 或 127.0.0.1");return uri;}if(Modern(o))return CloudEndpoint(o,"SpeechSynthesizer");if(o.Provider=="beijing")return new Uri("https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation");if(o.Provider=="singapore")return new Uri("https://dashscope-intl.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation");throw new InvalidDataException("未知语音服务");}
    public static string Voice(VoiceOptions o,string role){string value;if(o.Voices.TryGetValue(role,out value)&&!string.IsNullOrWhiteSpace(value))return value.Trim();if(!Modern(o)||o.Provider=="local")return "Cherry";throw new InvalidDataException("这位角色还没有克隆音色，请先上传复刻或填写音色 ID");}
    public static string Direction(string role){return RoleCatalog.VoiceDirection;}
    public static string Direction(VoiceOptions options,string role){string value;return options.Directions!=null&&options.Directions.TryGetValue(role,out value)?value:Direction(role);}
    public static double Rate(VoiceOptions options,string role){double value;if(options.Rates!=null&&options.Rates.TryGetValue(role,out value)){if(double.IsNaN(value)||double.IsInfinity(value)||value<.5||value>2)throw new InvalidDataException("语速需在 0.5 到 2 之间");return value;}return RoleCatalog.VoiceRate;}
    public static object RequestBody(VoiceOptions o,string role,string text,string instruction="",string language="zh"){
      o=ForRole(o,role);if(Realtime(o))return new{model=o.Model,session=QwenRealtime.Session(o,role,instruction,language),literal=text};
      if(o.Provider=="local")return new{model=o.Model,input=text,voice=Voice(o,role),response_format="wav"};
      if(Modern(o))return new{model=o.Model,input=new{text=text,voice=Voice(o,role),format="wav",sample_rate=24000,volume=50,rate=Rate(o,role),pitch=1.0,language_hints=new[]{language},instruction=Direction(o,role)+instruction}};
      return new{model=o.Model,input=new{text=text,voice=Voice(o,role),language_type=language=="ja"?"Japanese":"Chinese"}};
    }
    public static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    public string CachePath(VoiceOptions o,string role,string text,string instruction="",string language="zh"){o=ForRole(o,role);string identity=(Realtime(o)?QwenRealtime.Endpoint(o):EndpointFor(o))+"\n"+QwenCodec.Encode(RequestBody(o,role,text,instruction,language));string folder=Path.Combine(Folder,"voice-cache",Data.Character(role));Directory.CreateDirectory(folder);return Path.Combine(folder,Hash(Encoding.UTF8.GetBytes(identity))+".wav");}
    public static bool Wave(byte[] data){try{WaveInfo.Read(data,false);return true;}catch{return false;}}
    public static Uri AliUrl(string value){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.UserInfo.Length>0||!(uri.Host.EndsWith(".aliyuncs.com",StringComparison.OrdinalIgnoreCase))||(uri.Scheme!="https"&&uri.Scheme!="http")||(uri.Port!=80&&uri.Port!=443))throw new InvalidDataException("Qwen 未返回可用的阿里云音频地址");return uri.Scheme=="http"?new UriBuilder(uri){Scheme="https",Port=443}.Uri:uri;}
    public static Uri AudioUrl(object result){return AliUrl(Convert.ToString(Data.At(result,"output","audio","url")));}
    public bool CanSynthesize(string role){try{var o=ForRole(Options,role);if(!o.EnableCloudSpeech)return false;EndpointFor(o);if(o.Provider=="local")return true;if(Key().Length==0)return false;Voice(o,role);ValidateClone(o,role);return true;}catch{return false;}}
    void ReadySpeech(VoiceOptions o){if(!o.EnableCloudSpeech)throw new InvalidOperationException("内置语音可离线播放；若要生成新语音，请先在此便携版中配置并启用联网合成");Ready(o);}
    void Ready(VoiceOptions o){EndpointFor(o);if(o.Provider!="local"&&Key().Length==0)throw new InvalidOperationException("请在声音设置窗口填写 Qwen Key；不需要发到聊天里");}
    void ValidateClone(VoiceOptions o,string role){CloneRecord record;if(o.Clones.TryGetValue(role,out record)&&record.VoiceId==Voice(o,role)&&(record.Model!=o.Model||record.Workspace!=o.WorkspaceId))throw new InvalidDataException("这个音色属于原来的模型或业务空间，请切回原设置或重新克隆");}
    public async Task<string> Bake(string role,string text,CancellationToken cancel,string instruction="",string language="zh"){
      cancel.ThrowIfCancellationRequested();var o=ForRole(Options,role);if(text.Length==0||text.Length>600)throw new InvalidDataException("语音单句需在 1 到 600 字以内");ValidateClone(o,role);string target=CachePath(o,role,text,instruction,language);if(File.Exists(target)&&Wave(File.ReadAllBytes(target)))return target;ReadySpeech(o);string key=Key();
      return await Task.Run(async delegate{byte[] audio;if(Realtime(o)){RealtimeAudio result=null;for(int attempt=0;attempt<2;attempt++){try{result=await realtime.Synthesize(o,role,text,instruction,language,key,cancel);break;}catch(InvalidDataException e){if(attempt!=0||!e.Message.StartsWith("实时模型改写了台词"))throw;}}audio=result.Wave;ChatStore.Atomic(target+".realtime.json",QwenCodec.Encode(new{model=o.Model,voiceId=Voice(o,role),transcript=result.Transcript,rateControl="natural-language direction; no numeric speed parameter",usage=result.Usage}));if(result.Usage!=null)RecordUsage(new{usage=result.Usage},role,o.Model);}
        else if(o.Provider=="local")audio=transport.Send(EndpointFor(o),"POST",key,"application/json",Encoding.UTF8.GetBytes(QwenCodec.Encode(RequestBody(o,role,text,instruction,language))),false,cancel,12000000);
        else{object result=transport.Json(EndpointFor(o),"POST",key,RequestBody(o,role,text,instruction,language),false,cancel);audio=transport.Send(AudioUrl(result),"GET","","",null,false,cancel,12000000);RecordUsage(result,role,o.Model);}
        cancel.ThrowIfCancellationRequested();try{if(o.Provider!="local")audio=WaveInfo.CompleteProviderWave(audio);WaveInfo.Read(audio,false);}catch(InvalidDataException){string diagnostic=Path.Combine(Folder,"voice-diagnostics");Directory.CreateDirectory(diagnostic);File.WriteAllBytes(Path.Combine(diagnostic,Path.GetFileName(target)+".provider.wav"),audio);throw;}string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temp,audio);try{if(File.Exists(target))File.Delete(temp);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}return target;
      },cancel);
    }
    void RecordUsage(object result,string role,string model){object usage=Data.At(result,"usage");if(usage==null)return;lock(usageLock){File.AppendAllText(Path.Combine(Folder,"qwen-usage.jsonl"),QwenCodec.Encode(new{time=DateTime.UtcNow.ToString("o"),role=role,model=model,usage=usage})+Environment.NewLine);}}
    // Cloud enrollment accepts at most ten ASCII alphanumerics; role identity remains canonical.
    public static string EnrollmentPrefix(string role){string canonical=Data.Character(role),value="oc"+canonical;if(Regex.IsMatch(value,"^[A-Za-z0-9]{1,10}$"))return value;return "oc"+Hash(Encoding.UTF8.GetBytes(canonical)).Substring(0,8);}
    public static object EnrollmentBody(VoiceOptions o,string role,string url){if(o.Model=="qwen-audio-3.1-realtime-plus")return new{model="voice-enrollment",input=new{action="create_voice",target_model=o.Model,prefix=EnrollmentPrefix(role),url=url,enable_volume_normalization="false"}};return new{model="voice-enrollment",input=new{action="create_voice",target_model=o.Model,prefix=EnrollmentPrefix(role),url=url,language_hints=new[]{"ja"},max_prompt_audio_length=20.0,enable_preprocess=false,enable_volume_normalization="false"}};}
    public static byte[] Multipart(object policy,string name,byte[] audio,string boundary,out string objectKey){objectKey=Convert.ToString(Data.At(policy,"upload_dir")).TrimEnd('/')+"/"+name;if(objectKey.StartsWith("/")||objectKey.Contains(".."))throw new InvalidDataException("上传路径无效");var fields=new Dictionary<string,string>{{"OSSAccessKeyId","oss_access_key_id"},{"Signature","signature"},{"policy","policy"},{"x-oss-object-acl","x_oss_object_acl"},{"x-oss-forbid-overwrite","x_oss_forbid_overwrite"}};
      using(var memory=new MemoryStream()){Action<string> write=delegate(string s){byte[] b=Encoding.UTF8.GetBytes(s);memory.Write(b,0,b.Length);};foreach(var field in fields){string value=Convert.ToString(Data.At(policy,field.Value));if(value.Length==0)throw new InvalidDataException("缺少文件上传凭证");write("--"+boundary+"\r\nContent-Disposition: form-data; name=\""+field.Key+"\"\r\n\r\n"+value+"\r\n");}write("--"+boundary+"\r\nContent-Disposition: form-data; name=\"key\"\r\n\r\n"+objectKey+"\r\n--"+boundary+"\r\nContent-Disposition: form-data; name=\"success_action_status\"\r\n\r\n200\r\n--"+boundary+"\r\nContent-Disposition: form-data; name=\"file\"; filename=\""+name+"\"\r\nContent-Type: audio/wav\r\n\r\n");memory.Write(audio,0,audio.Length);write("\r\n--"+boundary+"--\r\n");return memory.ToArray();}
    }
    string Upload(byte[] bytes,string role,string key,CancellationToken cancel){var policy=transport.Json(new Uri("https://dashscope.aliyuncs.com/api/v1/uploads?action=getPolicy&model=voice-enrollment"),"GET",key,null,false,cancel);object data=Data.At(policy,"data");Uri host=AliUrl(Convert.ToString(Data.At(data,"upload_host")));string boundary="OCVoice"+Guid.NewGuid().ToString("N"),objectKey;byte[] form=Multipart(data,"oc-"+role+"-"+Guid.NewGuid().ToString("N")+".wav",bytes,boundary,out objectKey);transport.Send(host,"POST","","multipart/form-data; boundary="+boundary,form,false,cancel,262144);return "oss://"+objectKey;}
    void SaveRole(VoiceOptions effective,string role){lock(settingsLock){var current=Options;current.Voices[role]=effective.Voices[role];current.Clones[role]=effective.Clones[role];Save(current,"");}}
    public async Task<CloneRecord> Clone(string role,CancellationToken cancel,Action<string> progress){
      var o=ForRole(Options,role);if(!Modern(o)||o.Provider!="beijing")throw new InvalidDataException("克隆入口使用北京百炼 Qwen Audio 3.1");Ready(o);string key=Key(),path=o.References[role];if(!File.Exists(path))throw new InvalidDataException("这位角色的参考音路径不存在");byte[] bytes=File.ReadAllBytes(path);var info=WaveInfo.Read(bytes,true);CloneRecord existing;
      if(o.Clones.TryGetValue(role,out existing)&&existing.Model==o.Model&&existing.Workspace==o.WorkspaceId&&existing.ReferenceHash==info.Hash&&existing.Status!="UNDEPLOYED"){
        if(existing.Status=="OK"){o.Voices[role]=existing.VoiceId;SaveRole(o,role);return existing;}return await Query(role,cancel);
      }
      return await Task.Run(delegate{progress("正在上传 "+role+" 的日语参考音…");string url=o.ReferenceUrls[role].Trim();bool temporary=url.Length==0;
        if(temporary)url=Upload(bytes,role,key,cancel);else{Uri hosted;if(!Uri.TryCreate(url,UriKind.Absolute,out hosted)||hosted.Scheme!="https"||hosted.UserInfo.Length>0)throw new InvalidDataException("托管参考音需填写无需鉴权的 HTTPS 地址");}
        progress("参考音已上传，正在创建 "+role+" 音色…");object result=transport.Json(CloudEndpoint(o,"customization"),"POST",key,EnrollmentBody(o,role,url),temporary,cancel);string id=Convert.ToString(Data.At(result,"output","voice_id"));if(id.Length==0)throw new InvalidDataException("Qwen 没有返回音色 ID；请查询百炼音色列表后再重试");
        var record=new CloneRecord{VoiceId=id,Model=o.Model,Workspace=o.WorkspaceId,ReferenceHash=info.Hash,Status="DEPLOYING",Created=DateTime.UtcNow.ToString("o")};o.Clones[role]=record;SaveRole(o,role);return record;
      },cancel);
    }
    public async Task<CloneRecord> Query(string role,CancellationToken cancel){var o=ForRole(Options,role);Ready(o);CloneRecord record;if(!o.Clones.TryGetValue(role,out record))throw new InvalidDataException("这位角色还没有待查询的音色");if(record.Model!=o.Model||record.Workspace!=o.WorkspaceId)throw new InvalidDataException("请切回创建此音色时的模型和业务空间");string key=Key();return await Task.Run(delegate{object result=transport.Json(CloudEndpoint(o,"customization"),"POST",key,new{model="voice-enrollment",input=new{action="query_voice",voice_id=record.VoiceId}},false,cancel);string model=Convert.ToString(Data.At(result,"output","target_model"));if(model!=o.Model)throw new InvalidDataException("云端音色对应的模型与本地设置不一致");string state=Convert.ToString(Data.At(result,"output","status"));if(state!="OK"&&state!="DEPLOYING"&&state!="UNDEPLOYED")throw new InvalidDataException("云端返回未知音色状态");record.Status=state;if(state=="OK")o.Voices[role]=record.VoiceId;SaveRole(o,role);return record;},cancel);}
    public async Task<bool> WaitClone(string role,CancellationToken cancel,Action<string> progress){for(int n=0;n<30;n++){cancel.ThrowIfCancellationRequested();var record=await Query(role,cancel);progress(role+" · "+record.Status);if(record.Status=="OK")return true;if(record.Status=="UNDEPLOYED")throw new InvalidDataException("参考音未通过复刻处理，请换一段清晰日常音源");await Task.Delay(2000,cancel);}return false;}
  }
}
