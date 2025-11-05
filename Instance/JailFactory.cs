using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

public class JailFactory : ModSystem
{
    // 按 Y 行分组的操作点
    private Dictionary<int, List<Point>> _digTilesByY = new();
    private Dictionary<int, List<Point>> _killWallsByY = new();
    private Dictionary<int, List<Point>> _placeWallsByY = new();
    private Dictionary<int, List<Point>> _placeTiles_dn_ByY = new(); // 下层平台
    private Dictionary<int, List<Point>> _placeTiles_up_ByY = new(); // 上层木块

    private List<int> _allYs = new(); // 所有需要处理的 Y 坐标（从下到上排序）
    private int _currentIndex = 0;
    private int _delayTimer = 0;
    private bool _isProcessing = false;
    private Point _origin;

    public void StartGenerating(Point origin)
    {
        if (_isProcessing) return;

        _origin = origin;
        _isProcessing = true;
        _currentIndex = 0;
        _delayTimer = 0;

        // 清空旧数据
        _digTilesByY.Clear();
        _killWallsByY.Clear();
        _placeWallsByY.Clear();
        _placeTiles_dn_ByY.Clear();
        _placeTiles_up_ByY.Clear();
        _allYs.Clear();

        int width = 6;
        int height_dn = 4; // 下层高度
        int height_up = 6; // 上层高度
        int totalHeight = height_up + height_dn;
        int startY = origin.Y - totalHeight + 1; // 整体顶部 Y

        // === Step 1: 预计算所有要挖的 tile（按 Y 分组）===
        for (int y = 0; y < totalHeight; y++)
        {
            int worldY = startY + y;
            List<Point> tilesInRow = new();

            for (int x = 0; x < width; x++)
            {
                int worldX = origin.X - width / 2 + x;
                if (WorldGen.InWorld(worldX, worldY))
                {
                    tilesInRow.Add(new Point(worldX, worldY));
                }
            }

            if (tilesInRow.Count > 0)
            {
                _digTilesByY[worldY] = tilesInRow;
                if (!_allYs.Contains(worldY)) _allYs.Add(worldY);
            }
        }

        // === Step 2: 预计算要清除和放置的墙（内部区域）===
        int wallStartY = startY + 1;
        int wallHeight = totalHeight - 2;
        int wallWidth = width - 2;

        for (int y = 0; y < wallHeight; y++)
        {
            int worldY = wallStartY + y;
            List<Point> wallsInRow = new();

            for (int x = 0; x < wallWidth; x++)
            {
                int worldX = origin.X - width / 2 + 1 + x;
                if (WorldGen.InWorld(worldX, worldY))
                {
                    wallsInRow.Add(new Point(worldX, worldY));
                }
            }

            if (wallsInRow.Count > 0)
            {
                _killWallsByY[worldY] = wallsInRow;
                _placeWallsByY[worldY] = new List<Point>(wallsInRow);
                if (!_allYs.Contains(worldY)) _allYs.Add(worldY);
            }
        }

        // === Step 3: 下层 U 形平台（按 Y 分组）===
        for (int y = 0; y < height_dn; y++)
        {
            int worldY = origin.Y - height_dn + 1 + y;
            List<Point> tilesInRow = new();

            for (int x = 0; x < width; x++)
            {
                bool shouldPlace = (y == height_dn - 1) || (x == 0) || (x == width - 1);
                if (shouldPlace)
                {
                    int worldX = origin.X - width / 2 + x;
                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        tilesInRow.Add(new Point(worldX, worldY));
                    }
                }
            }

            if (tilesInRow.Count > 0)
            {
                _placeTiles_dn_ByY[worldY] = tilesInRow;
                if (!_allYs.Contains(worldY)) _allYs.Add(worldY);
            }
        }

        // === Step 4: 上层环形木块（按 Y 分组）===
        int upperTopY = origin.Y - height_dn - height_up + 1;
        for (int y = 0; y < height_up; y++)
        {
            int worldY = upperTopY + y;
            List<Point> tilesInRow = new();

            for (int x = 0; x < width; x++)
            {
                bool isEdge = (x == 0 || x == width - 1 || y == 0 || y == height_up - 1);
                if (isEdge)
                {
                    int worldX = origin.X - width / 2 + x;
                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        tilesInRow.Add(new Point(worldX, worldY));
                    }
                }
            }

