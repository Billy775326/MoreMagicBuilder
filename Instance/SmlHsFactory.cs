using Terraria.ID;
using Terraria.WorldBuilding;

// 假设 chair, chair1, bed 是 Point16 坐标对象
// 假设 FurnY, FurnY2 是 y 坐标，player 是当前玩家对象

public class FurnitureGen
{
    public void PlaceFurniture(Point16 chair, Point16 chair1, Point16 bed, int FurnY, int FurnY2, Player player)
    {
        // --- 椅子方向修改 ---

        // 放置椅子 (Chair - 1x2) - 靠近桌子中央侧
        // Style 0 = 面朝左; Style 1 = 面朝右

        // 示例：将第一把椅子固定为面朝右 (Style 1)
        WorldGen.PlaceObject(chair.X, FurnY, TileID.Chairs, style: 1);

        // 放置椅子 (Chair - 1x2) - 远端侧

        // 示例：将第二把椅子固定为面朝左 (Style 0)
        WorldGen.PlaceObject(chair1.X, FurnY, TileID.Chairs, style: 0);

        // --- 床的方向修改 ---

        // 放置床 (Bed - 4x2) - 放置在二楼
        // 床的水平方向通常由 WorldGen.PlaceObject 方法的 direction 参数控制，
        // 1 表示朝右 (默认)，-1 表示朝左 (翻转)。

        // 示例：将床固定为朝向左侧 (direction: -1)。
        // 这里的 style: 0 保持床的类型为默认的“木床”。
        WorldGen.PlaceObject(bed.X, FurnY2, TileID.Beds, style: 0, direction: -1);
    }
}