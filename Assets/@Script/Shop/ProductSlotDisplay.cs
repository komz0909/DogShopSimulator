using System;
using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 상판 위에 상품 프롭을 개수만큼 올린다. 진열대와 창고 선반이 함께 쓴다 —
    /// 세는 대상만 다르므로 개수 제공자를 주입받는다.
    ///
    /// 프롭은 처음 필요할 때만 만들고 이후에는 켜고 끄기만 한다.
    /// </summary>
    public class ProductSlotDisplay : MonoBehaviour
    {
        const float Gap = 0.03f;
        const int MaxColumns = 4;
        const int MaxRows = 2;

        readonly List<GameObject> props = new List<GameObject>();

        int productIndex = -1;
        Func<int> countProvider;
        float topY = 0.4f;
        float topWidth = 1.0f;
        float topDepth = 0.6f;

        GameObject prefab;
        int columns = 1;
        int capacity = 1;
        bool prepared;

        /// <summary>슬롯을 만든 매니저가 호출한다. 개수 제공자로 진열/창고를 구분한다.</summary>
        public void Configure(int productIndex, Func<int> countProvider, float topY, float topWidth, float topDepth)
        {
            this.productIndex = productIndex;
            this.countProvider = countProvider;
            this.topY = topY;
            this.topWidth = topWidth;
            this.topDepth = topDepth;

            prepared = false;
            Refresh();
        }

        void Start()
        {
            if (InventoryManager.Instance == null) return;
            InventoryManager.Instance.OnStockChanged += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged -= Refresh;
        }

        void Refresh()
        {
            if (productIndex < 0 || countProvider == null) return;

            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null) return;

            if (!prepared && !Prepare(inventory)) return;

            int wanted = Mathf.Min(countProvider(), capacity);

            while (props.Count < wanted) props.Add(CreateProp(props.Count));

            for (int i = 0; i < props.Count; i++)
                if (props[i] != null) props[i].SetActive(i < wanted);
        }

        bool Prepare(InventoryManager inventory)
        {
            ProductDef def = inventory.Catalog.Get(productIndex);
            prefab = def.propPrefab;
            if (prefab == null) { prepared = true; return false; }

            GameObject probe = Instantiate(prefab);
            Vector3 size = MeasureSize(probe);
            Destroy(probe);

            float stepX = Mathf.Max(0.05f, size.x + Gap);
            float stepZ = Mathf.Max(0.05f, size.z + Gap);

            columns = Mathf.Clamp(Mathf.FloorToInt(topWidth / stepX), 1, MaxColumns);
            int rows = Mathf.Clamp(Mathf.FloorToInt(topDepth / stepZ), 1, MaxRows);
            capacity = Mathf.Min(columns * rows, InventoryManager.ShelfCapacity);

            prepared = true;
            return true;
        }

        /// <summary>
        /// 빈 앵커에 진열 방향을 주고, 프롭은 그 아래에 <b>자기 회전을 그대로 유지한 채</b> 넣는다.
        /// 프롭 회전을 identity로 덮어쓰면 FBX 루트의 축 변환 보정(90도)이 사라져 프롭이 눕는다.
        /// </summary>
        GameObject CreateProp(int index)
        {
            GameObject anchor = new GameObject("Prop_" + index);
            anchor.transform.SetParent(transform, false);
            anchor.transform.localRotation = Quaternion.identity;

            GameObject prop = Instantiate(prefab, anchor.transform);
            prop.transform.localPosition = Vector3.zero;

            Place(anchor, index, MeasureSize(prop));
            return anchor;
        }

        void Place(GameObject anchor, int index, Vector3 size)
        {
            int column = index % columns;
            int row = index / columns;

            float stepX = size.x + Gap;
            float stepZ = size.z + Gap;

            float x = (column - (columns - 1) * 0.5f) * stepX;
            float z = -row * stepZ + stepZ * 0.5f;

            anchor.transform.localPosition = new Vector3(x, topY, z);

            // 피벗 위치와 무관하게 바닥면이 상판에 정확히 닿게 보정한다
            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float topWorldY = transform.TransformPoint(new Vector3(0f, topY, 0f)).y;
            anchor.transform.position += Vector3.up * (topWorldY - bounds.min.y);
        }

        static Vector3 MeasureSize(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Vector3(0.15f, 0.15f, 0.15f);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds.size;
        }
    }
}
