using System;
using UnityEngine;

namespace DogShop.Data
{
    public enum ProductCategory { Food, Supply, Medicine, Treat, Premium }

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
