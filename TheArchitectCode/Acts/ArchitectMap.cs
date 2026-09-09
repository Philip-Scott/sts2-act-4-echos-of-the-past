using MegaCrit.Sts2.Core.Map;

namespace TheArchitect.TheArchitectCode.Acts;

public sealed class ArchitectMap : ActMap
{
    public bool HasAncient { get; }
    protected override MapPoint?[,] Grid { get; }
    public override MapPoint StartingMapPoint { get; }
    public override MapPoint BossMapPoint { get; }

    public ArchitectMap(bool hasAncient = true)
    {
        HasAncient = hasAncient;
        Grid = new MapPoint?[7, hasAncient ? 3 : 2];
        StartingMapPoint = Point(0, hasAncient ? MapPointType.Ancient : MapPointType.RestSite);
        BossMapPoint = Point(hasAncient ? 3 : 2, MapPointType.Boss);
        var rest = StartingMapPoint;
        if (hasAncient)
        {
            rest = Point(1, MapPointType.RestSite);
            Grid[3, 1] = rest;
            StartingMapPoint.AddChildPoint(rest);
        }
        // Older saves retain their serialized Ancient-free room set and original coordinates.
        var shop = Point(hasAncient ? 2 : 1, MapPointType.Shop);
        Grid[3, shop.coord.row] = shop;
        rest.AddChildPoint(shop);
        shop.AddChildPoint(BossMapPoint);
        startMapPoints.Add(StartingMapPoint);
    }

    private static MapPoint Point(int row, MapPointType type) =>
        new(3, row) { PointType = type, CanBeModified = false };
}
