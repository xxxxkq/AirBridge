using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace AirBridge.UI {
 // Track is not a Panel: Panel.ZIndex cannot change its visual child order.
 public sealed class LayeredTrack : Track {
  protected override Visual GetVisualChild(int index){
   int count=VisualChildrenCount;if(index<0||index>=count)return base.GetVisualChild(index);
   if(Thumb==null)return base.GetVisualChild(index);
   int n=0;for(int i=0;i<count;i++){Visual v=base.GetVisualChild(i);if(v==Thumb)continue;if(n++==index)return v;}
   return Thumb;
  }
 }
 public sealed class ConnectionPulse : Control {
  public static readonly DependencyProperty PhaseProperty=DependencyProperty.Register("Phase",typeof(double),typeof(ConnectionPulse),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
  public ConnectionPulse(){Width=18;Height=18;IsHitTestVisible=false;Loaded+=delegate{Start();};IsVisibleChanged+=delegate{Start();};Unloaded+=delegate{BeginAnimation(PhaseProperty,null);};}
  void Start(){BeginAnimation(PhaseProperty,null);if(IsLoaded&&IsVisible&&Motion.Enabled){var a=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(2100));a.RepeatBehavior=RepeatBehavior.Forever;BeginAnimation(PhaseProperty,a);}}
  protected override void OnRender(DrawingContext dc){var ink=Foreground??Brushes.SeaGreen;dc.DrawEllipse(ink,null,new Point(9,9),3,3);if(!Motion.Enabled)return;double p=(double)GetValue(PhaseProperty);dc.PushOpacity((1-p)*0.5);dc.DrawEllipse(null,new Pen(ink,1),new Point(9,9),3+5*p,3+5*p);dc.Pop();}
 }
 public static class Motion {
  public static bool Enabled {get{return SystemParameters.ClientAreaAnimation;}}
  static readonly DependencyProperty Feedback=DependencyProperty.RegisterAttached("Feedback",typeof(object),typeof(Motion));
  static readonly DependencyProperty Hooked=DependencyProperty.RegisterAttached("Hooked",typeof(bool),typeof(Motion),new PropertyMetadata(false));
  public static DoubleAnimation Tween(double from,double to,int ms){return new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(Enabled?ms:1)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};}
  public static void Enter(FrameworkElement e,int ms){if(e==null||!Enabled||!e.IsVisible)return;var t=new TranslateTransform();e.RenderTransform=t;e.Opacity=1;e.BeginAnimation(UIElement.OpacityProperty,Tween(0.6,1,ms));t.BeginAnimation(TranslateTransform.YProperty,Tween(6,0,ms));}
  public static void Spin(Button button,bool running){var t=button.RenderTransform as RotateTransform;if(t==null){t=new RotateTransform();button.RenderTransform=t;button.RenderTransformOrigin=new Point(.5,.5);}t.BeginAnimation(RotateTransform.AngleProperty,null);t.Angle=0;button.IsEnabled=!running;if(running&&Enabled){t.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(950)){RepeatBehavior=RepeatBehavior.Forever});}}
  public static void ClosePopup(Window w){if(w==null||!w.IsVisible||Object.Equals(w.Tag,"closing"))return;w.Tag="closing";if(!Enabled){w.Close();return;}double from=w.Opacity;w.BeginAnimation(UIElement.OpacityProperty,null);w.Opacity=0;var a=Tween(from,0,160);a.Completed+=delegate{w.Close();};w.BeginAnimation(UIElement.OpacityProperty,a);}
  public static void Attach(DependencyObject root){if(root==null)return;var element=root as FrameworkElement;if(element!=null&&!(bool)element.GetValue(Hooked)){element.SetValue(Hooked,true);var slider=element as Slider;if(slider!=null)slider.SetValue(Feedback,new SliderFeedback(slider));var check=element as CheckBox;if(check!=null){check.Checked+=delegate{MoveSwitch(check);};check.Unchecked+=delegate{MoveSwitch(check);};check.Loaded+=delegate{MoveSwitch(check);};}var exp=element as Expander;if(exp!=null)exp.Expanded+=delegate{exp.Dispatcher.BeginInvoke(new Action(delegate{Enter(exp.Content as FrameworkElement,180);}),DispatcherPriority.Loaded);};var combo=element as ComboBox;if(combo!=null){combo.DropDownOpened+=delegate{combo.ApplyTemplate();var popup=combo.Template.FindName("PART_Popup",combo) as Popup;if(popup!=null)Enter(popup.Child as FrameworkElement,180);};}}
   foreach(object child in LogicalTreeHelper.GetChildren(root)){var d=child as DependencyObject;if(d!=null)Attach(d);}
  }
  static void MoveSwitch(CheckBox c){c.ApplyTemplate();var dot=c.Template.FindName("Dot",c) as FrameworkElement;if(dot==null)return;var t=dot.RenderTransform as TranslateTransform;if(t==null){t=new TranslateTransform();dot.RenderTransform=t;}double from=t.X,to=c.IsChecked==true?16:0;t.BeginAnimation(TranslateTransform.XProperty,null);t.X=to;if(Enabled)t.BeginAnimation(TranslateTransform.XProperty,Tween(from,to,180));}
  sealed class SliderFeedback {
   readonly Slider slider;Thumb thumb;ScaleTransform scale;Popup popup;Border bubble;TextBlock value;DispatcherTimer close;
   public SliderFeedback(Slider s){slider=s;s.Loaded+=delegate{Setup();};s.Unloaded+=delegate{if(close!=null)close.Stop();if(popup!=null)popup.IsOpen=false;};if(s.IsLoaded)Setup();s.ValueChanged+=delegate{if(value!=null)value.Text=Math.Round(s.Value).ToString();if(popup!=null&&popup.IsOpen){popup.HorizontalOffset=.01;popup.HorizontalOffset=0;}};}
   void Setup(){slider.ApplyTemplate();var track=slider.Template.FindName("PART_Track",slider) as Track;if(track==null||track.Thumb==thumb)return;thumb=track.Thumb;thumb.ApplyTemplate();var core=thumb.Template.FindName("ThumbCore",thumb) as FrameworkElement;if(core==null)return;scale=new ScaleTransform(1,1);core.RenderTransform=scale;core.RenderTransformOrigin=new Point(.5,.5);
    value=new TextBlock{FontSize=12,FontWeight=FontWeights.SemiBold};value.SetResourceReference(TextBlock.ForegroundProperty,"Brush_202634");bubble=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(10,5,10,5),BorderThickness=new Thickness(1),Child=value};bubble.SetResourceReference(Border.BackgroundProperty,"Brush_FFFFFF");bubble.SetResourceReference(Border.BorderBrushProperty,"Brush_D7DFEB");
    popup=new Popup{AllowsTransparency=true,StaysOpen=true,PlacementTarget=thumb,Placement=PlacementMode.Top,VerticalOffset=-10,Child=bubble};popup.Resources=slider.Resources; // Resolve theme across the popup boundary below.
    close=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};close.Tick+=delegate{close.Stop();popup.IsOpen=false;};
    thumb.MouseEnter+=delegate{Scale(thumb.IsDragging?1.3:1.12);};thumb.MouseLeave+=delegate{if(!thumb.IsDragging)Scale(1);};
    thumb.DragStarted+=delegate{close.Stop();bubble.BeginAnimation(UIElement.OpacityProperty,null);bubble.Opacity=1;value.Text=Math.Round(slider.Value).ToString();CopyBrush("Brush_FFFFFF");CopyBrush("Brush_D7DFEB");CopyBrush("Brush_202634");popup.IsOpen=true;bubble.BeginAnimation(UIElement.OpacityProperty,Tween(0,1,120));Scale(1.3);};
    thumb.DragCompleted+=delegate{Scale(thumb.IsMouseOver?1.12:1);bubble.Opacity=0;bubble.BeginAnimation(UIElement.OpacityProperty,Tween(1,0,150));close.Start();};
   }
   void CopyBrush(string key){var brush=slider.TryFindResource(key);if(brush!=null)bubble.Resources[key]=brush;}
   void Scale(double target){double x=scale.ScaleX;scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=target;scale.BeginAnimation(ScaleTransform.ScaleXProperty,Tween(x,target,140));scale.BeginAnimation(ScaleTransform.ScaleYProperty,Tween(x,target,140));}
  }
 }
}
