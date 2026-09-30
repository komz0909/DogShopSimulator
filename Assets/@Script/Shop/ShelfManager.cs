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
        public const float ShelfHalfHeight = 0.396f;

        public const float NearMultiplier = 1.5f;
        public const float FarMultiplier = 1.0f;

        public static ShelfManager Instance { get; private set; }

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
            // 진열대는 이제 <b>씬에 놓이거나 플레이어가 사서 배치한다</b>.
            // 레벨이 오를 때마다 상품별로 자동 생성하던 방식은 자유 배치와 맞지 않는다.
            foreach (ShelfTable table in FindObjectsByType<ShelfTable>(FindObjectsSortMode.None))
                Register(table);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 가구를 옮긴 뒤 그 진열대의 접근점과 입지 가중치를 다시 잡는다.
        /// 자유 배치라 통로가 어디 생길지 미리 알 수 없으므로 NavMesh에 직접 물어본다.
        /// </summary>
        public void RefreshApproach(PlaceableFurniture furniture)
        {
            if (furniture == null) return;

            ShelfTable table = furniture.GetComponent<ShelfTable>();
            if (table == null) return;

            Register(table);
            table.SetApproach(furniture.FindApproach(Entrance), ProximityFor(furniture.transform.position));
        }

        /// <summary>칸 상태를 세이브에 담는다. 진열대마다 칸 수가 다르므로 한 줄로 이어 붙인다.</summary>
        public void CaptureInto(DogShop.Core.SaveData data)
        {
            int total = 0;
            for (int i = 0; i < tables.Count; i++) total += tables[i].SlotCount;

            data.shelfSlotProduct = new int[total];
            data.shelfSlotStock = new int[total * (DogShop.Data.ItemGrades.Count + 1)];

            // 옛 칸(칸마다 등급 하나)은 더 쓰지 않는다. 옛 세이브를 읽을 때만 본다
            data.shelfSlotCount = new int[0];
            data.shelfSlotGrade = new int[0];

            int offset = 0;
            for (int i = 0; i < tables.Count; i++)
            {
                tables[i].CaptureInto(data.shelfSlotProduct, data.shelfSlotStock, offset);
                offset += tables[i].SlotCount;
            }
        }

        public void RestoreFrom(DogShop.Core.SaveData data)
        {
            if (data.shelfSlotProduct == null || data.shelfSlotCount == null) return;

            int offset = 0;
            for (int i = 0; i < tables.Count; i++)
            {
                tables[i].RestoreFrom(data.shelfSlotProduct, data.shelfSlotStock,
                                      data.shelfSlotCount, data.shelfSlotGrade, offset);
                offset += tables[i].SlotCount;
            }
        }

        /// <summary>
        /// 입구에서 멀수록 덜 팔린다. 배치가 자유로워졌으니 거리로만 판정한다.
        ///
        /// 기준 거리는 <b>매장 크기에서 뽑는다</b>. 6m 로 박아 두면, 가게를 넓힌 뒤
        /// 오른쪽 끝에 놓은 진열대가 전부 최저 배수로 떨어져 확장이 벌칙이 된다.
        /// </summary>
        float ProximityFor(Vector3 position)
        {
            float width = ShopSpace.Instance != null ? ShopSpace.Instance.Width : 8f;
            float far = new Vector2(width, 6f).magnitude * 0.5f;   // 매장 대각선의 절반

            float t = Mathf.Clamp01(Vector3.Distance(new Vector3(position.x, 0f, position.z), Entrance) / far);
            return Mathf.Lerp(NearMultiplier, FarMultiplier, t);
        }

        /// <summary>
        /// 그 상품이 <b>실제로 올려져 있는</b> 진열대. 칸이 비면 그 상품은 찾는 사람도 없다.
        ///
        /// 손님이 못 가는 자리(창고·옆방·가게 앞)에 있는 진열대는 <b>없는 것으로 친다</b>.
        /// 거기로 손님을 보내면 길을 못 찾고 인내심이 다 될 때까지 헤매다 나간다.
        /// </summary>
        public ShelfTable FindFor(int productIndex)
        {
            for (int i = 0; i < tables.Count; i++)
                if (tables[i].CustomersCanReach && tables[i].TotalOf(productIndex) > 0) return tables[i];
            return null;
        }

        /// <summary>
        /// 런타임에 산 진열대를 목록에 넣고 <b>접근점까지 잡는다</b>.
        /// 목록에만 넣으면 접근점이 원점에 남아 손님이 가게 밖으로 걸어간다.
        /// </summary>
        public void Register(ShelfTable table)
        {
            if (table == null || tables.Contains(table)) return;

            tables.Add(table);

            PlaceableFurniture furniture = table.GetComponent<PlaceableFurniture>();
            if (furniture == null) return;

            table.SetApproach(furniture.FindApproach(Entrance), ProximityFor(table.transform.position));
        }

        public void Unregister(ShelfTable table) => tables.Remove(table);

        // ---- 칸 배정 ----
        //
        // 규칙 하나다: <b>한 칸도 못 받은 상품이 남아 있으면 둘째 칸을 가져갈 수 없다.</b>
        //
        // 칸은 상품이 올라간 순간 그 상품에 잠기고 다 팔려야 풀린다. 그래서 먼저 채우는 쪽이
        // 빈 칸을 계속 집어삼키면 늦게 채우는 상품은 영영 자리를 못 받는데, 진열되지 않은 상품은
        // 손님조차 생기지 않아 그 수요가 통째로 사라진다. 칸 9개에 상품 9종이던 25차 측정에서
        // 손실률이 37.6%였던 이유가 이것이다.
        //
        // 상품마다 한 칸씩 먼저 잡아 두고 <b>남는 칸만</b> 나눠 준다. 남는 칸의 분배는 따로
        // 계산하지 않는다 — 발주량이 이미 수요 비례라(<see cref="InventoryManager.DemandTarget"/>)
        // 많이 팔리는 상품이 자연히 먼저 넘쳐 둘째 칸을 쓴다.

        /// <summary>빈 칸 계산 결과. 부피 조건(0=아무거나 1=작은 것 2=큰 것)별로 나눠 센다.</summary>
        struct SlotSupply
        {
            public int emptyAny, emptySmall, emptyBulky;    // 지금 비어 있는 칸
            public int leftAny, leftSmall, leftBulky;       // 굶는 상품들에게 한 칸씩 준 뒤 남는 칸
            public bool mineStarving;                       // 물어본 상품도 한 칸도 못 받았는가
        }

        /// <summary>
        /// 빈 칸을 세고, 아직 한 칸도 못 받은 <b>다른</b> 상품들에게 한 칸씩 떼어 준 뒤 남는 양을 구한다.
        ///
        /// 전용 칸(작은 것만 / 큰 것만)부터 먼저 쓰게 배정한다 — 아무거나 받는 칸은 양쪽 모두가
        /// 쓸 수 있어 가장 아까우므로 마지막에 돌린다.
        /// </summary>
        SlotSupply Supply(int productIndex)
        {
            SlotSupply s = new SlotSupply();

            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return s;

            int[] held = new int[inv.Catalog.Count];
            for (int t = 0; t < tables.Count; t++)
            {
                ShelfTable table = tables[t];
                for (int i = 0; i < table.SlotCount; i++)
                {
                    int p = table.ProductAt(i);
                    if (p >= 0)
                    {
                        if (p < held.Length) held[p]++;
                        continue;
                    }

                    if (table.AcceptedBulk == 1) s.emptySmall++;
                    else if (table.AcceptedBulk == 2) s.emptyBulky++;
                    else s.emptyAny++;
                }
            }

            int level = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Level : 1;

            int starveSmall = 0, starveBulky = 0;
            for (int p = 0; p < inv.Catalog.Count; p++)
            {
                if (p == productIndex) continue;
                if (inv.Catalog.Get(p).unlockLevel > level) continue;
                if (held[p] > 0) continue;

                if (CarryCrate.SlotCostOf(p) == 2) starveBulky++;
                else starveSmall++;
            }

            s.mineStarving = productIndex >= 0 && productIndex < held.Length && held[productIndex] == 0;

            int useSmall = Mathf.Min(starveSmall, s.emptySmall);
            int useBulky = Mathf.Min(starveBulky, s.emptyBulky);

            s.leftSmall = s.emptySmall - useSmall;
            s.leftBulky = s.emptyBulky - useBulky;
            s.leftAny = Mathf.Max(0, s.emptyAny - (starveSmall - useSmall) - (starveBulky - useBulky));
            return s;
        }

        /// <summary>
        /// 그 상품이 <paramref name="acceptedBulk"/> 조건의 빈 칸을 하나 가져가도 되는가.
        /// </summary>
        public bool CanClaimEmptySlot(int productIndex, int acceptedBulk)
        {
            if (InventoryManager.Instance == null) return true;

            int cost = CarryCrate.SlotCostOf(productIndex);
            if (acceptedBulk != 0 && acceptedBulk != cost) return false;

            SlotSupply s = Supply(productIndex);

            int left = acceptedBulk == 0 ? s.leftAny : (cost == 2 ? s.leftBulky : s.leftSmall);
            if (left > 0) return true;

            // 남는 칸이 없다. 이미 자리를 받은 상품은 여기서 끝이다
            if (!s.mineStarving) return false;

            // 나도 한 칸도 못 받았다 — 굶는 상품끼리는 먼저 온 쪽이 갖는다.
            // 다만 전용 칸이 비어 있으면 그쪽을 쓴다. 아무거나 받는 칸을 먼저 집으면
            // 반대쪽 부피의 상품이 갈 곳을 잃는다
            int dedicated = cost == 2 ? s.emptyBulky : s.emptySmall;
            if (acceptedBulk == 0) return dedicated <= 0 && s.emptyAny > 0;
            return dedicated > 0;
        }

        /// <summary>
        /// 승급해서 새 상품이 열리면, 칸을 둘 이상 쥐고 있던 상품에게서 한 칸씩 걷는다.
        ///
        /// 칸은 <b>다 팔려야</b> 풀린다(<see cref="ShelfTable.Take"/>). 그래서 여유 있던 시절에
        /// 둘째 칸까지 차지한 상품이 그 칸을 며칠씩 깔고 앉고, 새로 열린 상품은 자리가 나기만
        /// 기다린다 — 28차 측정에서 L4 승급일부터 사흘 동안 손님을 놓친 것이 이것이다.
        ///
        /// 걷은 물건은 <b>창고로 돌아간다.</b> 버리지 않으므로 손해는 없고,
        /// 다음 진열 때 자기 칸으로 다시 올라간다.
        /// </summary>
        public void ReleaseSurplus()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            int guard = 0;
            while (guard++ < 64)
            {
                int needy = FirstStranded(inv);
                if (needy < 0) return;
                if (!FreeOneSlotFor(inv, needy)) return;
            }
        }

        /// <summary>해금됐는데 칸도 없고 <b>빈 칸조차 없는</b> 상품. 빈 칸이 있으면 알아서 받는다.</summary>
        int FirstStranded(InventoryManager inv)
        {
            int level = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Level : 1;

            for (int p = 0; p < inv.Catalog.Count; p++)
            {
                if (inv.Catalog.Get(p).unlockLevel > level) continue;

                SlotSupply s = Supply(p);
                if (!s.mineStarving) continue;

                int cost = CarryCrate.SlotCostOf(p);
                if (s.emptyAny + (cost == 2 ? s.emptyBulky : s.emptySmall) > 0) continue;

                return p;
            }
            return -1;
        }

        /// <summary>칸을 둘 이상 쥔 상품에게서 <b>가장 적게 남은</b> 칸을 비워 창고로 돌린다.</summary>
        bool FreeOneSlotFor(InventoryManager inv, int needy)
        {
            int cost = CarryCrate.SlotCostOf(needy);

            int[] held = new int[inv.Catalog.Count];
            for (int t = 0; t < tables.Count; t++)
                for (int i = 0; i < tables[t].SlotCount; i++)
                {
                    int p = tables[t].ProductAt(i);
                    if (p >= 0 && p < held.Length) held[p]++;
                }

            ShelfTable bestTable = null;
            int bestSlot = -1, bestStock = int.MaxValue;

            for (int t = 0; t < tables.Count; t++)
            {
                ShelfTable table = tables[t];
                if (table.AcceptedBulk != 0 && table.AcceptedBulk != cost) continue;

                for (int i = 0; i < table.SlotCount; i++)
                {
                    int p = table.ProductAt(i);
                    if (p < 0 || p == needy || held[p] < 2) continue;
                    if (table.CountAt(i) >= bestStock) continue;

                    bestTable = table;
                    bestSlot = i;
                    bestStock = table.CountAt(i);
                }
            }

            if (bestTable == null) return false;

            int product = bestTable.ProductAt(bestSlot);
            int moved = 0;
            while (bestTable.Take(bestSlot)) moved++;
            if (moved > 0) inv.Store(product, moved);
            return true;
        }

        /// <summary>그 부피의 빈 칸을 기다리는 상품 이름. 거절 사유에 쓴다.</summary>
        public string WaitingName(int acceptedBulk)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return null;

            int level = ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Level : 1;

            for (int p = 0; p < inv.Catalog.Count; p++)
            {
                if (inv.Catalog.Get(p).unlockLevel > level) continue;

                int cost = CarryCrate.SlotCostOf(p);
                if (acceptedBulk != 0 && acceptedBulk != cost) continue;
                if (Supply(p).mineStarving) return inv.Catalog.Get(p).nameKo;
            }
            return null;
        }

        /// <summary>그 상품을 더 받을 수 있는 여유. <b>가져가도 되는 빈 칸까지만</b> 센다.</summary>
        public int RoomFor(int productIndex)
        {
            if (InventoryManager.Instance == null) return 0;

            SlotSupply s = Supply(productIndex);
            int cost = CarryCrate.SlotCostOf(productIndex);

            int budget = s.leftAny + (cost == 2 ? s.leftBulky : s.leftSmall);

            // 남는 칸이 없어도 나도 굶는 쪽이면 한 칸은 노려볼 수 있다 (먼저 온 쪽이 갖는다)
            if (budget <= 0 && s.mineStarving)
            {
                int compatible = s.emptyAny + (cost == 2 ? s.emptyBulky : s.emptySmall);
                budget = compatible > 0 ? 1 : 0;
            }

            int room = 0;
            for (int i = 0; i < tables.Count; i++)
            {
                ShelfTable table = tables[i];
                room += table.RoomInOwned(productIndex);

                int take = Mathf.Min(table.EmptySlotsFor(productIndex), budget);
                if (take <= 0) continue;

                budget -= take;
                room += take * table.CapacityFor(productIndex);
            }
            return room;
        }

        public float ProximityMultiplier(int productIndex)
        {
            ShelfTable table = FindFor(productIndex);
            return table != null ? table.ProximityMultiplier : 0f;
        }
    }
}
