using System;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
namespace AirBridge.UI {
 // One shared mark travels between items; independent leading/trailing edges
 // stretch the body without moving or relaying out the navigation controls.
 public sealed class LiquidIndicator : FrameworkElement {
  public static readonly DependencyProperty TopEdgeProperty=DependencyProperty.Register("TopEdge",typeof(double),typeof(LiquidIndicator),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
  public static readonly DependencyProperty BottomEdgeProperty=DependencyProperty.Register("BottomEdge",typeof(double),typeof(LiquidIndicator),new FrameworkPropertyMetadata(20.0,FrameworkPropertyMetadataOptions.AffectsRender));
  public static readonly DependencyProperty FillProperty=DependencyProperty.Register("Fill",typeof(Brush),typeof(LiquidIndicator),new FrameworkPropertyMetadata(Brushes.Black,FrameworkPropertyMetadataOptions.AffectsRender));
  public Brush Fill {get{return (Brush)GetValue(FillProperty);}set{SetValue(FillProperty,value);}}
  FrameworkElement target;bool positioned,animating;
  double center,velocity,length=10,lengthVelocity,destination,lastTime;
  readonly Stopwatch clock=new Stopwatch();
  public LiquidIndicator(){IsHitTestVisible=false;Loaded+=delegate{Place(false);};Unloaded+=delegate{Stop();positioned=false;};}
  public void Select(FrameworkElement item,bool animate){if(item==target&&positioned&&animate)return;target=item;if(IsLoaded)Place(animate);}
  protected override void OnRenderSizeChanged(SizeChangedInfo info){base.OnRenderSizeChanged(info);if(IsLoaded&&(!positioned||info.HeightChanged))Place(positioned);}
  void Place(bool animate){
   if(target==null||!target.IsVisible||target.ActualHeight<=0)return;
   double y;try{y=target.TranslatePoint(new Point(0,target.ActualHeight/2),this).Y;}catch(InvalidOperationException){return;}
   destination=y;
   if(!animate||!positioned||!SystemParameters.ClientAreaAnimation){Stop();center=y;length=10;velocity=lengthVelocity=0;positioned=true;Publish();return;}
   // Retarget only the destination. Preserve both position and velocity when
   // interrupted, including reversal while the liquid tail is still retracting.
   if(!animating){animating=true;clock.Restart();lastTime=0;CompositionTarget.Rendering+=Frame;}
  }
  static void Spring(ref double value,ref double speed,double goal,double frequency,double dt){
   double offset=value-goal,c=speed+frequency*offset,decay=Math.Exp(-frequency*dt);
   value=goal+(offset+c*dt)*decay;speed=(speed-frequency*c*dt)*decay;
  }
  void Frame(object sender,EventArgs args){
   double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.05,now-lastTime);lastTime=now;
   if(!SystemParameters.ClientAreaAnimation){Place(false);return;}
   if(dt<=0)return;
   double distance=Math.Abs(destination-center);
   Spring(ref center,ref velocity,destination,13,dt);
   // The body swells with motion, then contracts more slowly than it travels.
   double desiredLength=10+Math.Min(34,distance*.16+Math.Abs(velocity)*.024);
   Spring(ref length,ref lengthVelocity,desiredLength,11,dt);
   Publish();
   if(Math.Abs(destination-center)<.035&&Math.Abs(velocity)<.4&&Math.Abs(length-10)<.035&&Math.Abs(lengthVelocity)<.4){
    center=destination;length=10;velocity=lengthVelocity=0;Publish();Stop();
   }
  }
  void Publish(){SetValue(TopEdgeProperty,center-length);SetValue(BottomEdgeProperty,center+length);}
  void Stop(){if(animating){CompositionTarget.Rendering-=Frame;animating=false;}clock.Stop();}
  protected override void OnRender(DrawingContext dc){
   if(!positioned)return;
   double top=(double)GetValue(TopEdgeProperty),bottom=(double)GetValue(BottomEdgeProperty);
   if(bottom<top){double swap=top;top=bottom;bottom=swap;}
   double height=Math.Max(3,bottom-top),middle=(top+bottom)/2;
   double stretch=Math.Max(0,Math.Min(1,(height-20)/50));
   double r=1.7+.55*stretch,neck=1.7-.85*stretch,x=4.7;
   var body=new StreamGeometry();using(var c=body.Open()){
    c.BeginFigure(new Point(x,top),true,true);
    c.BezierTo(new Point(x+r,top),new Point(x+r,top+1),new Point(x+r,top+r),true,false);
    c.BezierTo(new Point(x+r,top+height*.27),new Point(x+neck,middle-height*.12),new Point(x+neck,middle),true,false);
    c.BezierTo(new Point(x+neck,middle+height*.12),new Point(x+r,bottom-height*.27),new Point(x+r,bottom-r),true,false);
    c.BezierTo(new Point(x+r,bottom+1),new Point(x-r,bottom+1),new Point(x-r,bottom-r),true,false);
    c.BezierTo(new Point(x-r,bottom-height*.27),new Point(x-neck,middle+height*.12),new Point(x-neck,middle),true,false);
    c.BezierTo(new Point(x-neck,middle-height*.12),new Point(x-r,top+height*.27),new Point(x-r,top+r),true,false);
    c.BezierTo(new Point(x-r,top+1),new Point(x-r,top),new Point(x,top),true,false);
   }body.Freeze();dc.DrawGeometry(Fill,null,body);
  }
 }
}
