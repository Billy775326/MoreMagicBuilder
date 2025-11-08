using System.Collections.Generic;
using System.Linq; // 引入 Linq
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Net; // For NetMessage

// 这是一个 ModSystem，用于在游戏中异步生成小型房屋结构。
// This ModSystem handles the asynchronous generation of a small house structure in-game.
public class SmlHsFactory : ModSystem
{
    // --- 结构参数 (House Structure Parameters) ---
    private int _updateTimer = 0;
    private Point _originPoint; // 房屋的中心底部坐标 (地面中心点) / The center bottom coordinate (ground level)
    private const int _width = 16; // 宽度改为 16 / Width changed to 16
    private const int _height = 15; // 总高度保持 15 / Total height remains 15
    private const int _rectHeight = 7; // 下半矩形高度改为 7 / Height of the lower rectangular section changed to 7
    private const int _triHeight = 8;     // 上半三角高度 (15 - 7 = 8) / Height of the upper triangular section (15 - 7 = 8)

    // --- 进度控制 (Progress Control) ---
    private int _currentWorldY; // 当前正在处理的世界 Y 坐标 / Current World Y coordinate being processed
    private int _minWorldY;     // 底部 Y 坐标 (_origin.Y) / Bottom Y coordinate
    private int _maxWorldY;     // 顶部 Y 坐标 (_origin.Y - height + 1) / Top Y coordinate
    private bool _isProcessing = false;
    private bool _isFinishedPlacingTiles = false; // 新增：标记物块和墙体是否放置完毕

    // --- 数据集 (Data Collections) ---
    private HashSet<Point> _tilesToClear;   // 要清除的物块区域 / Area to clear existing tiles
    private HashSet<Point> _wallsToClear;   // 要清除的墙壁区域 / Area to clear existing walls
    private HashSet<Point> _wallsToPlace;   // 要放置的墙壁区域 (内部) / Area to place background walls (internal)
    private HashSet<Point> _wallsToPlacetri;    // 要放置的墙壁区域 (三角形外围) / Area to place background walls 
    private HashSet<Point> _tilesToPlaceRect; // 要放置的下半部分物块 (墙/底) / Tiles for the rectangular frame/floor
    private HashSet<Point> _tilesToPlaceTri;    // 要放置的上半部分物块 (三角框架) / Tiles for the triangular frame

    // --- 家具放置延迟 (Furniture Placement Delay) ---
    // 将现有的布尔值改成一个枚举或整型状态，以更好地控制多个阶段
    private int _furnitureState = 0; // 0: 未开始, 1: 放置主要家具/门/平台, 2: 等待蜡烛延迟, 3: 放置蜡烛

    // 定义延迟帧数
    private const int CANDLE_DELAY_FRAMES = 5;

