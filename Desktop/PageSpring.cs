using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using AirBridge.UI;

namespace AirBridge.Desktop {
 // A persistent spring keeps both position and velocity across interrupted navigation.
 // Render transforms leave text layout, scroll extents and hit testing in WPF's hands.
 internal sealed class PageSpring : IDisposable {
  readonly FrameworkElement target;
  readonly bool heading;
  readonly TranslateTransform shift=new TranslateTransform();
  readonly Stopwatch clock=new Stopwatch();
  double position,velocity,last;
  bool running;

  public PageSpring(FrameworkElement target,bool heading){
   this.target=target;this.heading=heading;
   // Keep text and card surfaces at their final size and opacity throughout.
   // Scaling and fading the whole page made settling look like a brightness flash.
   target.RenderTransform=shift;
  }
  public void Enter(){
   if(!Motion.Enabled){Dispose();return;}
   if(!running){position=1;velocity=0;last=0;clock.Restart();running=true;CompositionTarget.Rendering+=Frame;}
   else velocity=Math.Min(velocity+.65,3); // Small impulse, never reset an in-flight frame.
   Paint();
  }
  void Frame(object sender,EventArgs e){
   if(!target.IsVisible||!Motion.Enabled){Dispose();return;}
   double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.04,now-last);last=now;
   // Substeps keep damping consistent at low frame rates and after UI-thread stalls.
   while(dt>0){double step=Math.Min(dt,1.0/240);const double omega=22,zeta=.56;
    velocity+=(-omega*omega*position-2*zeta*omega*velocity)*step;
    position+=velocity*step;dt-=step;
   }
   Paint();
   if(Math.Abs(position)<.0008&&Math.Abs(velocity)<.018)Dispose();
  }
  void Paint(){
   double amount=heading?.35:1;
   shift.Y=16*position*amount;
  }
  public void Dispose(){
   if(running)CompositionTarget.Rendering-=Frame;
   running=false;clock.Stop();position=velocity=0;Paint();
  }
 }
}
