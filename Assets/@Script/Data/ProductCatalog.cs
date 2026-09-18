using System;
using UnityEngine;

namespace DogShop.Data
{
    /// <summary>
    /// 상점창의 진열 칸. <b>손님의 수요가 아니라 플레이어가 물건을 찾는 방식</b>으로 나눈다 —
    /// 사료를 사러 들어왔으면 사료만 보면 되고, 고급이냐 기본이냐는 그 안에서 가격으로 갈린다.
    ///
    /// 예전 구분(Food/Supply/Medicine/Treat/Premium)은 Premium이 사료·간식·미용을 한꺼번에
    /// 삼켜서 레벨이 오를수록 한 칸에만 물건이 쌓였다. 등급은 칸이 아니라 <b>해금 레벨</b>이 말한다.
    /// </summary>
    public enum ProductCategory
    {
        Food = 0,        // 동물사료 — 사료와 간식
        Supply = 1,      // 애견용품 — 목줄, 장난감 같은 물건
        Care = 2,        // 약품/미용 — 약, 샴푸, 미용키트
        Furniture = 3    // 가구 — 진열대와 침대. 상품이 아니라 가게에 놓는 것
    }

    public static class ProductCategories
    {
        /// <summary>상점창 탭 순서. 손님이 가장 많이 사는 것부터 왼쪽에 둔다.</summary>
        public static readonly ProductCategory[] Tabs =
        {
            ProductCategory.Food,
            ProductCategory.Supply,
            ProductCategory.Care,
            ProductCategory.Furniture
        };

        public static string NameOf(ProductCategory category)
        {
            switch (category)
            {
                case ProductCategory.Supply: return "애견용품";
                // 가운뎃점(·)과 줄표(—)는 HUD 글꼴(BMJUA)에 글리프가 없어 빈칸으로 찍힌다
                case ProductCategory.Care: return "약품/미용";
                case ProductCategory.Furniture: return "가구";
                default: return "동물사료";
            }
        }
    }

    /// <summary>상점 상품 1종. 판매 상품이면서 강아지 소모품이기도 하다(재고 이중용도).</summary>
    [Serializable]
    public class ProductDef
    {
        public string nameKo = "";
        public ProductCategory category = ProductCategory.Food;
        public int wholesale = 30;
        public int retail = 60;
        [Min(1)] public int demandWeight = 1;
        [Min(1)] public int unlockLevel = 1;

        /// <summary>진열대에 올릴 3D 프롭. 비어 있으면 진열대에 아무것도 안 올라간다.</summary>
        public GameObject propPrefab;

        /// <summary>
        /// 상점창에 뜨는 물건 사진. <see cref="propPrefab"/>을 실제로 찍어 구운 것이라
        /// 창에서 본 모습과 진열대에 올라간 모습이 같다. DogShop/상품 사진 굽기 로 다시 굽는다.
        /// </summary>
        public Texture2D icon;

        /// <summary>
        /// 운반 상자에서 차지하는 칸 수. 상자는 8칸이므로 1칸 상품은 8개, 2칸 상품은 4개까지 든다.
        /// AIPropWiring이 프롭 크기에서 자동으로 채운다(최대 변 0.40m 이상이면 2칸).
        /// </summary>
        [Range(1, 2)] public int slotCost = 1;
    }

    [CreateAssetMenu(menuName = "DogShop/Product Catalog", fileName = "ProductCatalog")]
    public class ProductCatalog : ScriptableObject
    {
        [SerializeField] ProductDef[] products = new ProductDef[0];

        public int Count => products.Length;
        public ProductDef Get(int index) => products[index];
    }
}
