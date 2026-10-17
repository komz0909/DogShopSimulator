using System.Collections.Generic;
using DogShop.Core;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가구를 사고 받는다. 상품과 <b>같은 리듬</b>이다 — 오늘 주문하면 내일 아침 가게 앞에 온다.
    ///
    /// 다만 가구는 개수가 아니라 <b>물건 그 자체</b>로 온다. 앞마당에 실물이 서 있고,
    /// 플레이어가 배치 모드(F)로 들어 가게 안에 놓는다. 그래서 더미도 팔레트도 쓰지 않는다.
    ///
    /// 산 가구는 세이브에 남는다. 안 남기면 진열대를 사서 놓아 둔 가게가 불러올 때마다
    /// 처음 두 개로 돌아간다.
    /// </summary>
    public class FurnitureShop : MonoBehaviour, ISaveParticipant
    {
        /// <summary>배달 온 가구가 서는 자리. 앞마당 오른쪽을 쓴다(배달 더미는 왼쪽부터 찬다).</summary>
        static readonly Vector3 DropSpot = new Vector3(6.6f, 0f, -2.2f);
        const float DropSpacing = 1.5f;

        public static FurnitureShop Instance { get; private set; }

        [SerializeField] FurnitureCatalog catalog;

        /// <summary>대금을 낸 뒤 아직 안 온 것. 카탈로그 번호가 줄줄이 들어간다.</summary>
        readonly List<int> ordered = new List<int>();

        public FurnitureCatalog Catalog => catalog;
        public int OrderedCount => ordered.Count;

        /// <summary>배달을 기다리는 가구의 카탈로그 번호. 오는 물건이 어떤 칸을 달고 오는지 보려고.</summary>
        public int OrderedAt(int i) => i >= 0 && i < ordered.Count ? ordered[i] : -1;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += Deliver;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= Deliver;
            if (Instance == this) Instance = null;
        }

        public bool IsUnlocked(int index) =>
            catalog != null && index >= 0 && index < catalog.Count
            && catalog.Get(index).unlockLevel <= ShopLevelManager.Instance.Level;

        /// <summary>그 가구를 아직 살 수 있는가. 놓을 프리팹이 없으면 못 산다.</summary>
        public bool CanBuy(int index, out string reason)
        {
            if (catalog == null || index < 0 || index >= catalog.Count) { reason = "없는 가구"; return false; }

            FurnitureDef def = catalog.Get(index);
            if (def.placedPrefab == null) { reason = "아직 들여놓을 수 없다"; return false; }
            if (!IsUnlocked(index)) { reason = "Lv " + def.unlockLevel + " 부터"; return false; }
            if (OneOnly(index) && OwnedCount(index) > 0) { reason = "하나만 둘 수 있다 (이미 있다)"; return false; }
            if (GameManager.Instance.Money < def.price)
            {
                reason = "재화 부족: " + GameManager.Instance.Money + " / " + def.price;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 명성 장식 가구(<see cref="FurnitureDef.IsDecor"/>)는 <b>종류마다 하나만</b> 살 수 있다.
        /// 같은 장식을 여러 개 깔아 명성을 불리는 대신, 여러 종류를 모으게 한다.
        /// </summary>
        public bool OneOnly(int index) => catalog != null && index >= 0 && index < catalog.Count && catalog.Get(index).IsDecor;

        /// <summary>그 가구를 몇 개 가졌나 — 매장·앞마당에 서 있는 것과 배달을 기다리는 주문까지.</summary>
        public int OwnedCount(int index)
        {
            int n = 0;
            foreach (BoughtFurniture bought in FindObjectsByType<BoughtFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (bought.CatalogIndex == index) n++;
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i] == index) n++;
            return n;
        }

        public bool TryBuy(int index)
        {
            string reason;
            if (!CanBuy(index, out reason)) return false;
            if (!GameManager.Instance.TrySpend(catalog.Get(index).price, GameManager.SpendKind.Furniture)) return false;

            ordered.Add(index);
            return true;
        }

        /// <summary>장바구니에 담긴 가구의 값. 첨자는 카탈로그 번호다.</summary>
        public int CostOf(IList<int> cart)
        {
            if (cart == null || catalog == null) return 0;

            int sum = 0;
            for (int i = 0; i < cart.Count && i < catalog.Count; i++)
            {
                if (cart[i] <= 0) continue;
                sum += catalog.Get(i).price * cart[i];
            }
            return sum;
        }

        /// <summary>
        /// 담아 둔 가구를 한 번에 산다. 상품 장바구니와 같은 이유로 값을 <b>통째로</b> 치른다 —
        /// 하나씩 빼면 중간에 돈이 떨어져 절반만 주문된 채로 끝난다.
        /// </summary>
        public bool TryBuyCart(IList<int> cart, out string reason)
        {
            int cost = CostOf(cart);
            if (cost <= 0) { reason = "담은 가구가 없다"; return false; }

            for (int i = 0; i < cart.Count && i < catalog.Count; i++)
            {
                if (cart[i] <= 0) continue;

                string why;
                if (!CanBuy(i, out why)) { reason = catalog.Get(i).nameKo + ": " + why; return false; }
                if (OneOnly(i) && cart[i] > 1) { reason = catalog.Get(i).nameKo + ": 하나만 둘 수 있다"; return false; }
            }

            if (!GameManager.Instance.TrySpend(cost, GameManager.SpendKind.Furniture)) { reason = "결제 실패"; return false; }

            for (int i = 0; i < cart.Count && i < catalog.Count; i++)
                for (int n = 0; n < cart[i]; n++) ordered.Add(i);

            reason = null;
            return true;
        }

        /// <summary>아침. 주문한 가구를 앞마당에 세운다.</summary>
        void Deliver()
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                GameObject placed = Spawn(ordered[i], DropAt(i));
                // 창고 선반은 원점이 몸통 가운데라 바닥 높이에 그대로 세우면 반쯤 묻힌다
                if (placed != null && placed.GetComponent<StorageRack>() != null)
                    placed.transform.position += Vector3.up * StorageRack.PivotHeight;
            }
            ordered.Clear();
        }

        /// <summary>같은 날 여러 개를 시키면 겹치지 않게 옆으로 늘어놓는다.</summary>
        static Vector3 DropAt(int slot) => DropSpot + Vector3.left * (slot % 4) * DropSpacing
                                                    + Vector3.back * (slot / 4) * DropSpacing;

        /// <summary>
        /// 가구 하나를 세운다. 진열대라면 <see cref="ShelfManager"/>에 등록해야 손님이 쓴다 —
        /// 등록을 빼먹으면 물건은 올라가는데 아무도 사러 오지 않는다.
        /// </summary>
        public GameObject Spawn(int index, Vector3 position, Quaternion? rotation = null)
        {
            if (catalog == null || index < 0 || index >= catalog.Count) return null;

            FurnitureDef def = catalog.Get(index);
            if (def.placedPrefab == null) return null;

            GameObject instance = Instantiate(def.placedPrefab, position, rotation ?? Quaternion.identity);
            instance.name = def.placedPrefab.name + "_" + index;

            BoughtFurniture tag = instance.GetComponent<BoughtFurniture>();
            if (tag == null) tag = instance.AddComponent<BoughtFurniture>();
            tag.Bind(index);

            ShelfTable table = instance.GetComponent<ShelfTable>();
            if (table != null && ShelfManager.Instance != null) ShelfManager.Instance.Register(table);

            return instance;
        }

        // ---- 세이브 ----

        public void CaptureInto(SaveData data)
        {
            data.furnitureOrdered = ordered.ToArray();

            Player.FurniturePlacer placer = FindAnyObjectByType<Player.FurniturePlacer>();

            List<FurnitureSave> placed = new List<FurnitureSave>();
            foreach (BoughtFurniture bought in FindObjectsByType<BoughtFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Vector3 position;
                Quaternion rotation;
                SpotOf(bought.GetComponent<PlaceableFurniture>(), bought.transform, placer, out position, out rotation);
                placed.Add(new FurnitureSave
                {
                    catalogIndex = bought.CatalogIndex,
                    position = position,
                    rotation = rotation.eulerAngles
                });
            }
            data.furniturePlaced = placed.ToArray();

            // 씬에 원래 있던 가구(계산대 등)는 자리만 남긴다
            List<SceneFurnitureSave> scene = new List<SceneFurnitureSave>();
            foreach (PlaceableFurniture furniture in FindObjectsByType<PlaceableFurniture>(FindObjectsSortMode.None))
            {
                if (furniture.GetComponent<BoughtFurniture>() != null) continue;
                Vector3 position;
                Quaternion rotation;
                SpotOf(furniture, furniture.transform, placer, out position, out rotation);
                scene.Add(new SceneFurnitureSave { name = KeyOf(furniture.transform), position = position, rotation = rotation.eulerAngles });
            }
            data.sceneFurniture = scene.ToArray();
        }

        /// <summary>
        /// 씬 가구를 찾는 열쇠 = 계층 경로(형제 순번 포함). 이름만으로는 기본 진열대처럼 같은 이름이 둘이면 구분이 안 된다.
        /// 산 가구가 아니므로 계층은 씬 그대로라 매번 같은 값이 나온다.
        /// </summary>
        static string KeyOf(Transform t)
        {
            string key = t.name + "#" + t.GetSiblingIndex();
            for (Transform p = t.parent; p != null; p = p.parent) key = p.name + "#" + p.GetSiblingIndex() + "/" + key;
            return key;
        }

        /// <summary>저장할 자리. 들고 옮기는 중이면 집어 들기 전 자리다.</summary>
        static void SpotOf(PlaceableFurniture furniture, Transform t, Player.FurniturePlacer placer, out Vector3 position, out Quaternion rotation)
        {
            if (placer != null && furniture != null && placer.TryGetHome(furniture, out position, out rotation)) return;
            position = t.position;
            rotation = t.rotation;
        }

        /// <summary>씬 가구를 저장된 자리로 되돌린다. 계산대를 옮겼으면 길찾기를 다시 굽고 줄을 다시 세운다.</summary>
        static void RestoreSceneFurniture(SaveData data)
        {
            if (data.sceneFurniture == null || data.sceneFurniture.Length == 0) return;

            bool counterMoved = false;
            foreach (PlaceableFurniture furniture in FindObjectsByType<PlaceableFurniture>(FindObjectsSortMode.None))
            {
                if (furniture.GetComponent<BoughtFurniture>() != null) continue;
                string key = KeyOf(furniture.transform);
                for (int i = 0; i < data.sceneFurniture.Length; i++)
                {
                    SceneFurnitureSave saved = data.sceneFurniture[i];
                    if (saved == null || saved.name != key) continue;

                    Quaternion rotation = Quaternion.Euler(saved.rotation);
                    if ((furniture.transform.position - saved.position).sqrMagnitude < 1e-4f
                        && Quaternion.Angle(furniture.transform.rotation, rotation) < 0.5f) break;

                    furniture.transform.SetPositionAndRotation(saved.position, rotation);
                    if (ShelfManager.Instance != null) ShelfManager.Instance.RefreshApproach(furniture);
                    if (furniture.GetComponent<ShopCounter>() != null) counterMoved = true;
                    break;
                }
            }

            if (!counterMoved) return;
            if (ShopSpace.Instance != null) ShopSpace.Instance.Rebake();
            if (CustomerManager.Instance != null) CustomerManager.Instance.RefreshQueue();
        }

        public void RestoreFrom(SaveData data)
        {
            ordered.Clear();
            if (data.furnitureOrdered != null) ordered.AddRange(data.furnitureOrdered);
            RestoreSceneFurniture(data);

            // 지금 서 있는 산 가구를 걷어내고 세이브대로 다시 세운다.
            // 자리만 옮기면 개수가 다를 때 맞지 않는다.
            foreach (BoughtFurniture bought in FindObjectsByType<BoughtFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ShelfTable table = bought.GetComponent<ShelfTable>();
                if (table != null && ShelfManager.Instance != null) ShelfManager.Instance.Unregister(table);
                Destroy(bought.gameObject);
            }

            if (data.furniturePlaced == null) return;

            // v3 이하: 가게가 x 0~폭 으로 오른쪽으로만 넓어졌다. 이제는 문(x 4)을 가운데 두고 양쪽으로 넓어지므로
            // 판매장 가구를 (4 - 폭/2) 만큼 옮긴다 — 안 옮기면 넓은 가게 세이브의 오른쪽 가구가 새 오른쪽 벽 밖에 섰다
            float shiftX = 0f;
            if (data.version < 4 && ShopLevelManager.Instance != null)
                shiftX = ShopSpace.CenterX - ShopLevelManager.Instance.FloorWidthAt(data.shopLevel) * 0.5f;

            for (int i = 0; i < data.furniturePlaced.Length; i++)
            {
                FurnitureSave saved = data.furniturePlaced[i];
                Vector3 position = saved.position;
                if (shiftX != 0f && position.z > -0.5f && position.z < 6f) position.x += shiftX;
                Spawn(saved.catalogIndex, position, Quaternion.Euler(saved.rotation));
            }
        }
    }
}
