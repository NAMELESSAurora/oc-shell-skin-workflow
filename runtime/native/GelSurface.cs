using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OCShell {
  // A small native spring mesh deforms only the gel texture, keeping controls legible.
  // The optical material is authored offline in Three.js; no browser runs in the overlay.
  public sealed class GelSurface : Border {
    const int Side=11,Count=Side*Side;
    readonly Viewport3D viewport=new Viewport3D {IsHitTestVisible=false,ClipToBounds=false};
    readonly MeshGeometry3D mesh=new MeshGeometry3D();
    readonly GeometryModel3D model=new GeometryModel3D();
    readonly Vector[] offset=new Vector[Count],velocity=new Vector[Count],nextVelocity=new Vector[Count];
    ImageSource material;
    double width,height;
    Point origin,current;
    bool held,running;
    TimeSpan lastFrame;
    public bool Running {get{return running;}}
    public double MaxDisplacement {get{double max=0;foreach(var p in offset)max=Math.Max(max,p.Length);return max;}}
    public GelSurface(){IsHitTestVisible=false;Child=viewport;model.Geometry=mesh;viewport.Children.Add(new ModelVisual3D{Content=model});}
    public void SetMaterial(ImageSource source,double w,double h){
      if(material==source&&width==w&&height==h)return;
      Stop();material=source;width=w;height=h;Width=w;Height=h;
      var brush=new ImageBrush(source){Stretch=Stretch.Fill};brush.Freeze();var paint=new EmissiveMaterial(brush);paint.Freeze();model.Material=model.BackMaterial=paint;
      var uv=new PointCollection();var normals=new Vector3DCollection();var indices=new Int32Collection();
      for(int y=0;y<Side;y++)for(int x=0;x<Side;x++){uv.Add(new Point(x/(double)(Side-1),y/(double)(Side-1)));normals.Add(new Vector3D(0,0,1));if(x<Side-1&&y<Side-1){int i=y*Side+x;indices.Add(i);indices.Add(i+Side);indices.Add(i+1);indices.Add(i+1);indices.Add(i+Side);indices.Add(i+Side+1);}}
      uv.Freeze();normals.Freeze();indices.Freeze();mesh.TextureCoordinates=uv;mesh.Normals=normals;mesh.TriangleIndices=indices;
      viewport.Camera=new OrthographicCamera(new Point3D(w/2,h/2,500),new Vector3D(0,0,-1),new Vector3D(0,1,0),w){NearPlaneDistance=.1,FarPlaneDistance=1000};UpdateMesh();
    }
    Point Rest(int i){return new Point((i%Side)*width/(Side-1),(i/Side)*height/(Side-1));}
    double Weight(Point p){return Math.Exp(-((p-origin).LengthSquared)/(2*100*100));}
    Vector Target(int i){Point p=Rest(i);Vector radial=p-origin;Vector pull=current-origin;if(pull.Length>32)pull*=32/pull.Length;double weight=Weight(p);return (pull*.72-radial*.085+new Vector(0,2.5))*weight;}
    public void Press(Point p){if(material==null||!SystemParameters.ClientAreaAnimation)return;origin=current=p;held=true;for(int i=0;i<Count;i++)offset[i]=Target(i)*.65;UpdateMesh();Wake();}
    public void Pull(Point p){if(!held)return;current=p;Wake();}
    public void Release(){if(!held)return;held=false;Wake();}
    public void Nudge(Point p){if(material==null||!SystemParameters.ClientAreaAnimation)return;origin=p;for(int i=0;i<Count;i++){Vector radial=Rest(i)-p;if(radial.Length>1)radial.Normalize();velocity[i]+=(radial*30+new Vector(0,-34))*Weight(Rest(i));}Wake();}
    void Wake(){if(running)return;running=true;lastFrame=TimeSpan.Zero;CompositionTarget.Rendering+=Render;}
    void Render(object sender,EventArgs args){TimeSpan time=((RenderingEventArgs)args).RenderingTime;double dt=lastFrame==TimeSpan.Zero?1.0/60:(time-lastFrame).TotalSeconds;lastFrame=time;Tick(dt);}
    public void Tick(double dt){
      if(!running)return;dt=Math.Max(.001,Math.Min(.032,dt));int steps=(int)Math.Ceiling(dt/.009);double step=dt/steps;
      for(int sub=0;sub<steps;sub++){
        for(int i=0;i<Count;i++){
          int x=i%Side,y=i/Side,n=0;Vector neighbors=new Vector();if(x>0){neighbors+=offset[i-1];n++;}if(x<Side-1){neighbors+=offset[i+1];n++;}if(y>0){neighbors+=offset[i-Side];n++;}if(y<Side-1){neighbors+=offset[i+Side];n++;}
          Vector target=held?Target(i):new Vector();Vector acceleration=(target-offset[i])*235-velocity[i]*16+(neighbors/n-offset[i])*35;nextVelocity[i]=velocity[i]+acceleration*step;
        }
        for(int i=0;i<Count;i++){velocity[i]=nextVelocity[i];offset[i]+=velocity[i]*step;}
      }
      UpdateMesh();if(!held){double energy=0;for(int i=0;i<Count;i++)energy=Math.Max(energy,offset[i].Length+velocity[i].Length*.04);if(energy<.012)Stop();}
    }
    void UpdateMesh(){if(width<=0)return;var positions=new Point3DCollection(Count);for(int i=0;i<Count;i++){Point p=Rest(i);positions.Add(new Point3D(p.X+offset[i].X,height-p.Y-offset[i].Y,0));}mesh.Positions=positions;}
    public void Stop(){if(running)CompositionTarget.Rendering-=Render;running=held=false;Array.Clear(offset,0,Count);Array.Clear(velocity,0,Count);UpdateMesh();}
  }
}
