// DelayedStructureSystem.cs
using System.Collections.Generic;
using Microsoft.Build.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

public class JailFactory1 : ModSystem
{
    private int _timer = 0;
    private Point _origin;  // 缓存 origin 用于后续无参调用
    private HashSet<Point> _tilesToDig;
    private HashSet<Point> _wallsToKill;    //记录要清除的墙
    private HashSet<Point> _tilesToPlace_dn;    // 下层 U形
    private HashSet<Point> _tilesToPlace_up;    // 上层 环形
    private HashSet<Point> _tilesToPlaceWall;   // 6*10 墙
    private bool _isProcessing = false;

    public void StartGenerating(Point origin) //下半平台+上半环矩形
    {
        if (_isProcessing) return;
        _origin = origin; // ✅ 保存 origin 供后续使用
        int width = 6;
        int height_dn = 4;        // 下层 U形高度
        int height_up = 6;        // 上层 环形高度
        
        _tilesToDig = new HashSet<Point>();
        _tilesToPlace_dn = new HashSet<Point>();
        _tilesToPlace_up = new HashSet<Point>();
        _tilesToPlaceWall = new HashSet<Point>();
        _wallsToKill = new HashSet<Point>();

        Player player = Main.player[Main.myPlayer]; // 获取本地玩家

        // ✅ Step 1: 计算整个结构的总高度，并定义挖掘区域
        int totalHeight = height_up + height_dn;  // 紧密连接，无空气
        int startY = origin.Y - totalHeight + 1;  // 整个结构的最顶部 Y

        for (int y = 0; y < totalHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int worldX = origin.X - width / 2 + x;
                int worldY = startY + y;

                if (WorldGen.InWorld(worldX, worldY))
                {
                    _tilesToDig.Add(new Point(worldX, worldY));
                    

                }
            }
        }
        // 放墙的宽度和高度各减去2，以确保比实际结构小一圈
        int WallWidth = width - 2;
        int WallHeight = totalHeight - 2;

        for (int y = 0; y < WallHeight; y++)
        {
            for (int x = 0; x < WallWidth; x++)
            {
                // 注意这里的坐标调整
                int worldX = origin.X - width / 2 + 1 + x;
                int worldY = startY + 1 + y;

                if (WorldGen.InWorld(worldX, worldY))
                {
                    _tilesToPlaceWall.Add(new Point(worldX, worldY));   //添加墙的范围
                    _wallsToKill.Add(new Point(worldX, worldY));    //清除墙的范围
                }
            }
        }

