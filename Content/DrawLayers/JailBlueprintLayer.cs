using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.GameContent;
using MoreMagicBuilder.Content.Items;

namespace MoreMagicBuilder.Content.DrawLayers
{
    public class JailBlueprintLayer : ModSystem
    {
        private const int StructureWidth = 6;
        private const int StructureHeight = 10;
        
        // --- 帧同步变量 ---
        // 移除帧计数器，采用每帧实时更新
        // ------------------

        private bool _debugPrinted = false;
        
        // 存储蓝图左上角的世界物块坐标，用于跨钩子同步。
        private Point _blueprintTopLeftTile;
        // 存储玩家是否手持 Jail 物品的状态。
        private bool _isHoldingBlueprintItem = false;


        // 🚀 在 Update 阶段，每帧实时更新坐标
        public override void PostUpdateEverything()
        {
            Player player = Main.LocalPlayer;
            if (Main.dedServ || player == null) 
            {
                _isHoldingBlueprintItem = false;
                return;
            }

            // 检查是否手持物品，并同步状态
            _isHoldingBlueprintItem = player.HeldItem != null && player.HeldItem.type == ModContent.ItemType<Jail>();
            
            if (!_isHoldingBlueprintItem)
            {
                return;
            }
            
            // 实时获取鼠标位置
            Vector2 mouseWorld = Main.MouseWorld;
            Point baseTile = mouseWorld.ToTileCoordinates();

            // === 坐标计算：标准“鼠标在底行中心” ===
            
            // 🚀 修正 1: X 轴标准居中。 leftX = baseTile.X - 3
            int leftX = baseTile.X - StructureWidth / 2; 

            // 🚀 修正 2: Y 轴位于底行。 topY = baseTile.Y - 10 + 1 = baseTile.Y - 9
            int topY = baseTile.Y - StructureHeight + 1;
            
            // 存储计算好的坐标，供 PostDrawInterface 使用
            _blueprintTopLeftTile = new Point(leftX, topY);
        }

        // 绘制阶段：使用预先计算好的坐标生成粒子
        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (Main.dedServ || !_isHoldingBlueprintItem) return;
            
            // if (!_debugPrinted)
            // {
            //     Main.NewText($"[JailBlueprintLayer] Final logic: Mouse is Bottom Center (X-3, Y-9).", 0, 200, 255);
            //     _debugPrinted = true;
            // }

            // 使用预先计算好的坐标
            Point blueprintTopLeftTile = _blueprintTopLeftTile;

            // --- 粒子生成逻辑 ---
            
            for (int x = 0; x < StructureWidth; x++)
            {
                for (int y = 0; y < StructureHeight; y++)
                {
                    // 只在蓝图的边缘生成粒子
                    if (x == 0 || x == StructureWidth - 1 || y == 0 || y == StructureHeight - 1)
                    {
                        // 粒子数量：Main.rand.Next(1, 2) 永远只生成 1 个
                        int particleCount = Main.rand.Next(1, 2); 

                        for (int i = 0; i < particleCount; i++)
                        {
                            // 随机偏移
                            Vector2 randomOffset = new Vector2(
                                Main.rand.NextFloat(-6f, 6f),
                                Main.rand.NextFloat(-6f, 6f)
                            );

                            // 计算当前物块的世界中心坐标
                            Vector2 tileCenterWorld = (blueprintTopLeftTile + new Point(x, y)).ToWorldCoordinates(8, 8);
                            Vector2 worldPos = tileCenterWorld + randomOffset;
                            
                            // 核心修正：使用世界坐标 (World Position) 调用 Dust.NewDustDirect
                            Dust dust = Dust.NewDustDirect(
                                worldPos - new Vector2(4), 
                                8, 8,
                                255	,  // 粒子
                                Scale: Main.rand.NextFloat(0.2f, 0.3f) // 随机大小
                            );

                            dust.noGravity = true;  
                            dust.velocity = new Vector2(
                                Main.rand.NextFloat(-0.5f, 0.5f),
                                Main.rand.NextFloat(-0.5f, 0.5f)
                            );
                            dust.noLight = false;
                            dust.color = Color.White * Main.rand.NextFloat(0.7f, 1.0f);
                            dust.fadeIn = 0.3f;
                        }
                    }
                }
            }
        }
    }
}