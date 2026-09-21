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
            if (GameManager.Instance.Money < def.price)
            {
                reason = "재화 부족 — " + GameManager.Instance.Money + " / " + def.price;
                return false;
            }

            reason = null;
            return true;
        }

        public bool TryBuy(int index)
        {
            string reason;
            if (!CanBuy(index, out reason)) return false;
            if (!GameManager.Instance.TrySpend(catalog.Get(index).price)) return false;

            ordered.Add(index);
            return true;
        }

        /// <summary>아침. 주문한 가구를 앞마당에 세운다.</summary>
        void Deliver()
        {
            for (int i = 0; i < ordered.Count; i++) Spawn(ordered[i], DropAt(i));
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

            List<FurnitureSave> placed = new List<FurnitureSave>();
            foreach (BoughtFurniture bought in FindObjectsByType<BoughtFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                placed.Add(new FurnitureSave
                {
                    catalogIndex = bought.CatalogIndex,
                    position = bought.transform.position,
                    rotation = bought.transform.eulerAngles
                });
            }
            data.furniturePlaced = placed.ToArray();
        }

        public void RestoreFrom(SaveData data)
        {
            ordered.Clear();
            if (data.furnitureOrdered != null) ordered.AddRange(data.furnitureOrdered);

            // 지금 서 있는 산 가구를 걷어내고 세이브대로 다시 세운다.
            // 자리만 옮기면 개수가 다를 때 맞지 않는다.
            foreach (BoughtFurniture bought in FindObjectsByType<BoughtFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ShelfTable table = bought.GetComponent<ShelfTable>();
                if (table != null && ShelfManager.Instance != null) ShelfManager.Instance.Unregister(table);
                Destroy(bought.gameObject);
            }

            if (data.furniturePlaced == null) return;

            for (int i = 0; i < data.furniturePlaced.Length; i++)
            {
                FurnitureSave saved = data.furniturePlaced[i];
                Spawn(saved.catalogIndex, saved.position, Quaternion.Euler(saved.rotation));
            }
        }
    }
}
