using System.Collections.Generic;
using System.Linq; // 引入 Linq
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Net; // For NetMessage

// 这是一个 ModSystem，用于在游戏中异步生成中型房屋结构（三层小洋房）。
public class MedHsFactory : ModSystem
{
    // --- 结构参数 (House Structure Parameters) ---
    private int _updateTimer = 0;
    private Point _originPoint; // 房屋的中心底部坐标 (地面中心点) / The center bottom coordinate (ground level)
    private const int _width = 18; // 总宽度 (18)
    private const int _height = 21; // 总高度 (21)
    private const int _rectHeight = 7; // 单个矩形房间高度 (Ground or Middle)
    private const int _totalRectHeight = 13; // 两个矩形房间总高度 (Y 轴索引 0 到 12，共 13 层)
    private const int _triHeight = 9; // 上半三角高度 (21 - 14 = 7)

    // --- 新增: 玩家朝向和结构偏移常量 ---
    private int _playerDirection = 1; // 1: 右 (Left-aligned middle room), -1: 左 (Right-aligned middle room)
    private const int REDUCED_FRAME_WIDTH = 12; // 缩小后的中间层框架宽度
    private const int FULL_FRAME_WIDTH = _width - 2; // 16 宽框架
    private const int FULL_FRAME_X_OFFSET = 1; // 16 宽框架的 X 偏移

    // --- 进度控制 (Progress Control) ---
    private int _currentWorldY; // 当前正在处理的世界 Y 坐标
    private int _minWorldY;     // 底部 Y 坐标 (_origin.Y)
    private int _maxWorldY;     // 顶部 Y 坐标 (_origin.Y - height + 1)
    private bool _isProcessing = false;
    private bool _isFinishedPlacingTiles = false; // 标记物块和墙体是否放置完毕

    // --- 数据集 (Data Collections) ---
    private HashSet<Point> _tilesToClear;   // 要清除的物块区域
    private HashSet<Point> _wallsToClear;   // 要清除的墙壁区域
    private HashSet<Point> _wallsToPlace;   // 要放置的墙壁区域 (内部 木墙/窗户)
    private HashSet<Point> _wallsToPlacetri;    // 要放置的墙壁区域 (三角形外围 石墙)
    private HashSet<Point> _tilesToPlaceRect; // 要放置的下半部分物块 (两层矩形框架)
    private HashSet<Point> _tilesToPlaceTri;    // 要放置的上半部分物块 (三角框架)

    // --- 家具放置延迟 (Furniture Placement Delay) ---
    private int _furnitureState = 0; // 0: 未开始, 1: 放置主要家具/门/平台, 2: 等待蜡烛延迟, 3: 放置蜡烛
    private const int CANDLE_DELAY_FRAMES = 5;

