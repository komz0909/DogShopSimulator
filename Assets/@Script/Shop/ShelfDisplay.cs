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
        const int MaxRows = 2;

        /// <summary>
        /// 프롭의 목표 크기(가장 긴 변). 종마다 원본 크기가 제각각이라 그대로 올리면
        /// 사료 포대가 진열대만 해지고 목줄은 안 보인다. 진열대를 키워도 물건은 이 치수를 지킨다.
        /// </summary>
        [SerializeField] float propTargetSize = 0.22f;

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

            float largest = Mathf.Max(native.x, Mathf.Max(native.y, native.z));
            float scale = largest > 0.0001f ? propTargetSize / largest : 1f;
            Vector3 size = native * scale;

            int columns = Mathf.Clamp(Mathf.FloorToInt(table.SlotWidth / Mathf.Max(0.05f, size.x + Gap)), 1, MaxColumns);
            int rows = Mathf.Clamp(Mathf.FloorToInt(table.SlotDepth / Mathf.Max(0.05f, size.z + Gap)), 1, MaxRows);

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
                float x = (column - (columns - 1) * 0.5f) * (size.x + Gap);
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
