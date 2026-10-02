using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 앞마당의 배달 더미 배치. 아침에 온 물건이 여기 놓이고, 플레이어가 상자로 나른다.
    ///
    /// 창고 선반(<see cref="StorageManager"/>)과 같은 구조다 — 재고는 전부
    /// <see cref="InventoryManager"/>가 들고 있고 여기서는 <b>보여 주기만</b> 한다.
    /// 그래서 세이브에 참여하지 않는다: 불러오면 배달 수량을 다시 읽어 더미를 다시 세운다.
    ///
    /// 더미는 <b>배달이 있는 상품만</b> 왼쪽부터 채워 세운다. 상품마다 자리를 고정하면
    /// 사료 하나 시킨 날에도 앞마당이 빈 팔레트로 가득 찬다.
    /// </summary>
    public class DeliveryManager : MonoBehaviour
    {
        /// <summary>
        /// 앞마당은 x 0~8 / z -4.5~0 이다. 배달은 <b>왼쪽 구석에만</b> 모아 내린다.
        ///
        /// 예전에는 가로로 다섯 자리를 폈는데, 0.9 + 1.45×n 이라 세 번째가 정확히 x 3.80 —
        /// 문틈(x 3~5) 한가운데였다. 팔레트 폭이 0.95 라 x 3.33~4.28 을 차지해서,
        /// 상품이 세 종만 배달돼도 매일 아침 출입구가 막혔다.
        /// 주석에는 "문 앞을 비워 둔다"고 적혀 있었지만 숫자가 그렇지 않았다.
        ///
        /// 이제 세 줄짜리 격자를 왼쪽에 세로로 쌓는다 — 가장 오른쪽 자리도 x 2.93 에서 끝나
        /// 문틈에 닿지 않는다.
        /// </summary>
        const float FirstX = 0.55f;
        const float SpacingX = 0.95f;
        const int PerRow = 3;

        /// <summary>맨 앞줄. 손님 통로(z -0.85~0)보다 뒤에 있어야 한다.</summary>
        const float FirstZ = -1.35f;
        const float SpacingZ = 0.85f;

        /// <summary>한 구석이 받는 줄 수. 네 줄이면 맨 뒤 팔레트가 z -4.28 로 울타리(-4.5) 안이다.</summary>
        const int RowsPerBlock = 4;

        /// <summary>왼쪽이 다 차면 넘어가는 오른쪽 구석의 첫 칸. 여기도 문틈(x 3~5)을 비킨다.</summary>
        const float RightBlockX = 5.55f;

        /// <summary>팔레트 위에 상품이 놓이는 로컬 높이 = 널빤지 윗면.</summary>
        const float PalletTopY = 0.12f;
        const float PalletWidth = 0.95f;
        const float PalletDepth = 0.75f;

        /// <summary>한 더미에 보일 최대 개수. 2단으로 쌓아 12개까지 보여 준다.</summary>
        const int Tiers = 2;
        const float TierSpacing = 0.30f;
        const int MaxVisible = 12;

        // ---- 안 뜯은 상자 ----

        /// <summary>상자 한 변. VARCO 로 뽑은 모델이 39cm 각이다.</summary>
        const float ParcelSize = 0.40f;

        /// <summary>왼쪽 벽에 붙여 쌓는 자리. 손님 통로(z -0.85~0)보다 뒤다.</summary>
        const float ParcelX = 0.48f;
        const float ParcelFirstZ = -1.15f;

        /// <summary>세 단까지 올리고 그 위로는 앞으로 나간다.</summary>
        const int ParcelTiers = 3;
        const float ParcelSpacingZ = 0.46f;

        /// <summary>뜯은 내용물을 상자 기둥에서 이만큼 떼어 내려놓는다. 팔레트 폭의 한 칸이다.</summary>
        const float SpillOffsetX = 0.95f;

        /// <summary>
        /// 쏟는 줄 수. 두 줄이면 x 1.43·2.38 — 팔레트 오른쪽 끝이 2.86 이라 문틈(x 3)에 안 닿는다.
        /// 한 줄에 네 더미라 여덟 종까지 상자 옆에 모인다.
        /// </summary>
        const int SpillColumns = 2;

        public static DeliveryManager Instance { get; private set; }

        [SerializeField] GameObject palletPrefab;

        /// <summary>안 뜯은 상자 모델. 비워 두면 상자 없이 예전처럼 더미만 선다.</summary>
        [SerializeField] GameObject parcelPrefab;

        readonly List<DeliveryStack> stacks = new List<DeliveryStack>();
        readonly List<DeliveryParcel> parcels = new List<DeliveryParcel>();

        /// <summary>
        /// 뜯은 상자가 서 있던 자리. 내용물 더미를 <b>그 자리에</b> 세우려고 적어 둔다 —
        /// 상자를 뜯었는데 물건이 마당 저쪽에 생기면 무슨 일이 일어났는지 안 보인다.
        /// </summary>
        readonly Dictionary<int, Vector3> spilled = new Dictionary<int, Vector3>();

        bool dirty = true;

        public int Count => stacks.Count;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged += MarkDirty;
            Rebuild();
        }

        void OnDestroy()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        void MarkDirty() => dirty = true;

        /// <summary>
        /// 재고가 바뀔 때마다 즉시 다시 세우지 않는다. 하나 집을 때마다 전부 부수고 새로 만들면
        /// 집는 순간 조준하던 콜라이더가 사라져 다음 E가 허공을 짚는다.
        /// </summary>
        void LateUpdate()
        {
            if (!dirty) return;

            dirty = false;
            Rebuild();
        }

        /// <summary>
        /// 상자를 뜯는다. 내용물이 쏟아질 자리를 먼저 적어 두고 비운다 —
        /// 적어 두지 않으면 더미가 격자 순서대로 엉뚱한 데 선다.
        /// </summary>
        public bool Open(DeliveryParcel parcel)
        {
            if (parcel == null || InventoryManager.Instance == null) return false;

            int product = parcel.ProductIndex;
            if (!InventoryManager.Instance.OpenParcel(parcel.Id)) return false;

            // 같은 상품 더미가 이미 있으면 거기에 합친다
            if (!spilled.ContainsKey(product)) spilled[product] = FreeSpillSpot();
            MarkDirty();
            return true;
        }

        /// <summary>
        /// 쏟을 자리. 상자 기둥 <b>옆</b>에 팔레트 간격으로 줄을 선다.
        ///
        /// 뜯은 상자 자리를 그대로 쓰면 두 가지가 어긋난다 — 위에 남은 상자가 더미를 뚫고 서고,
        /// 상자는 0.46m 간격인데 팔레트는 0.75m 깊이라 이웃한 더미끼리 한 판으로 겹친다.
        /// 벽에서 한 칸 떨어진 줄을 앞에서부터 채우고, 두 줄이 다 차면 격자로 넘어간다.
        /// </summary>
        Vector3 FreeSpillSpot()
        {
            for (int column = 0; column < SpillColumns; column++)
                for (int row = 0; row < RowsPerBlock; row++)
                {
                    Vector3 candidate = new Vector3(ParcelX + SpillOffsetX + column * SpacingX, 0f,
                                                    ParcelFirstZ - row * SpacingZ);
                    if (!SpillTaken(candidate)) return candidate;
                }

            // 두 줄이 다 찼으면 오른쪽 구석 격자에 맡긴다
            return PositionOf(PerRow * RowsPerBlock + spilled.Count);
        }

        bool SpillTaken(Vector3 spot)
        {
            foreach (var pair in spilled)
                if ((pair.Value - spot).sqrMagnitude < 0.01f) return true;
            return false;
        }

        void Rebuild()
        {
            InventoryManager inventory = InventoryManager.Instance;
            if (palletPrefab == null || inventory == null) return;

            ProductCatalog catalog = inventory.Catalog;

            RebuildParcels(inventory, catalog);

            // 배달이 남은 상품만, 카탈로그 순서대로 자리를 잡는다
            int placed = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (inventory.DeliveredOf(i) <= 0) continue;

                DeliveryStack stack = FindFor(i);
                bool fresh = stack == null;
                if (fresh) stack = Create(i, catalog.Get(i).nameKo);

                // 상자를 뜯어서 생긴 더미는 그 자리에 둔다. 나머지는 격자로
                Vector3 spot;
                if (fresh && spilled.TryGetValue(i, out spot)) stack.transform.localPosition = spot;
                else if (fresh) stack.transform.localPosition = PositionOf(placed);

                placed++;
            }

            // 다 나른 더미는 치운다
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                if (stacks[i] != null && inventory.DeliveredOf(stacks[i].ProductIndex) > 0) continue;

                if (stacks[i] != null) Destroy(stacks[i].gameObject);
                spilled.Remove(stacks[i] != null ? stacks[i].ProductIndex : -1);
                stacks.RemoveAt(i);
            }
        }

        /// <summary>
        /// 안 뜯은 상자를 왼쪽 벽에 세 단까지 쌓고, 그 위로는 앞으로 나간다.
        /// 목록 순서를 그대로 쓰므로 먼저 온 상자가 아래에 깔린다.
        /// </summary>
        void RebuildParcels(InventoryManager inventory, ProductCatalog catalog)
        {
            if (parcelPrefab == null) return;

            // 뜯겨서 사라진 상자를 치운다
            for (int i = parcels.Count - 1; i >= 0; i--)
            {
                if (parcels[i] != null && Alive(inventory, parcels[i].Id)) continue;

                if (parcels[i] != null) Destroy(parcels[i].gameObject);
                parcels.RemoveAt(i);
            }

            for (int i = 0; i < inventory.ParcelCount; i++)
            {
                int id = inventory.ParcelIdAt(i);
                int product = inventory.ParcelProductAt(i);

                DeliveryParcel box = FindParcel(id);
                if (box == null)
                {
                    GameObject instance = Instantiate(parcelPrefab, transform);
                    instance.name = "Parcel_" + catalog.Get(product).nameKo;
                    box = instance.GetComponent<DeliveryParcel>();
                    if (box == null) box = instance.AddComponent<DeliveryParcel>();
                    box.Bind(id, product);
                    parcels.Add(box);
                }

                box.transform.localPosition = ParcelPositionOf(i);
            }
        }

        static bool Alive(InventoryManager inventory, int id)
        {
            for (int i = 0; i < inventory.ParcelCount; i++)
                if (inventory.ParcelIdAt(i) == id) return true;
            return false;
        }

        DeliveryParcel FindParcel(int id)
        {
            for (int i = 0; i < parcels.Count; i++)
                if (parcels[i] != null && parcels[i].Id == id) return parcels[i];
            return null;
        }

        /// <summary>세 단을 채우고 앞으로 한 칸 나간다.</summary>
        static Vector3 ParcelPositionOf(int slot)
        {
            int tier = slot % ParcelTiers;
            int column = slot / ParcelTiers;
            return new Vector3(ParcelX,
                               ParcelSize * (0.5f + tier),
                               ParcelFirstZ - column * ParcelSpacingZ);
        }

        /// <summary>
        /// 왼쪽 구석을 세 칸씩 네 줄로 채우고, 다 차면 오른쪽 구석으로 넘어간다.
        ///
        /// 한 구석을 끝없이 뒤로 늘이면 울타리(z -4.5)를 넘어간다 — 상품 스무 종이
        /// 동시에 배달되는 L10 에서 실제로 일곱 더미가 마당 밖에 섰다.
        /// 두 구석 합쳐 24 자리라 카탈로그 전체(20종)가 한꺼번에 와도 받는다.
        /// </summary>
        static Vector3 PositionOf(int slot)
        {
            int perBlock = PerRow * RowsPerBlock;
            int block = Mathf.Min(slot / perBlock, 1);   // 그 이상은 오른쪽 구석에 겹쳐 쌓는다
            int local = slot - block * perBlock;

            float originX = block == 0 ? FirstX : RightBlockX;
            int column = local % PerRow;
            int row = (local / PerRow) % RowsPerBlock;

            return new Vector3(originX + column * SpacingX, 0f, FirstZ - row * SpacingZ);
        }

        DeliveryStack Create(int productIndex, string nameKo)
        {
            GameObject instance = Instantiate(palletPrefab, transform);
            instance.name = "Delivery_" + nameKo;

            DeliveryStack stack = instance.GetComponent<DeliveryStack>();
            stack.Bind(productIndex);

            int captured = productIndex;
            ProductSlotDisplay display = instance.GetComponent<ProductSlotDisplay>();
            if (display != null)
                display.Configure(captured,
                    () => InventoryManager.Instance.DeliveredOf(captured),
                    PalletTopY, PalletWidth, PalletDepth,
                    Tiers, TierSpacing, MaxVisible);

            stacks.Add(stack);
            return stack;
        }

        public DeliveryStack FindFor(int productIndex)
        {
            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i] != null && stacks[i].ProductIndex == productIndex) return stacks[i];
            return null;
        }
    }
}
