using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 매장에 놓은 장식 가구가 주는 명성 보너스. 연령대마다 따로 센다.
    ///
    /// <b>판매장 안에 놓은 것만</b> 친다 — 배달되어 앞마당에 서 있는 건 아직 꾸민 게 아니다.
    /// 같은 연령대 장식을 여러 개 놓으면 더해지지만 <b>한 개 효과의 <see cref="StackCap"/>배</b>에서 멈춘다.
    /// 화분만 열 개 깔아 아이 명성을 몇 배로 불리는 걸 막으려는 것이다.
    /// </summary>
    public static class DecorBonus
    {
        public const float StackCap = 2f;

        /// <summary>판매장 경계를 이만큼 봐준다 — 벽걸이는 벽에 붙어 경계 위에 걸린다.</summary>
        const float FloorSlack = 0.3f;

        public static float For(CustomerAge age)
        {
            FurnitureShop shop = FurnitureShop.Instance;
            if (shop == null || shop.Catalog == null) return 0f;

            float sum = 0f, single = 0f;
            foreach (BoughtFurniture bought in Object.FindObjectsByType<BoughtFurniture>(FindObjectsSortMode.None))
            {
                int index = bought.CatalogIndex;
                if (index < 0 || index >= shop.Catalog.Count) continue;

                FurnitureDef def = shop.Catalog.Get(index);
                if (!def.IsDecor || def.decorAge != age) continue;
                if (!OnSalesFloor(bought.transform.position)) continue;

                sum += def.decorBonus;
                single = Mathf.Max(single, def.decorBonus);
            }
            return Mathf.Min(sum, single * StackCap);
        }

        static bool OnSalesFloor(Vector3 p)
        {
            if (ShopSpace.Instance == null) return true;
            Rect floor = ShopSpace.Instance.FloorRect;
            return p.x > floor.xMin - FloorSlack && p.x < floor.xMax + FloorSlack
                && p.z > floor.yMin - FloorSlack && p.z < floor.yMax + FloorSlack;
        }
    }
}
