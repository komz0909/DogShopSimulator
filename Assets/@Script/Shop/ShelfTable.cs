using System;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 진열대 1개. <b>칸(slot)</b>을 여러 개 가질 수 있고 칸마다 상품 1종만 올린다.
    ///
    /// 한 번 올린 칸은 <b>다 팔리거나 상자로 다 뺄 때까지 다른 상품을 못 받는다</b>.
    /// 재고를 칸이 직접 들고 있으므로 이 게임에서 "진열된 개수"의 유일한 출처다 —
    /// InventoryManager는 전역 합계를 여기서 모아 온다.
    ///
    /// 부피 제한도 칸이 아니라 <b>가구</b>가 정한다. 벽 진열대는 작은 것만,
    /// 아일랜드 진열대는 사료 같은 큰 것만 받는다.
    /// </summary>
    public class ShelfTable : MonoBehaviour
    {
        /// <summary>칸 상판의 로컬 높이. 아래에서 위로 적는다. 길이가 곧 칸 수다.</summary>
        [SerializeField] float[] slotHeights = { 0.40f };

        /// <summary>
        /// 칸 상판의 로컬 <b>앞뒤 중심</b>. 벽 진열대는 널빤지가 뒤판에서 앞으로 뻗어 있어
        /// 오브젝트 원점과 상판 중심이 어긋난다 — 이걸 안 쓰면 물건이 상판 앞으로 삐져나간다.
        /// 비워 두면 0으로 본다.
        /// </summary>
        [SerializeField] float[] slotCenterZ = { 0f };

        /// <summary>
        /// 칸 상판의 로컬 <b>좌우 중심</b>. 한 상판을 좌우로 갈라 칸 둘을 올릴 때 쓴다 —
        /// 아일랜드 진열대가 그렇다(왼쪽 −0.40 / 오른쪽 +0.40). 비워 두면 0으로 본다.
        /// </summary>
        [SerializeField] float[] slotCenterX = { 0f };

        /// <summary>
        /// 받는 부피. 0이면 아무거나, 1이면 <see cref="Data.ProductDef.slotCost"/>가 1인 작은 것만,
        /// 2면 사료처럼 부피 큰 것만 받는다.
        /// </summary>
        [SerializeField] int acceptedBulk;

        [SerializeField] int capacityPerSlot = 8;

        /// <summary>프롭을 늘어놓을 상판 크기.</summary>
        [SerializeField] float slotWidth = 1.0f;
        [SerializeField] float slotDepth = 0.4f;

        int[] product;

        /// <summary>
        /// 칸마다 <b>등급별 개수</b>. 첨자는 <c>칸 * Lanes + 등급레인</c>.
        ///
        /// 한 칸이 등급을 섞어 담는다. 예전에는 칸마다 등급을 하나만 허용했는데,
        /// 그러면 상자에서 나온 <b>낱개 하나가 6개짜리 칸을 통째로 먹었다</b> —
        /// L5에 칸이 9개뿐인데 등급품 10개가 들어오자 매장이 낱개로 도배되어
        /// 하루 손님 15명 중 7명이 빈손으로 나갔다(39차 D15).
        ///
        /// 섞어도 값을 가릴 수 있는 이유: 손님은 <see cref="Consume"/>에서 <b>가장 좋은 등급부터</b>
        /// 집어 가고, 그때 어느 등급이었는지를 돌려주므로 결제가 흐려지지 않는다.
        /// </summary>
        int[] stock;

        /// <summary>발주품(등급 없음) + 등급 7종.</summary>
        const int Lanes = ItemGrades.Count + 1;

        static int LaneOf(ItemGrade grade) => (int)grade + 1;
        static ItemGrade GradeOfLane(int lane) => (ItemGrade)(lane - 1);

        public event Action OnChanged;

        public float ProximityMultiplier { get; private set; } = 1f;

        /// <summary>손님이 실제로 서는 자리. 진열대 중심은 콜라이더 안이라 도달할 수 없다.</summary>
        public Vector3 ApproachPoint { get; private set; }

        /// <summary>
        /// 손님이 이 진열대 앞까지 올 수 있는가.
        ///
        /// 창고·옆방·가게 앞마당은 <b>직원 구역</b>이라 손님이 못 들어간다. 거기 세워 둔
        /// 진열대에 물건을 올리면 손님은 그 상품을 사러 왔다가 길을 못 찾고 그냥 나간다 —
        /// 28차 측정에서 봇이 산 진열대가 배달 자리(가게 앞)에 그대로 서 있었고,
        /// 그 세 상품 때문에 손실률이 22.4%까지 올랐다.
        /// </summary>
        public bool CustomersCanReach { get; private set; } = true;

        public int SlotCount => slotHeights.Length;
        public int CapacityPerSlot => capacityPerSlot;

        /// <summary>
        /// 이 진열대 한 칸에 그 상품을 몇 개까지 올릴 수 있는가.
        /// 상품이 <see cref="DogShop.Data.ProductDef.shelfCapacity"/> 로 더 낮게 정할 수 있다 —
        /// 강아지 침대는 한 칸을 거의 다 먹으므로 반 칸에 하나만 들어간다.
        /// </summary>
        public int CapacityFor(int productIndex)
        {
            if (InventoryManager.Instance == null || productIndex < 0) return capacityPerSlot;

            int limit = InventoryManager.Instance.Catalog.Get(productIndex).shelfCapacity;
            return limit > 0 ? Mathf.Min(limit, capacityPerSlot) : capacityPerSlot;
        }

        /// <summary>그 칸에 이미 놓인 상품 기준의 용량. 빈 칸이면 진열대 기본값.</summary>
        public int CapacityAt(int slot) =>
            Valid(slot) && product[slot] >= 0 ? CapacityFor(product[slot]) : capacityPerSlot;
        public float SlotWidth => slotWidth;
        public float SlotDepth => slotDepth;
        public int AcceptedBulk => acceptedBulk;

        public float HeightOf(int slot) => slotHeights[Mathf.Clamp(slot, 0, SlotCount - 1)];

        public float CenterZOf(int slot)
        {
            if (slotCenterZ == null || slotCenterZ.Length == 0) return 0f;
            return slotCenterZ[Mathf.Clamp(slot, 0, slotCenterZ.Length - 1)];
        }

        public float CenterXOf(int slot)
        {
            if (slotCenterX == null || slotCenterX.Length == 0) return 0f;
            return slotCenterX[Mathf.Clamp(slot, 0, slotCenterX.Length - 1)];
        }
        public int ProductAt(int slot) => Valid(slot) ? product[slot] : -1;

        /// <summary>그 칸에 든 총 개수. 등급을 가리지 않는다.</summary>
        public int CountAt(int slot)
        {
            if (!Valid(slot)) return 0;

            int sum = 0;
            for (int lane = 0; lane < Lanes; lane++) sum += stock[slot * Lanes + lane];
            return sum;
        }

        public int CountAt(int slot, ItemGrade grade) =>
            Valid(slot) ? stock[slot * Lanes + LaneOf(grade)] : 0;

        public int RoomAt(int slot) => Valid(slot) ? CapacityAt(slot) - CountAt(slot) : 0;

        /// <summary>
        /// 그 칸에 든 것 중 <b>가장 비싼 등급</b>. 표시와 손님 구매에 쓴다.
        ///
        /// 등급 열거형 순서가 아니라 <see cref="ItemGrades.ValueOf"/>로 고른다 —
        /// F·E는 발주품보다 싸므로 열거형으로 재면 싼 것을 먼저 내보내게 된다.
        /// </summary>
        public ItemGrade BestGradeAt(int slot)
        {
            if (!Valid(slot)) return ItemGrade.None;

            ItemGrade best = ItemGrade.None;
            float bestValue = -1f;
            bool found = false;

            for (int lane = 0; lane < Lanes; lane++)
            {
                if (stock[slot * Lanes + lane] <= 0) continue;

                ItemGrade g = GradeOfLane(lane);
                float value = ItemGrades.ValueOf(g);
                if (found && value <= bestValue) continue;

                best = g;
                bestValue = value;
                found = true;
            }
            return best;
        }

        /// <summary>예전 이름. 칸이 등급을 섞어 담게 된 뒤로는 <b>가장 좋은 등급</b>을 뜻한다.</summary>
        public ItemGrade GradeAt(int slot) => BestGradeAt(slot);

        void Awake()
        {
            product = new int[SlotCount];
            stock = new int[SlotCount * Lanes];
            for (int i = 0; i < SlotCount; i++) product[i] = -1;
        }

        bool Valid(int slot) => product != null && slot >= 0 && slot < product.Length;

        // ---- 배정 ----

        /// <summary>이 가구가 그 상품의 부피를 받는가.</summary>
        public bool AcceptsBulk(int productIndex)
        {
            if (acceptedBulk == 0) return true;
            if (InventoryManager.Instance == null) return true;
            return CarryCrate.SlotCostOf(productIndex) == acceptedBulk;
        }

        public bool CanPlace(int slot, int productIndex, out string reason)
        {
            if (!Valid(slot)) { reason = "칸이 없다"; return false; }

            if (!AcceptsBulk(productIndex))
            {
                reason = acceptedBulk == 1
                    ? "작은 물건만 올릴 수 있다"
                    : "사료처럼 부피 큰 물건만 올릴 수 있다";
                return false;
            }

            if (product[slot] >= 0 && product[slot] != productIndex)
            {
                string other = InventoryManager.Instance.Catalog.Get(product[slot]).nameKo;
                reason = other + "이(가) 올려져 있다 — 다 팔리거나 상자로 빼야 한다";
                return false;
            }

            // 빈 칸이라고 아무나 가져가지 못한다 — 한 칸도 못 받은 상품이 먼저다
            if (product[slot] < 0 && ShelfManager.Instance != null
                && !ShelfManager.Instance.CanClaimEmptySlot(productIndex, acceptedBulk))
            {
                string waiting = ShelfManager.Instance.WaitingName(acceptedBulk);
                reason = (waiting != null ? waiting : "아직 한 칸도 못 받은 상품")
                       + " 자리다 — 진열대를 더 놓아야 한다";
                return false;
            }

            if (stock[slot] >= CapacityFor(productIndex)) { reason = "이 칸이 가득 찼다"; return false; }

            reason = null;
            return true;
        }

        public void Place(int slot, int productIndex) => Place(slot, productIndex, ItemGrade.None);

        public void Place(int slot, int productIndex, ItemGrade itemGrade)
        {
            if (!Valid(slot)) return;

            product[slot] = productIndex;
            stock[slot * Lanes + LaneOf(itemGrade)]++;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 1개 뺀다. <b>가장 좋은 등급부터</b> 나간다 — 손님은 진열대에서 좋은 것을 집는다.
        /// 칸이 비면 배정도 함께 풀려 다른 상품을 올릴 수 있게 된다.
        /// </summary>
        public bool Take(int slot) => Take(slot, BestGradeAt(slot));

        /// <summary>등급을 골라서 1개 뺀다. 상자로 되담을 때처럼 무엇을 빼는지 정해진 경우다.</summary>
        public bool Take(int slot, ItemGrade itemGrade)
        {
            if (!Valid(slot)) return false;

            int lane = slot * Lanes + LaneOf(itemGrade);
            if (stock[lane] <= 0) return false;

            stock[lane]--;
            if (CountAt(slot) == 0) product[slot] = -1;

            OnChanged?.Invoke();
            return true;
        }

        // ---- 전역 조회 (InventoryManager가 모아 쓴다) ----

        public int TotalOf(int productIndex)
        {
            int sum = 0;
            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex) sum += CountAt(i);
            return sum;
        }

        /// <summary>
        /// <b>이미 그 상품이 올려진</b> 칸에 남은 여유. 빈 칸은 세지 않는다 —
        /// 빈 칸을 새로 가져가도 되는지는 진열대 하나가 아니라 가게 전체를 봐야 정해지므로
        /// <see cref="ShelfManager.RoomFor"/>가 두 몫을 합친다.
        /// </summary>
        public int RoomInOwned(int productIndex)
        {
            if (!AcceptsBulk(productIndex)) return 0;

            int room = 0;
            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex) room += CapacityFor(productIndex) - CountAt(i);
            return room;
        }

        /// <summary>이 진열대에서 그 상품이 쓸 수 있는 빈 칸 수. 가져가도 되는지는 여기서 안 따진다.</summary>
        public int EmptySlotsFor(int productIndex)
        {
            if (!AcceptsBulk(productIndex)) return 0;

            int count = 0;
            for (int i = 0; i < SlotCount; i++)
                if (product[i] < 0) count++;
            return count;
        }

        /// <summary>손님 구매. 그 상품이 있는 칸에서 1개 뺀다.</summary>
        public bool Consume(int productIndex)
        {
            ItemGrade taken;
            return Consume(productIndex, out taken);
        }

        /// <summary>
        /// 손님 구매. <b>어느 등급을 집어 갔는지 돌려준다</b> — 값이 등급마다 다르므로
        /// 결제하는 쪽이 그걸 알아야 한다.
        ///
        /// 좋은 등급부터 나간다. 진열대에 S와 F가 같이 있으면 손님은 좋은 것을 집는다.
        /// </summary>
        public bool Consume(int productIndex, out ItemGrade taken)
        {
            taken = ItemGrade.None;

            int best = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                if (product[i] != productIndex || CountAt(i) <= 0) continue;
                if (best < 0
                    || ItemGrades.ValueOf(BestGradeAt(i)) > ItemGrades.ValueOf(BestGradeAt(best))) best = i;
            }
            if (best < 0) return false;

            taken = BestGradeAt(best);
            return Take(best, taken);
        }

        /// <summary>기다리다 포기한 손님이 물건을 도로 놓는다. 들고 있던 등급 그대로 돌아간다.</summary>
        public bool Restore(int productIndex, ItemGrade itemGrade = ItemGrade.None)
        {
            int target = FirstSlotFor(productIndex, itemGrade);
            if (target < 0) return false;

            Place(target, productIndex, itemGrade);
            return true;
        }

        /// <summary>
        /// 그 상품을 놓을 수 있는 첫 칸. 이미 같은 상품이 있는 칸을 먼저 본다.
        ///
        /// 빈 칸을 <b>새로</b> 가져가는 건 아무 때나 되지 않는다. 예전에는 됐고, 그래서 수요 큰
        /// 기본 사료가 칸을 둘셋 먹는 동안 나머지 상품은 한 칸도 못 받았다. 진열되지 않은 상품은
        /// 손님조차 생기지 않으므로(<see cref="CustomerManager"/>의 소환 가드) 칸이 상품 수만큼
        /// 있어도 손실률이 37%까지 올랐다(25차 측정). 판정은 가게 전체를 봐야 하므로
        /// <see cref="ShelfManager.CanClaimEmptySlot"/>에 맡긴다.
        /// </summary>
        public int FirstSlotFor(int productIndex) => FirstSlotFor(productIndex, ItemGrade.None);

        public int FirstSlotFor(int productIndex, ItemGrade itemGrade)
        {
            if (!AcceptsBulk(productIndex)) return -1;

            // 등급은 칸을 가르지 않는다. 같은 상품이면 한 칸에 섞어 담는다 —
            // 등급마다 칸을 따로 주면 상자에서 나온 낱개 하나가 칸 하나를 통째로 먹는다
            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex && CountAt(i) < CapacityFor(productIndex)) return i;

            if (ShelfManager.Instance != null
                && !ShelfManager.Instance.CanClaimEmptySlot(productIndex, acceptedBulk)) return -1;

            for (int i = 0; i < SlotCount; i++)
                if (product[i] < 0) return i;

            return -1;
        }

        // ---- 조준 ----

        /// <summary>
        /// 조준한 높이에 가장 가까운 칸. 벽 진열대처럼 위아래 두 칸이면
        /// 플레이어가 어디를 보느냐로 칸이 정해진다 — 키를 더 만들 이유가 없다.
        /// </summary>
        /// <summary>상판 앞 모서리·두께를 조준해도 그 칸으로 친다.</summary>
        const float BoardTolerance = 0.06f;

        /// <summary>
        /// 조준한 지점이 가리키는 칸.
        ///
        /// 예전엔 <b>높이가 가장 가까운 상판</b>을 골랐다. 그러면 두 번째 칸의 물건 놓는 공간을
        /// 조준해도 지점이 바로 위 상판(세 번째 칸)의 밑면에 더 가까워 세 번째 칸으로 들어갔고,
        /// 같은 높이에 좌우·앞뒤 칸이 있는 진열대는 늘 첫 번째(왼쪽) 칸만 골랐다.
        ///
        /// 지금은 ① 조준점 <b>아래에 있는 상판 중 가장 높은 것</b>(물건이 그 위에 놓이므로)을 층으로 고르고
        /// ② 그 층 안에서 좌우·앞뒤 중심이 가장 가까운 칸을 고른다.
        /// </summary>
        public int SlotAt(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);

            // ① 층: 조준점 아래(허용치 포함) 상판 중 가장 높은 것. 맨 아래 상판보다 낮으면 맨 아래 층
            float floor = float.MinValue;
            float lowest = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                float h = slotHeights[i];
                lowest = Mathf.Min(lowest, h);
                if (h <= local.y + BoardTolerance && h > floor) floor = h;
            }
            if (floor == float.MinValue) floor = lowest;

            // ② 그 층에서 좌우·앞뒤로 가장 가까운 칸
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                if (Mathf.Abs(slotHeights[i] - floor) > 0.02f) continue;
                float dx = local.x - CenterXOf(i);
                float dz = local.z - CenterZOf(i);
                float d = dx * dx + dz * dz;
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        // ---- 배치 ----

        public void Bind(float proximityMultiplier, Vector3 approachPoint)
        {
            SetApproach(approachPoint, proximityMultiplier);
        }

        /// <summary>옮겨 놓은 뒤 손님이 설 자리를 다시 잡는다.</summary>
        public void SetApproach(Vector3 approachPoint, float proximityMultiplier)
        {
            ApproachPoint = approachPoint;
            ProximityMultiplier = proximityMultiplier;

            UnityEngine.AI.NavMeshHit hit;
            CustomersCanReach = UnityEngine.AI.NavMesh.SamplePosition(
                                    approachPoint, out hit, 1f, Customer.WalkableAreas)
                             && OnSalesFloor(approachPoint);
        }

        /// <summary>손님이 설 자리를 판매장 밖으로 이만큼까지는 봐준다 — 벽에 붙인 진열대 앞자리가 경계에 걸린다.</summary>
        const float FloorSlack = 0.15f;

        /// <summary>
        /// NavMesh 만 보면 <b>앞마당 진열대가 손님 구역으로 잡힌다.</b> 손님이 문으로 드나들게
        /// 앞마당 금지 구역을 문 앞(z −0.85~0)만큼 비웠더니, 배달 자리에 선 진열대의 앞자리가
        /// 그 통로에서 0.17m 거리라 1m 반경에 걸렸다. 44차 측정에서 봇이 산 진열대 4대가
        /// 앞마당 한 점에 겹쳐 선 채 "닿는다"로 판정돼 안으로 안 옮겨졌고, 손님들이 문 앞 좁은 통로로 몰려
        /// 엉키면서 16일차부터 놓침이 하루 10건 넘게 나고 명성이 331에서 0까지 무너졌다.
        /// </summary>
        static bool OnSalesFloor(Vector3 point)
        {
            if (ShopSpace.Instance == null) return true;

            Rect floor = ShopSpace.Instance.FloorRect;
            return point.x > floor.xMin - FloorSlack && point.x < floor.xMax + FloorSlack
                && point.z > floor.yMin - FloorSlack && point.z < floor.yMax + FloorSlack;
        }

        // ---- 세이브 ----

        /// <summary>칸 수만큼 상품을, 칸 x 등급레인 만큼 개수를 적는다.</summary>
        public void CaptureInto(int[] outProduct, int[] outStock, int offset)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                outProduct[offset + i] = product[i];
                for (int lane = 0; lane < Lanes; lane++)
                    outStock[(offset + i) * Lanes + lane] = stock[i * Lanes + lane];
            }
        }

        /// <summary>
        /// <paramref name="oldCount"/>·<paramref name="oldGrade"/>는 <b>칸마다 등급 하나</b>였던
        /// 옛 세이브용이다. 새 세이브는 <paramref name="inStock"/>만 채워져 온다.
        /// </summary>
        public void RestoreFrom(int[] inProduct, int[] inStock, int[] oldCount, int[] oldGrade, int offset)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (offset + i >= inProduct.Length) break;
                product[i] = inProduct[offset + i];

                for (int lane = 0; lane < Lanes; lane++) stock[i * Lanes + lane] = 0;

                int baseIndex = (offset + i) * Lanes;
                if (inStock != null && baseIndex + Lanes <= inStock.Length)
                {
                    for (int lane = 0; lane < Lanes; lane++)
                        stock[i * Lanes + lane] = inStock[baseIndex + lane];
                }
                else if (oldCount != null && offset + i < oldCount.Length)
                {
                    // 옛 세이브: 칸 하나에 등급 하나
                    ItemGrade g = oldGrade != null && offset + i < oldGrade.Length
                        ? (ItemGrade)oldGrade[offset + i]
                        : ItemGrade.None;
                    stock[i * Lanes + LaneOf(g)] = oldCount[offset + i];
                }
            }
            OnChanged?.Invoke();
        }
    }
}
