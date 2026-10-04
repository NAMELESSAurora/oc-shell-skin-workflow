using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OCShell {
  public static class SoftSounds {
    public static byte[] Make(string role,string action){
      const int rate=24000;double duration=action=="release"?.22:action=="impact"?.15:.11;int n=(int)(rate*duration);double pitch=RoleCatalog.SoundPitch(role);double phase=0;var random=new Random(11);var samples=new short[n];
      for(int i=0;i<n;i++){double t=i/(double)rate,u=t/duration;double freq=action=="release"?pitch*(.55+.9*u):pitch*(1.2-.7*u);phase+=2*Math.PI*freq/rate;double envelope=Math.Pow(Math.Sin(Math.PI*u),1.8)*Math.Exp(-3*u);double noise=(random.NextDouble()*2-1)*.06;double v=(Math.Sin(phase)*.72+Math.Sin(phase*.51)*.16+noise)*envelope*.24;samples[i]=(short)(v*32767);}
      using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+n*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(n*2);foreach(short sample in samples)writer.Write(sample);return stream.ToArray();}
    }
  }
  public sealed class CompanionAudio {
    readonly string root;readonly Dispatcher dispatcher;readonly IWavePlayback sound,voicePlayer;readonly SemaphoreSlim worker=new SemaphoreSlim(1,1);readonly Queue<string> playback=new Queue<string>();
    readonly DispatcherTimer watchdog=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
    readonly DispatcherTimer gapTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(330)};bool gapActive;
    CancellationTokenSource cancel=new CancellationTokenSource();DateTime lastSound=DateTime.MinValue,lastReaction=DateTime.MinValue,playStarted,pendingAt;double expectedSeconds;bool closed,playing;TouchLine pendingTouch;string pendingRole;
    public readonly QwenVoice Voice;
    public readonly InteractionVoices Interactions;
    public bool PanelOpen;
    public string LastStatus="",LastEvent="idle",LastFile="";public int OpenedCount,EndedCount;
    public bool Playing {get{return playing;}}public bool BetweenUtterances {get{return gapActive;}}
    public Action<string> Status;
    public CompanionAudio(string root,string folder,IWavePlayback effects=null,IWavePlayback speech=null,QwenTransport transport=null){this.root=root;Voice=new QwenVoice(folder,root,transport);Interactions=new InteractionVoices(root);dispatcher=Dispatcher.CurrentDispatcher;sound=effects??new WavePlayback();voicePlayer=speech??new WavePlayback();voicePlayer.Opened+=delegate{OpenedCount++;Trace("opened",LastFile);};voicePlayer.Ended+=delegate{EndedCount++;playing=false;Trace("ended",LastFile);gapActive=true;gapTimer.Start();};voicePlayer.Failed+=delegate(string error){playing=false;playback.Clear();pendingTouch=null;Trace("failed",LastFile);Report(error);};sound.Failed+=delegate(string error){Report(error);};gapTimer.Tick+=delegate{gapTimer.Stop();gapActive=false;PlayNext();PlayPending();};watchdog.Tick+=delegate{CheckPlaybackTimeout(DateTime.UtcNow);};watchdog.Start();}
    void Trace(string kind,string detail){LastEvent=kind;try{string folder=Path.Combine(root,"runtime");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"audio-events-"+System.Diagnostics.Process.GetCurrentProcess().Id+".jsonl"),QwenCodec.Encode(new{time=DateTime.UtcNow.ToString("o"),kind=kind,detail=detail})+Environment.NewLine);}catch{}}
    void Report(string text){LastStatus=text;if(Status!=null)Status(text);}
    public void Sound(string role,string action){Voice.ReloadIfChanged();if(closed||!Voice.Options.Sfx||(DateTime.UtcNow-lastSound).TotalMilliseconds<120)return;lastSound=DateTime.UtcNow;try{string path=Path.Combine(Voice.Folder,"sfx",role+"-"+action+".wav");if(!File.Exists(path)){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,SoftSounds.Make(role,action));}sound.Volume=Voice.Options.Volume;sound.Play(path);}catch{Report("互动音效暂时无法播放");}}
    public void Reaction(string role,string text){if(!Voice.Options.Reactions||(DateTime.UtcNow-lastReaction).TotalSeconds<3)return;lastReaction=DateTime.UtcNow;Speak(role,text);}
    public void Reply(string role,string text){Voice.ReloadIfChanged();if(!Voice.Options.Chat)return;string language=System.Text.RegularExpressions.Regex.IsMatch(text,"[\u3040-\u30ff]")?"ja":"zh";foreach(string part in Chunks(text))Speak(role,part,"",language);}
    public static List<string> Chunks(string text){var result=new List<string>();var line=new StringBuilder();foreach(char c in text){line.Append(c);if(line.Length>=180||((c=='。'||c=='！'||c=='？'||c=='\n')&&line.Length>=25)){result.Add(line.ToString());line.Clear();}}if(line.Length>0)result.Add(line.ToString());return result;}
    async void Speak(string role,string text,string instruction="",string language="zh"){if(closed)return;if(!Voice.CanSynthesize(role)){Trace("offline-text",role);Report("这句暂未收录为本地语音；文字可以继续显示。联网合成需另行配置此便携版的 Key 与音色。");return;}var token=cancel.Token;bool acquired=false;try{await worker.WaitAsync(token);acquired=true;string file=await Voice.Bake(role,text,token,instruction,language);if(closed||token.IsCancellationRequested)return;playback.Enqueue(file);PlayNext();}catch(OperationCanceledException){}catch(Exception e){if(!closed)Report(e is InvalidOperationException||e is InvalidDataException?e.Message:"语音暂时没有接上");}finally{if(acquired)worker.Release();PlayPending();}}
    void PlayNext(){if(playing||gapActive||closed||playback.Count==0)return;string file=playback.Dequeue();try{expectedSeconds=WaveInfo.Read(File.ReadAllBytes(file),false).Seconds;playing=true;playStarted=DateTime.UtcNow;LastFile=Path.GetFileName(file);LastStatus="";Trace("play",LastFile);voicePlayer.Volume=Voice.Options.Volume;voicePlayer.Play(file);}catch{playing=false;playback.Clear();Report("本地语音文件暂时无法读取");}}
    public void CheckPlaybackTimeout(DateTime now){if(playing&&(now-playStarted).TotalSeconds>expectedSeconds+3){voicePlayer.Stop();playing=false;playback.Clear();pendingTouch=null;Trace("timeout",LastFile);Report("音频输出中断，已重置播放；可以再次点击");}}
    public void StopVoice(){cancel.Cancel();cancel.Dispose();cancel=new CancellationTokenSource();gapTimer.Stop();gapActive=false;voicePlayer.Stop();playback.Clear();pendingTouch=null;playing=false;lastReaction=DateTime.MinValue;Trace("stop","");}
    public void Stop(){StopVoice();sound.Stop();}
    public void Close(){closed=true;watchdog.Stop();Stop();sound.Close();voicePlayer.Close();}
    public void PlayFile(string path){StopVoice();playback.Enqueue(Path.GetFullPath(path));PlayNext();}
    public void Touch(string role,TouchLine line){Voice.ReloadIfChanged();Interactions.ReloadIfChanged();if(line==null||closed)return;if(!Voice.Options.Reactions){Trace("touch-disabled",line.Id);return;}if((DateTime.UtcNow-lastReaction).TotalMilliseconds<500){Trace("touch-debounce",line.Id);return;}lastReaction=DateTime.UtcNow;if(playing||gapActive||worker.CurrentCount==0){pendingRole=role;pendingTouch=line;pendingAt=DateTime.UtcNow;Trace("touch-pending",line.Id);return;}PlayTouch(role,line);}
    void PlayTouch(string role,TouchLine line){Trace("touch",line.Id);string cached=Interactions.Cached(Voice,role,line);if(cached!=null){Trace("cache-hit",line.Id);playback.Enqueue(cached);PlayNext();}else{Trace("cache-miss",line.Id);Speak(role,line.Japanese,Interactions.Direction(line),"ja");}}
    void PlayPending(){if(closed||playing||gapActive||worker.CurrentCount==0||pendingTouch==null)return;var line=pendingTouch;string role=pendingRole;pendingTouch=null;if((DateTime.UtcNow-pendingAt).TotalSeconds<=15)PlayTouch(role,line);}
    public void Settings(Window owner,string role,FontFamily font){
      if(PanelOpen)return;PanelOpen=true;try{new VoiceSettings(this,root,role,font,owner).Window.ShowDialog();}finally{PanelOpen=false;}
    }
  }
}