    // --- 收集逻辑 (StartGenerating) ---
    public void StartGenerating(Point origin)
    {
        if (_isProcessing) return;

        _originPoint = origin;
        _minWorldY = origin.Y;
        _maxWorldY = origin.Y - _height + 1; // 顶部 Y 坐标

        // --- 捕获玩家朝向 (Capture Player Direction) ---
        _playerDirection = Main.player[Main.myPlayer].direction; // 捕获当前玩家方向

        // 初始化集合 (Initialize collections)
        _tilesToClear = new HashSet<Point>();
        _wallsToClear = new HashSet<Point>();
        _wallsToPlace = new HashSet<Point>();
        _wallsToPlacetri = new HashSet<Point>();
        _tilesToPlaceRect = new HashSet<Point>();
        _tilesToPlaceTri = new HashSet<Point>();

        // ----------------------------------------------------
        // 1. 收集：挖掘区域 (18x14矩形基座 & 18x7三角形屋顶) - Collect: Area to Dig
        // ----------------------------------------------------
        for (int y = 0; y < _height; y++) // y=1 是最底层物块上方一格
        {
            int worldY = _minWorldY - y; 
            int currentWidth;
            int leftOffset;

            if (y <= _totalRectHeight) // y 从 0 到 13 (两层矩形基座)
            {
                // ▼ 下半部分：18×14 矩形 (完整宽度)
                currentWidth = _width;
                leftOffset = 0;
            }
            else // y 从 14 到 20 (三角形屋顶)
            {
                // ▲ 上半部分：18×7 三角形
                int triRow = y - _totalRectHeight; // 1 ~ 7
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
        foreach (Point p in _tilesToClear)
        {
            _wallsToClear.Add(p);

            int y = _minWorldY - p.Y;
            int currentWidth = (y <= _totalRectHeight) 
                                 ? _width 
                                 : _width - (y - _totalRectHeight) * 2;
                                 
            if (currentWidth <= 0) continue;

            int leftEdgeX = origin.X - _width / 2 + (y <= _totalRectHeight ? 0 : (_width - currentWidth) / 2);
            int rightEdgeX = leftEdgeX + currentWidth - 1;

            // 内部木墙区域 (比挖掘区域窄 4 块，左右各留 2 块物块空间)
            if (p.X > leftEdgeX + 1 && p.X < rightEdgeX - 1)
            {
                _wallsToPlace.Add(p);
            }

            // 三角形墙体区域外围 (比挖掘区域窄 2 块，左右各留 1 块物块空间) - 用于放置石墙背景
            if (p.X > leftEdgeX && p.X < rightEdgeX && y > _totalRectHeight) // 仅在三角形部分放置
            {
                _wallsToPlacetri.Add(p);
            }
        }

        // ----------------------------------------------------
        // 3. 收集：WoodBlock 框架 (区分 Ground (16 宽) 和 Middle/Top (12 宽)) - Collect: Rectangular Frame
        // ----------------------------------------------------
        
        for (int y = 0; y <= _totalRectHeight; y++) // y 从 0 (顶) 到 12 (底)
        {
            int currentFrameWidth;
            int currentXOffset;

            // 确定当前层的框架宽度和 X 偏移
            // 顶层地板 (y=0) 和 中间层墙体 (y=1到5) 采用 12 宽框架，并根据玩家朝向对齐。
            bool isReducedFrame = (y >= 0 && y <= 5); 

            if (isReducedFrame)
            {
                currentFrameWidth = REDUCED_FRAME_WIDTH; // 12 wide
                
                // 玩家面向右 (Align Left): X 偏移为 1 (与 16 宽框架左对齐)
                if (_playerDirection == 1) 
                {
                    currentXOffset = FULL_FRAME_X_OFFSET; // 1
                }
                // 玩家面向左 (Align Right): X 偏移为 1 + (16-12) = 5 (与 16 宽框架右对齐)
                else 
                {
                    currentXOffset = FULL_FRAME_X_OFFSET + (FULL_FRAME_WIDTH - REDUCED_FRAME_WIDTH); // 5
                }
            }
            else
            {
                // 中层地板 (y=6), 底层墙体 (y=7 到 11), 底部地板 (y=12) 保持 16 宽居中
                currentFrameWidth = FULL_FRAME_WIDTH; // 16 wide
                currentXOffset = FULL_FRAME_X_OFFSET; // 1 offset
            }

            for (int x = 0; x < currentFrameWidth; x++) // x 从 0 到 currentFrameWidth - 1
            {
                // 核心结构：顶层地板 (y=0), 中层地板 (y=6), 底部地板 (y=12), 两侧墙壁 (x=0, x=currentFrameWidth - 1)
                bool shouldPlace = (y == 0) || // 顶层地板 (Floor of the attic/Base of the triangle)
                                   (y == _rectHeight - 1) || // 中层地板/天花板 (Ground floor ceiling)
                                   (y == _totalRectHeight - 1) || // 底部地板 (Ground floor/base)
                                   (x == 0) || (x == currentFrameWidth - 1); // 两侧墙壁

                // 【门洞排除逻辑】 (未修改)
                // 中间层门洞 (y=3, 4, 5) - 仅在 12 宽房间墙体应用，只留一侧
                if (isReducedFrame && y >= 3 && y <= 5)
                {
                    if (y >= 3 && y <= 5)
                    {
                        // 玩家面向右 (Align Left): 门在右边墙 (x = currentFrameWidth - 1, 对应内侧)
                        if (_playerDirection == 1 && x == currentFrameWidth - 1)
                        {
                            shouldPlace = false; // 为门留出空间
                        }
                        // 玩家面向左 (Align Right): 门在左边墙 (x = 0, 对应内侧)
                        else if (_playerDirection == -1 && x == 0)
                        {
                            shouldPlace = false; // 为门留出空间
                        }
                    }
                }
                // 底层门洞 (y=9, 10, 11) - 仅在底层墙体应用 (底层一直是 16 宽)，两侧都留门
                else if (!isReducedFrame && y >= 7 && y <= 11)
                {
                    // 这里 currentFrameWidth 总是 16
                    // 门洞在两侧墙壁 (x=0 和 x=currentFrameWidth - 1)
                    if ((x == 0 || x == currentFrameWidth - 1) && y >= 9 && y <= 11)
                    {
                        shouldPlace = false;
                    }
                }

                // 【平台/楼梯排除逻辑】(已修改: 确保中层平台洞与顶层对齐)
                // 顶层平台洞 (y=0) - 12 宽框架内居中 (x=4到7)
                if (y == 0 && currentFrameWidth == REDUCED_FRAME_WIDTH && x >= 4 && x <= 7)
                {
                    shouldPlace = false;
                }
                
                // 中层平台洞 (y=_rectHeight-1 = 6) - 16 宽框架，动态对齐
                if (y == _rectHeight - 1 && currentFrameWidth == FULL_FRAME_WIDTH)
                {
                    bool isHoleBlock = false;
                    // 如果 12 宽框架左对齐 (玩家朝右), 对应 16 宽框架的 x=4 到 x=7
                    if (_playerDirection == 1) 
                    {
                        if (x >= 4 && x <= 7) isHoleBlock = true;
                    }
                    // 如果 12 宽框架右对齐 (玩家朝左), 对应 16 宽框架的 x=8 到 x=11
                    else 
                    {
                        if (x >= 8 && x <= 11) isHoleBlock = true;
                    }
                    
                    if (isHoleBlock)
                    {
                        shouldPlace = false;
                    }
                }

                int worldX = origin.X - _width / 2 + currentXOffset + x;
                int worldY = origin.Y - _totalRectHeight + 1 + y;

                if (shouldPlace && WorldGen.InWorld(worldX, worldY))
                    _tilesToPlaceRect.Add(new Point(worldX, worldY));
            }

            // --- [新增逻辑] 顶层地板左右边缘各往上放置一个木块，以封闭上层矩形房间顶部的侧边 ---
            if (y == 0 && currentFrameWidth == REDUCED_FRAME_WIDTH)
            {
                // 顶层地板的 World Y 坐标: origin.Y - _totalRectHeight + 1
                int floorY = origin.Y - _totalRectHeight + 1; 

                // 放置位置的 World Y 坐标 (地板上方一格): origin.Y - _totalRectHeight
                int topWallY = floorY - 1; 

                // 左边缘 X: origin.X - _width / 2 + currentXOffset
                int leftEdgeX = origin.X - _width / 2 + currentXOffset;
                
                // 右边缘 X: origin.X - _width / 2 + currentXOffset + currentFrameWidth - 1
                int rightEdgeX = origin.X - _width / 2 + currentXOffset + currentFrameWidth - 1;

                // 放置左侧物块 (12宽框架左侧最外侧物块, 位于屋顶斜坡起始点下方)
                if (WorldGen.InWorld(leftEdgeX, topWallY))
                    _tilesToPlaceRect.Add(new Point(leftEdgeX, topWallY));

                // 放置右侧物块 (12宽框架右侧最外侧物块, 位于屋顶斜坡起始点下方)
                if (WorldGen.InWorld(rightEdgeX, topWallY))
                    _tilesToPlaceRect.Add(new Point(rightEdgeX, topWallY));
            }
        }

        // ----------------------------------------------------
        // 4. 收集：上半部分 Stone 框架 (三角形外墙, 2物块厚度) - Collect: Triangular Frame
        // 
        // 基座宽度调整为 12 格，并与中层矩形房间对齐。
        // ----------------------------------------------------
        
        int triBaseWidth = REDUCED_FRAME_WIDTH; // 12 格宽基座
        int triBaseLeftOffset; 
        
        // 计算 12 格宽基座相对于 18 格世界的左侧偏移
        if (_playerDirection == 1) // 玩家朝右 (Left-aligned): 偏移 1
        {
            triBaseLeftOffset = FULL_FRAME_X_OFFSET; 
        }
        else // 玩家朝左 (Right-aligned): 偏移 5
        {
            triBaseLeftOffset = FULL_FRAME_X_OFFSET + (FULL_FRAME_WIDTH - REDUCED_FRAME_WIDTH); 
        }

        // 循环只从 y = _totalRectHeight (13) 开始，即三角形主体部分
        for (int y = _totalRectHeight - 2; y < _height + 1; y++) 
        {
            int triRow = y - _totalRectHeight; // 0 ~ 7
            
            // 1. 计算当前行的宽度和偏移 (基于 12 宽底座)
            int currentWidth = triBaseWidth - triRow * 2; // 12, 10, 8, 6, 4, 2, 0

            if (currentWidth <= 0) continue;
            
            // 当前行相对于 12 格底座的左侧偏移 (用于居中)
            int triInnerOffset = (triBaseWidth - currentWidth) / 2;
            
            // 当前行相对于整个 18 格世界的左侧偏移
            int fullWorldLeftOffset = triBaseLeftOffset + triInnerOffset;
            
            // 2. 遍历并放置物块
            for (int x = 0; x < currentWidth; x++) // x 从 0 (左边缘) 到 currentWidth - 1 (右边缘)
            {
                // 内部坐标计算
                int worldX = origin.X - _width / 2 + fullWorldLeftOffset + x;
                int worldY = _minWorldY - y - 2; 

                // 确定边缘 (用于 2 物块厚度)
                bool isEdge = (x == 0) || (x == currentWidth - 1);
                if (currentWidth <= 2) isEdge = true; // 确保尖端被放置
                
                if (isEdge)
                {
                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        // 1. 放置内层框架（水平内侧一格，实现 2 物块厚度）- 仅在宽度 > 2 时
                        if (currentWidth > 2) 
                        {
                            // 相对世界坐标的内侧 X 
                            int innerX = worldX + (x == 0 ? 1 : -1); 
                            if (WorldGen.InWorld(innerX, worldY))
                                _tilesToPlaceTri.Add(new Point(innerX, worldY));
                        }

                        // 2. 放置外层框架 (这是边缘)
                        _tilesToPlaceTri.Add(new Point(worldX, worldY));
                        // == 新增：左右边缘向下延申一块 ==
                            int downX = worldX;
                            int downY = worldY + 1;
                            if (WorldGen.InWorld(downX, downY))
                                _tilesToPlaceTri.Add(new Point(downX, downY));
                    }
                }
            }
        }

        // 初始化：从最底层开始
        _currentWorldY = _minWorldY;
        _isProcessing = true;
        _isFinishedPlacingTiles = false; 
        _furnitureState = 0; 
        _updateTimer = 0;
    }

    public override void PostUpdateEverything()
    {
        if (!_isProcessing) return;
        _updateTimer++;

        // --- 阶段一：逐层放置物块/墙体 (Phase 1: Layer-by-layer tile/wall placement) ---
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
                _isFinishedPlacingTiles = true;
                _updateTimer = 0; 
                _furnitureState = 1; // 进入放置主要家具阶段
            }
        }

