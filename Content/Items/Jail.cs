using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;


namespace MoreMagicBuilder.Content.Items
{
    public class Jail : ModItem
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
            ModContent.GetInstance<JailFactory2>().StartGenerating(p);
            return false;
        }

        // public static void GenerateStructure(Point origin)
        // {
        //     ShapeData JailData = new ShapeData();//记录形状
        //     int width = 6;
        //     int height = 10;
        //     //新中心点
        //     Point Jailorigin = new Point(origin.X + width/2, origin.Y - height / 2);

        //     WorldUtils.Gen(
        //         Jailorigin,
        //         new Shapes.Rectangle(new Rectangle(-width / 2, height-6, width, height-6)),//new一个从鼠标位置-width/2到width/2的height-6的矩形
        //         new Actions.ClearTile(frameNeighbors: true).Output(JailData));
        //         //这里是清除范围内的物块，并且使用GenAction提供的Output方法记录图形

        //     WorldUtils.Gen(
        //         Jailorigin,
        //         new ModShapes.InnerOutline(JailData),//用之前记录的形状
        //         Actions.Chain(
        //             new Modifiers.IsNotSolid(),//判断液体与否
        //             new Actions.SetTile(TileID.Platforms)//使用Set强制放置物块，不用清理后在放了
        //         )
        //     );
        // }
    }
}