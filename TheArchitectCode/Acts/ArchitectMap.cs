using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TheArchitect.TheArchitectCode.Acts;

public sealed class ArchitectMap : ActMap
{
    public bool HasAncient { get; }
    public bool HasCorruptedPlayers { get; }
    protected override MapPoint?[,] Grid { get; }
    public override MapPoint StartingMapPoint { get; }
    public override MapPoint BossMapPoint { get; }

    public ArchitectMap(bool hasAncient = true, bool hasCorruptedPlayers = false)
    {
        HasAncient = hasAncient;
        HasCorruptedPlayers = hasCorruptedPlayers;
        var bossRow = (hasAncient ? 3 : 2) + (hasCorruptedPlayers ? 1 : 0);
        Grid = new MapPoint?[7, bossRow];
        StartingMapPoint = Point(0, hasAncient ? MapPointType.Ancient : MapPointType.RestSite);
        BossMapPoint = Point(bossRow, MapPointType.Boss);
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
        if (hasCorruptedPlayers)
        {
            var elite = Point(shop.coord.row + 1, MapPointType.Elite);
            Grid[3, elite.coord.row] = elite;
            shop.AddChildPoint(elite);
            elite.AddChildPoint(BossMapPoint);
        }
        else
            shop.AddChildPoint(BossMapPoint);
        startMapPoints.Add(StartingMapPoint);
    }

    private static MapPoint Point(int row, MapPointType type) =>
        new(3, row) { PointType = type, CanBeModified = false };

    internal static void AddCorruptedElite(SerializableActMap map)
    {
        if (map.Points.Any(point => point.PointType == MapPointType.Elite))
            return;
        var eliteCoord = map.BossPoint.Coord;
        map.BossPoint.Coord = new MapCoord(eliteCoord.col, eliteCoord.row + 1);
        map.GridHeight = map.BossPoint.Coord.row;
        map.Points.Add(new SerializableMapPoint
        {
            Coord = eliteCoord,
            PointType = MapPointType.Elite,
            CanBeModified = false,
            ChildCoords = [map.BossPoint.Coord]
        });
    }

    internal static MapCoord? RemoveCorruptedElite(SerializableActMap map)
    {
        var elite = map.Points.SingleOrDefault(point => point.PointType == MapPointType.Elite);
        if (elite == null)
            return null;
        foreach (var point in map.Points.Append(map.StartingPoint))
            if (point.ChildCoords?.Remove(elite.Coord) == true)
                point.ChildCoords.Add(map.BossPoint.Coord);
        map.Points.Remove(elite);
        return elite.Coord;
    }
}
