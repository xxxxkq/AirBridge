using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
namespace AirBridge.UI {
 public sealed class AudioNode {public string Id,Name,Detail,Artwork;public bool Input,Available,Loopback,Module;}
 public sealed class AudioLink {public string Source,Target,SourceName,TargetName;public bool Loopback;public bool Enabled=true;public double Gain=100;}
 public sealed class NodePosition {public string Id;public double X,Y;}
 public sealed class BoardContextEventArgs : EventArgs {public string NodeId;public AudioLink Link;public Point Position;}
 public sealed class AudioBoard : Canvas {
  public event EventHandler<BoardContextEventArgs> ContextRequested;
  void Context(string id,AudioLink link,MouseButtonEventArgs e){if(ContextRequested!=null)ContextRequested(this,new BoardContextEventArgs{NodeId=id,Link=link,Position=World(e.GetPosition(this))});e.Handled=true;}
  public void ForgetPosition(string id){positions.Remove(id);}
  public Point ViewCenter(){return World(new Point(ActualWidth/2,ActualHeight/2));}
  public event EventHandler ViewChanged;
  void ViewDone(){if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
  public Rect Viewport {get{return new Rect(World(new Point(0,0)),new Size(Math.Max(1,ActualWidth)/Zoom,Math.Max(1,ActualHeight)/Zoom));}}
  public void CenterOn(Point point){pan.X=ActualWidth/2-point.X*Zoom;pan.Y=ActualHeight/2-point.Y*Zoom;InvalidateVisual();ViewDone();}
  public void ResetZoom(){ZoomBy(1/Zoom);}
  public event EventHandler LinksChanged,LayoutChanged,DeleteNodeRequested,EditRequested;
  public string SelectedNodeId;public AudioLink SelectedLink {get{return selected;}}
  public void SelectNode(string id){SelectedNodeId=id;selected=null;Rebuild();}
  public List<AudioNode> Nodes=new List<AudioNode>();public List<AudioLink> Links=new List<AudioLink>();
  readonly Canvas world=new Canvas();readonly Dictionary<string,Point> ports=new Dictionary<string,Point>(),positions=new Dictionary<string,Point>();
  readonly ScaleTransform scale=new ScaleTransform(1,1);readonly TranslateTransform pan=new TranslateTransform(24,72);
  AudioNode pending,moving;AudioLink selected;Path preview;Point down,origin;bool dragged,panning,drawing,viewInitialized;
  readonly Dictionary<string,Path> flowPaths=new Dictionary<string,Path>();HashSet<string> activeEdges=new HashSet<string>();readonly HashSet<string> movingEdges=new HashSet<string>();
  public void SetActivity(IEnumerable<string> edges){activeEdges=new HashSet<string>(edges);UpdateFlow();}
  void UpdateFlow(){foreach(var pair in flowPaths){bool active=IsVisible&&Motion.Enabled&&activeEdges.Contains(pair.Key);if(active&&!movingEdges.Contains(pair.Key)){movingEdges.Add(pair.Key);pair.Value.BeginAnimation(Shape.StrokeDashOffsetProperty,new DoubleAnimation(0,-19,TimeSpan.FromSeconds(1.8)){RepeatBehavior=RepeatBehavior.Forever});pair.Value.Opacity=.32;pair.Value.BeginAnimation(UIElement.OpacityProperty,Motion.Tween(0,.32,220));}else if(!active&&movingEdges.Remove(pair.Key)){double offset=pair.Value.StrokeDashOffset;pair.Value.BeginAnimation(Shape.StrokeDashOffsetProperty,null);pair.Value.StrokeDashOffset=offset;double from=pair.Value.Opacity;pair.Value.Opacity=0;pair.Value.BeginAnimation(UIElement.OpacityProperty,Motion.Tween(from,0,220));}}}
  const double Card=244,NodeHeight=80;
  public double Zoom {get{return scale.ScaleX;}}
  public AudioBoard(){IsVisibleChanged+=(s,e)=>UpdateFlow();Unloaded+=(s,e)=>SetActivity(new string[0]);MinHeight=280;ClipToBounds=true;Focusable=true;Background=Brushes.Transparent;Children.Add(world);var group=new TransformGroup();group.Children.Add(scale);group.Children.Add(pan);world.RenderTransform=group;
   MouseRightButtonUp+=(s,e)=>{if(!e.Handled)Context(null,null,e);};
   SizeChanged+=(s,e)=>{InvalidateVisual();ViewDone();if(!viewInitialized&&IsVisible&&ActualWidth>0&&Nodes.Count>0)Fit();};MouseMove+=MovePointer;MouseLeftButtonUp+=EndPointer;MouseUp+=(s,e)=>{if(panning){panning=false;ReleaseMouseCapture();Cursor=Cursors.Arrow;}};
   MouseDown+=(s,e)=>{if(e.ChangedButton==MouseButton.Middle){Focus();panning=true;down=e.GetPosition(this);origin=new Point(pan.X,pan.Y);CaptureMouse();Cursor=Cursors.SizeAll;e.Handled=true;}else if(e.ChangedButton==MouseButton.Left&&e.OriginalSource==this){pending=null;selected=null;SelectedNodeId=null;Focus();Rebuild();}};
   MouseWheel+=(s,e)=>{ZoomAt(e.Delta>0?1.12:1/1.12,e.GetPosition(this));e.Handled=true;};
   KeyDown+=(s,e)=>{if(e.Key==Key.Delete){if(selected!=null)Remove(selected);else if(SelectedNodeId!=null&&DeleteNodeRequested!=null)DeleteNodeRequested(this,EventArgs.Empty);e.Handled=true;}else if(e.Key==Key.F2||e.Key==Key.Enter){if(EditRequested!=null)EditRequested(this,EventArgs.Empty);e.Handled=true;}else if(e.Key==Key.Escape){Cancel();e.Handled=true;}};
   LostMouseCapture+=(s,e)=>{if(preview!=null){world.Children.Remove(preview);preview=null;}moving=null;panning=false;Cursor=Cursors.Arrow;};
  }
  protected override void OnRender(DrawingContext dc){base.OnRender(dc);var brush=TryFindResource("Brush_D7DFEB") as Brush??Brushes.LightGray;dc.PushOpacity(.6);double step=16*Zoom;if(step<10)step*=2;for(double x=pan.X%step;x<ActualWidth;x+=step)for(double y=pan.Y%step;y<ActualHeight;y+=step)dc.DrawEllipse(brush,null,new Point(x,y),.8,.8);dc.Pop();}
  static Brush Color(string value){return (Brush)new BrushConverter().ConvertFromString(value);}
  TextBlock Text(string text,double size,string resource){var t=new TextBlock{Text=text,FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};t.SetResourceReference(TextBlock.ForegroundProperty,resource);return t;}
  static Path Curve(Point a,Point b,Brush brush,double thickness){double d=Math.Max(60,Math.Abs(b.X-a.X)*.48);var figure=new PathFigure{StartPoint=a};figure.Segments.Add(new BezierSegment(new Point(a.X+d,a.Y),new Point(b.X-d,b.Y),b,true));var g=new PathGeometry();g.Figures.Add(figure);return new Path{Data=g,Stroke=brush,StrokeThickness=thickness,IsHitTestVisible=false};}
  Point World(Point p){return new Point((p.X-pan.X)/Zoom,(p.Y-pan.Y)/Zoom);}
  public Point PositionOf(string id){return positions[id];}
  public NodePosition[] ExportPositions(){return positions.Select(p=>new NodePosition{Id=p.Key,X=p.Value.X,Y=p.Value.Y}).ToArray();}
  public void RestorePosition(string id,double x,double y){if(String.IsNullOrEmpty(id)||Double.IsNaN(x)||Double.IsNaN(y)||Double.IsInfinity(x)||Double.IsInfinity(y))return;positions[id]=new Point(x,y);}
  public void MoveNode(string id,double x,double y){RestorePosition(id,x,y);Rebuild();LayoutDone();}
  void LayoutDone(){if(LayoutChanged!=null)LayoutChanged(this,EventArgs.Empty);}
  public void ArrangeNodes(){positions.Clear();foreach(var p in BoardLayout.Arrange(Nodes,Links))positions[p.Id]=new Point(p.X,p.Y);Rebuild();Fit();LayoutDone();}
  public void ZoomBy(double factor){ZoomAt(factor,new Point(ActualWidth/2,ActualHeight/2));}
  void ZoomAt(double factor,Point anchor){var p=World(anchor);double z=Math.Max(.35,Math.Min(1.8,Zoom*factor));scale.ScaleX=scale.ScaleY=z;pan.X=anchor.X-p.X*z;pan.Y=anchor.Y-p.Y*z;InvalidateVisual();ViewDone();}
  public void Fit(){if(Nodes.Count==0)return;viewInitialized=true;Rebuild();var visible=Nodes.Select(n=>positions[n.Id]).ToArray();double x=visible.Min(p=>p.X),y=visible.Min(p=>p.Y),w=visible.Max(p=>p.X)+Card-x,h=visible.Max(p=>p.Y)+NodeHeight-y;double z=Math.Max(.35,Math.Min(1.2,Math.Min((Math.Max(300,ActualWidth)-48)/w,(Math.Max(280,ActualHeight)-96)/h)));scale.ScaleX=scale.ScaleY=z;pan.X=(ActualWidth-w*z)/2-x*z;pan.Y=72-y*z;InvalidateVisual();ViewDone();}
  public void Refresh(){if(!IsMouseCaptured){Rebuild();if(!viewInitialized&&IsVisible&&ActualWidth>0&&Nodes.Count>0)Fit();}}
  public void Cancel(){pending=null;selected=null;moving=null;ReleaseMouseCapture();Rebuild();}
  public void ConnectNodes(string source,string target){var a=Nodes.FirstOrDefault(n=>n.Id==source&&(n.Input||n.Module)&&n.Available);var b=Nodes.FirstOrDefault(n=>n.Id==target&&(!n.Input||n.Module)&&n.Available);if(a==null||b==null)throw new ArgumentException("Select an available input and output");pending=a;Connect(b);}
  public void Remove(AudioLink link){Links.Remove(link);if(selected==link)selected=null;Changed();}
  void Changed(){Rebuild();if(LinksChanged!=null)LinksChanged(this,EventArgs.Empty);}
  public void Rebuild(){if(drawing)return;drawing=true;try{
   foreach(var path in flowPaths.Values)path.BeginAnimation(Shape.StrokeDashOffsetProperty,null);flowPaths.Clear();movingEdges.Clear();world.Children.Clear();ports.Clear();int left=0,right=0,middle=0;
   foreach(var n in Nodes){int index=n.Module?middle++:n.Input?left++:right++;if(!positions.ContainsKey(n.Id))positions[n.Id]=new Point(n.Module?330:n.Input?0:(Nodes.Any(x=>x.Module)?660:350),index*110);var p=positions[n.Id];ports[n.Id]=new Point(p.X+Card,p.Y+NodeHeight/2);ports["in:"+n.Id]=new Point(p.X,p.Y+NodeHeight/2);}
   foreach(var link in Links){if(!ports.ContainsKey(link.Source)||!ports.ContainsKey(link.Target))continue;
    var wire=Curve(ports[link.Source],ports["in:"+link.Target],Color(!link.Enabled?"#A0AABB":selected==link?"#2868EB":"#729BDD"),selected==link?3:2);if(!link.Enabled)wire.StrokeDashArray=new DoubleCollection(new double[]{4,3});world.Children.Add(wire);
    Point mid,tangent;((PathGeometry)wire.Data).GetPointAtFractionLength(.52,out mid,out tangent);double angle=Math.Atan2(tangent.Y,tangent.X);var arrow=new Polygon{Points=new PointCollection(new[]{new Point(5,0),new Point(-3.5,-3),new Point(-3.5,3)}),Fill=wire.Stroke,Opacity=link.Enabled?.65:.35,IsHitTestVisible=false,RenderTransform=new RotateTransform(angle*180/Math.PI)};SetLeft(arrow,mid.X);SetTop(arrow,mid.Y);world.Children.Add(arrow);if(link.Enabled&&link.Gain>0){var flow=Curve(ports[link.Source],ports["in:"+link.Target],Color("#346CC1"),2);flow.StrokeDashArray=new DoubleCollection(new double[]{1,18});flow.StrokeDashCap=PenLineCap.Round;flow.Opacity=0;flowPaths[link.Source+"\n"+link.Target]=flow;world.Children.Add(flow);}
    var hit=Curve(ports[link.Source],ports["in:"+link.Target],Brushes.Transparent,14);hit.IsHitTestVisible=true;hit.Cursor=Cursors.Hand;hit.MouseRightButtonUp+=(s,e)=>Context(null,link,e);hit.ToolTip="选择连线 · Delete 删除";hit.MouseLeftButtonDown+=(s,e)=>{selected=link;SelectedNodeId=null;pending=null;Focus();Rebuild();e.Handled=true;};world.Children.Add(hit);
   }
   foreach(var n in Nodes){var p=positions[n.Id];
    var box=new Border{Width=Card,Height=NodeHeight,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(pending==n||SelectedNodeId==n.Id?2:1),Padding=new Thickness(14,8,14,8),Opacity=n.Available?1:.62,Cursor=Cursors.SizeAll};box.SetResourceReference(Border.BackgroundProperty,"Brush_FFFFFF");box.SetResourceReference(Border.BorderBrushProperty,pending==n||SelectedNodeId==n.Id?"Brush_2868EB":"Brush_E8EBF0");
    var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36)});grid.ColumnDefinitions.Add(new ColumnDefinition());var art=new DeviceArt{Kind=n.Artwork??"audio",Width=28,Height=28,VerticalAlignment=VerticalAlignment.Center};grid.Children.Add(art);
    var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var role=Text(n.Module?"处理  /  PROCESS":n.Input?"输入  /  SOURCE":"输出  /  OUTPUT",9,"Brush_6685B9");labels.Children.Add(role);var name=Text(n.Name,12,"Brush_202634");name.FontWeight=FontWeights.SemiBold;name.Margin=new Thickness(0,3,0,0);name.ToolTip=n.Name;labels.Children.Add(name);var detail=Text(n.Detail,10,"Brush_7C8595");detail.Margin=new Thickness(0,4,0,0);detail.ToolTip=n.Detail;labels.Children.Add(detail);Grid.SetColumn(labels,1);grid.Children.Add(labels);box.Child=grid;
    box.MouseRightButtonUp+=(s,e)=>Context(n.Id,null,e);
    box.MouseLeftButtonDown+=(s,e)=>{Focus();SelectedNodeId=n.Id;selected=null;if(e.ClickCount==2){if(EditRequested!=null)EditRequested(this,EventArgs.Empty);e.Handled=true;return;}moving=n;down=e.GetPosition(this);origin=positions[n.Id];CaptureMouse();Rebuild();e.Handled=true;};SetLeft(box,p.X);SetTop(box,p.Y);world.Children.Add(box);
    if(n.Input||n.Module)AddPort(n,true);if(!n.Input||n.Module)AddPort(n,false);

   }
  }finally{drawing=false;UpdateFlow();ViewDone();}}
  void AddPort(AudioNode node,bool outgoing){var p=ports[(outgoing?"":"in:")+node.Id];var port=new Border{Width=18,Height=18,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(3),Background=Color(node.Available?"#5C8FE1":"#A0AABB"),BorderBrush=Color("#EAF0FA"),Cursor=Cursors.Cross,ToolTip=outgoing?"输出端口 · 拖动连接":"输入端口"};port.MouseLeftButtonDown+=(s,e)=>{e.Handled=true;Focus();if(!node.Available)return;if(outgoing){pending=node;down=e.GetPosition(this);dragged=false;CaptureMouse();}else if(pending!=null)Connect(node);};SetLeft(port,p.X-9);SetTop(port,p.Y-9);world.Children.Add(port);}
  void MovePointer(object sender,MouseEventArgs e){if(!IsMouseCaptured)return;var view=e.GetPosition(this);
   if(panning){pan.X=origin.X+view.X-down.X;pan.Y=origin.Y+view.Y-down.Y;InvalidateVisual();ViewDone();return;}
   if(moving!=null){positions[moving.Id]=new Point(origin.X+(view.X-down.X)/Zoom,origin.Y+(view.Y-down.Y)/Zoom);Rebuild();return;}
   if(pending==null)return;var p=World(view);if((view-down).Length>5)dragged=true;if(preview!=null)world.Children.Remove(preview);preview=Curve(ports[pending.Id],p,Color("#5C8FE1"),2);world.Children.Add(preview);
  }
  void EndPointer(object sender,MouseButtonEventArgs e){if(!IsMouseCaptured||panning)return;bool wasMoving=moving!=null;var p=World(e.GetPosition(this));ReleaseMouseCapture();if(wasMoving){LayoutDone();e.Handled=true;return;}
   if(dragged){var target=Nodes.FirstOrDefault(n=>(!n.Input||n.Module)&&n.Available&&ports.ContainsKey("in:"+n.Id)&&(ports["in:"+n.Id]-p).Length<24/Zoom);if(target!=null)Connect(target);else{pending=null;Rebuild();}}else Rebuild();e.Handled=true;
  }
  bool Reachable(string from,string target,HashSet<string> seen){if(from==target)return true;if(!seen.Add(from))return false;return Links.Where(l=>l.Source==from).Any(l=>Reachable(l.Target,target,seen));}
  void Connect(AudioNode target){if(pending==null)return;if(pending==target||Reachable(target.Id,pending.Id,new HashSet<string>())){pending=null;Rebuild();return;}if(!Links.Any(l=>l.Source==pending.Id&&l.Target==target.Id))Links.Add(new AudioLink{Source=pending.Id,Target=target.Id,SourceName=pending.Name,TargetName=target.Name,Loopback=pending.Loopback});pending=null;Changed();}
 }
 public sealed class AudioMinimap : FrameworkElement {
  AudioBoard board;Rect bounds;double factor,offsetX,offsetY;
  public AudioBoard Board {get{return board;}set{if(board!=null)board.ViewChanged-=Changed;board=value;if(board!=null)board.ViewChanged+=Changed;InvalidateVisual();}}
  void Changed(object s,EventArgs e){InvalidateVisual();}
  public AudioMinimap(){Width=176;Height=100;ClipToBounds=true;Cursor=Cursors.Hand;ToolTip="点击定位画布 / Click to navigate";MouseLeftButtonDown+=(s,e)=>{if(board==null||factor<=0)return;var p=e.GetPosition(this);board.CenterOn(new Point((p.X-offsetX)/factor+bounds.X,(p.Y-offsetY)/factor+bounds.Y));e.Handled=true;};}
  protected override void OnRender(DrawingContext dc){base.OnRender(dc);dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));if(board==null)return;
   bounds=board.Viewport;var positions=board.ExportPositions().ToDictionary(p=>p.Id);foreach(var node in board.Nodes){NodePosition p;if(positions.TryGetValue(node.Id,out p))bounds.Union(new Rect(p.X,p.Y,244,80));}
   bounds.Inflate(30,30);factor=Math.Min((ActualWidth-12)/Math.Max(1,bounds.Width),(ActualHeight-12)/Math.Max(1,bounds.Height));offsetX=(ActualWidth-bounds.Width*factor)/2;offsetY=(ActualHeight-bounds.Height*factor)/2;
   var ink=TryFindResource("Brush_6685B9") as Brush??Brushes.SteelBlue;
   foreach(var link in board.Links){NodePosition a,b;if(positions.TryGetValue(link.Source,out a)&&positions.TryGetValue(link.Target,out b))dc.DrawLine(new Pen(ink,.6),Map(new Point(a.X+244,a.Y+40)),Map(new Point(b.X,b.Y+40)));}
   foreach(var n in board.Nodes){NodePosition p;if(positions.TryGetValue(n.Id,out p))dc.DrawRoundedRectangle(ink,null,new Rect(Map(new Point(p.X,p.Y)),new Size(244*factor,80*factor)),2,2);}
   var view=board.Viewport;var accent=TryFindResource("Brush_2868EB") as Brush??Brushes.RoyalBlue;dc.PushOpacity(.12);dc.DrawRectangle(accent,null,new Rect(Map(view.TopLeft),new Size(view.Width*factor,view.Height*factor)));dc.Pop();dc.DrawRectangle(null,new Pen(accent,1),new Rect(Map(view.TopLeft),new Size(view.Width*factor,view.Height*factor)));
  }
  Point Map(Point p){return new Point(offsetX+(p.X-bounds.X)*factor,offsetY+(p.Y-bounds.Y)*factor);}
 }

}
