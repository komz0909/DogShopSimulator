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
        /// <summary>
        /// 선반을 두 줄로 세운다. 캐릭터 키만큼 키우면서 폭도 0.99m 가 되어
        /// 한 줄에 10개를 세우면 창고 폭 8m 를 넘는다.
        /// </summary>
        /// <summary>
        /// 선반 줄의 z. 상품이 16종으로 늘면서 두 줄(10자리)로는 모자라 세 줄이 됐다 —
        /// 자리가 없으면 새 선반이 앞 선반 위에 겹쳐 생긴다.
        /// 창고는 z 5.8~10 이므로 6.7 이 앞쪽 한계다.
        /// </summary>
        static readonly float[] RowZ = { 9.3f, 8.0f, 6.7f };
        const int PerRow = 6;

        const float FirstRackX = 0.9f;

        /// <summary>줄당 6개가 창고 폭 8m 안에 들어가는 간격. 0.9 + 5×1.25 = 7.15m.</summary>
        const float RackSpacing = 1.25f;

        /// <summary>
        /// 선반 높이의 절반. 프리팹 콜라이더가 중심 기준(-0.45~+0.45)이라
        /// y=0에 놓으면 아래 절반이 바닥에 묻힌다. 진열대는 ShelfManager가 0.4를 더해
        /// 바닥 위에 세우는데 창고 선반만 이 보정이 빠져 있었다.
        /// </summary>
        const float RackHalfHeight = 0.836f;

        /// <summary>상품을 놓는 로컬 높이 = 맨 위 선반판.</summary>
        /// <summary>
        /// 맨 위 선반판의 로컬 높이. 메시 정점 해석으로 실측했다 (위를 향한 면이
        /// +0.42 / +0.07 / -0.27 세 높이에 몰려 있다).
        /// </summary>
        /// 루트 스케일이 1 이므로 Body 스케일(1.9)을 곱한 실제 높이를 쓴다.
        const float RackTopY = 0.798f;

        /// <summary>선반 단 수와 단 사이 간격. 위에서 아래로 내려가므로 음수다.</summary>
        const int RackTiers = 3;
        const float RackTierSpacing = -0.6555f;

        /// <summary>창고는 진열대보다 많이 보여준다. 3단이므로 단당 4개씩.</summary>
        const int RackMaxVisible = 12;
        const float RackTopWidth = 0.88f;
        const float RackTopDepth = 0.34f;

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
                int column = i % PerRow;
                float rowZ = RowZ[Mathf.Min(i / PerRow, RowZ.Length - 1)];
                instance.transform.localPosition = new Vector3(FirstRackX + column * RackSpacing, RackHalfHeight, rowZ);
                instance.name = "Rack_" + catalog.Get(i).nameKo;

                StorageRack rack = instance.GetComponent<StorageRack>();
                rack.Bind(i);

                int captured = i;
                ProductSlotDisplay display = instance.GetComponent<ProductSlotDisplay>();
                if (display != null)
                    display.Configure(captured,
                        () => InventoryManager.Instance.StorageOf(captured),
                        RackTopY, RackTopWidth, RackTopDepth,
                        RackTiers, RackTierSpacing, RackMaxVisible);

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
