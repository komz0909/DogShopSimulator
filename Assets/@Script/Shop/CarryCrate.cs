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
        /// <summary>상자 칸 수가 없을 때 쓰는 값. 레벨 매니저가 아직 없는 테스트 상황용이다.</summary>
        public const int BaseSlots = 8;

        /// <summary>
        /// 상자 칸 수. <b>레벨 보상으로 커진다</b> — 1칸 상품은 이만큼, 2칸 상품은 절반까지 든다.
        ///
        /// 상수였다가 레벨을 타게 바꿨다. 한 번에 더 많이 나르면 왕복이 줄고,
        /// 하루가 5분뿐이라 그 왕복이 곧 시간이다 — 돈을 주지 않고 시간을 돌려주는 보상이다.
        /// </summary>
        public static int SlotCapacity =>
            ShopLevelManager.Instance != null ? ShopLevelManager.Instance.Current.crateSlots : BaseSlots;

        /// <summary>상자 안에 실제로 보이는 최대 개수. 그 이상은 라벨 숫자로만 표시한다.</summary>
        const int MaxVisible = 4;

        [SerializeField] Transform contentAnchor;
        /// <summary>
        /// 상자 안 물건의 <b>목표 크기</b>(가장 긴 변, 월드 기준 미터).
        /// 상품마다 원래 크기가 달라서(샴푸 0.32 · 사료 0.45) 일률 배율을 쓰면
        /// 작은 것이 유독 작아 보인다. 전부 이 크기로 맞춰 눈에 잘 띄게 한다.
        /// </summary>
        [SerializeField] float contentTargetSize = 0.20f;

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

        /// <summary>상품 하나가 쓰는 칸 수. 발주품(None) + 등급 7종.</summary>
        const int Lanes = ItemGrades.Count + 1;

        static int Lane(int productIndex, ItemGrade grade) => productIndex * Lanes + ((int)grade + 1);

        void EnsureCounts()
        {
            int size = InventoryManager.Instance != null ? InventoryManager.Instance.Catalog.Count * Lanes : 0;
            if (counts == null || counts.Length != size) counts = new int[size];
        }

        /// <summary>등급 상관없이 그 상품이 몇 개 담겼나. 화면 표시와 "가진 것"판정에 쓴다.</summary>
        public int CountOf(int productIndex)
        {
            EnsureCounts();
            if (productIndex < 0) return 0;

            int sum = 0;
            for (int g = -1; g < ItemGrades.Count; g++)
            {
                int lane = Lane(productIndex, (ItemGrade)g);
                if (lane >= 0 && lane < counts.Length) sum += counts[lane];
            }
            return sum;
        }

        public int CountOf(int productIndex, ItemGrade grade)
        {
            EnsureCounts();
            int lane = Lane(productIndex, grade);
            return lane >= 0 && lane < counts.Length ? counts[lane] : 0;
        }

        /// <summary>종류는 가리지 않는다. 남은 칸이 그 상품의 칸 수만큼 있는지만 본다.</summary>
        public bool Accepts(int productIndex) => Room >= SlotCostOf(productIndex);

        public static int SlotCostOf(int productIndex)
        {
            var catalog = InventoryManager.Instance.Catalog;
            if (productIndex < 0 || productIndex >= catalog.Count) return 1;
            return Mathf.Clamp(catalog.Get(productIndex).slotCost, 1, 2);
        }

        public bool AddOne(int productIndex) => AddOne(productIndex, ItemGrade.None);

        public bool AddOne(int productIndex, ItemGrade grade)
        {
            EnsureCounts();

            int lane = Lane(productIndex, grade);
            if (productIndex < 0 || lane < 0 || lane >= counts.Length) return false;
            if (!Accepts(productIndex)) return false;

            counts[lane]++;
            Total++;
            UsedSlots += SlotCostOf(productIndex);
            RefreshVisual();
            OnChanged?.Invoke();
            return true;
        }

        public bool RemoveOne(int productIndex) => RemoveOne(productIndex, ItemGrade.None);

        public bool RemoveOne(int productIndex, ItemGrade grade)
        {
            EnsureCounts();
            if (CountOf(productIndex, grade) <= 0) return false;

            counts[Lane(productIndex, grade)]--;
            Total--;
            UsedSlots = Mathf.Max(0, UsedSlots - SlotCostOf(productIndex));
            RefreshVisual();
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 그 상품 중 담겨 있는 <b>가장 좋은 등급</b>. 진열할 때 무엇부터 꺼낼지 정한다 —
        /// 좋은 것부터 내놓아야 손님이 비싼 값을 치른다.
        /// </summary>
        public ItemGrade BestGradeOf(int productIndex)
        {
            EnsureCounts();

            // 값 기준이다. 등급 열거형 순서로 고르면 발주품(1.0배)보다 싼
            // F(0.6배)·E(0.8배)를 먼저 내놓게 된다
            ItemGrade best = ItemGrade.None;
            float bestValue = -1f;
            bool found = false;

            for (int g = -1; g < ItemGrades.Count; g++)
            {
                ItemGrade grade = (ItemGrade)g;
                if (CountOf(productIndex, grade) <= 0) continue;

                float value = ItemGrades.ValueOf(grade);
                if (found && value <= bestValue) continue;

                best = grade;
                bestValue = value;
                found = true;
            }
            return best;
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
                if (listed > 0) text.Append(" / ");
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

            // 프롭의 실제 크기를 재서 목표 크기로 정규화한다
            Renderer[] rends = prop.GetComponentsInChildren<Renderer>(true);
            float largest = 0.1f;
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            }
            float lossy = contentAnchor.lossyScale.x;
            float scale = largest > 0.0001f ? (contentTargetSize / largest) / Mathf.Max(0.0001f, lossy) : 1f;

            anchor.transform.localScale = Vector3.one * scale;
            anchor.transform.localPosition = new Vector3(
                (slot % 2 == 0 ? -1f : 1f) * 0.085f,
                0f,
                (slot < 2 ? -1f : 1f) * 0.065f);

            return anchor;
        }
    }
}
