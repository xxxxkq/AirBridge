using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AirBridge.UI;
namespace AirBridge.Desktop {
 // Icons live on a fixed rail. Only the column and label reveal are animated.
 internal sealed class SidebarMotion : IDisposable {
  readonly ColumnDefinition column;readonly Action<double> reveal;
  readonly Stopwatch clock=new Stopwatch();
  double width,velocity,target,last;bool running;
  public SidebarMotion(ColumnDefinition column,Action<double> reveal){this.column=column;this.reveal=reveal;width=column.Width.Value;}
  public void Set(bool collapsed,bool animate){
   target=collapsed?56:176;
   if(!animate||!Motion.Enabled){Dispose();width=target;velocity=0;Paint();return;}
   // Retain velocity and position when an in-flight animation is reversed.
   if(!running){width=column.Width.Value;clock.Restart();last=0;running=true;CompositionTarget.Rendering+=Frame;}
  }
  void Frame(object sender,EventArgs e){
   double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.1,now-last);last=now;
   if(!Motion.Enabled){Set(target==56,false);return;}
   const double frequency=18;
   double offset=width-target,c=velocity+frequency*offset,decay=Math.Exp(-frequency*dt);
   width=target+(offset+c*dt)*decay;velocity=(velocity-frequency*c*dt)*decay;
   if(Math.Abs(width-target)<.04&&Math.Abs(velocity)<.5){width=target;velocity=0;Dispose();}
   Paint();
  }
  void Paint(){column.Width=new GridLength(Math.Max(56,Math.Min(176,width)));double progress=Math.Max(0,Math.Min(1,(width-64)/100));reveal(progress*progress*(3-2*progress));}
  public void Dispose(){if(running)CompositionTarget.Rendering-=Frame;running=false;clock.Stop();}
 }
}
