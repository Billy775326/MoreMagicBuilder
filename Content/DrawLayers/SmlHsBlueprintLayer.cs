using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.GameContent;
using MoreMagicBuilder.Content.Items;

namespace MoreMagicBuilder.Content.DrawLayers
{
    public class SmlHsBlueprintLayer : ModSystem
    {
        private const int _width = 18;    // 总宽度（屋顶宽）
        private const int _height = 15;   // 总高度
        private const int _rectHeight = 7; // 下半部分矩形高度
        private const int _triHeight = 8;  // 上半部分屋顶高度

        private Point _blueprintTopLeftTile;
        private bool _isHoldingBlueprintItem = false;

        public override void PostUpdateEverything()
        {
            Player player = Main.LocalPlayer;
            if (Main.dedServ || player == null)
            {
                _isHoldingBlueprintItem = false;
                return;
            }

            _isHoldingBlueprintItem = player.HeldItem != null && player.HeldItem.type == ModContent.ItemType<SmlHs>();
            if (!_isHoldingBlueprintItem) return;

            Vector2 mouseWorld = Main.MouseWorld;
            Point baseTile = mouseWorld.ToTileCoordinates();

            // 鼠标位置为底部中心
            int lowX = baseTile.X - _width / 2;
            int topY = baseTile.Y - _height;

            _blueprintTopLeftTile = new Point(lowX, topY);
        }

        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (Main.dedServ || !_isHoldingBlueprintItem) return;

            Point topLeft = _blueprintTopLeftTile;

            for (int x = 0; x < _width ; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    bool draw = false;

                    // --- 屋顶部分（上半部分） ---
                    if (y < _triHeight + 1)
                    {
                        int roofRow = y;
                        int leftEdge = _width / 2 - roofRow - 1;
                        int rightEdge = _width / 2 + roofRow;

                        if (x == leftEdge || x == rightEdge)
                            draw = true;
                    }
                    if (y < _triHeight + 2 )
                    {
                        int roofRow = y;
                        int leftEdge = _width / 2 - roofRow ;
                        int rightEdge = _width  / 2 + roofRow-1 ;

                        if (x == leftEdge || x == rightEdge)
                            draw = true;
                    }
                    // --- 矩形房身部分 ---
                    if(y >= _triHeight)
                    {
                        int rectY = y - _triHeight;
                        int rectWidth = _width - 2; // 矩形左右各缩小 1 格
                        int rectOffsetX = 1;        // 左边偏移 1 格实现居中

                        if (x >= rectOffsetX && x < rectOffsetX + rectWidth)
                        {
                            if (rectY == 0 || rectY == _rectHeight - 1) // 上下边
                                draw = true;
                            else if (x == rectOffsetX + 1 || x == rectOffsetX + rectWidth - 1 - 1) // 左右边
                                draw = true;
                        }
                    }

                    if (draw)
                    {
                        int particleCount = Main.rand.Next(1, 3);
                        for (int i = 0; i < particleCount; i++)
                        {
                            Vector2 randomOffset = new Vector2(
                                Main.rand.NextFloat(-6f, 6f),
                                Main.rand.NextFloat(-6f, 6f)
                            );

                            Vector2 tileCenterWorld = (topLeft + new Point(x, y)).ToWorldCoordinates(8, 8);
                            Vector2 worldPos = tileCenterWorld + randomOffset;

                            Dust dust = Dust.NewDustDirect(
                                worldPos - new Vector2(4),
                                8, 8,
                                255,
                                Scale: Main.rand.NextFloat(0.3f, 0.5f)
                            );

                            dust.noGravity = true;
                            dust.velocity = new Vector2(
                                Main.rand.NextFloat(-0.5f, 0.5f),
                                Main.rand.NextFloat(-0.5f, 0.5f)
                            );
                            dust.color = Color.White * Main.rand.NextFloat(0.7f, 1.0f);
                            dust.fadeIn = 0.3f;
                        }
                    }
                }
            }
        }
    }
}
