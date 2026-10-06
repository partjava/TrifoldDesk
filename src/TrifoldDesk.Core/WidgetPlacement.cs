namespace TrifoldDesk.Core;
public static class WidgetPlacement
{
 public static bool Overlaps(WidgetBounds a,WidgetBounds b)=>a.X<b.X+b.Width&&a.X+a.Width>b.X&&a.Y<b.Y+b.Height&&a.Y+a.Height>b.Y;
 public static WidgetBounds? Avoid(WidgetBounds requested,IEnumerable<WidgetBounds> occupied,double width,double height,double gap=6)
 {
  if(requested.Width>width||requested.Height>height)return null;
  var start=requested with{X=Math.Clamp(requested.X,0,width-requested.Width),Y=Math.Clamp(requested.Y,0,height-requested.Height)};
  var blocks=occupied.Where(b=>b.Width>0&&b.Height>0).ToArray();if(!blocks.Any(b=>Overlaps(start,b)))return start;
  var xs=new[]{start.X,0,width-start.Width}.Concat(blocks.SelectMany(b=>new[]{b.X-start.Width-gap,b.X+b.Width+gap})).Where(x=>x>=0&&x<=width-start.Width).Distinct().OrderBy(x=>Math.Abs(x-start.X)).Take(64).ToArray();
  var ys=new[]{start.Y,0,height-start.Height}.Concat(blocks.SelectMany(b=>new[]{b.Y-start.Height-gap,b.Y+b.Height+gap})).Where(y=>y>=0&&y<=height-start.Height).Distinct().OrderBy(y=>Math.Abs(y-start.Y)).Take(64).ToArray();
  return xs.SelectMany(x=>ys.Select(y=>start with{X=x,Y=y})).OrderBy(b=>(b.X-start.X)*(b.X-start.X)+(b.Y-start.Y)*(b.Y-start.Y)).Where(b=>!blocks.Any(o=>Overlaps(b,o))).Select(b=>(WidgetBounds?)b).FirstOrDefault();
 }
}
