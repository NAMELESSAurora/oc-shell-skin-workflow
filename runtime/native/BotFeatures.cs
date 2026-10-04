using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace OCShell {
  // A floating soft toy has inertia and wall collisions, but no forced fall to the bottom.
  public sealed class ToyMotion {
    public Point Position;
    public Vector Velocity;
    public double MaxX,MaxY;
    public bool Initialized,Holding;
    public int Collisions;
    public Action<double,double,double> Impact;
    Point target,previous;DateTime sample;
    public void Bounds(double width,double height,double size) {
      MaxX=Math.Max(0,width-size);MaxY=Math.Max(0,height-size);
      if(!Initialized){Reset();Initialized=true;}
      Position=Clamp(Position);target=Clamp(target);
    }
    Point Clamp(Point p){return new Point(Math.Max(0,Math.Min(MaxX,p.X)),Math.Max(0,Math.Min(MaxY,p.Y)));}
    public void Reset(){Position=new Point(Math.Max(0,MaxX-68),49);Position=Clamp(Position);target=Position;Velocity=new Vector();Holding=false;}
    public void Grab(){Holding=true;Velocity=new Vector();target=previous=Position;sample=DateTime.UtcNow;}
    public void Drag(Point p) {
      Point next=Clamp(p);double dt=Math.Max(.008,Math.Min(.05,(DateTime.UtcNow-sample).TotalSeconds));
      Vector speed=(next-previous)/dt;Velocity=Velocity*.45+speed*.55;if(Velocity.Length>1100){Velocity.Normalize();Velocity*=1100;}
      if((p-next).Length>2 && Impact!=null)Impact(p.X<0||p.X>MaxX?1:0,p.Y<0||p.Y>MaxY?1:0,Math.Min(500,(p-next).Length*9));
      target=Position=next;previous=next;sample=DateTime.UtcNow;
    }
    public void Release(){Holding=false;if((DateTime.UtcNow-sample).TotalMilliseconds>90)Velocity=new Vector();}
    public bool Tick(double dt) {
      if(Holding)return true;
      dt=Math.Max(.001,Math.Min(.032,dt));Position+=Velocity*dt;
      bool x=Position.X<0||Position.X>MaxX,y=Position.Y<0||Position.Y>MaxY;
      if(x||y){double speed=Velocity.Length;Position=Clamp(Position);if(x)Velocity.X*=-.56;if(y)Velocity.Y*=-.56;Collisions++;if(Impact!=null)Impact(x?1:0,y?1:0,speed);}
      Velocity*=Math.Exp(-3.7*dt);if(Velocity.Length<3){Velocity=new Vector();return false;}return true;
    }
    public void Suspend(){Holding=false;Velocity=new Vector();}
  }

  public sealed class DialogueBook {
    readonly object data;
    readonly Dictionary<string,int> counters=new Dictionary<string,int>();
    readonly Dictionary<string,DateTime> touched=new Dictionary<string,DateTime>();
    readonly Dictionary<string,int> streaks=new Dictionary<string,int>();
    public DialogueBook(string root) {try{data=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(root,"companion-dialogue.json")));}catch{data=null;}}
    public string Text(string role,string field,string fallback) {return Convert.ToString(Data.At(data,"characters",role,field)??fallback);}
    public string Say(string role,string context) {
      if(context=="touch"){DateTime previous;int streak;bool repeated=touched.TryGetValue(role,out previous)&&(DateTime.UtcNow-previous).TotalSeconds<3;streaks.TryGetValue(role,out streak);streaks[role]=repeated?streak+1:1;touched[role]=DateTime.UtcNow;if(streaks[role]>=3&&Count(role,"repeatTouch")>0)context="repeatTouch";}
      var entries=Data.At(data,"characters",role,context) as object[];
      if(entries==null || entries.Length==0)entries=Data.At(data,"characters",role,"greeting") as object[];
      if(entries==null || entries.Length==0)return "我在这里。";
      string id=role+":"+context;int index;if(!counters.TryGetValue(id,out index))index=0;counters[id]=(index+1)%entries.Length;
      return Convert.ToString(entries[index]);
    }
    public int Count(string role,string context) {var entries=Data.At(data,"characters",role,context) as object[];return entries==null?0:entries.Length;}
    public List<string> BakeLines(string role){var result=new List<string>();foreach(string context in new[]{"greeting","touch","squeeze","repeatTouch"}){var entries=Data.At(data,"characters",role,context) as object[];if(entries!=null)for(int i=0;i<Math.Min(2,entries.Length);i++)result.Add(Convert.ToString(entries[i]));}return result;}
  }

  // Coordinates describe the visible artwork, not its transparent spring padding.
  public sealed class BotDock {
    public double X=1,Y=1;
    public int Horizontal=1,Vertical=1;
    public Point Position(double width,double height,double artworkWidth,double artworkHeight) {
      double mx=Math.Max(0,width-artworkWidth),my=Math.Max(0,height-artworkHeight);
      return new Point(Horizontal<0?0:Horizontal>0?mx:X*mx,Vertical<0?0:Vertical>0?my:Y*my);
    }
    public void Move(double x,double y,double width,double height,double artworkWidth,double artworkHeight) {
      double mx=Math.Max(0,width-artworkWidth),my=Math.Max(0,height-artworkHeight);
      X=mx==0?0:Math.Max(0,Math.Min(1,x/mx));Y=my==0?0:Math.Max(0,Math.Min(1,y/my));Horizontal=Vertical=0;
    }
    public void Snap(double width,double height,double artworkWidth,double artworkHeight) {
      double mx=Math.Max(0,width-artworkWidth),my=Math.Max(0,height-artworkHeight);double x=X*mx,y=Y*my;
      Horizontal=x<=32?-1:mx-x<=32?1:0;Vertical=y<=32?-1:my-y<=32?1:0;
    }
    public void Reset() {X=Y=1;Horizontal=Vertical=1;}
    public string Description {get{return (Horizontal<0?"左":Horizontal>0?"右":"")+(Vertical<0?"上":Vertical>0?"下":"")+(Horizontal==0 && Vertical==0?"自由摆放":"吸附");}}
  }

  public sealed class BalanceSnapshot {
    public decimal? Amount;
    public string Currency="USD",Provider="GPTEAM",Label="当前密钥可用额度",Status="尚未刷新";
    public bool Unlimited,Stale;
    public DateTime Updated;
    public string Display {get{return Unlimited?"不限额":Amount.HasValue?(Amount.Value>0 && Amount.Value<.01m?"<0.01":Amount.Value.ToString("0.00",CultureInfo.InvariantCulture))+" "+Currency:"—";}}
  }

  public sealed class BalanceService {
    readonly Dispatcher dispatcher;
    bool stopped;
    int generation;
    string connectionIdentity="";
    DateTime lastAttempt;
    public bool Busy {get;private set;}
    public BalanceSnapshot Current=new BalanceSnapshot();
    public Action Changed;
    public BalanceService(Dispatcher dispatcher) {this.dispatcher=dispatcher;}
    static object First(object data,params string[] names) {foreach(string name in names){object item=Data.At(data,name);if(item!=null)return item;}return null;}
    public static BalanceSnapshot Parse(object data) {
      if(Data.At(data,"isValid") is bool && !(bool)Data.At(data,"isValid"))throw new InvalidDataException("当前密钥不可用");
      object value=First(data,"effective_available_quota_usd","effective_available_quota","available_quota_usd","remaining_usd","available_quota","remaining","balance");
      if(value==null)value=Data.At(data,"quota","remaining");decimal amount;
      bool unlimited=object.Equals(Data.At(data,"unlimited"),true);
      if(value!=null && !(value is bool) && decimal.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out amount)) {
        if(amount<0)unlimited=true;
        return new BalanceSnapshot {Amount=unlimited?(decimal?)null:amount,Unlimited=unlimited,Currency=Data.Clean(First(data,"unit")??Data.At(data,"quota","unit")??"USD",8),Status="已更新",Updated=DateTime.UtcNow};
      }
      if(unlimited)return new BalanceSnapshot {Unlimited=true,Status="已更新",Updated=DateTime.UtcNow};
      throw new InvalidDataException("接口未返回有效额度");
    }
    static BalanceSnapshot Fetch(ClaudeConnection connection) {
      if(connection==null)throw new InvalidDataException("无法读取 Claude 密钥配置");
      var json=new JavaScriptSerializer();string baseUrl=connection.BaseUrl,key=connection.Key;
      Uri endpoint;
      if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out endpoint) || endpoint.Scheme!="https" || endpoint.Host!="api.gpteamservices.com" || endpoint.Port!=443 || !string.IsNullOrEmpty(endpoint.UserInfo))
        throw new InvalidDataException("当前服务商暂未接入余额查询");
      if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("未找到当前 API 密钥");
      // This is GPTEAM's documented CC Switch GET /usage endpoint. No model call is made.
      var request=(HttpWebRequest)WebRequest.Create(new Uri(endpoint,"/usage"));request.Method="GET";request.AllowAutoRedirect=false;request.Timeout=8000;request.ReadWriteTimeout=8000;
      request.Accept="application/json";request.UserAgent="OCShell-Companion/0.6";request.Headers[HttpRequestHeader.Authorization]="Bearer "+key;
      ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
      using(var response=(HttpWebResponse)request.GetResponse()) {
        if(response.StatusCode!=HttpStatusCode.OK || (response.ContentType??"").IndexOf("json",StringComparison.OrdinalIgnoreCase)<0)throw new InvalidDataException("余额接口返回了非 JSON 响应");
        using(var stream=response.GetResponseStream())using(var memory=new MemoryStream()) {
          var buffer=new byte[4096];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){if(memory.Length+count>65536)throw new InvalidDataException("余额响应过大");memory.Write(buffer,0,count);}
          return Parse(json.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray())));
        }
      }
    }
    public bool Refresh() {
      if(Busy || stopped || (DateTime.UtcNow-lastAttempt).TotalSeconds<3)return false;
      ClaudeConnection connection=null;try{connection=ClaudeKeyStore.Resolve();}catch{}
      string identity=connection==null?"":ClaudeKeyStore.Hash(connection.BaseUrl+"\n"+connection.AuthMode+"\n"+connection.Key);
      if(connectionIdentity!=identity){connectionIdentity=identity;Current=new BalanceSnapshot{Provider=connection==null?"API":connection.Provider};}
      int epoch=generation;lastAttempt=DateTime.UtcNow;Busy=true;if(Changed!=null)Changed();
      Task.Factory.StartNew(delegate {
        BalanceSnapshot result=null;string error="";
        try {result=Fetch(connection);}
        catch(InvalidDataException e){error=e.Message;}
        catch(WebException e){var response=e.Response as HttpWebResponse;error=response==null?"网络暂时无法连接": "余额查询失败 · HTTP "+(int)response.StatusCode;if(response!=null)response.Dispose();}
        catch{error="无法读取余额配置";}
        if(stopped || dispatcher.HasShutdownStarted)return;
        dispatcher.BeginInvoke(new Action(delegate {
          if(stopped||epoch!=generation)return;Busy=false;
          if(result!=null)Current=result;
          else {Current.Status=error;Current.Stale=Current.Amount.HasValue || Current.Unlimited;}
          if(Changed!=null)Changed();
        }));
      });
      return true;
    }
    public void Stop() {stopped=true;}
    public void ConfigurationChanged(){generation++;Busy=false;connectionIdentity="";lastAttempt=DateTime.MinValue;Current=new BalanceSnapshot{Status="密钥已切换，等待刷新"};if(Changed!=null)Changed();Refresh();}
  }
}