    // --- 收集逻辑 (StartGenerating) 保持不变 ---
    // ... (StartGenerating 方法内容不变) ...
    public void StartGenerating(Point origin)
    {
        if (_isProcessing) return;

        _originPoint = origin;
        _minWorldY = origin.Y;
        _maxWorldY = origin.Y - _height + 1; // 顶部 Y 坐标

        // 初始化集合 (Initialize collections)
        _tilesToClear = new HashSet<Point>();
        _wallsToClear = new HashSet<Point>();
        _wallsToPlace = new HashSet<Point>();
        _wallsToPlacetri = new HashSet<Point>();
        _tilesToPlaceRect = new HashSet<Point>();
        _tilesToPlaceTri = new HashSet<Point>();

        // ----------------------------------------------------
        // 1. 收集：挖掘区域 (16x7矩形 & 16x8三角形) - Collect: Area to Dig
        // ----------------------------------------------------
        for (int y = 1; y < _height; y++)
        {
            int worldY = _minWorldY - y; // 从下往上 (y=0 是 _minY)
            int currentWidth;
            int leftOffset;

            if (y < _rectHeight)
            {
                // ▼ 下半部分：16×7 矩形 (完整宽度)
                currentWidth = _width;
                leftOffset = 0;
            }
            else
            {
                // ▲ 上半部分：16×8 三角形
                int triRow = y - _rectHeight; // 0 ~ 7
                currentWidth = _width - triRow * 2;
                if (currentWidth <= 0) continue;
                leftOffset = (_width - currentWidth) / 2;
            }

            for (int x = 0; x < currentWidth; x++)
            {
                int worldX = origin.X - _width / 2 + leftOffset + x;
                if (WorldGen.InWorld(worldX, worldY))
                    _tilesToClear.Add(new Point(worldX, worldY));
            }
        }

        // ----------------------------------------------------
        // 2. 收集：墙壁区域 (清除 & 放置) - Collect: Wall Areas (Clear & Place)
        // ----------------------------------------------------
        // 与挖掘区域体积一致 / Same volume as the tiles to clear
        foreach (Point p in _tilesToClear)
        {
            _wallsToClear.Add(p);

            // 墙壁放置区域：比挖掘区域略窄，确保边缘有物块 (The internal wall area is slightly narrower)
            int y = _minWorldY - p.Y;
            int triRow = y < _rectHeight ? -1 : y - _rectHeight;

            int currentWidth = (triRow == -1) ? _width : _width - triRow * 2;
            int leftEdgeX = origin.X - _width / 2 + (triRow == -1 ? 0 : (_width - currentWidth) / 2);
            int rightEdgeX = leftEdgeX + currentWidth - 1;

            // 内部木墙区域 (12宽, 左右各留 2 块物块空间)
            if (p.X > leftEdgeX + 1 && p.X < rightEdgeX - 1)
            {
                _wallsToPlace.Add(p);
            }

            // 三角形墙体区域外围 (14宽, 左右各留 1 块物块空间) - 用于放置石墙背景
            if (p.X > leftEdgeX && p.X < rightEdgeX && y >= _rectHeight)//三角形墙体区域外围
            {
                _wallsToPlacetri.Add(p);
            }
        }

        // ----------------------------------------------------
        // 3. 收集：下半部分 WoodBlock 墙体 (14宽环形矩形) - Collect: Rectangular Frame (14 wide)
        // ----------------------------------------------------
        // 矩形框架宽度改为 14，以在 16 宽的挖掘区域内居中
        const int rectFrameWidth = _width - 2; // 14
        const int rectXOffset = 1; // X 轴偏移 1 块，使 14 宽框架居中于 16 宽区域
        for (int y = 0; y < _rectHeight; y++) // y 从 0 到 6 (共 7 层)
        {
            for (int x = 0; x < rectFrameWidth; x++) // x 从 0 到 13 (共 14 宽)
            {
                // 环形矩形条件：顶部 (y=0), 底部 (y=6), 两侧 (x=0, x=13)
                bool shouldPlace = (y == 0) || (y == _rectHeight - 1) || (x == 0) || (x == rectFrameWidth - 1);

                // 【门洞排除逻辑】
                // 门占据世界 Y: _originPoint.Y - 1, _originPoint.Y - 2, _originPoint.Y - 3
                // 这些对应于相对 y 索引 5, 4, 3。
                if ((x == 0 || x == rectFrameWidth - 1) && y >= 3 && y <= 5)
                {
                    shouldPlace = false; // 在侧壁且位于门洞高度时，不放置物块
                }

                // 【平台排除逻辑】
                // 平台位于顶部 (y=0) 且 X 坐标在 [5, 8] 范围内 (共 4 块)
                // 对应 frame_x 索引 [5, 8]
                if (y == 0 && x >= 5 && x <= 8)
                {
                    shouldPlace = false; // 排除平台区域的物块
                }

                int worldX = origin.X - _width / 2 + rectXOffset + x;
                // y=0 是矩形顶部，y=_rectHeight-1 (即 6) 是底部
                int worldY = origin.Y - _rectHeight + 1 + y;

                if (shouldPlace && WorldGen.InWorld(worldX, worldY))
                    _tilesToPlaceRect.Add(new Point(worldX, worldY));
            }
        }

        // ----------------------------------------------------
        // 4. 收集：上半部分 Stone 框架 (三角形外墙, 2物块厚度) - Collect: Triangular Frame (2 block thickness)
        // ----------------------------------------------------
        for (int y = _rectHeight - 2; y < _height; y++) // y 从 5 到 14 (包含矩形上两行)
        {
            int triRow = y - _rectHeight; // 三角形内部行索引 (-2 ~ 8)
            int currentWidth = _width - triRow * 2;

            if (currentWidth <= 0) continue;

            int leftOffset = (_width - currentWidth) / 2;
            int worldY = _minWorldY - y; // 正确的世界 Y 坐标 (从下往上数)

            for (int x = 0; x < currentWidth; x++)
            {
                // 边缘条件：最左侧 (x=0) 或 最右侧 (x=currentWidth-1)
                bool isEdge = (x == 0) || (x == currentWidth - 1);

                // 顶部尖端只有 2 块或 1 块，确保它们都被放置
                if (currentWidth <= 2) isEdge = true;

                if (isEdge)
                {
                    int worldX = origin.X - _width / 2 + leftOffset + x;

                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        // ** 1. 放置内层框架（水平内侧一格，实现 2 物块厚度）** - 覆盖全范围 (y=5 到 14)
                        // Place inner frame (one tile horizontally inward, for 2-block thickness) - covers full range
                        if (currentWidth > 2) // 顶部尖端不需要内层
                        {
                            int innerX = worldX; // 用于计算内层 X 坐标

                            // 如果是左边缘 (x=0), 放置在 worldX + 1 (向右/内侧)
                            if (x == 0)
                            {
                                innerX = worldX + 1;
                            }
                            // 如果是右边缘 (x=currentWidth-1), 放置在 worldX - 1 (向左/内侧)
                            else
                            {
                                innerX = worldX - 1;
                            }

                            // 放置内层物块 (Place inner tile)
                            if (WorldGen.InWorld(innerX, worldY))
                                _tilesToPlaceTri.Add(new Point(innerX, worldY));
                        }

                        // ** 2. 放置外层框架 (Place outer frame) **
                        // 外层框架只放置在 y 结构索引 [6, 14] 范围内 (对应 triRow 从 -1 到 8)。
                        if (y >= _rectHeight - 1) // _rectHeight - 1 即为 6
                        {
                            _tilesToPlaceTri.Add(new Point(worldX, worldY));
                        }
                    }
                }
            }
        }

