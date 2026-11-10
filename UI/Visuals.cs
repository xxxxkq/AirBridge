using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AirBridge.UI {
 public sealed class SourceOption : System.ComponentModel.INotifyPropertyChanged {
  public string Id {get;set;} public string Name {get;set;}
  public BatteryGauge Battery {get;private set;}
  public string CardTemplateId {get;set;}
  string artwork="phone";public string Artwork {get{return artwork;}set{if(artwork==value)return;artwork=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("Artwork"));}}
  public FrameworkElement Actions {get;set;} public FrameworkElement ConnectionActions {get;set;}
  bool expanded;public bool Expanded {get{return expanded;}set{if(expanded==value)return;expanded=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("Expanded"));}}
  string identityHint="";public string IdentityHint {get{return identityHint;}set{if(identityHint==value)return;identityHint=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("IdentityHint"));}}
  string description="";public string Description {get{return description;}set{if(description==value)return;description=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("Description"));}}
  bool connected;public bool IsConnected {get{return connected;}set{if(connected==value)return;connected=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("IsConnected"));}}
  string status="";public string Status {get{return status;}set{if(status==value)return;status=value;if(PropertyChanged!=null)PropertyChanged(this,new System.ComponentModel.PropertyChangedEventArgs("Status"));}}
  public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
  public SourceOption(){Battery=new BatteryGauge{Width=48,Height=22};Battery.SetResourceReference(Control.ForegroundProperty,"Brush_344054");}
  public override string ToString(){return Name??"";}
 }
 public sealed class BatteryGauge : Control {
  int level=-1;bool charging,estimate;string label="";
  public int Level {get{return level;}set{level=value>=0&&value<=100?value:-1;InvalidateVisual();}}
  public bool Charging {get{return charging;}set{charging=value;InvalidateVisual();}}
  public bool Estimate {get{return estimate;}set{estimate=value;InvalidateVisual();}}
  public string Label {get{return label;}set{label=value??"";InvalidateVisual();}}
  public BatteryGauge(){Width=78;Height=22;Foreground=Brushes.SlateGray;IsHitTestVisible=true;}
  void Text(DrawingContext dc,string text,double x,double y,Brush brush){dc.DrawText(new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));}
  protected override void OnRender(DrawingContext dc){base.OnRender(dc);Brush ink=Foreground??Brushes.SlateGray;var caption=new FormattedText(label,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(caption,new Point(0,12-caption.Height/2));double x=label.Length==0?0:Math.Ceiling(caption.Width)+6;
   dc.DrawRoundedRectangle(null,new Pen(ink,1.2),new Rect(x,5,34,14),3,3);
   dc.DrawRoundedRectangle(ink,null,new Rect(x+35,9,2,6),1,1);
   if(level>0){var fill=new SolidColorBrush(level<=10?Color.FromArgb(95,225,76,76):Color.FromArgb(150,25,150,100));dc.DrawRoundedRectangle(fill,null,new Rect(x+2,7,30*level/100.0,10),2,2);}
   var value=new FormattedText(level<0?"—":level+"%",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),10,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
   dc.DrawText(value,new Point(x+(34-value.Width)/2,5+(14-value.Height)/2));
   if(charging){var bolt=Geometry.Parse("M 7,1 L 1,11 L 5,11 L 3,20 L 11,7 L 7,7 Z");dc.PushTransform(new TranslateTransform(x+40,1));dc.DrawGeometry(ink,null,bolt);dc.Pop();}
  }
 }
 public sealed class DeviceArt : Control {
  public static readonly DependencyProperty KindProperty=DependencyProperty.Register("Kind",typeof(string),typeof(DeviceArt),new FrameworkPropertyMetadata("buds",FrameworkPropertyMetadataOptions.AffectsRender));
  public string Kind {get{return (string)GetValue(KindProperty);}set{SetValue(KindProperty,value);}}
  static DeviceArt(){WidthProperty.OverrideMetadata(typeof(DeviceArt),new FrameworkPropertyMetadata(52.0));HeightProperty.OverrideMetadata(typeof(DeviceArt),new FrameworkPropertyMetadata(52.0));}
  public DeviceArt(){IsHitTestVisible=false;Foreground=new SolidColorBrush(Color.FromRgb(102,133,185));}
  protected override void OnRender(DrawingContext output){
   base.OnRender(output);var group=new DrawingGroup();var dc=group.Open();
   var outline=new Pen(new SolidColorBrush(Color.FromRgb(151,164,185)),0.8);
   var shell=new LinearGradientBrush(Color.FromRgb(255,255,255),Color.FromRgb(210,219,232),25);
   var dark=new SolidColorBrush(Color.FromRgb(49,59,74));
   if(Kind=="microphone"){
    dc.DrawRoundedRectangle(shell,outline,new Rect(24,7,16,29),8,8);dc.DrawGeometry(null,new Pen(dark,2.5),Geometry.Parse("M 18,27 L 18,31 C 18,49 46,49 46,31 L 46,27 M 32,44 L 32,55 M 23,55 L 41,55"));
   }else if(Kind=="display"){
    dc.DrawRoundedRectangle(shell,outline,new Rect(5,10,54,34),4,4);dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(156,190,222)),null,new Rect(9,14,46,26),2,2);dc.DrawGeometry(null,new Pen(dark,3),Geometry.Parse("M 32,44 L 32,53 M 21,54 L 43,54"));
   }else if(Kind=="digital"||Kind=="linein"){
    dc.DrawRoundedRectangle(shell,outline,new Rect(9,16,46,32),5,5);dc.DrawEllipse(dark,null,new Point(32,32),10,10);dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(106,167,191)),null,new Point(32,32),5,5);
   }else if(Kind=="virtual"||Kind=="audio"){
    dc.DrawRoundedRectangle(shell,outline,new Rect(7,11,50,42),7,7);dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(117,141,193)),3),Geometry.Parse("M 15,32 L 21,32 L 26,22 L 32,43 L 38,24 L 43,32 L 49,32"));
   }else if(Kind=="phone"||Kind=="iphone"||Kind=="android"){
    dc.DrawRoundedRectangle(shell,outline,new Rect(18,5,28,54),6,6);
    var screen=new LinearGradientBrush(Color.FromRgb(222,231,243),Color.FromRgb(163,182,209),new Point(0,0),new Point(1,1));
    dc.DrawRoundedRectangle(screen,null,new Rect(21,9,22,46),3.5,3.5);
    if(Kind=="android")dc.DrawEllipse(dark,null,new Point(32,12),1.3,1.3);
    else dc.DrawRoundedRectangle(dark,null,new Rect(27,10,10,2.5),1.2,1.2);
    dc.DrawRoundedRectangle(Brushes.White,null,new Rect(28,51,8,1.5),.75,.75);
   }else if(Kind=="speaker"){
    dc.DrawRoundedRectangle(shell,outline,new Rect(17,8,30,48),10,10);
    for(int y=20;y<49;y+=4)for(int x=23;x<43;x+=4)dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(100,100,119,145)),null,new Point(x,y),0.7,0.7);
    dc.DrawRoundedRectangle(dark,null,new Rect(27,12,10,2),1,1);
   }else if(Kind=="headset"){
    dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(115,139,173)),5),Geometry.Parse("M 12,36 L 12,27 C 12,4 52,4 52,27 L 52,36"));
    dc.DrawGeometry(null,new Pen(shell,2),Geometry.Parse("M 12,30 L 12,27 C 12,4 52,4 52,27 L 52,30"));
    dc.DrawRoundedRectangle(shell,outline,new Rect(8,28,13,26),6,6);dc.DrawRoundedRectangle(shell,outline,new Rect(43,28,13,26),6,6);
    dc.DrawRoundedRectangle(dark,null,new Rect(18,32,3,18),1.5,1.5);dc.DrawRoundedRectangle(dark,null,new Rect(43,32,3,18),1.5,1.5);
   }else{
    DrawBud(dc,shell,outline,dark,14,8,-16);
    DrawBud(dc,shell,outline,dark,43,13,16);
   }
   dc.Close();var bounds=group.Bounds;double scale=Math.Min(ActualWidth,ActualHeight)/64;
   output.PushTransform(new TranslateTransform((ActualWidth-bounds.Width*scale)/2-bounds.X*scale,(ActualHeight-bounds.Height*scale)/2-bounds.Y*scale));
   output.PushTransform(new ScaleTransform(scale,scale));output.DrawDrawing(group);output.Pop();output.Pop();
  }
  static void DrawBud(DrawingContext dc,Brush shell,Pen edge,Brush dark,double x,double y,double angle){
   dc.PushTransform(new TranslateTransform(x,y));dc.PushTransform(new RotateTransform(angle,2,20));
   dc.DrawRoundedRectangle(shell,edge,new Rect(0,15,7,31),3.5,3.5);
   dc.DrawGeometry(shell,edge,Geometry.Parse("M 2,0 C -6,-1 -12,5 -11,13 C -10,20 -4,23 3,21 C 10,19 12,13 10,7 C 9,3 6,1 2,0 Z"));
   dc.DrawEllipse(dark,null,new Point(-6,10),2.6,4.7);
   dc.DrawRoundedRectangle(dark,null,new Rect(3,3,4,1.8),0.9,0.9);
   dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(151,164,185)),null,new Rect(1.4,42,4.2,1.5),0.7,0.7);
   dc.DrawGeometry(null,new Pen(Brushes.White,0.9),Geometry.Parse("M 5,24 L 5,39"));
   dc.Pop();dc.Pop();
  }
 }
}
