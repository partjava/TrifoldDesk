using TrifoldDesk.Core;
public static class PlacementTests
{
 public static void Run(Action<bool,string> check)
 {
  var occupied=new[]{new WidgetBounds(0,0,100,100)};
  var placed=WidgetPlacement.Avoid(new WidgetBounds(20,20,60,60),occupied,300,300);
  check(placed.HasValue && !WidgetPlacement.Overlaps(placed.Value,occupied[0]),"drag finds nearest non-overlapping position within pane");
  check(WidgetPlacement.Avoid(new WidgetBounds(0,0,100,100),occupied,100,100)==null,"full pane reports unavailable instead of moving outside boundary");
  var free=new WidgetBounds(160,80,60,60);check(WidgetPlacement.Avoid(free,occupied,300,300)==free,"placement preserves deliberate free position");
 }
}
