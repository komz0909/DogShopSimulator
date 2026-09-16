using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;
using UnityEngine.AI;

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
        /// 진열대 높이의 절반. AI 에셋 실측 0.48m 기준. 콜라이더가 중심 기준이라
        /// 이만큼 띄워야 바닥에 선다. 모델을 바꾸면 이 값과 TopY를 함께 고칠 것.
        /// </summary>
        public const float ShelfHalfHeight = 0.24f;

        /// <summary>
        /// 진열대는 <b>두 줄</b>이고 가운데 x 3.8~5.0 를 비운다. 이 세로 통로가
        /// 입구(x 3~5)와 창고 문(x 3.2~4.8)을 일직선으로 잇는 가게의 등뼈다.
        ///
        /// 테이블은 NavMesh를 1.00 x 0.60 으로 깎는다. 줄 간격을 1.8m 로 벌려
        /// 통로 폭이 1.2m 가 되게 했다 — 손님 지름 0.56 이라 둘이 비껴갈 수 있다.
        /// 이전 배치(간격 1.6, 통로 1.0)에서는 마주친 둘이 그대로 굳어
        /// 손님 12명 중 11명을 놓쳤다(7차 측정).
        /// </summary>
        const float FrontRowZ = 2.4f;
        const float BackRowZ = 4.2f;

        static readonly Vector3[] Slots =
        {
            new Vector3(0.9f, ShelfHalfHeight, FrontRowZ), new Vector3(2.1f, ShelfHalfHeight, FrontRowZ), new Vector3(3.3f, ShelfHalfHeight, FrontRowZ),
            new Vector3(5.5f, ShelfHalfHeight, FrontRowZ), new Vector3(6.7f, ShelfHalfHeight, FrontRowZ),
            new Vector3(0.9f, ShelfHalfHeight, BackRowZ),  new Vector3(2.1f, ShelfHalfHeight, BackRowZ),  new Vector3(3.3f, ShelfHalfHeight, BackRowZ),
            new Vector3(5.5f, ShelfHalfHeight, BackRowZ),  new Vector3(6.7f, ShelfHalfHeight, BackRowZ)
        };

        /// <summary>두 줄 사이 통로. <b>뒷줄 손님만</b> 여기로 들어온다.</summary>
        const float AisleZ = 3.3f;

        /// <summary>
        /// 앞줄은 통로가 아니라 <b>입구 쪽</b>에서 접근한다.
        /// 전부 통로로 몰면 앞줄 손님과 뒷줄 손님이 같은 1.2m 에서 엉킨다.
        /// </summary>
        const float FrontApproachZ = 1.75f;

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

                Vector3 approach = new Vector3(Slots[i].x, 0f, Slots[i].z < AisleZ ? FrontApproachZ : AisleZ);
                if (!Reachable(approach)) approach = new Vector3(Slots[i].x, 0f, AisleZ);

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

        /// <summary>
        /// 접근점이 실제로 NavMesh 위인지 본다. 계산대 옆처럼 침식으로 막힌 자리가 생기면
        /// 손님이 그 진열대에 영영 닿지 못하고 시간 초과로 나가버린다 —
        /// 배치를 손볼 때마다 사람이 눈으로 확인할 일이 아니다.
        /// </summary>
        static bool Reachable(Vector3 point)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(point + Vector3.up * 0.2f, out hit, 0.35f, NavMesh.AllAreas)) return false;
            return Mathf.Abs(hit.position.x - point.x) < 0.25f && Mathf.Abs(hit.position.z - point.z) < 0.25f;
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
