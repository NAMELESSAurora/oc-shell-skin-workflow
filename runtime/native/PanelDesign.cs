using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace OCShell {
  public sealed class RoleVisual {
    public string Role,Ink,Muted,Accent,Paper,Soft,Line,Motif,Room;
    public CornerRadius Corners,StripCorners;
    public static RoleVisual For(string role){return RoleCatalog.Visual(role);}
    public static Brush Paint(string color){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(color);b.Freeze();return b;}
    public Brush Glow {get{var b=new LinearGradientBrush(((SolidColorBrush)Paint(Paper)).Color,((SolidColorBrush)Paint(Soft)).Color,new Point(0,0),new Point(1,1));b.Freeze();return b;}}
  }
  public sealed class PanelPlacement {
    readonly string path;
    Dictionary<string,object> values;
    readonly HashSet<string> dirty=new HashSet<string>();
    public PanelPlacement(string folder){Directory.CreateDirectory(folder);path=Path.Combine(folder,"panel-layout.json");Load();}
    void Load(){try{values=Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}catch{values=new Dictionary<string,object>();}}
    public static Rect Safe(double width,double height){double top=Math.Min(64,Math.Max(12,height*.14));return new Rect(16,top,Math.Max(0,width-32),Math.Max(0,height-top-16));}
    public static Point Clamp(Point p,double width,double height,double panelWidth,double panelHeight){Rect a=Safe(width,height);return new Point(Math.Max(a.Left,Math.Min(a.Right-panelWidth,p.X)),Math.Max(a.Top,Math.Min(a.Bottom-panelHeight,p.Y)));}
    public Point Position(string role,string panel,double width,double height,double pw,double ph,Point fallback){
      Rect a=Safe(width,height);object entry;if(!values.TryGetValue(role+":"+panel,out entry))return Clamp(fallback,width,height,pw,ph);
      double x=Convert.ToDouble(Data.At(entry,"x")??0),y=Convert.ToDouble(Data.At(entry,"y")??0);return Clamp(new Point(a.Left+x*Math.Max(0,a.Width-pw),a.Top+y*Math.Max(0,a.Height-ph)),width,height,pw,ph);
    }
    public void Move(string role,string panel,Point p,double width,double height,double pw,double ph){Rect a=Safe(width,height);p=Clamp(p,width,height,pw,ph);string id=role+":"+panel;values[id]=new Dictionary<string,object>{{"x",Math.Max(0,Math.Min(1,(p.X-a.Left)/Math.Max(1,a.Width-pw)))},{"y",Math.Max(0,Math.Min(1,(p.Y-a.Top)/Math.Max(1,a.Height-ph)))}};dirty.Add(id);}
    public void Reset(string role,string panel){string id=role+":"+panel;values.Remove(id);dirty.Add(id);Save();}
    public void Save(){try{var pending=new Dictionary<string,object>(values);Load();foreach(string id in dirty){object value;if(pending.TryGetValue(id,out value))values[id]=value;else values.Remove(id);}ChatStore.Atomic(path,Data.Json.Serialize(values));dirty.Clear();}catch{}}
    public bool Custom(string role,string panel){return values.ContainsKey(role+":"+panel);}
  }
  public sealed class PanelDrag {
    readonly FrameworkElement grip;readonly Window window;readonly PanelPlacement placement;readonly string panel;readonly Func<string> role;readonly Action refresh;
    Point pointer,start;bool held;Native.Point origin;double width,height,dpi=1,pw,ph;
    public bool Active {get{return held;}}
    public Func<bool> Allow;
    public Action<Point> Press,Pull;
    public Action Release;
    public PanelDrag(Window window,FrameworkElement grip,PanelPlacement placement,string panel,Func<string> role,Action refresh){
      this.window=window;this.grip=grip;this.placement=placement;this.panel=panel;this.role=role;this.refresh=refresh;grip.Cursor=Cursors.SizeAll;grip.ToolTip="拖动摆放 · 双击归位";
      grip.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(IsButton(e.OriginalSource as DependencyObject)||(Allow!=null&&!Allow()))return;if(e.ClickCount==2){placement.Reset(role(),panel);if(refresh!=null)refresh();e.Handled=true;return;}Native.Rect r;if(!Native.GetWindowRect(new WindowInteropHelper(window).Handle,out r))return;Begin(grip.PointToScreen(e.GetPosition(grip)),new Point((r.Left-origin.X)/dpi,(r.Top-origin.Y)/dpi),e.GetPosition(grip));grip.CaptureMouse();e.Handled=true;};
      grip.PreviewMouseMove+=delegate(object s,MouseEventArgs e){if(!held)return;Move(grip.PointToScreen(e.GetPosition(grip)),e.GetPosition(grip));e.Handled=true;};
      grip.PreviewMouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e){if(!held)return;End();grip.ReleaseMouseCapture();e.Handled=true;};grip.LostMouseCapture+=delegate{End();};
    }
    public void Begin(Point screen,Point position,Point local){pointer=screen;start=position;held=true;if(Press!=null)Press(local);}
    public void Move(Point screen,Point local){if(!held)return;placement.Move(role(),panel,start+(screen-pointer)/dpi,width,height,pw,ph);if(Pull!=null)Pull(local);if(refresh!=null)refresh();}
    public void End(){if(!held)return;held=false;placement.Save();if(Release!=null)Release();}
    public void Bounds(Native.Point origin,double width,double height,double dpi,double pw,double ph){this.origin=origin;this.width=width;this.height=height;this.dpi=dpi;this.pw=pw;this.ph=ph;}
    static bool IsButton(DependencyObject n){while(n!=null){if(n is Button)return true;n=n is Visual?VisualTreeHelper.GetParent(n):LogicalTreeHelper.GetParent(n);}return false;}
  }
}