        // 初始化：从最底层开始 (Initialize: Start from the bottom layer)
        _currentWorldY = _minWorldY;
        _isProcessing = true;
        _isFinishedPlacingTiles = false; // 重置
        _furnitureState = 0; // 重置家具状态
        _updateTimer = 0;
    }

    public override void PostUpdateEverything()
    {
        if (!_isProcessing) return;
        _updateTimer++;

        // --- 阶段一：逐层放置物块/墙体 (Phase 1: Layer-by-layer tile/wall placement) ---
        // 控制节奏：每 5 帧处理一层 / Control pace: process one layer every 5 ticks
        if (!_isFinishedPlacingTiles && _updateTimer % 5 == 0)
        {
            if (_currentWorldY >= _maxWorldY)
            {
                ProcessLayer(_currentWorldY);
                _currentWorldY--; // 向上移动到下一层
                return;
            }
            else
            {
                // 所有层处理完毕
                _isFinishedPlacingTiles = true;
                _updateTimer = 0; // 重置计时器，用于下一阶段
                _furnitureState = 1; // 进入放置主要家具阶段
            }
        }

        // --- 阶段二：家具放置 (Phase 2: Furniture Placement) ---
        if (_isFinishedPlacingTiles)
        {
            if (_furnitureState == 1)
            {
                // 放置主要家具 (门/平台/桌子/椅子/床/梳妆台/书架)
                PlaceMainFurniture();
                _updateTimer = 0; // 重置计时器
                _furnitureState = 2; // 进入等待蜡烛延迟阶段
                return;
            }

            if (_furnitureState == 2)
            {
                // 等待蜡烛延迟 (5 帧)
                if (_updateTimer >= CANDLE_DELAY_FRAMES)
                {
                    // 放置蜡烛
                    PlaceCandles();
                    _furnitureState = 3; // 进入清理和结束阶段
                    // 不需要 return，让它立即进入清理阶段
                }
                else
                {
                    // 仍在等待延迟
                    return;
                }
            }

            // --- 阶段三：清理并结束 (Phase 3: Cleanup and Finalize) ---
            if (_furnitureState == 3)
            {
                // 清理并结束处理 (Cleanup and finalize process)
                _tilesToClear?.Clear();
                _wallsToClear?.Clear();
                _wallsToPlace?.Clear();
                _wallsToPlacetri?.Clear();
                _tilesToPlaceRect?.Clear();
                _tilesToPlaceTri?.Clear();
                _isProcessing = false;
                _isFinishedPlacingTiles = false;
                _furnitureState = 0;
            }
        }
    }

    // 处理指定 Y 层的所有操作
    // Processes all world modifications for the given Y layer
    private void ProcessLayer(int y)
    {
        // 1. 定义关键 Y 坐标 (Define key Y coordinates for the bottom wall rows)
        // 矩形内部墙体最低层 Y 坐标 (地板上方一格)
        int lowestRectWallY = _originPoint.Y - 1;
        // 三角形外部墙体最低层 Y 坐标 (矩形天花板墙层)
        int lowestTriWallY = _originPoint.Y - _rectHeight;

        // 1️⃣ 挖掘该层所有物块 (Dig all tiles in this layer)
        // 使用 ToList() 避免在迭代时修改集合时发生错误
        var layerTilesToClear = _tilesToClear.Where(p => p.Y == y).ToList();
        foreach (Point p in layerTilesToClear)
        {
            WorldGen.KillTile(p.X, p.Y, fail: false, effectOnly: false);
            _tilesToClear.Remove(p); // 移除已处理的点
                                     // 暂不发送同步信息，等待下一阶段统一发送
        }

        // 2️⃣ 清除该层墙 (Clear walls in this layer)
        var layerWallsToClear = _wallsToClear.Where(p => p.Y == y).ToList();
        foreach (Point p in layerWallsToClear)
        {
            WorldGen.KillWall(p.X, p.Y, fail: false);
            _wallsToClear.Remove(p);
            // 暂不发送同步信息，等待下一阶段统一发送
        }

        // 3️⃣ 放置该层墙（内部 wood）(Place internal wood walls)
        var layerWallsToPlace = _wallsToPlace.Where(p => p.Y == y).ToList();

        foreach (Point p in layerWallsToPlace)
        {
            ushort wallTypeToPlace = WallID.Wood;

            // 【修改点 A】: 如果是矩形墙体最低层, 则放置石墙 (Change lowest rectangular wall to Stone)
            if (y == lowestRectWallY)
            {
                wallTypeToPlace = WallID.Stone;
            }

            // 【修改点 B】: 确保三角形墙体最低层也是石墙 (Ensure lowest triangular wall is Stone)
            if (y == lowestTriWallY)
            {
                wallTypeToPlace = WallID.Stone;
            }

            // 【修改点 C】: 放置生命木墙作为中央装饰墙 (Place LivingWood Wall for central decoration)
            //              放置玻璃作为窗户
            // 仅在矩形房间的主体部分 (非地板和天花板墙层) 的中央 2列区域放置。
            if (p.Y != lowestTriWallY && p.Y != lowestRectWallY)
            {
                // 检查 X 坐标是否在中间4列区域 (_originPoint.X - 2 , _originPoint.X + 1,_originPoint.X - 4, _originPoint.X + 3 )
                if (p.X == _originPoint.X - 2 || p.X == _originPoint.X + 1
                        || p.X == _originPoint.X + 4 || p.X == _originPoint.X - 5)
                {
                    wallTypeToPlace = WallID.LivingWood; // 使用 LivingWood/Glass (此处用LivingWood)
                }
                //检查y坐标是否处于当前部分中央位置
                if (p.Y == lowestTriWallY - 1 || p.Y == lowestRectWallY - 2
                        || p.Y == lowestRectWallY - 3)
                {
                    // 检查 X 坐标是否在左右两列的中央区域 
                    // (_originPoint.X - 2 , _originPoint.X + 1,_originPoint.X - 4, _originPoint.X + 3 )
                    if (p.X == _originPoint.X - 3 || p.X == _originPoint.X - 4)
                    {
                        wallTypeToPlace = WallID.Glass; // 使用 LivingWood/Glass (此处用Glass)
                    }
                    if (p.X == _originPoint.X + 2 || p.X == _originPoint.X + 3)
                    {
                        wallTypeToPlace = WallID.Glass; // 使用 LivingWood/Glass (此处用Glass)
                    }
                }
                if (p.Y == lowestTriWallY - 2)
                {
                    // 检查 X 坐标是否在左右两列的中央区域 
                    // (_originPoint.X - 2 , _originPoint.X + 1,_originPoint.X - 4, _originPoint.X + 3 )
                    if (p.X == _originPoint.X - 3 || p.X == _originPoint.X - 4)
                    {
                        wallTypeToPlace = WallID.LivingWood; // 使用 LivingWood/Glass (此处用LivingWood)
                    }
                    if (p.X == _originPoint.X + 2 || p.X == _originPoint.X + 3)
                    {
                        wallTypeToPlace = WallID.LivingWood; // 使用 LivingWood/Glass (此处用LivingWood)
                    }
                }

            }

            // 避免覆盖特殊墙体 (Avoid overwriting special walls)
            Tile tile = Main.tile[p.X, p.Y];
            if (tile.WallType == WallID.None || Main.wallDungeon[tile.WallType])
            {
                WorldGen.PlaceWall(p.X, p.Y, wallTypeToPlace);
                WorldGen.SquareWallFrame(p.X, p.Y);
                // 同步墙壁放置 / Sync wall placement
                NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
            }
            _wallsToPlace.Remove(p);
        }

        // 3️⃣.5 放置该层墙（三角形外部stone）(Place external triangular stone walls)
        var layertriWallsToPlace = _wallsToPlacetri.Where(p => p.Y == y).ToList();

        foreach (Point p in layertriWallsToPlace)
        {
            ushort wallTypeToPlace = WallID.Stone; // 默认是石墙

            // 避免覆盖特殊墙体 (Avoid overwriting special walls)
            Tile tile = Main.tile[p.X, p.Y];
            if (tile.WallType == WallID.None || Main.wallDungeon[tile.WallType])
            {
                WorldGen.PlaceWall(p.X, p.Y, wallTypeToPlace);
                WorldGen.PlaceWall(_originPoint.X + _width / 2 - 1, _originPoint.Y - _rectHeight + 1, wallTypeToPlace);
                WorldGen.PlaceWall(_originPoint.X - _width / 2, _originPoint.Y - _rectHeight + 1, wallTypeToPlace);
                WorldGen.SquareWallFrame(p.X, p.Y);
                // 同步墙壁放置 / Sync wall placement
                NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
            }
            _wallsToPlacetri.Remove(p);
        }


        // 4️⃣ 放置该层 WoodBlock 结构 (墙体/框架) (Place WoodBlock structure (frame))
        // 下层 环形矩形结构 (WoodBlock)
        var layerRectTiles = _tilesToPlaceRect.Where(p => p.Y == y).ToList();
        foreach (Point p in layerRectTiles)
        {
            // 只有当没有物块时才放置，以保证门洞和平台洞是空的 (Only place if no tile, to ensure door/platform cavity is empty)
            if (!Main.tile[p.X, p.Y].HasTile)
            {
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.WoodBlock))
                {
                    WorldGen.SquareTileFrame(p.X, p.Y, true);
                    NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
                }
            }
            _tilesToPlaceRect.Remove(p);
        }

        // 上层三角形框架 (Stone - 石头) (Place Stone triangular frame)
        var layerTriTiles = _tilesToPlaceTri.Where(p => p.Y == y).ToList();
        foreach (Point p in layerTriTiles)
        {
            if (!Main.tile[p.X, p.Y].HasTile)
            {
                // 放置 Stone 物块 (Place Stone tile)
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.Stone))
                {
                    WorldGen.SquareTileFrame(p.X, p.Y, true);
                    NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
                }
            }
            _tilesToPlaceTri.Remove(p);
        }

        // 5️⃣ 地板替换 (GrayBrick)
        // 仅在最底层 (y = _originPoint.Y) 放置地板 / Place floor only on the bottom layer
        if (y == _originPoint.Y)
        {
            // 房屋宽度 16。外墙在 X-8 和 X+7。地板范围在 X-8 到 X+7 (共 16 块)
            int floorStart = _originPoint.X - _width / 2;     // X-8 (最左侧)
            int floorEnd = _originPoint.X + _width / 2 - 1; // X+7 (最右侧)

            for (int x = floorStart; x <= floorEnd; x++)
            {
                // 强制替换 WoodBlock 为 GrayBrick (Force replace WoodBlock with GrayBrick)
                ForcePlaceTile(x, _originPoint.Y, TileID.GrayBrick);
            }
        }
    }

    /// <summary>
    /// 强制放置物块,无视 WorldGen.PlaceTile 规则。用于替换地板。
    /// Forces a tile placement, ignoring WorldGen.PlaceTile rules. Used for floor replacement.
    /// </summary>
    private bool ForcePlaceTile(int x, int y, ushort type)
    {
        if (!WorldGen.InWorld(x, y)) return false; // 修正：返回 false 而不是 true
        Tile tile = Main.tile[x, y];

        // 强制设置物块类型 (Force set tile type)
        tile.TileType = type;
        tile.HasTile = true;
        tile.IsActuated = false; // 确保不会被致动 (Ensure it's not actuated)

        // 刷新物块帧 (Refresh tile frame)
        WorldGen.SquareTileFrame(x, y);

        // 多人同步 (Multiplayer synchronization)
        if (Main.netMode != NetmodeID.SinglePlayer)
            NetMessage.SendTileSquare(-1, x, y, 1);
        return true;
    }

    // 新增方法：放置除蜡烛外的所有主要家具 (New method: Place all main furniture except candles)
    private void PlaceMainFurniture()
    {
        int floor_Y = _originPoint.Y; // 地板的 Y 坐标

        // =======================================================================================================
        // ✅ 门放置 (Door Placement Fix) - 必须使用 WorldGen.PlaceTile
        // =======================================================================================================
        int doorY = floor_Y - 1; // 门底部 Y 坐标 (地板上方一格)

        // 右侧门 (Right Door): 位于右侧墙壁内侧 1 格 (X = _originPoint.X + 6)
        int rightDoorX = _originPoint.X + _width / 2 - 2;
        if (WorldGen.PlaceTile(rightDoorX, doorY, TileID.ClosedDoor, mute: true, forced: false, plr: -1, style: 0))
        {
            WorldGen.SquareTileFrame(rightDoorX, doorY);
            NetMessage.SendTileSquare(-1, rightDoorX, doorY - 2, 1, 3);
        }

        // 左侧门 (Left Door): 位于左侧墙壁内侧 1 格 (X = _originPoint.X - 7)
        int leftDoorX = _originPoint.X - _width / 2 + 1;
        if (WorldGen.PlaceTile(leftDoorX, doorY, TileID.ClosedDoor, mute: true, forced: false, plr: -1, style: 0))
        {
            WorldGen.SquareTileFrame(leftDoorX, doorY);
            NetMessage.SendTileSquare(-1, leftDoorX, doorY - 2, 1, 3);
        }

        // =======================================================================================================
        // ✅ 平台放置 (Platform Placement)
        // =======================================================================================================
        int platformY = _originPoint.Y - _rectHeight + 1; // 顶部 Y 坐标

        for (int x = _originPoint.X - 2; x <= _originPoint.X + 1; x++) // X 坐标范围 (共 4 块)
        {
            if (WorldGen.InWorld(x, platformY) && !Main.tile[x, platformY].HasTile)
            {
                // style: 0 对应 Wood Platform (木平台)
                if (WorldGen.PlaceTile(x, platformY, TileID.Platforms, mute: true, forced: false, plr: -1, style: 0))
                {
                    WorldGen.SquareTileFrame(x, platformY, true);
                    NetMessage.SendTileSquare(-1, x, platformY, 1);
                }
            }
        }

        // =======================================================================================================
        // ✅ 放置非蜡烛类家具 (Place non-Candle Furniture)
        // =======================================================================================================
        Player player = Main.player[Main.myPlayer];
        int FurnY = floor_Y - 1; // 放置在一楼地板上方一格
        int FurnY2 = floor_Y - _rectHeight; // 放置在二楼地板上方一格
        
        int tables;
        int chair;
        int chair1;
        int bed;
        int dresser;
        int bookcase;
        int book;
        
        // ----------------------------------------------------
        // ❌ 移除蜡烛相关的变量计算
        // ----------------------------------------------------

        if (player.direction == 1) // 玩家朝右/结构向右延伸
        {
            // 计算家具X坐标
            tables = _originPoint.X + 1;
            chair = tables - 2;
            chair1 = tables + 2;
            bed = _originPoint.X + 2;
            dresser = tables - 5;
            // ❌ 移除 candle 的计算
            // ❌ 移除 candle2 的计算
            bookcase = tables - 5;
            book = dresser + 1 - 1; // 重新计算 bookX (原 candle2 - 1)
        }
        else // 玩家朝左/结构向左延伸
        {
            // 计算家具X坐标
            tables = _originPoint.X - 2; 
            chair = tables + 2;
            chair1 = tables - 2;
            bed = _originPoint.X - 4;
            dresser = tables + 5;
            // ❌ 移除 candle 的计算
            // ❌ 移除 candle2 的计算
            bookcase = tables + 5;
            book = dresser - 1 + 1; // 重新计算 bookX (原 candle2 + 1)
        }

        // 放置桌子 (Table - 3x2)
        WorldGen.PlaceObject(tables, FurnY, TileID.Tables);

        // 放置椅子 (Chair - 1x2) - 靠近桌子中央侧
        WorldGen.PlaceObject(chair, FurnY, TileID.Chairs, direction: player.direction == 1 ? 1 : 0);

        // 放置椅子 (Chair - 1x2) - 远端侧
        WorldGen.PlaceObject(chair1, FurnY, TileID.Chairs, direction: player.direction == 1 ? 0 : 1);

        // 放置床 (Bed - 4x2) - 放置在二楼
        WorldGen.PlaceObject(bed, FurnY2, TileID.Beds, direction: player.direction == 1 ? 0 : 1);

        // 放置梳妆台 (Dresser - 3x2) - 放置在二楼
        WorldGen.PlaceObject(dresser, FurnY2, TileID.Dressers);

        // 放置书架 (Bookcase - 3x4)
        WorldGen.PlaceObject(bookcase, FurnY, TileID.Bookcases);

        // 放置二楼书 (Book - 1x2)
        WorldGen.PlaceObject(book, FurnY2 - 2, TileID.Books);

        // ----------------------------------------------------
        // ❌ 移除蜡烛的 WorldGen.PlaceObject 调用
        // ----------------------------------------------------
    }

    // 新增方法：单独放置蜡烛 (New method: Place only candles)
    private void PlaceCandles()
    {
        Player player = Main.player[Main.myPlayer];
        int floor_Y = _originPoint.Y; // 地板的 Y 坐标
        int FurnY = floor_Y - 1; // 放置在一楼地板上方一格
        int FurnY2 = floor_Y - _rectHeight; // 放置在二楼地板上方一格

        int tables;
        int dresser;
        int candle;
        int candle2;
        
        // 必须重新计算坐标，确保和 PlaceMainFurniture 中使用的位置一致
        if (player.direction == 1)
        {
            tables = _originPoint.X + 1;
            dresser = tables - 5;
            candle = tables;
            candle2 = dresser + 1;
        }
        else
        {
            tables = _originPoint.X - 2; 
            dresser = tables + 5;
            candle = tables;
            candle2 = dresser - 1;
        }

        // 放置一楼蜡烛 (Candle - 1x2) - 放置在桌子上方
        // 这里实现了您要求的 5 帧延迟效果
        WorldGen.PlaceObject(candle, FurnY - 2, TileID.Candles);

        // 放置二楼蜡烛 (Candle - 1x2) - 放置在梳妆台上方
        WorldGen.PlaceObject(candle2, FurnY2 - 2, TileID.Candles);
    }
}