        // ✅ Step 2: 生成下层 U形（6x4），底部对齐 origin.Y
        for (int y = 0; y < height_dn; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool shouldPlace = false;

                if (y == height_dn - 1)                 // 底部一行
                    shouldPlace = true;
                else if (x == 0 || x == width - 1)      // 左右两列
                    shouldPlace = true;

                if (shouldPlace)
                {
                    int worldX = origin.X - width / 2 + x;
                    int worldY = origin.Y - height_dn + 1 + y;  // Y: 197 ~ 200

                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        _tilesToPlace_dn.Add(new Point(worldX, worldY));

                    }
                    
                }
                
            }
        }

        // ✅ Step 3: 生成上层 6x6 环形，紧密贴合在下层上方
        // 上层底部 Y = 下层顶部 Y - 1
        // 下层顶部 Y = origin.Y - height_dn + 1
        // 上层底部 Y = (origin.Y - height_dn + 1) - 1 = origin.Y - height_dn
        // 上层顶部 Y = 上层底部 Y - height_up + 1 = origin.Y - height_dn - height_up + 1
        int upperTopY = origin.Y - height_dn - height_up + 1;

        for (int y = 0; y < height_up; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // 环形：四边
                bool isEdge = (x == 0 || x == width - 1 || y == 0 || y == height_up - 1);

                if (isEdge)
                {
                    int worldX = origin.X - width / 2 + x;
                    int worldY = upperTopY + y;  // 从上层顶部开始

                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        _tilesToPlace_up.Add(new Point(worldX, worldY));
                        
                    }
                }
            }
        }

        // ✅ Step 4: 执行挖掘
        foreach (Point p in _tilesToDig)
        {
            WorldGen.KillTile(p.X, p.Y, fail: false, effectOnly: false);
        }
        
        // ✅ Step 4.5: 清除所有墙
        foreach (Point p in _wallsToKill)
        {
            WorldGen.KillWall(p.X, p.Y, fail: false); // 清除墙，无动画
        }
            // ✅ Step 5: 启动计时器
            _timer = 0;
            _isProcessing = true;
    }

    public override void PostUpdateEverything()
    {
        if (!_isProcessing) return;

        _timer++;

        if (_timer >= 10)
        {
            // ✅ 立即放置木墙
            PlaceWalls();
            // ✅ 放置火把
            PlaceTorchAtOffset();       
            PlaceStructureAfterDelay_dn();
            PlaceStructureAfterDelay_up();
            // ✅ 放置工作台（在火把上方一格，X=origin.X）
             PlaceWorkbenchAndChair();   
                        

            // ✅ 统一清理
            _tilesToDig?.Clear();
            _tilesToPlace_dn?.Clear();
            _tilesToPlace_up?.Clear();

            _isProcessing = false;
        }
    }

    private void PlaceStructureAfterDelay_dn()//下部放置Platforms
    {
        foreach (Point p in _tilesToPlace_dn)
        {
            int x = p.X, y = p.Y;
            if (!WorldGen.InWorld(x, y)) continue;

            Tile tile = Main.tile[x, y];
            if (tile == null || tile.HasTile) continue;

            if (WorldGen.PlaceTile(x, y, TileID.Platforms))
            {
                WorldGen.SquareTileFrame(x, y, true);
            }
        }
    }

    private void PlaceStructureAfterDelay_up()//上部放置WoodBlock
    {
        foreach (Point p in _tilesToPlace_up)
        {
            int x = p.X, y = p.Y;
            if (!WorldGen.InWorld(x, y)) continue;

            Tile tile = Main.tile[x, y];
            if (tile == null || tile.HasTile) continue;

            if (WorldGen.PlaceTile(x, y, TileID.WoodBlock))
            {
                WorldGen.SquareTileFrame(x, y, true);
            }
        }
    }

    // ✅ 新增：放置木墙
        private void PlaceWalls()
        {
            foreach (Point p in _tilesToPlaceWall)
            {
                int x = p.X, y = p.Y;
                if (!WorldGen.InWorld(x, y)) continue;

                Tile tile = Main.tile[x, y];
                if (tile == null) continue;

                // 只有当墙不是实心墙时才放置
                if (tile.WallType != WallID.Wood)
                {
                    WorldGen.PlaceWall(x, y, WallID.Wood, mute: true);
                }
            }
        }

    //放置火把
    private void PlaceTorchAtOffset()
    {   
        Player player = Main.player[Main.myPlayer]; // 获取本地玩家
        if (!_isProcessing) return;

        int width = 6;
        int height_dn = 4;
        int dx;
        int dy = height_dn;     // 4

        int torchX;
        if (player.direction == 1)
        {
            dx = width / 2 - 1;
            torchX = _origin.X + dx - 1;
        }
        else
        {
            dx = -width / 2 + 1;    // -2 
            torchX = _origin.X + dx;
        }
        
        int torchY = _origin.Y - dy;

        if (WorldGen.InWorld(torchX, torchY))
        {
            Tile tile = Main.tile[torchX, torchY];
            if (tile != null && !tile.HasTile && !tile.BottomSlope && !tile.TopSlope)
            {
                // ✅ 自动同步（如果是服务器）
                bool success = WorldGen.PlaceObject(torchX, torchY, TileID.Torches, true);
                if (success)
                {
                    // 强制刷新图块帧
                    WorldGen.SquareTileFrame(torchX, torchY);
                }
            }
        }
    }

    //放置木椅和工作台
    private void PlaceWorkbenchAndChair()
{
    Player player = Main.player[Main.myPlayer];
    if (!_isProcessing) return;

    int width;
    int height_dn = 4;

    // 🔧 先计算火把位置（用于参考）
    int torchX;
    if (player.direction == 1) // 面朝右
    {
        torchX = _origin.X - 1; // X - 1 （结构左中）
    }
    else // 面朝左
    {
        torchX = _origin.X;     // X + 0 （结构中右）—— 注意：原逻辑有误，这里修正
    }

    int torchY = _origin.Y - height_dn; // Y - 4

    // ✅ 工作台和椅子的目标位置
    int workbenchX, workbenchY;
    int chairX, chairY;

    if (player.direction == 1) // 面朝右 → 家具在左侧（前方）
    {
        workbenchX = _origin.X - 3; // 最左边
        workbenchY = torchY - 1;    // Y - 5

        chairX = _origin.X - 3;     // 和工作台同 X
        chairY = workbenchY;        // 同 Y（椅子占 Y 和 Y+1）
    }
    else // 面朝左 → 家具在右侧
    {
        workbenchX = _origin.X + 1; // X + 1（避开最右）
        workbenchY = torchY - 1;    // Y - 5

        chairX = _origin.X + 1;
        chairY = workbenchY;
    }

    // ✅ 检查并放置工作台 (2x2)
    bool canPlaceWorkbench = true;
    for (int dy = 0; dy < 2; dy++)
    {
        for (int dx = 0; dx < 2; dx++)
        {
            int x = workbenchX + dx;
            int y = workbenchY + dy;

            if (!WorldGen.InWorld(x, y))
            {
                canPlaceWorkbench = false;
                break;
            }

            Tile tile = Main.tile[x, y];
            if (tile == null || tile.HasTile || tile.Slope != 0 || tile.IsHalfBlock)
            {
                canPlaceWorkbench = false;
                break;
            }
        }
        if (!canPlaceWorkbench) break;
    }

    if (canPlaceWorkbench)
    {
        WorldGen.PlaceObject(workbenchX, workbenchY, TileID.WorkBenches, true);
        for (int dy = 0; dy < 2; dy++)
        {
            for (int dx = 0; dx < 2; dx++)
            {
                WorldGen.SquareTileFrame(workbenchX + dx, workbenchY + dy);
            }
        }
    }

    // ✅ 检查并放置木椅 (1x2)
    bool canPlaceChair = true;
    for (int dy = 0; dy < 2; dy++)
    {
        int x = chairX;
        int y = chairY + dy;

        if (!WorldGen.InWorld(x, y))
        {
            canPlaceChair = false;
            break;
        }

        Tile tile = Main.tile[x, y];
        if (tile == null || tile.HasTile || tile.Slope != 0 || tile.IsHalfBlock)
        {
            canPlaceChair = false;
            break;
        }
    }

    if (canPlaceChair)
    {
        WorldGen.PlaceObject(chairX, chairY, TileID.Chairs, true);
        WorldGen.SquareTileFrame(chairX, chairY);
        WorldGen.SquareTileFrame(chairX, chairY + 1);
    }
}
}