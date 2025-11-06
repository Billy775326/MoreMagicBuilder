
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

public class SmlHsFactory : ModSystem
{
    private int _timer = 0;
    private Point _origin;
    // 存储要操作的坐标
    private HashSet<Point> _tilesToDig;
    private HashSet<Point> _wallsToKill;
    private HashSet<Point> _wallsToPlace;
    private HashSet<Point> _tilesToPlace_dn;
    private HashSet<Point> _tilesToPlace_up;

    private bool _isProcessing = false;

    // 控制进度：逐层向上
    private int _currentY;
    private int _minY;
    private int _maxY;

    // 火把延迟放置
    private bool _pendingTorchPlacement = false;
    private int _torchX, _torchY;

    public void StartGenerating(Point origin)//18*15   18*14   18*7  &18*8的三角
    {
        if (_isProcessing) return;
        _origin = origin;

        int width = 18;
        int height = 15;
        int height_dn = 6;// 下半矩形高度
        int height_up = 9;// 上半三角高度（6+9=15）

        _minY = origin.Y;
        _maxY = origin.Y - height + 1;

        _tilesToDig = new HashSet<Point>();
        _wallsToKill = new HashSet<Point>();
        _wallsToPlace = new HashSet<Point>();
        _tilesToPlace_dn = new HashSet<Point>();
        _tilesToPlace_up = new HashSet<Point>();

        // 收集：挖掘区域 (18*6矩形 && 18*9▲)
        for (int y = 0; y < height; y++)
        {
            int worldY = _minY - y; // ✅ 在此处定义，整个循环内可用

            if (y < height_dn)
            {
                // ▼ 下半部分：18×6 矩形
                for (int x = 0; x < width; x++)
                {
                    int worldX = origin.X - width / 2 + x;
                    if (WorldGen.InWorld(worldX, worldY))
                        _tilesToDig.Add(new Point(worldX, worldY));
                }
            }
            else
            {
                // ▲ 上半部分：18×9 三角形（底部宽18，顶部宽2）
                int triRow = y - height_dn; // 0 ~ 8
                int currentWidth = width - triRow * 2;

                if (currentWidth <= 0) continue; // 安全保护

                int leftOffset = (width - currentWidth) / 2;

                for (int x = 0; x < currentWidth; x++)
                {
                    int worldX = origin.X - width / 2 + leftOffset + x;
                    if (WorldGen.InWorld(worldX, worldY)) // ✅ 正确调用
                        _tilesToDig.Add(new Point(worldX, worldY));
                }
            }
        }
        // 收集：清除墙区域 (16*6矩形 && 18*9▲)
        int wallWidth = width;
        int wallHeight = height;
        for (int y = 0; y < wallHeight; y++)
        {
            int worldY = _minY - y; // ✅ 在此处定义，整个循环内可用
            if (y < height_dn)
            {
                for (int x = 0; x < wallWidth; x++)//下半矩形
                {
                    int worldX = origin.X - width / 2 + x;
                    if (WorldGen.InWorld(worldX, worldY))
                        _wallsToKill.Add(new Point(worldX, worldY));
                }
            }
            else
            {
                // ▲ 上半部分：18×9 三角形（底部宽18，顶部宽2）
                int triRow = y - height_dn; // 0 ~ 8
                int currentWidth = width - triRow * 2;

                int leftOffset = (width - currentWidth) / 2;

                for (int x = 0; x < currentWidth; x++)
                {
                    int worldX = origin.X - width / 2 + leftOffset + x;
                    if (WorldGen.InWorld(worldX, worldY)) // ✅ 正确调用
                        _wallsToKill.Add(new Point(worldX, worldY));
                }
            }
        }

        // 收集：放置墙区域（与清除区域一致）
        for (int y = 0; y < wallHeight; y++)
        {
            int worldY = _minY - y; // ✅ 在此处定义，整个循环内可用
            if (y < height_dn)
            {
                for (int x = 0; x < wallWidth-4; x++)//下半矩形
                {
                    int worldX = origin.X - (width-4) / 2 + x;
                    if (WorldGen.InWorld(worldX, worldY))
                        _wallsToPlace.Add(new Point(worldX, worldY));
                }
            }
            else
            {
                // ▲ 上半部分：18×9 三角形（底部宽18，顶部宽2）
                int triRow = y - height_dn; // 0 ~ 8
                int currentWidth = width - triRow * 2;

                int leftOffset = (width - currentWidth) / 2;

                for (int x = 0; x < currentWidth; x++)
                {
                    int worldX = origin.X - width / 2 + leftOffset + x;
                    if (WorldGen.InWorld(worldX, worldY)) // ✅ 正确调用
                        _wallsToPlace.Add(new Point(worldX, worldY));
                }
            }
        }
        //_wallsToPlace = new HashSet<Point>(_wallsToKill);

        // 收集：一楼小盒子  14*7
        for (int y = 0; y < height_dn ; y++)
        {
            for (int x = 0; x < width ; x++)
            {

                bool shouldPlace = (y == height_dn - 1) || (x == 0) || (x == width - 1);
                int worldX = origin.X - width / 2 + x;
                int worldY = origin.Y - height_dn + 1 + y;
                if (shouldPlace && WorldGen.InWorld(worldX, worldY))
                    _tilesToPlace_dn.Add(new Point(worldX, worldY));
            }
        }



        // 收集：上层封顶 (5x1)
        for (int y = 0; y < height_up; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isEdge = (x == 0 || x == width - 1 || y == 0 || y == height_up - 1);
                int worldX = origin.X - width / 2 + x;
                int worldY = origin.Y - height_dn - height_up + 1 + y;
                if (isEdge && WorldGen.InWorld(worldX, worldY))
                    _tilesToPlace_up.Add(new Point(worldX, worldY));
            }
        }