        // --- 阶段二：家具放置 (Phase 2: Furniture Placement) ---
        if (_isFinishedPlacingTiles)
        {
            if (_furnitureState == 1)
            {
                PlaceMainFurniture();
                _updateTimer = 0; 
                _furnitureState = 2; // 进入等待蜡烛延迟阶段
                return;
            }

            if (_furnitureState == 2)
            {
                // 等待蜡烛延迟 (5 帧)
                if (_updateTimer >= CANDLE_DELAY_FRAMES)
                {
                    PlaceCandles();
                    _furnitureState = 3; // 进入清理和结束阶段
                }
                else
                {
                    return;
                }
            }

            // --- 阶段三：清理并结束 (Phase 3: Cleanup and Finalize) ---
            if (_furnitureState == 3)
            {
                // 清理并结束处理
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
    private void ProcessLayer(int y)
    {
        // 1. 定义关键 Y 坐标 (天花板/地板层)
        int groundFloorWallY = _originPoint.Y - 1; // 底层地板上方一格墙体
        int middleFloorCeilingY = _originPoint.Y - _rectHeight; // 中层天花板墙体 (Y-7)
        int topFloorCeilingY = _originPoint.Y - _totalRectHeight; // 顶层天花板墙体 (Y-13)

        // 1️⃣ 挖掘该层所有物块
        var layerTilesToClear = _tilesToClear.Where(p => p.Y == y).ToList();
        foreach (Point p in layerTilesToClear)
        {
            WorldGen.KillTile(p.X, p.Y, fail: false, effectOnly: false);
            _tilesToClear.Remove(p);
        }

        // 2️⃣ 清除该层墙
        var layerWallsToClear = _wallsToClear.Where(p => p.Y == y).ToList();
        foreach (Point p in layerWallsToClear)
        {
            WorldGen.KillWall(p.X, p.Y, fail: false);
            _wallsToClear.Remove(p);
        }

        // 3️⃣ 放置该层墙（内部 wood/stone/glass）
        var layerWallsToPlace = _wallsToPlace.Where(p => p.Y == y).ToList();
        foreach (Point p in layerWallsToPlace)
        {
            ushort wallTypeToPlace = WallID.Wood;

            // 地板/天花板层墙体统一用 Stone
            if (y == groundFloorWallY || y == middleFloorCeilingY || y == topFloorCeilingY)
            {
                wallTypeToPlace = WallID.Stone;
            }
            // 矩形房间内部墙体 (木墙 + 窗户/装饰)
            else if (y > topFloorCeilingY && y < _originPoint.Y) 
            {
                wallTypeToPlace = WallID.Wood; // Default wood wall
                
                // 窗口中心 Y 坐标 (Ground: Y-4, Middle: Y-11)
                int floorYCenter = 0; 
                if (y >= _originPoint.Y - 6 && y <= _originPoint.Y - 2) floorYCenter = _originPoint.Y - 4; // Ground floor
                if (y >= _originPoint.Y - 13 && y <= _originPoint.Y - 9) floorYCenter = _originPoint.Y - 11; // Middle floor
                
                // 如果是房间内部墙体 (有 LivingWood/Glass 装饰)
                if (floorYCenter != 0)
                {
                    // 左右窗口区域 (X 坐标)
                    bool isWindowArea = (p.X >= _originPoint.X - 4 && p.X <= _originPoint.X - 3) || 
                                           (p.X >= _originPoint.X + 2 && p.X <= _originPoint.X + 3);

                    if (isWindowArea)
                    {
                        // 窗口中心 Y: Glass 
                        if (y == floorYCenter) 
                        {
                            wallTypeToPlace = WallID.Glass;
                        }
                        // 窗口上下 Y: LivingWood 边框
                        else if (y == floorYCenter - 1 || y == floorYCenter + 1) 
                        {
                            wallTypeToPlace = WallID.LivingWood;
                        }
                    }
                    
                    // 中央装饰柱 LivingWood
                    bool isCentralDeco = p.X == _originPoint.X - 2 || p.X == _originPoint.X + 1;
                    if(isCentralDeco)
                    {
                        wallTypeToPlace = WallID.LivingWood;
                    }
                }
            }


            Tile tile = Main.tile[p.X, p.Y];
            if (tile.WallType == WallID.None || Main.wallDungeon[tile.WallType])
            {
                WorldGen.PlaceWall(p.X, p.Y, wallTypeToPlace);
                WorldGen.SquareWallFrame(p.X, p.Y);
                NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
            }
            _wallsToPlace.Remove(p);
        }

        // 3️⃣.5 放置该层墙（三角形外部stone）
        var layertriWallsToPlace = _wallsToPlacetri.Where(p => p.Y == y).ToList();
        foreach (Point p in layertriWallsToPlace)
        {
            ushort wallTypeToPlace = WallID.Stone; 
            Tile tile = Main.tile[p.X, p.Y];
            if (tile.WallType == WallID.None || Main.wallDungeon[tile.WallType])
            {
                WorldGen.PlaceWall(p.X, p.Y, wallTypeToPlace);
                WorldGen.SquareWallFrame(p.X, p.Y);
                NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
            }
            _wallsToPlacetri.Remove(p);
        }


        // 4️⃣ 放置该层 WoodBlock 结构 (矩形框架)
        var layerRectTiles = _tilesToPlaceRect.Where(p => p.Y == y).ToList();
        foreach (Point p in layerRectTiles)
        {
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

        // 上层三角形框架 (Stone - 石头)
        var layerTriTiles = _tilesToPlaceTri.Where(p => p.Y == y).ToList();
        foreach (Point p in layerTriTiles)
        {
            if (!Main.tile[p.X, p.Y].HasTile)
            {
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.Stone))
                {
                    WorldGen.SquareTileFrame(p.X, p.Y, true);
                    NetMessage.SendTileSquare(-1, p.X, p.Y, 1, 1);
                }
            }
            _tilesToPlaceTri.Remove(p);
        }

        // 5️⃣ 地板替换 (GrayBrick)
        if (y == _originPoint.Y)
        {
            int floorStart = _originPoint.X - _width / 2; 
            int floorEnd = _originPoint.X + _width / 2 - 1; 

            for (int x = floorStart; x <= floorEnd; x++)
            {
                ForcePlaceTile(x, _originPoint.Y, TileID.GrayBrick);
            }
        }
    }

    /// <summary>
    /// 强制放置物块,无视 WorldGen.PlaceTile 规则。
    /// </summary>
    private bool ForcePlaceTile(int x, int y, ushort type)
    {
        if (!WorldGen.InWorld(x, y)) return false; 
        Tile tile = Main.tile[x, y];

        tile.TileType = type;
        tile.HasTile = true;
        tile.IsActuated = false; 

        WorldGen.SquareTileFrame(x, y);

        if (Main.netMode != NetmodeID.SinglePlayer)
            NetMessage.SendTileSquare(-1, x, y, 1);
        return true;
    }

    // 放置除蜡烛外的所有主要家具
    private void PlaceMainFurniture()
    {
        int floor_Y = _originPoint.Y; // 地面层底部 Y 坐标

        // 新的家具放置 Y 坐标
        int FurnY_Ground = floor_Y - 1; // 底层房间地板上方一格
        int FurnY_Middle = floor_Y - _rectHeight; // 中层房间地板上方一格 (Y-8)
        int FurnY_Top = floor_Y - _totalRectHeight; // 顶层房间地板 (Y-14)

        // =======================================================================================================
        // ✅ 门放置 (Door Placement)
        // =======================================================================================================
        
        // --- 地面层门 (Ground Floor Doors - 16 wide frame, centered) ---
        // 左墙 X: _originPoint.X - 8, 右墙 X: _originPoint.X + 7
        int groundRightDoorX = _originPoint.X + _width / 2 - 2; 
        int groundLeftDoorX = _originPoint.X - _width / 2 + 1; 
        
        // --- 中间层门 (Middle Floor Doors - 12 wide frame, dynamically aligned) ---
        int middleLeftDoorX, middleRightDoorX;
        
        if (_playerDirection == 1) // 面向右 (Left-aligned): 左墙 X-8, 右墙 X+3
        {
            middleLeftDoorX = groundLeftDoorX; // X - 8
            middleRightDoorX = _originPoint.X + 3; 
        }
        else // 面向左 (Right-aligned): 左墙 X-4, 右墙 X+7
        {
            middleLeftDoorX = _originPoint.X - 4;
            middleRightDoorX = groundRightDoorX; // X + 7
        }


        // 底层门 (使用 16 宽框架的边缘) - 两侧都放置
        if (WorldGen.PlaceTile(groundRightDoorX, FurnY_Ground, TileID.ClosedDoor, mute: true, forced: false, plr: -1, style: 0))
            NetMessage.SendTileSquare(-1, groundRightDoorX, FurnY_Ground - 2, 1, 3);
        if (WorldGen.PlaceTile(groundLeftDoorX, FurnY_Ground, TileID.ClosedDoor, mute: true, forced: false, plr: -1, style: 0))
            NetMessage.SendTileSquare(-1, groundLeftDoorX, FurnY_Ground - 2, 1, 3);
            
        // 中层门 (根据玩家朝向，只放置在 12 宽房间的内侧边缘)
        if (_playerDirection == 1) // Align Left: 门在右侧内墙
        {
            if (WorldGen.PlaceTile(middleRightDoorX, FurnY_Middle, TileID.ClosedDoor, mute: true, forced: false, plr: -1, style: 0))
                NetMessage.SendTileSquare(-1, middleRightDoorX, FurnY_Middle - 2, 1, 3);
        }
        else // Align Right: 门在左侧内墙
        {
            if (WorldGen.PlaceTile(middleLeftDoorX, FurnY_Middle, TileID.ClosedDoor, mute: true, forced: false, plr: -1, style: 0))
                NetMessage.SendTileSquare(-1, middleLeftDoorX, FurnY_Middle - 2, 1, 3);
        }


        // =======================================================================================================
        // ✅ 平台放置 (Platform Placement) - 已修改为动态对齐
        // =======================================================================================================
        int platformY_GroundCeiling = floor_Y - _rectHeight + 1; // 中层地板 (Y-6)
        int platformY_MiddleCeiling = floor_Y - _totalRectHeight + 1; // 顶层地板 (Y-13)

        int platformXStart, platformXEnd;
        
        // 根据玩家朝向确定平台放置的 X 范围，使其与 12 宽框架中的居中开口对齐。
        if (_playerDirection == 1) // 玩家朝右 (Left-aligned 12 wide frame): 世界 X-4 到 X-1
        {
            platformXStart = _originPoint.X - 4;
            platformXEnd = _originPoint.X - 1;
        }
        else // 玩家朝左 (Right-aligned 12 wide frame): 世界 X 到 X+3
        {
            platformXStart = _originPoint.X;
            platformXEnd = _originPoint.X + 3;
        }

        for (int x = platformXStart; x <= platformXEnd; x++) // 平台为 4 块宽
        {
            // 中层地板平台
            if (WorldGen.InWorld(x, platformY_GroundCeiling) && !Main.tile[x, platformY_GroundCeiling].HasTile)
            {
                // 确保这里使用的是 Wood Platform (Style 0)
                if (WorldGen.PlaceTile(x, platformY_GroundCeiling, TileID.Platforms, mute: true, forced: false, plr: -1, style: 0))
                    NetMessage.SendTileSquare(-1, x, platformY_GroundCeiling, 1);
            }
            // 顶层地板平台 (屋顶入口)
            if (WorldGen.InWorld(x, platformY_MiddleCeiling) && !Main.tile[x, platformY_MiddleCeiling].HasTile)
            {
                if (WorldGen.PlaceTile(x, platformY_MiddleCeiling, TileID.Platforms, mute: true, forced: false, plr: -1, style: 0))
                    NetMessage.SendTileSquare(-1, x, platformY_MiddleCeiling, 1);
            }
        }

        // =======================================================================================================
        // ✅ 放置非蜡烛类家具 (Place non-Candle Furniture) (未修改)
        // =======================================================================================================
        Player player = Main.player[Main.myPlayer];
        
        // 坐标计算逻辑不变 (使用 player.direction)
        int tables, chair, chair1, bed, dresser, bookcase, book;
        
        if (player.direction == 1) // 玩家朝右
        {
            tables = _originPoint.X + 1;
            chair = tables - 2;
            chair1 = tables + 2;
            bed = _originPoint.X + 2; // 靠右
            dresser = tables - 5; // 靠左
            bookcase = tables - 5;
            book = dresser + 1 - 1; 
        }
        else // 玩家朝左
        {
            tables = _originPoint.X - 2; 
            chair = tables + 2;
            chair1 = tables - 2;
            bed = _originPoint.X - 4; // 靠左
            dresser = tables + 5; // 靠右
            bookcase = tables + 5;
            book = dresser - 1 + 1; 
        }

        // --- 地面层 (Living Area) ---
        // 放置桌子 (Table - 3x2)
        WorldGen.PlaceObject(tables, FurnY_Ground, TileID.Tables);

        // 放置椅子 (Chair - 1x2) - 靠近桌子中央侧
        WorldGen.PlaceObject(chair, FurnY_Ground, TileID.Chairs, direction: player.direction == 1 ? 1 : 0);

        // 放置椅子 (Chair - 1x2) - 远端侧
        WorldGen.PlaceObject(chair1, FurnY_Ground, TileID.Chairs, direction: player.direction == 1 ? 0 : 1);

        // 放置书架 (Bookcase - 3x4)
        WorldGen.PlaceObject(bookcase, FurnY_Ground, TileID.Bookcases);
        
        // --- 中间层 (Bedroom) ---
        // 放置床 (Bed - 4x2)
        WorldGen.PlaceObject(bed, FurnY_Middle, TileID.Beds, direction: player.direction == 1 ? 0 : 1);

        // 放置梳妆台 (Dresser - 3x2) 
        WorldGen.PlaceObject(dresser, FurnY_Middle, TileID.Dressers);

        // 放置书 (Book - 1x2)
        WorldGen.PlaceObject(book, FurnY_Middle - 2, TileID.Books);
    }

    // 单独放置蜡烛
    private void PlaceCandles()
    {
        Player player = Main.player[Main.myPlayer];
        int floor_Y = _originPoint.Y;
        int FurnY_Ground = floor_Y - 1; 
        int FurnY_Middle = floor_Y - _rectHeight - 1; 

        int tables, dresser, candle, candle2;
        
        if (player.direction == 1)
        {
            tables = _originPoint.X + 1;
            dresser = tables - 5;
            candle = tables; // 桌子中心
            candle2 = dresser + 1; // 梳妆台中心
        }
        else
        {
            tables = _originPoint.X - 2; 
            dresser = tables + 5;
            candle = tables;
            candle2 = dresser - 1;
        }

        // 放置底层蜡烛 (桌子上方)
        WorldGen.PlaceObject(candle, FurnY_Ground - 2, TileID.Candles);

        // 放置中层蜡烛 (梳妆台上方)
        WorldGen.PlaceObject(candle2, FurnY_Middle - 2, TileID.Candles);
    }
}