using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 창고 선반 배치. 해금된 상품 수만큼 런타임에 생성해 이 매니저의 자식으로 붙인다.
    /// 발주한 재고가 창고 선반에 실제로 쌓여 보이고, 플레이어가 여기서 상자에 담아 나른다.
    /// </summary>
    public class StorageManager : MonoBehaviour
    {
        /// <summary>창고는 가게 뒤쪽(z 6.4~10.0)에 있고 선반은 뒷벽에 붙는다.</summary>
        const float RackZ = 9.2f;
        const float FirstRackX = 0.5f;
        const float RackSpacing = 0.78f;

        /// <summary>선반 상판 크기(본체 큐브 스케일과 맞춤).</summary>
        const float RackTopY = 0.45f;
        const float RackTopWidth = 0.75f;
        const float RackTopDepth = 0.5f;

        public static StorageManager Instance { get; private set; }

        [SerializeField] GameObject rackPrefab;

        readonly List<StorageRack> racks = new List<StorageRack>();

        public int Count => racks.Count;
        public StorageRack Get(int index) => racks[index];

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            Rebuild();
            ShopLevelManager.Instance.OnLevelUp += HandleLevelUp;
        }

        void OnDestroy()
        {
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.OnLevelUp -= HandleLevelUp;
            if (Instance == this) Instance = null;
        }

        void HandleLevelUp(int level) => Rebuild();

        void Rebuild()
        {
            if (rackPrefab == null) return;

            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            int level = ShopLevelManager.Instance.Level;

            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog.Get(i).unlockLevel > level) continue;
                if (FindFor(i) != null) continue;

                GameObject instance = Instantiate(rackPrefab, transform);
                instance.transform.localPosition = new Vector3(FirstRackX + i * RackSpacing, 0f, RackZ);
                instance.name = "Rack_" + catalog.Get(i).nameKo;

                StorageRack rack = instance.GetComponent<StorageRack>();
                rack.Bind(i);

                int captured = i;
                ProductSlotDisplay display = instance.GetComponent<ProductSlotDisplay>();
                if (display != null)
                    display.Configure(captured,
                        () => InventoryManager.Instance.StorageOf(captured),
                        RackTopY, RackTopWidth, RackTopDepth);

                racks.Add(rack);
            }
        }

        public StorageRack FindFor(int productIndex)
        {
            for (int i = 0; i < racks.Count; i++)
                if (racks[i].ProductIndex == productIndex) return racks[i];
            return null;
        }
    }
}
