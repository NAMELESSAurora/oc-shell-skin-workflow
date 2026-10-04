using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace OCShell {
  public interface IWavePlayback {
    event EventHandler Opened;
    event EventHandler Ended;
    event Action<string> Failed;
    double Volume {get;set;}
    void Play(string file);
    void Stop();
    void Close();
  }
  // PCM playback has no codec startup or MediaPlayer completion-event dependency.
  // Voice and effects use separate waveOut handles, so a click cannot cut off a voice.
  public sealed class WavePlayback : IWavePlayback {
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort Tag,Channels;public uint Rate,ByteRate;public ushort Align,Bits,Extra;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr Data;public uint Length,Recorded;public UIntPtr User;public uint Flags,Loops;public IntPtr Next;public UIntPtr Reserved;}
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr handle);
    public event EventHandler Opened,Ended;public event Action<string> Failed;
    public double Volume {get;set;}
    readonly DispatcherTimer timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(40)};
    IntPtr handle,header,data;bool prepared,closed;readonly uint headerSize=(uint)Marshal.SizeOf(typeof(Header));
    public WavePlayback(){Volume=.38;timer.Tick+=delegate{if(header==IntPtr.Zero)return;var value=(Header)Marshal.PtrToStructure(header,typeof(Header));if((value.Flags&1)!=0){Stop();var callback=Ended;if(callback!=null)callback(this,EventArgs.Empty);}};}
    public void Play(string file){
      if(closed)return;Stop();try{
        byte[] bytes=File.ReadAllBytes(file);var info=WaveInfo.Read(bytes,false);int start=0,length=0;
        for(int p=12;p+8<=bytes.Length;){int n=BitConverter.ToInt32(bytes,p+4);if(Encoding.ASCII.GetString(bytes,p,4)=="data"){start=p+8;length=n;break;}p+=8+n+n%2;}
        if(length==0)throw new InvalidDataException("PCM 数据为空");byte[] pcm=new byte[length];Buffer.BlockCopy(bytes,start,pcm,0,length);double gain=Math.Max(0,Math.Min(1,Volume));
        for(int p=0;p+1<pcm.Length;p+=2){short value=(short)(BitConverter.ToInt16(pcm,p)*gain);pcm[p]=(byte)(value&255);pcm[p+1]=(byte)((value>>8)&255);}
        var format=new Format{Tag=1,Channels=(ushort)info.Channels,Rate=(uint)info.Rate,ByteRate=(uint)(info.Rate*info.Channels*2),Align=(ushort)(info.Channels*2),Bits=16};
        Check(waveOutOpen(out handle,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0));data=Marshal.AllocHGlobal(length);Marshal.Copy(pcm,0,data,length);header=Marshal.AllocHGlobal((int)headerSize);Marshal.StructureToPtr(new Header{Data=data,Length=(uint)length},header,false);
        Check(waveOutPrepareHeader(handle,header,headerSize));prepared=true;Check(waveOutWrite(handle,header,headerSize));timer.Start();var callback=Opened;if(callback!=null)callback(this,EventArgs.Empty);
      }catch(Exception e){Stop();var callback=Failed;if(callback!=null)callback(e is InvalidOperationException||e is InvalidDataException?e.Message:"无法打开本地 WAV");}
    }
    static void Check(uint result){if(result!=0)throw new InvalidOperationException("Windows 音频输出未就绪（waveOut "+result+"），请检查当前输出设备");}
    public void Stop(){timer.Stop();if(handle!=IntPtr.Zero){waveOutReset(handle);if(prepared&&header!=IntPtr.Zero)waveOutUnprepareHeader(handle,header,headerSize);waveOutClose(handle);}prepared=false;handle=IntPtr.Zero;if(header!=IntPtr.Zero)Marshal.FreeHGlobal(header);if(data!=IntPtr.Zero)Marshal.FreeHGlobal(data);header=data=IntPtr.Zero;}
    public void Close(){closed=true;Stop();}
  }
}
