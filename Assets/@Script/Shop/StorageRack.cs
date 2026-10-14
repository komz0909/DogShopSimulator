using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 창고 선반 1개. 3단이고 <b>단마다 상품 1종</b>이 들어간다 — 1번 칸 기본 사료, 2번 칸 샴푸 … 식으로 정돈된다.
    /// 상품 크기와 상관없이 어느 단에나 들어간다. 그 상품의 창고 재고가 그 단에 쌓여 보인다.
    /// 조준한 <b>높이</b>로 어느 단인지 고르고, E 로 그 상품을 상자에 담고 R 로 창고에 내린다.
    ///
    /// 기본 4개는 StorageManager 가 세우고, 가구 상점에서 산 것은 FurnitureShop 이 세운다 —
    /// 어느 쪽이든 스스로 단을 꾸미고 StorageManager 에 등록한다(어느 단에 무엇을 둘지는 매니저가 정한다).
    /// </summary>
    // 콜라이더를 다시 씌운 뒤에 PlaceableFurniture 가 크기를 재야 한다(그쪽 Awake 가 크기를 한 번 잡아 둔다)
    [DefaultExecutionOrder(-100)]
    public class StorageRack : MonoBehaviour
    {
        public const int Tiers = 3;

        /// <summary>선반 높이의 절반. 원점이 몸통 가운데라 바닥에 세우려면 이만큼 올린다.</summary>
        public const float PivotHeight = 0.836f;

        /// <summary>
        /// 맨 위 선반판의 로컬 높이. 메시 정점 해석으로 실측했다 (위를 향한 면이
        /// +0.42 / +0.07 / -0.27 세 높이에 몰려 있다). 루트 스케일이 1 이므로 Body 스케일(1.9)을 곱한 실제 높이를 쓴다.
        /// </summary>
        const float TopY = 0.798f;
        /// <summary>단 사이 간격. 위에서 아래로 내려가므로 음수다.</summary>
        const float TierSpacing = -0.6555f;
        const float TopWidth = 0.88f;
        const float TopDepth = 0.34f;
        /// <summary>한 단에 보이는 최대 개수 — 단에 상품이 하나뿐이라 한 줄 가득.</summary>
        const int TierMaxVisible = 4;

        readonly int[] tierProducts = { -1, -1, -1 };
        readonly ProductSlotDisplay[] displays = new ProductSlotDisplay[Tiers];

        void Awake()
        {
            FitCollider();

            // 프리팹에 붙은 표시(선반 하나 = 상품 하나 시절)는 쓰지 않는다. 단마다 따로 붙인다
            ProductSlotDisplay legacy = GetComponent<ProductSlotDisplay>();
            if (legacy != null) legacy.enabled = false;
            for (int t = 0; t < Tiers; t++)
            {
                var tier = new GameObject("Tier_" + (t + 1));
                tier.transform.SetParent(transform, false);
                displays[t] = tier.AddComponent<ProductSlotDisplay>();
            }
        }

        void Start()
        {
            if (StorageManager.Instance != null) StorageManager.Instance.Register(this);
        }

        void OnDestroy()
        {
            if (StorageManager.Instance != null) StorageManager.Instance.Unregister(this);
        }

        /// <summary>
        /// 프리팹 콜라이더가 아래로 치우쳐(바닥 밑 −0.84 ~ 1.08) 맨 윗단(1.6m)을 조준해도 안 잡혔다.
        /// 보이는 몸통에 맞춰 다시 씌운다. 회전과 무관하게 맞추려고 잠깐 똑바로 세워서 잰다.
        /// </summary>
        void FitCollider()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (box == null || renderers.Length == 0) return;

            Quaternion keep = transform.rotation;
            transform.rotation = Quaternion.identity;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            box.center = transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;   // 루트 스케일 1 이라 월드 크기 = 로컬 크기
            transform.rotation = keep;
        }

        /// <summary>그 단에 둘 상품을 바꾼다(-1 = 빈 단). 바뀌면 쌓여 보이는 물건도 바꾼다.</summary>
        public void SetTier(int tier, int productIndex)
        {
            if (tier < 0 || tier >= Tiers || tierProducts[tier] == productIndex) return;
            tierProducts[tier] = productIndex;

            int captured = productIndex;
            // 랜덤 상자에서 나온 등급품도 같은 단에 쌓여 보인다(E 로 담을 때 좋은 등급부터 집는다)
            displays[tier].Configure(captured,
                captured >= 0 ? () => InventoryManager.Instance.StorageOf(captured) + InventoryManager.Instance.GradedTotalOf(captured) : (System.Func<int>)null,
                TopY + tier * TierSpacing, TopWidth, TopDepth, 1, 0f, TierMaxVisible);
        }

        public int ProductOfTier(int tier) => tier >= 0 && tier < Tiers ? tierProducts[tier] : -1;

        /// <summary>
        /// 그 점이 몇 번째 단(0 = 맨 위)인가. 물건은 상판 위에 서 있으므로
        /// 상판에서 위 단 상판 바로 아래까지가 그 단이다.
        /// </summary>
        public int TierAt(Vector3 worldPoint)
        {
            float u = (TopY - transform.InverseTransformPoint(worldPoint).y) / -TierSpacing;
            return Mathf.Clamp(Mathf.CeilToInt(u), 0, Tiers - 1);
        }

        public int ProductAt(Vector3 worldPoint) => ProductOfTier(TierAt(worldPoint));

        /// <summary>그 단 이름표를 띄울 자리(단 상판 조금 위).</summary>
        public Vector3 LabelPoint(int tier) =>
            transform.TransformPoint(new Vector3(0f, TopY + tier * TierSpacing + 0.36f, 0f));
    }
}