        // 初始化：从最底层开始
        _currentY = _minY;
        _isProcessing = true;
        _timer = 0;
    }

    public override void PostUpdateEverything()
    {
        if (!_isProcessing) return;
        _timer++;

        // 控制节奏：每 5 帧处理一层
        if (_timer % 5 != 0) return;

        Player player = Main.player[Main.myPlayer];

        // ✅ 主循环：从下往上，逐层处理
        if (_currentY >= _maxY)
        {
            ProcessLayer(_currentY);
            _currentY--;
            return;
        }

        // ✅ 所有层处理完毕：放置最终家具
        if (_currentY < _maxY && _isProcessing)
        {
            // 放置火把（准备）
            if (!_pendingTorchPlacement)
            {
                PlaceFurniture();
                PlaceTorchAtOffset();
                return;
            }

            // 强制放置火把   平台?
            if (_pendingTorchPlacement)
            {
                if (WorldGen.InWorld(_torchX, _torchY))
                {

                    if (WorldGen.PlaceObject(_torchX, _torchY, TileID.Torches, true))
                    {
                        WorldGen.SquareTileFrame(_torchX, _torchY);
                    }
                }
                _pendingTorchPlacement = false;
            }

            // 清理
            _tilesToDig?.Clear();
            _wallsToKill?.Clear();
            _wallsToPlace?.Clear();
            _tilesToPlace_dn?.Clear();
            _tilesToPlace_up?.Clear();
            _isProcessing = false;
        }
    }

    // 处理指定 Y 层的所有操作
    private void ProcessLayer(int y)
    {
        // 1️⃣ 挖掘该层所有瓦片
        foreach (Point p in _tilesToDig)
        {
            if (p.Y == y)
            {
                WorldGen.KillTile(p.X, p.Y, fail: false, effectOnly: false);
            }
        }

        // 2️⃣ 清除该层墙
        foreach (Point p in _wallsToKill)
        {
            if (p.Y == y)
            {
                WorldGen.KillWall(p.X, p.Y, fail: false);
            }
        }

        // 3️⃣ 放置该层墙（内部）
        foreach (Point p in _wallsToPlace)
        {
            if (p.Y == y)
            {
                if (!Main.wallDungeon[Main.tile[p.X, p.Y].WallType] &&
                    Main.tile[p.X, p.Y].WallType != WallID.None)
                {
                    continue; // 避免覆盖特殊墙
                }

                WorldGen.PlaceWall(p.X, p.Y, WallID.IronBrick);
                WorldGen.SquareWallFrame(p.X, p.Y);
            }
        }

        // 4️⃣ 放置该层结构
        // 下层U形结构  与 平台 
        var toPlace_dn = new List<Point>(_tilesToPlace_dn);
        Player player = Main.player[Main.myPlayer];
        foreach (Point p in toPlace_dn)
        {
            if (p.Y == y && !Main.tile[p.X, p.Y].HasTile)
            {
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.GrayBrick))
                {
                    WorldGen.SquareTileFrame(p.X, p.Y, true);
                    if (p.Y == y)
                    {
                        ForcePlaceTile(_origin.X - 2, _origin.Y, TileID.GrayBrick);
                        ForcePlaceTile(_origin.X, _origin.Y, TileID.GrayBrick);
                        ForcePlaceTile(_origin.X + 2, _origin.Y, TileID.GrayBrick);
                        WorldGen.PlaceTile(_origin.X - 1, _origin.Y, TileID.Platforms);
                        WorldGen.PlaceTile(_origin.X + 1, _origin.Y, TileID.Platforms);
                    }

                }
                _tilesToPlace_dn.Remove(p);

            }

        }

        // 上层石头
        var toPlace_up = new List<Point>(_tilesToPlace_up);
        foreach (Point p in toPlace_up)
        {
            if (p.Y == y && !Main.tile[p.X, p.Y].HasTile)
            {
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.Stone))
                {
                    WorldGen.SquareTileFrame(p.X, p.Y, true);
                    _tilesToPlace_up.Remove(p);
                }
            }
        }
    }

    // 准备火把位置
    private void PlaceTorchAtOffset()
    {
        Player player = Main.player[Main.myPlayer];
        if (!_isProcessing) return;

        _torchX = _origin.X;
        _torchY = _origin.Y - 9;
        _pendingTorchPlacement = true;
    }

    private void ForcePlaceTile(int x, int y, ushort type)//强制放置物块,无视规则
    {
        if (!WorldGen.InWorld(x, y)) return;
        Tile tile = Main.tile[x, y];
        tile.TileType = type;
        tile.HasTile = true;
        WorldGen.SquareTileFrame(x, y);

        // 多人同步
        if (Main.netMode != NetmodeID.SinglePlayer)
            NetMessage.SendTileSquare(-1, x, y, 1);
    }
    //放置工作台和椅子
    private void PlaceFurniture()
    {
        Player player = Main.player[Main.myPlayer];

        int center_X = _origin.X;
        int center_Y = _origin.Y;

        int workbenchX, chairX;

        if (player.direction == 1) // 玩家向右
        {
            workbenchX = center_X;
            chairX = center_X - 1;
        }
        else // 玩家向左
        {
            workbenchX = center_X - 1;
            chairX = center_X + 1; // 注意对称
        }

        // ✅ 放置平台（可选）
        //WorldGen.PlaceTile(center_X - 1, center_Y, TileID.Platforms);
        //WorldGen.PlaceTile(center_X + 1, center_Y, TileID.Platforms);
        //  放置石砖
        //ForcePlaceTile(center_X - 2, center_Y, TileID.GrayBrick);
        //ForcePlaceTile(center_X,     center_Y, TileID.GrayBrick);
        //ForcePlaceTile(center_X + 2, center_Y, TileID.GrayBrick);

        // ✅ 放置工作台
        if (WorldGen.PlaceObject(workbenchX, center_Y - 1, TileID.WorkBenches, mute: true))
        {
            WorldGen.SquareTileFrame(workbenchX, center_Y - 1);
        }

        // ✅ 放置椅子（注意朝向）
        if (WorldGen.PlaceObject(chairX, center_Y - 1, TileID.Chairs,
            mute: true, style: 0, alternate: 0, random: -1, direction: player.direction))
        {
            WorldGen.SquareTileFrame(chairX, center_Y - 1);
        }
    }
}

