using System;
using System.Collections.Generic;
using System.Text;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 운반 상자. 월드에 실제로 존재하는 오브젝트다 — 바닥에 놓여 있고, 주워서 들고 다니고, 내려놓는다.
    /// <b>여러 종류를 섞어</b> 담을 수 있고, 담기·꺼내기는 한 개씩 일어난다.
    /// 진열대는 상자에서 자기 상품만 꺼내간다.
    /// </summary>
    public class CarryCrate : MonoBehaviour
    {
        /// <summary>상자 칸 수. 1칸 상품은 8개, 2칸 상품은 4개까지 든다.</summary>
        public const int SlotCapacity = 8;

        /// <summary>상자 안에 실제로 보이는 최대 개수. 그 이상은 라벨 숫자로만 표시한다.</summary>
        const int MaxVisible = 4;

        [SerializeField] Transform contentAnchor;
        [SerializeField] float contentScale = 0.5f;

        readonly List<GameObject> visible = new List<GameObject>();
        int[] counts;

        /// <summary>담긴 개수(칸 수가 아니다).</summary>
        public int Total { get; private set; }

        /// <summary>차지한 칸 수. 부피가 큰 상품은 하나가 2칸이다.</summary>
        public int UsedSlots { get; private set; }

        public bool IsEmpty => Total <= 0;
        public int Room => SlotCapacity - UsedSlots;
        public bool IsFull => Room <= 0;

        public event Action OnChanged;

        void Awake()
        {
            if (contentAnchor == null) contentAnchor = transform;
            EnsureCounts();
        }

        void EnsureCounts()
        {
            int size = InventoryManager.Instance != null ? InventoryManager.Instance.Catalog.Count : 0;
            if (counts == null || counts.Length != size) counts = new int[size];
        }

        public int CountOf(int productIndex)
        {
            EnsureCounts();
            return productIndex >= 0 && productIndex < counts.Length ? counts[productIndex] : 0;
        }

        /// <summary>종류는 가리지 않는다. 남은 칸이 그 상품의 칸 수만큼 있는지만 본다.</summary>
        public bool Accepts(int productIndex) => Room >= SlotCostOf(productIndex);

        public static int SlotCostOf(int productIndex)
        {
            var catalog = InventoryManager.Instance.Catalog;
            if (productIndex < 0 || productIndex >= catalog.Count) return 1;
            return Mathf.Clamp(catalog.Get(productIndex).slotCost, 1, 2);
        }

        public bool AddOne(int productIndex)
        {
            EnsureCounts();
            if (productIndex < 0 || productIndex >= counts.Length) return false;
            if (!Accepts(productIndex)) return false;

            counts[productIndex]++;
            Total++;
            UsedSlots += SlotCostOf(productIndex);
            RefreshVisual();
            OnChanged?.Invoke();
            return true;
        }

        public bool RemoveOne(int productIndex)
        {
            EnsureCounts();
            if (CountOf(productIndex) <= 0) return false;

            counts[productIndex]--;
            Total--;
            UsedSlots = Mathf.Max(0, UsedSlots - SlotCostOf(productIndex));
            RefreshVisual();
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 내용물만 버린다. <b>창고로 돌려주지 않는다</b> — 세이브 복원처럼
        /// 저장값으로 다시 채울 때 쓴다. 여기서 Grant를 하면 로드마다 재고가 늘어난다.
        /// </summary>
        public void Clear()
        {
            EnsureCounts();
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
            Total = 0;
            UsedSlots = 0;
            RefreshVisual();
            OnChanged?.Invoke();
        }

        /// <summary>담긴 것을 전부 창고로 되돌린다.</summary>
        public void ReturnAllToStorage()
        {
            EnsureCounts();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                InventoryManager.Instance.Grant(i, counts[i]);
                counts[i] = 0;
            }
            Total = 0;
            UsedSlots = 0;
            RefreshVisual();
            OnChanged?.Invoke();
        }

        public string Describe()
        {
            if (IsEmpty) return "빈 상자  0 / " + SlotCapacity + "칸";

            EnsureCounts();
            ProductCatalog catalog = InventoryManager.Instance.Catalog;

            StringBuilder text = new StringBuilder();
            int listed = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                if (listed >= 3) { text.Append(" …"); break; }
                if (listed > 0) text.Append(" · ");
                text.Append(catalog.Get(i).nameKo).Append(" ").Append(counts[i]);
                if (SlotCostOf(i) > 1) text.Append("(2칸)");
                listed++;
            }

            return text.Append("   ").Append(UsedSlots).Append(" / ").Append(SlotCapacity).Append("칸").ToString();
        }

        /// <summary>담긴 순서와 무관하게, 종류별로 앞에서부터 최대 4개를 상자 안에 세운다.</summary>
        void RefreshVisual()
        {
            for (int i = 0; i < visible.Count; i++)
                if (visible[i] != null) Destroy(visible[i]);
            visible.Clear();

            if (IsEmpty) return;

            EnsureCounts();
            ProductCatalog catalog = InventoryManager.Instance.Catalog;

            int slot = 0;
            for (int product = 0; product < counts.Length && slot < MaxVisible; product++)
            {
                for (int n = 0; n < counts[product] && slot < MaxVisible; n++)
                {
                    GameObject created = CreateContent(catalog.Get(product).propPrefab, slot);
                    if (created != null) visible.Add(created);
                    slot++;
                }
            }
        }

        GameObject CreateContent(GameObject prefab, int slot)
        {
            if (prefab == null) return null;

            // 프롭 회전은 건드리지 않는다 — identity로 덮으면 FBX 축 보정이 사라져 눕는다
            GameObject anchor = new GameObject("Content_" + slot);
            anchor.transform.SetParent(contentAnchor, false);

            GameObject prop = Instantiate(prefab, anchor.transform);
            prop.transform.localPosition = Vector3.zero;

            anchor.transform.localScale = Vector3.one * contentScale;
            anchor.transform.localPosition = new Vector3(
                (slot % 2 == 0 ? -1f : 1f) * 0.085f,
                0f,
                (slot < 2 ? -1f : 1f) * 0.065f);

            return anchor;
        }
    }
}
