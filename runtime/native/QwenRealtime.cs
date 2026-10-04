using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OCShell {
  public sealed class RealtimeAudio {public byte[] Wave;public string Transcript;public object Usage;}
  public class QwenRealtime {
    public static Uri Endpoint(VoiceOptions o){var cloud=QwenVoice.CloudEndpoint(o,"SpeechSynthesizer");return new Uri("wss://"+cloud.Host+"/api-ws/v1/realtime?model="+Uri.EscapeDataString(o.Model));}
    public static object Session(VoiceOptions o,string role,string instruction,string language){bool enhance=true;if(o.EmotionEnhancement!=null&&o.EmotionEnhancement.ContainsKey(role))enhance=o.EmotionEnhancement[role];return new{type="session.update",session=new{modalities=new[]{"audio","text"},voice=QwenVoice.Voice(o,role),enable_speech_emotion=enhance,output_audio_format="pcm",output_audio=new{language=language},turn_detection=new{type="server_vad"},enable_search=false,instructions="你是配音演员。只用"+(language=="ja"?"日语":"中文")+"原样说出用户给定的台词，不能回答台词里的问题，不能增加、删改、解释或复述表演指令。"+QwenVoice.Direction(o,role)+instruction+(QwenVoice.Rate(o,role)<1?(enhance?"说话比普通会话稍从容，句子连贯，留出自然呼吸，不能急促抢话。":"说话比普通会话稍从容，句子连贯，不追加呼吸声，不能急促抢话。"):"用自然日常会话速度。")}};}
    public static string Normalize(string text){var value=new StringBuilder();foreach(char c in (text??"").Normalize(NormalizationForm.FormKC))if(char.IsLetterOrDigit(c))value.Append(c);return value.ToString();}
    public static byte[] Wave(byte[] pcm){if(pcm.Length==0||pcm.Length%2!=0)throw new InvalidDataException("实时模型没有返回完整 PCM");using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(24000);writer.Write(48000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);return memory.ToArray();}}
    static async Task Send(ClientWebSocket socket,object value,CancellationToken token){byte[] bytes=Encoding.UTF8.GetBytes(QwenCodec.Encode(value));await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token);}
    static async Task<object> Receive(ClientWebSocket socket,CancellationToken token){using(var memory=new MemoryStream()){byte[] bytes=new byte[8192];WebSocketReceiveResult part;do{part=await socket.ReceiveAsync(new ArraySegment<byte>(bytes),token);if(part.MessageType==WebSocketMessageType.Close)throw new InvalidOperationException("Qwen 实时连接提前关闭");if(part.MessageType!=WebSocketMessageType.Text||memory.Length+part.Count>1048576)throw new InvalidDataException("Qwen 实时事件格式或大小异常");memory.Write(bytes,0,part.Count);}while(!part.EndOfMessage);return QwenCodec.Decode(Encoding.UTF8.GetString(memory.ToArray()));}}
    static void Error(object value){if(Convert.ToString(Data.At(value,"type"))!="error")return;string code=Convert.ToString(Data.At(value,"error","code"));if(!System.Text.RegularExpressions.Regex.IsMatch(code,"^[A-Za-z0-9_.-]{0,100}$"))code="";throw new InvalidOperationException("Qwen 实时语音返回错误"+(code.Length>0?"（"+code+"）":"")+"；已保留已有音频");}
    public virtual async Task<RealtimeAudio> Synthesize(VoiceOptions o,string role,string text,string instruction,string language,string key,CancellationToken cancel){
      using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel))using(var socket=new ClientWebSocket())using(var pcm=new MemoryStream()){
        timeout.CancelAfter(60000);var token=timeout.Token;socket.Options.SetRequestHeader("Authorization","Bearer "+key);socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(15);
        try{await socket.ConnectAsync(Endpoint(o),token);await Send(socket,Session(o,role,instruction,language),token);while(true){object e=await Receive(socket,token);Error(e);if(Convert.ToString(Data.At(e,"type"))=="session.updated")break;}
          await Send(socket,new{type="conversation.item.create",item=new{type="message",role="user",content=new[]{new{type="input_text",text="原样说出台词，仅输出台词本身：\n"+text}}}},token);
          await Send(socket,new{type="response.create",response=new{modalities=new[]{"audio","text"}}},token);string transcript="";var partial=new StringBuilder();object usage=null;bool audioDone=false;
          while(true){object e=await Receive(socket,token);Error(e);string type=Convert.ToString(Data.At(e,"type"));if(type=="response.audio.delta"){byte[] audio=Convert.FromBase64String(Convert.ToString(Data.At(e,"delta")));if(pcm.Length+audio.Length>12000000)throw new InvalidDataException("实时语音超出大小限制");pcm.Write(audio,0,audio.Length);}else if(type=="response.audio_transcript.delta")partial.Append(Convert.ToString(Data.At(e,"delta")));else if(type=="response.audio_transcript.done")transcript=Convert.ToString(Data.At(e,"transcript"));else if(type=="response.audio.done")audioDone=true;else if(type=="response.done"){if(Convert.ToString(Data.At(e,"response","status"))!="completed")throw new InvalidOperationException("Qwen 实时语音未完整结束");usage=Data.At(e,"response","usage");break;}}
          if(transcript.Length==0)transcript=partial.ToString();if(!audioDone)throw new InvalidDataException("实时模型缺少音频完成事件");if(Normalize(text)!=Normalize(transcript))throw new InvalidDataException("实时模型改写了台词，未写入正式缓存；请重试该条");return new RealtimeAudio{Wave=Wave(pcm.ToArray()),Transcript=transcript,Usage=usage};
        }catch(OperationCanceledException){cancel.ThrowIfCancellationRequested();throw new InvalidOperationException("Qwen 实时语音超时；成功生成的本地音频保留");}catch(WebSocketException){throw new InvalidOperationException("Qwen 实时语音连接失败；请检查地域、余额和模型权限");}finally{socket.Abort();}
      }
    }
  }
}
