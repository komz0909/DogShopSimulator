using System;
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
        int[] stock;

        public event Action OnChanged;

        public float ProximityMultiplier { get; private set; } = 1f;

        /// <summary>손님이 실제로 서는 자리. 진열대 중심은 콜라이더 안이라 도달할 수 없다.</summary>
        public Vector3 ApproachPoint { get; private set; }

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
        public int CountAt(int slot) => Valid(slot) ? stock[slot] : 0;
        public int RoomAt(int slot) => Valid(slot) ? CapacityAt(slot) - stock[slot] : 0;

        void Awake()
        {
            product = new int[SlotCount];
            stock = new int[SlotCount];
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

            if (stock[slot] >= CapacityFor(productIndex)) { reason = "이 칸이 가득 찼다"; return false; }

            reason = null;
            return true;
        }

        public void Place(int slot, int productIndex)
        {
            if (!Valid(slot)) return;

            product[slot] = productIndex;
            stock[slot]++;
            OnChanged?.Invoke();
        }

        /// <summary>1개 뺀다. 칸이 비면 배정도 함께 풀려 다른 상품을 올릴 수 있게 된다.</summary>
        public bool Take(int slot)
        {
            if (!Valid(slot) || stock[slot] <= 0) return false;

            stock[slot]--;
            if (stock[slot] == 0) product[slot] = -1;
            OnChanged?.Invoke();
            return true;
        }

        // ---- 전역 조회 (InventoryManager가 모아 쓴다) ----

        public int TotalOf(int productIndex)
        {
            int sum = 0;
            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex) sum += stock[i];
            return sum;
        }

        /// <summary>그 상품을 더 받을 수 있는 여유. 빈 칸도 여유로 센다.</summary>
        public int RoomFor(int productIndex)
        {
            if (!AcceptsBulk(productIndex)) return 0;

            int room = 0;
            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex || product[i] < 0) room += CapacityFor(productIndex) - stock[i];
            return room;
        }

        /// <summary>손님 구매. 그 상품이 있는 칸에서 1개 뺀다.</summary>
        public bool Consume(int productIndex)
        {
            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex && stock[i] > 0) return Take(i);
            return false;
        }

        /// <summary>기다리다 포기한 손님이 물건을 도로 놓는다.</summary>
        public bool Restore(int productIndex)
        {
            int target = FirstSlotFor(productIndex);
            if (target < 0) return false;

            Place(target, productIndex);
            return true;
        }

        /// <summary>그 상품을 놓을 수 있는 첫 칸. 이미 같은 상품이 있는 칸을 먼저 본다.</summary>
        public int FirstSlotFor(int productIndex)
        {
            if (!AcceptsBulk(productIndex)) return -1;

            for (int i = 0; i < SlotCount; i++)
                if (product[i] == productIndex && stock[i] < CapacityFor(productIndex)) return i;

            for (int i = 0; i < SlotCount; i++)
                if (product[i] < 0) return i;

            return -1;
        }

        // ---- 조준 ----

        /// <summary>
        /// 조준한 높이에 가장 가까운 칸. 벽 진열대처럼 위아래 두 칸이면
        /// 플레이어가 어디를 보느냐로 칸이 정해진다 — 키를 더 만들 이유가 없다.
        /// </summary>
        public int SlotNear(float worldY)
        {
            float local = worldY - transform.position.y;

            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                float d = Mathf.Abs(slotHeights[i] - local);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        // ---- 배치 ----

        public void Bind(float proximityMultiplier, Vector3 approachPoint)
        {
            ProximityMultiplier = proximityMultiplier;
            ApproachPoint = approachPoint;
        }

        /// <summary>옮겨 놓은 뒤 손님이 설 자리를 다시 잡는다.</summary>
        public void SetApproach(Vector3 approachPoint, float proximityMultiplier)
        {
            ApproachPoint = approachPoint;
            ProximityMultiplier = proximityMultiplier;
        }

        // ---- 세이브 ----

        public void CaptureInto(int[] outProduct, int[] outStock, int offset)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                outProduct[offset + i] = product[i];
                outStock[offset + i] = stock[i];
            }
        }

        public void RestoreFrom(int[] inProduct, int[] inStock, int offset)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (offset + i >= inProduct.Length) break;
                product[i] = inProduct[offset + i];
                stock[i] = inStock[offset + i];
            }
            OnChanged?.Invoke();
        }
    }
}
