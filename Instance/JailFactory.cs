// DelayedStructureSystem.cs
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

public class JailFactory : ModSystem
{
    private int _timer = 0;
    private HashSet<Point> _tilesToDig;      // 要挖的所有位置（矩形区域）
    private HashSet<Point> _tilesToPlace;    // 最终要放置图块的位置（倒U形等）
    private bool _isProcessing = false;

    public void StartGenerating(Point origin)
    {
        if (_isProcessing) return;

        int width = 6;
        int height = 4;

        _tilesToDig = new HashSet<Point>();
        _tilesToPlace = new HashSet<Point>();

        // Step 1: 定义要挖的矩形区域（全部清除）
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int worldX = origin.X - width / 2 + x;           
                int worldY = origin.Y - height + 1 + y;      

                if (WorldGen.InWorld(worldX, worldY))
                {
                    _tilesToDig.Add(new Point(worldX, worldY));
                }
            }
        }

        // Step 2: 定义要放置图块的“倒U形”结构
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool shouldPlace = false;

                if (y == height - 1)                     // 底部一行
                    shouldPlace = true;
                else if (x == 0 || x == width - 1)       // 左右两列
                    shouldPlace = true;

                if (shouldPlace)
                {
                    int worldX = origin.X - width / 2 + x;           
                    int worldY = origin.Y - height + 1 + y;      

                    if (WorldGen.InWorld(worldX, worldY))
                    {
                        _tilesToPlace.Add(new Point(worldX, worldY));
                    }
                }
            }
        }

        // Step 3: 执行挖掘（全部 KillTile）
        foreach (Point p in _tilesToDig)
        {
            WorldGen.KillTile(p.X, p.Y, fail: false, effectOnly: false);
        }

        // Step 4: 启动计时器
        _timer = 0;
        _isProcessing = true;
    }

    public override void PostUpdateEverything()
    {
        if (!_isProcessing) return;

        _timer++;

        if (_timer >= 10) // 等待10帧，让 KillTile 生效
        {
            PlaceStructureAfterDelay();
            _isProcessing = false;
        }
    }

    private void PlaceStructureAfterDelay()
    {
        foreach (Point p in _tilesToPlace)
        {
            int x = p.X;
            int y = p.Y;

            if (!WorldGen.InWorld(x, y)) continue;

            Tile tile = Main.tile[x, y];
            if (tile == null) continue;

            // ✅ 只有在图块已被清除的情况下才放置
            if (!tile.HasTile)
            {
                if (WorldGen.PlaceTile(x, y, TileID.Platforms))
                {
                    WorldGen.SquareTileFrame(x, y, true); // 更新帧拼接
                }
            }
            // 如果 HasTile 仍为 true，说明没挖掉（如宝箱、不可破坏物），跳过
        }

        // 清理
        _tilesToDig?.Clear();
        _tilesToPlace?.Clear();
    }
}