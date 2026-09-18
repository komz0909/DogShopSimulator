using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 진열대 칸마다 그 칸에 배정된 상품 프롭을 올린다.
    ///
    /// 창고 랙이 쓰는 <see cref="ProductSlotDisplay"/>와 나눈 이유: 랙은 상품 1종을 여러 단에
    /// 쌓지만 진열대는 <b>칸마다 다른 상품</b>이 올라간다. 프롭 프리팹이 칸마다 달라지므로
    /// 하나를 재사용하는 방식이 성립하지 않는다.
    ///
    /// 바뀔 때마다 통째로 다시 만든다. 칸이 바뀌는 건 E를 누르거나 하나 팔릴 때뿐이라
    /// 풀링해서 아낄 만한 빈도가 아니다.
    /// </summary>
    public class ShelfDisplay : MonoBehaviour
    {
        const float Gap = 0.03f;
        const int MaxColumns = 4;
        const int MaxRows = 3;

        /// <summary>
        /// 새 배열이 이만큼 더 커야 줄 수를 늘린다. 줄이 적을수록 앞에서 잘 보이므로
        /// 몇 퍼센트 차이로는 뒤로 겹쳐 쌓지 않는다.
        /// </summary>
        const float RowPenalty = 1.1f;

        /// <summary>
        /// 프롭의 <b>최대</b> 크기(가장 긴 변). 종마다 원본 크기가 제각각이라 그대로 올리면
        /// 사료 포대가 진열대만 해지고 목줄은 안 보인다.
        ///
        /// 실제 크기는 <b>칸을 가득 채웠을 때 들어가는 최대치</b>로 정해지고 이 값이 상한이다 —
        /// 좁은 칸이면 알아서 작아지고, 넓은 칸이면 여기까지 커진다.
        ///
        /// 기본값은 창고 랙과 같은 값을 받아 온다. 여기에 숫자를 따로 박아 두면 씬에 놓인
        /// 진열대(0.30)와 <b>나중에 새로 놓는 진열대</b>의 물건 크기가 달라진다.
        /// </summary>
        [SerializeField] float propTargetSize = ProductSlotDisplay.DisplaySize;

        readonly List<GameObject> spawned = new List<GameObject>();

        ShelfTable table;
        bool dirty;

        public void Bind(ShelfTable target)
        {
            if (table != null) table.OnChanged -= MarkDirty;

            table = target;
            if (table != null) table.OnChanged += MarkDirty;

            MarkDirty();
        }

        void Awake()
        {
            if (table == null) Bind(GetComponent<ShelfTable>());
        }

        void OnDestroy()
        {
            if (table != null) table.OnChanged -= MarkDirty;
        }

        void MarkDirty() => dirty = true;

        void LateUpdate()
        {
            if (!dirty) return;

            dirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Destroy(spawned[i]);
            spawned.Clear();

            if (table == null || InventoryManager.Instance == null) return;

            ProductCatalog catalog = InventoryManager.Instance.Catalog;

            for (int slot = 0; slot < table.SlotCount; slot++)
            {
                int index = table.ProductAt(slot);
                if (index < 0) continue;

                GameObject prefab = catalog.Get(index).propPrefab;
                if (prefab == null) continue;

                BuildSlot(slot, prefab, table.CountAt(slot));
            }
        }

        void BuildSlot(int slot, GameObject prefab, int count)
        {
            // 한 번 만들어 크기를 재고 그 값으로 격자를 짠다
            GameObject probe = Instantiate(prefab);
            Vector3 native = Measure(probe);
            DestroyImmediate(probe);

            // 칸 용량을 기준으로 격자를 잡는다. 지금 개수로 잡으면 하나 팔릴 때마다
            // 남은 물건의 크기와 자리가 통째로 바뀐다.
            int capacity = Mathf.Max(1, table.CapacityPerSlot);
            int columns = 1, rows = 1;
            float scale = 0f;

            for (int r = 1; r <= MaxRows; r++)
            {
                int c = Mathf.CeilToInt(capacity / (float)r);
                if (c > MaxColumns) continue;

                float cellX = table.SlotWidth / c - Gap;
                float cellZ = table.SlotDepth / r - Gap;
                if (cellX <= 0f || cellZ <= 0f) continue;

                float fit = Mathf.Min(cellX / Mathf.Max(0.001f, native.x), cellZ / Mathf.Max(0.001f, native.z));
                if (fit <= scale * RowPenalty) continue;    // 줄을 늘릴 만큼 이득이 크지 않다

                scale = fit;
                columns = c;
                rows = r;
            }

            float largest = Mathf.Max(native.x, Mathf.Max(native.y, native.z));
            float cap = largest > 0.0001f ? propTargetSize / largest : 1f;
            if (scale <= 0f) scale = cap;
            scale = Mathf.Min(scale, cap);                  // 칸이 넓어도 이 이상은 키우지 않는다

            Vector3 size = native * scale;
            int visible = Mathf.Min(count, columns * rows);
            float top = table.HeightOf(slot);

            for (int i = 0; i < visible; i++)
            {
                GameObject anchor = new GameObject("Slot" + slot + "_" + i);
                anchor.transform.SetParent(transform, false);
                anchor.transform.localRotation = Quaternion.identity;
                anchor.transform.localScale = Vector3.one * scale;

                // 프롭 회전은 건드리지 않는다 — identity 로 덮으면 FBX 축 보정이 사라져 눕는다
                GameObject prop = Instantiate(prefab, anchor.transform);
                prop.transform.localPosition = Vector3.zero;

                int column = i % columns;
                int row = i / columns;

                // 격자를 상판 중심에 맞춰 편다. 앞으로 밀면 널빤지 밖으로 나간다
                float x = table.CenterXOf(slot) + (column - (columns - 1) * 0.5f) * (size.x + Gap);
                float z = table.CenterZOf(slot) + (row - (rows - 1) * 0.5f) * (size.z + Gap);
                anchor.transform.localPosition = new Vector3(x, top, z);

                // 피벗이 어디 있든 바닥면이 상판에 닿게 올린다
                Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);

                    float topWorldY = transform.TransformPoint(new Vector3(0f, top, 0f)).y;
                    anchor.transform.position += Vector3.up * (topWorldY - b.min.y);
                }

                spawned.Add(anchor);
            }
        }

        static Vector3 Measure(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Vector3(0.15f, 0.15f, 0.15f);

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.size;
        }
    }
}
