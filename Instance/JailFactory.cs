// DelayedStructureSystem.cs
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

public class JailFactory : ModSystem
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

    public void StartGenerating(Point origin)
    {
        if (_isProcessing) return;
        _origin = origin;

        int width = 6;
        int height_dn = 4;
        int height_up = 6;
        int totalHeight = height_dn + height_up;

        _minY = origin.Y;
        _maxY = origin.Y - totalHeight + 1;

        _tilesToDig = new HashSet<Point>();
        _wallsToKill = new HashSet<Point>();
        _wallsToPlace = new HashSet<Point>();
        _tilesToPlace_dn = new HashSet<Point>();
        _tilesToPlace_up = new HashSet<Point>();

        // 收集：挖掘区域 (6x10)
        for (int y = 0; y < totalHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int worldX = origin.X - width / 2 + x;
                int worldY = _maxY + y;
                if (WorldGen.InWorld(worldX, worldY))
                    _tilesToDig.Add(new Point(worldX, worldY));
            }
        }

        // 收集：清除墙区域 (内部 4x8)
        int wallWidth = width - 2;
        int wallHeight = totalHeight - 2;
        for (int y = 0; y < wallHeight; y++)
        {
            for (int x = 0; x < wallWidth; x++)
            {
                int worldX = origin.X - width / 2 + 1 + x;
                int worldY = _maxY + 1 + y;
                if (WorldGen.InWorld(worldX, worldY))
                    _wallsToKill.Add(new Point(worldX, worldY));
            }
        }

        // 收集：放置墙区域（与清除区域一致）
        _wallsToPlace = new HashSet<Point>(_wallsToKill);

        // 收集：下层 U形平台 (6x4)
        for (int y = 0; y < height_dn; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool shouldPlace = (y == height_dn - 1) || (x == 0) || (x == width - 1);
                int worldX = origin.X - width / 2 + x;
                int worldY = origin.Y - height_dn + 1 + y;
                if (shouldPlace && WorldGen.InWorld(worldX, worldY))
                    _tilesToPlace_dn.Add(new Point(worldX, worldY));
            }
        }

        // 收集：上层环形木块 (6x6)
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
                PlaceTorchAtOffset();
                return;
            }

            // 强制放置火把
            if (_pendingTorchPlacement)
            {
                if (WorldGen.InWorld(_torchX, _torchY))
                {
                    WorldGen.KillTile(_torchX, _torchY, fail: false, effectOnly: false, noItem: true);
                    if (WorldGen.PlaceObject(_torchX, _torchY, TileID.Torches, true))
                    {
                        WorldGen.SquareTileFrame(_torchX, _torchY);
                    }
                }
                _pendingTorchPlacement = false;
            }

            // 放置工作台和椅子
            PlaceWorkbenchAndChair();

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

                WorldGen.PlaceWall(p.X, p.Y, WallID.Wood);
                WorldGen.SquareWallFrame(p.X, p.Y);
            }
        }

        // 4️⃣ 放置该层结构
        // 下层平台
        var toPlace_dn = new List<Point>(_tilesToPlace_dn);
        foreach (Point p in toPlace_dn)
        {
            if (p.Y == y && !Main.tile[p.X, p.Y].HasTile)
            {
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.Platforms))
                {
                    WorldGen.SquareTileFrame(p.X, p.Y, true);
                    _tilesToPlace_dn.Remove(p);
                }
            }
        }

        // 上层木块
        var toPlace_up = new List<Point>(_tilesToPlace_up);
        foreach (Point p in toPlace_up)
        {
            if (p.Y == y && !Main.tile[p.X, p.Y].HasTile)
            {
                if (WorldGen.PlaceTile(p.X, p.Y, TileID.WoodBlock))
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

        int width = 6;
        int height_dn = 4;
        int dx;
        int torchX;
        int torchY = _origin.Y - height_dn;

        if (player.direction == 1)
        {
            dx = width / 2 - 1;
            torchX = _origin.X + dx - 1;
        }
        else
        {
            dx = -width / 2 + 1;
            torchX = _origin.X + dx;
        }

        _torchX = torchX;
        _torchY = torchY;
        _pendingTorchPlacement = true;
    }

    // 放置工作台和椅子
    private void PlaceWorkbenchAndChair()
    {
        Player player = Main.player[Main.myPlayer];
        if (!_isProcessing) return;

        int height_dn = 4;
        int torchY = _origin.Y - height_dn;
        int furnitureY = torchY - 1;
        int workbenchX, chairX;

        if (player.direction == 1)
        {
            workbenchX = _origin.X - 1;
            chairX = workbenchX - 1;
        }
        else
        {
            workbenchX = _origin.X - 1;
            chairX = workbenchX + 2;
        }

        WorldGen.PlaceObject(workbenchX, furnitureY, TileID.WorkBenches, true);
        WorldGen.PlaceObject(chairX, furnitureY, TileID.Chairs,
            mute: true, style: 0, alternate: 0, random: -1, direction: player.direction);

        WorldGen.SquareTileFrame(workbenchX, furnitureY);
        WorldGen.SquareTileFrame(chairX, furnitureY);
    }
}