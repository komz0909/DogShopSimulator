using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 창고 선반 관리. 발주한 재고가 창고 선반에 실제로 쌓여 보이고, 플레이어가 여기서 상자에 담아 나른다.
    ///
    /// 선반은 <b>기본 4개</b> — 창고 앞벽(가게 쪽) 2개와 뒷벽 2개가 마주 본다. 더 필요하면 가구 상점에서 사서 직접 놓는다.
    /// 선반마다 3단이고 <b>단마다 상품 1종</b>이 들어간다(크기와 상관없이). 어느 단에 무엇을 둘지는 여기서 정한다 —
    /// 창고에 재고가 있는 상품을 해금 순서대로 빈 단부터 채우고(1번 칸 기본 사료, 2번 칸 샴푸 …),
    /// 빈 단이 없으면 재고가 0 이 된 상품의 단을 넘겨받는다. 한 번 놓인 상품은 다 떨어져도 다른 상품이 필요로 할 때까지 그 자리를 지킨다.
    ///
    /// 배달 상자는 E 로 뜯으면 곧장 창고 선반에 정리된다(<see cref="DeliveryManager.OpenIntoStorage"/>).
    /// 그 상품이 들어갈 단이 없으면(<see cref="CanAccept"/>) 상자를 뜯지 않는다.
    /// </summary>
    public class StorageManager : MonoBehaviour
    {
        /// <summary>
        /// 기본 선반 자리(x, z)와 정면 yaw. 앞벽 둘이 뒷벽 둘과 마주 본다.
        /// 창고는 x 0~8, z 6.2(가게와의 벽 안쪽)~10. 앞벽 가운데(x 3.2~4.8)는 가게로 가는 통로, 오른쪽(x 5.6~7.2)은 포장대라 비운다.
        /// </summary>
        static readonly Vector3[] BaseSpots =
        {
            new Vector3(0.9f, FrontZ, 180f), new Vector3(2.2f, FrontZ, 180f),
            new Vector3(0.9f, BackZ, 0f),    new Vector3(2.2f, BackZ, 0f),
        };

        /// <summary>
        /// 시연 모드에서 더 세우는 선반 자리. 뒷벽 오른쪽(x 3~8)은 비어 있다 — 앞벽 쪽은 통로와 포장대라 못 쓴다.
        /// </summary>
        static readonly Vector3[] ExhibitionSpots =
        {
            new Vector3(3.5f, BackZ, 0f), new Vector3(4.8f, BackZ, 0f), new Vector3(6.1f, BackZ, 0f), new Vector3(7.4f, BackZ, 0f),
        };

        /// <summary>선반 깊이(0.42)의 절반 + 벽과 틈. 앞벽 안쪽 면은 z 6.2, 뒷벽 안쪽 면은 z 10.</summary>
        const float FrontZ = 6.2f + 0.23f;
        const float BackZ = 10f - 0.23f;

        public static StorageManager Instance { get; private set; }

        [SerializeField] GameObject rackPrefab;

        /// <summary>기본 선반이 먼저, 산 선반은 놓인 순서대로. 빈 단은 이 순서로 채운다.</summary>
        readonly List<StorageRack> racks = new List<StorageRack>();

        /// <summary>상품을 해금 레벨 순(같으면 카탈로그 순)으로 늘어놓은 것. 빈 단을 이 순서로 채운다.</summary>
        int[] order;
        bool dirty = true;

        public int Count => racks.Count;
        public StorageRack Get(int index) => racks[index];

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (rackPrefab != null)
            {
                // 시연 모드는 Lv 6 상품 13종이 다 들어가게 뒷벽에 4개를 더 세운다
                int extra = Core.Exhibition.Active ? Mathf.Min(Core.Exhibition.ExtraStorageRacks, ExhibitionSpots.Length) : 0;
                for (int i = 0; i < BaseSpots.Length + extra; i++)
                {
                    GameObject instance = Instantiate(rackPrefab, transform);
                    instance.name = "Rack_" + (i + 1);
                    Vector3 spot = i < BaseSpots.Length ? BaseSpots[i] : ExhibitionSpots[i - BaseSpots.Length];
                    instance.transform.localPosition = new Vector3(spot.x, StorageRack.PivotHeight, spot.y);
                    instance.transform.localRotation = Quaternion.Euler(0f, spot.z, 0f);
                    // 기본 선반은 자리가 세이브에 안 남는다 — 옮길 수 있게 두면 불러올 때마다 제자리로 돌아간다. 못 옮기게 한다
                    PlaceableFurniture movable = instance.GetComponent<PlaceableFurniture>();
                    if (movable != null) Destroy(movable);
                    Register(instance.GetComponent<StorageRack>());
                }
            }
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged += MarkDirty;
            Arrange();
        }

        void OnDestroy()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        void MarkDirty() => dirty = true;

        void LateUpdate()
        {
            if (dirty) Arrange();
        }

        public void Register(StorageRack rack)
        {
            if (rack == null || racks.Contains(rack)) return;
            racks.Add(rack);
            dirty = true;
        }

        /// <summary>산 선반이 사라졌다(세이브 복원 등). 그 위 상품은 다음 정리 때 다른 빈 단으로 간다.</summary>
        public void Unregister(StorageRack rack)
        {
            racks.Remove(rack);
            dirty = true;
        }

        static int Stock(int product)
        {
            InventoryManager inv = InventoryManager.Instance;
            return inv.StorageOf(product) + inv.GradedTotalOf(product);
        }

        /// <summary>그 상품을 창고에 들일 수 있나 — 이미 단이 있거나, 빈 단(또는 재고 0 인 단)이 남았거나.</summary>
        public bool CanAccept(int product)
        {
            if (FindFor(product) != null) return true;
            return FreeTier(out _, out _);
        }

        /// <summary>
        /// 그 상품에 단을 <b>지금</b> 잡아 준다(이미 있으면 그대로). 상자를 뜯기 직전에 부른다 —
        /// 확인만 하고 뜯으면 다음 정리에서 해금 순서가 앞선 다른 상품이 그 빈 단을 먼저 가져가, 방금 넣은 물건이 갈 데를 잃었다.
        /// </summary>
        public bool Reserve(int product)
        {
            if (FindFor(product) != null) return true;
            if (!FreeTier(out StorageRack rack, out int tier)) return false;
            rack.SetTier(tier, product);
            return true;
        }

        /// <summary>새 상품이 들어갈 단. 빈 단이 먼저, 없으면 재고가 다 떨어진 상품의 단.</summary>
        bool FreeTier(out StorageRack rack, out int tier)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int r = 0; r < racks.Count; r++)
                    for (int t = 0; t < StorageRack.Tiers; t++)
                    {
                        int p = racks[r].ProductOfTier(t);
                        if (pass == 0 ? p < 0 : Stock(p) <= 0)
                        {
                            rack = racks[r];
                            tier = t;
                            return true;
                        }
                    }
            rack = null;
            tier = -1;
            return false;
        }

        /// <summary>재고가 있는데 단이 없는 상품에 단을 준다. 재고가 바뀔 때마다 돈다.</summary>
        public void Arrange()
        {
            dirty = false;
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            ProductCatalog catalog = inv.Catalog;
            if (order == null || order.Length != catalog.Count) BuildOrder(catalog);

            // 같은 상품이 두 단에 있으면(선반이 사라졌다 돌아오는 등) 뒤의 것을 비운다
            var seen = new HashSet<int>();
            for (int r = 0; r < racks.Count; r++)
                for (int t = 0; t < StorageRack.Tiers; t++)
                {
                    int p = racks[r].ProductOfTier(t);
                    if (p < 0) continue;
                    if (!seen.Add(p)) racks[r].SetTier(t, -1);
                }

            foreach (int product in order)
            {
                if (Stock(product) <= 0 || FindFor(product) != null) continue;
                if (FreeTier(out StorageRack rack, out int tier))
                {
                    rack.SetTier(tier, product);
                    continue;
                }
                // 자리가 없다. 상자 뜯기는 막지만, 진열대 회수·포장 취소·랜덤 상자처럼 창고로 돌아오는 길은 막을 수 없다 —
                // 그 물건은 단이 빌 때까지(다른 상품이 다 떨어지면 저절로) 선반에 안 보인다. 한 번은 알린다
                if (warned.Add(product))
                {
                    var hud = FindAnyObjectByType<UI.GameHud>();
                    if (hud != null) hud.Toast("창고 선반 칸이 모자라 " + catalog.Get(product).nameKo + "을(를) 못 올렸다. 창고 선반을 사거나 비우자", new Color(1f, 0.45f, 0.4f));
                }
            }
            // 자리를 얻은 상품은 다시 모자라면 또 알린다
            warned.RemoveWhere(p => FindFor(p) != null || Stock(p) <= 0);
        }

        readonly HashSet<int> warned = new HashSet<int>();

        /// <summary>
        /// 플레이어가 빈 단을 보고 R 로 상자 물건을 내릴 때 — 그 상품에 아직 단이 없으면 이 단을 준다.
        /// 이미 다른 단에 있으면 그 단을 쓴다(같은 상품을 두 단에 나누지 않는다).
        /// </summary>
        public bool ClaimForStore(StorageRack rack, int tier, int product)
        {
            if (FindFor(product) != null) return true;
            int current = rack.ProductOfTier(tier);
            if (current >= 0 && Stock(current) > 0) return false;
            rack.SetTier(tier, product);
            return true;
        }

        void BuildOrder(ProductCatalog catalog)
        {
            var list = new List<int>();
            for (int i = 0; i < catalog.Count; i++) list.Add(i);
            list.Sort((a, b) =>
            {
                int byLevel = catalog.Get(a).unlockLevel.CompareTo(catalog.Get(b).unlockLevel);
                return byLevel != 0 ? byLevel : a.CompareTo(b);
            });
            order = list.ToArray();
        }

        /// <summary>그 상품이 놓인 선반(없으면 null).</summary>
        public StorageRack FindFor(int productIndex)
        {
            for (int i = 0; i < racks.Count; i++)
                for (int t = 0; t < StorageRack.Tiers; t++)
                    if (racks[i].ProductOfTier(t) == productIndex) return racks[i];
            return null;
        }
    }
}