            if (tilesInRow.Count > 0)
            {
                _placeTiles_up_ByY[worldY] = tilesInRow;
                if (!_allYs.Contains(worldY)) _allYs.Add(worldY);
            }
        }

        // ✅ 关键修正：从下往上生成 → Y 从大到小排序
        _allYs.Sort((a, b) => b.CompareTo(a)); // 大 Y（底部）在前，小 Y（顶部）在后
    }

    public override void PostUpdateEverything()
    {
        if (!_isProcessing || _allYs.Count == 0) return;

        _delayTimer++;

        // ✅ 每 5 帧处理一行（从底部开始向上）
        if (_delayTimer >= 5)
        {
            int currentY = _allYs[_currentIndex];

            // 🔸 挖掘 Tile
            if (_digTilesByY.TryGetValue(currentY, out var digList))
            {
                foreach (var p in digList)
                {
                    WorldGen.KillTile(p.X, p.Y, fail: false, effectOnly: false);
                }
            }

            // 🔸 清除 Wall
            if (_killWallsByY.TryGetValue(currentY, out var killWallList))
            {
                foreach (var p in killWallList)
                {
                    WorldGen.KillWall(p.X, p.Y, fail: false);
                }
            }

            // 🔸 放置 Wall
            if (_placeWallsByY.TryGetValue(currentY, out var placeWallList))
            {
                foreach (var p in placeWallList)
                {
                    if (WorldGen.InWorld(p.X, p.Y))
                    {
                        Tile tile = Main.tile[p.X, p.Y];
                        if (tile != null && tile.WallType != WallID.Wood)
                        {
                            WorldGen.PlaceWall(p.X, p.Y, WallID.Wood, mute: true);
                        }
                    }
                }
            }

            // 🔸 放置下层平台（U 形）
            if (_placeTiles_dn_ByY.TryGetValue(currentY, out var placeDnList))
            {
                foreach (var p in placeDnList)
                {
                    if (WorldGen.InWorld(p.X, p.Y))
                    {
                        Tile tile = Main.tile[p.X, p.Y];
                        if (tile != null && !tile.HasTile)
                        {
                            if (WorldGen.PlaceTile(p.X, p.Y, TileID.Platforms))
                            {
                                WorldGen.SquareTileFrame(p.X, p.Y, true);
                            }
                        }
                    }
                }
            }

            // 🔸 放置上层木块（环形）
            if (_placeTiles_up_ByY.TryGetValue(currentY, out var placeUpList))
            {
                foreach (var p in placeUpList)
                {
                    if (WorldGen.InWorld(p.X, p.Y))
                    {
                        Tile tile = Main.tile[p.X, p.Y];
                        if (tile != null && !tile.HasTile)
                        {
                            if (WorldGen.PlaceTile(p.X, p.Y, TileID.WoodBlock))
                            {
                                WorldGen.SquareTileFrame(p.X, p.Y, true);
                            }
                        }
                    }
                }
            }

            // 推进到下一行
            _currentIndex++;
            _delayTimer = 0;

            // 全部完成
            if (_currentIndex >= _allYs.Count)
            {
                _isProcessing = false;
                PlaceTorchAtOffset();
                PlaceWorkbenchAndChair();
            }
        }
    }

    // ========== 一次性放置家具 ==========

    private void PlaceTorchAtOffset()
    {
        Player player = Main.player[Main.myPlayer];
        int width = 6;
        int height_dn = 4;
        int torchY = _origin.Y - height_dn;

        int torchX;
        if (player.direction == 1)
        {
            torchX = _origin.X + (width / 2 - 1) - 1; // 右侧内一格
        }
        else
        {
            torchX = _origin.X + (-width / 2 + 1); // 左侧内一格
        }

        if (WorldGen.InWorld(torchX, torchY))
        {
            Tile tile = Main.tile[torchX, torchY];
            tile.ClearTile();       // 清空整个格子
            if (tile != null && !tile.HasTile && !tile.TopSlope && !tile.BottomSlope)
            {
                if (WorldGen.PlaceObject(torchX, torchY, TileID.Torches, true))
                {
                    WorldGen.SquareTileFrame(torchX, torchY);
                }
            }
        }
    }

    private void PlaceWorkbenchAndChair()
    {
        Player player = Main.player[Main.myPlayer];
        int height_dn = 4;
        int furnitureY = _origin.Y - height_dn - 1; // 火把上方一格

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
        WorldGen.PlaceObject(chairX, furnitureY, TileID.Chairs, mute: true, style: 0, direction: player.direction);

        WorldGen.SquareTileFrame(workbenchX, furnitureY);
        WorldGen.SquareTileFrame(chairX, furnitureY);
    }
}