using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 진열대 배치. 해금된 상품 수만큼 런타임에 생성해 이 매니저의 자식으로 붙인다
    /// (테이블 구매 시스템을 따로 만들지 않는다).
    /// 입구(4, 0)에서 가까운 자리일수록 판매 가중치가 높다 — Plan.md의 배치 규칙 1개.
    /// </summary>
    public class ShelfManager : MonoBehaviour
    {
        static readonly Vector3 Entrance = new Vector3(4f, 0f, 0f);

        /// <summary>
        /// 앞줄 z=2.0(입구에 가까움), 뒷줄 z=3.6. 두 줄 사이 z 2.3~3.3이 통로다.
        /// x는 중앙(3.9~5.1)을 비워 입구에서 통로로 들어오는 길을 남긴다 —
        /// 이 간격이 손님 지름(0.56)보다 좁으면 진열대가 벽이 되어 뒷줄에 갈 수 없다.
        /// </summary>
        /// <summary>
        /// 진열대 높이의 절반. AI 에셋 실측 0.48m 기준. 콜라이더가 중심 기준이라
        /// 이만큼 띄워야 바닥에 선다. 모델을 바꾸면 이 값과 TopY를 함께 고칠 것.
        /// </summary>
        public const float ShelfHalfHeight = 0.24f;

        static readonly Vector3[] Slots =
        {
            new Vector3(1.0f, ShelfHalfHeight, 2.0f), new Vector3(2.2f, ShelfHalfHeight, 2.0f), new Vector3(3.4f, ShelfHalfHeight, 2.0f),
            new Vector3(5.5f, ShelfHalfHeight, 2.0f), new Vector3(6.6f, ShelfHalfHeight, 2.0f),
            new Vector3(1.0f, ShelfHalfHeight, 3.6f), new Vector3(2.2f, ShelfHalfHeight, 3.6f), new Vector3(3.4f, ShelfHalfHeight, 3.6f),
            new Vector3(5.5f, ShelfHalfHeight, 3.6f), new Vector3(6.6f, ShelfHalfHeight, 3.6f)
        };

        /// <summary>두 줄 사이 통로. 손님은 진열대 중심이 아니라 이 지점으로 걸어온다.</summary>
        const float AisleZ = 2.8f;

        public const float NearMultiplier = 1.5f;
        public const float FarMultiplier = 1.0f;

        public static ShelfManager Instance { get; private set; }

        [SerializeField] GameObject tablePrefab;

        readonly List<ShelfTable> tables = new List<ShelfTable>();

        public int Count => tables.Count;
        public ShelfTable Get(int index) => tables[index];

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

        /// <summary>해금된 상품에 테이블이 없으면 만든다. 이미 있는 테이블은 건드리지 않는다.</summary>
        void Rebuild()
        {
            if (tablePrefab == null) return;

            ProductCatalog catalog = InventoryManager.Instance.Catalog;
            int level = ShopLevelManager.Instance.Level;

            float minDistance = float.MaxValue;
            float maxDistance = 0f;
            for (int i = 0; i < Slots.Length; i++)
            {
                float d = Vector3.Distance(Slots[i], Entrance);
                if (d < minDistance) minDistance = d;
                if (d > maxDistance) maxDistance = d;
            }

            for (int i = 0; i < catalog.Count && i < Slots.Length; i++)
            {
                if (catalog.Get(i).unlockLevel > level) continue;
                if (FindFor(i) != null) continue;

                GameObject instance = Instantiate(tablePrefab, transform);
                instance.transform.localPosition = Slots[i];

                float distance = Vector3.Distance(Slots[i], Entrance);
                float t = maxDistance > minDistance ? (distance - minDistance) / (maxDistance - minDistance) : 0f;
                float multiplier = Mathf.Lerp(NearMultiplier, FarMultiplier, t);

                Vector3 approach = new Vector3(Slots[i].x, 0f, AisleZ);

                ShelfTable table = instance.GetComponent<ShelfTable>();
                table.Bind(i, multiplier, approach);
                instance.name = "Shelf_" + catalog.Get(i).nameKo;

                int captured = i;
                ProductSlotDisplay display = instance.GetComponent<ProductSlotDisplay>();
                if (display != null)
                    display.Configure(captured,
                        () => InventoryManager.Instance.ShelfOf(captured),
                        ShelfHalfHeight, 0.95f, 0.40f);

                tables.Add(table);
            }
        }

        public ShelfTable FindFor(int productIndex)
        {
            for (int i = 0; i < tables.Count; i++)
                if (tables[i].ProductIndex == productIndex) return tables[i];
            return null;
        }

        public float ProximityMultiplier(int productIndex)
        {
            ShelfTable table = FindFor(productIndex);
            return table != null ? table.ProximityMultiplier : 0f;
        }
    }
}
