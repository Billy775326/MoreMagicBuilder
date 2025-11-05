using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent;
using Terraria.DataStructures;

namespace MoreMagicBuilder.Content.Items
{
    public class JailA : ModItem
    {
        

        public override void SetDefaults()
        {
            
            Item.useStyle = ItemUseStyleID.Swing;
            //Item.useStyle = ItemUseStyleID.Thrust;//生命水晶使用模式
            Item.autoReuse = false;//自动连用
            Item.rare = ItemRarityID.White;//稀有度
            Item.value = Item.buyPrice(silver: 5);//价值
            Item.useAnimation = 15;//使用一次的动画时间
            Item.useTime = 15;//使用一次的时间
            Item.consumable = true;//消耗品
            Item.maxStack = 999;//最大堆栈
            Item.noMelee = true;//无近战
            Item.UseSound = SoundID.Shatter;

            Item.useTurn = true; // ✅ 让玩家转身使用，减少偏移
            Item.holdStyle = 0; // holdStyle = 0：默认手持 holdStyle = 1：更贴近身体
            Item.noUseGraphic = false;  // 确保使用时显示贴图
            

            Item.width = 16;//掉落时宽高
            Item.height = 16;
            Item.scale = 0.5f; 
        }

        public override void HoldItem(Player player)
        {
            // 可以在这里添加持有时的效果
        }


        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Wood, 20)       // 20 木头
                .AddIngredient(ItemID.StoneBlock, 5) // 5 石头 GrayBrick石砖
                .AddIngredient(ItemID.IronOre, 3)    // 3 铁矿 IronBrick 
                .AddIngredient(ItemID.Gel, 1)        // 1 凝胶
                .AddTile(TileID.Furnaces)           //制作台 熔炉
                .Register(); // 注册配方
        }


        public override bool? UseItem(Player player)
        {
            Vector2 myVector = Main.MouseWorld;//获取鼠标在世界中的位置单位是“像素”
            Point p = myVector.ToTileCoordinates();//将“像素坐标”转换为“图块坐标”
            //GenerateStructure(p);
            ModContent.GetInstance<JailAFactory>().StartGenerating(p);
            //Main.NewText("🔧 UseItem 被调用！", 255, 0, 0); // 红色提示

            // ✅ 使用成功，返回 true 表示消耗物品
            return true;
        }
        

    }
}