using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MoreMagicBuilder.Content.Items; // ✅ 引用 Items 命名空间

namespace MoreMagicBuilder.Content.NPCs
{
    public class MerchantGlobalNPC : GlobalNPC
    {
        public override void ModifyShop(NPCShop shop)
        {
            // 判断是否是商人
            if (shop.NpcType == NPCID.Merchant)
            {
                // 添加物品到商店（永远出售）
                shop.Add<Jail>();
            }
        }
    }
}
