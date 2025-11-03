using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;


namespace MoreMagicBuilder.Content.Items
{
    public class JailA : ModItem
    {
        public override void SetDefaults()
        {
            Item.useStyle = ItemUseStyleID.Thrust;//生命水晶使用模式
            Item.autoReuse = false;//自动连用
            Item.rare = ItemRarityID.White;//稀有度
            Item.value = 1000;//价值
            Item.width = 30;//掉落时宽高
            Item.height = 30;
            Item.useAnimation = 15;//使用一次的动画时间
            Item.useTime = 15;//使用一次的时间
            Item.consumable = true;//消耗品
            Item.maxStack = 999;//最大堆栈
            Item.noMelee = true;//无近战
            Item.UseSound = SoundID.Shatter;

        }
        public override bool CanUseItem(Player player)
        {
            Vector2 myVector = Main.MouseWorld;//获取鼠标在世界中的位置单位是“像素”
            Point p = myVector.ToTileCoordinates();//将“像素坐标”转换为“图块坐标”
            //GenerateStructure(p);
            ModContent.GetInstance<JailAFactory>().StartGenerating(p);
            return false;
        }
    }
}