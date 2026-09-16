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

        /// <summary>선반 단 수. 창고 랙처럼 여러 층에 놓을 때 1보다 커진다.</summary>
        int tiers = 1;

        /// <summary>단 사이 높이차. 위에서 아래로 쌓으면 음수다.</summary>
        float tierSpacing = 0f;

        /// <summary>화면에 보일 최대 개수. 0이면 진열대 용량을 쓴다.</summary>
        int maxVisible;

        /// <summary>
        /// 상품 프롭의 목표 크기(가장 긴 변). AI로 뽑은 프롭은 종마다 원본 크기가 제각각이라
        /// 그대로 올리면 사료 포대가 진열대만 해지고 목줄은 안 보인다. 한 치수로 맞춘다.
        /// 진열대를 키워도 물건이 같이 커지지 않는 것도 이 값 덕분이다.
        /// </summary>
        float propTargetSize = 0.22f;

        /// <summary>목표 크기에 맞추려고 프롭에 곱하는 배수.</summary>
        float propScale = 1f;

        GameObject prefab;
        int columns = 1;
        int rowsPerTier = 1;
        int capacity = 1;
        bool prepared;

        /// <summary>슬롯을 만든 매니저가 호출한다. 개수 제공자로 진열/창고를 구분한다.</summary>
        public void Configure(int productIndex, Func<int> countProvider, float topY, float topWidth, float topDepth,
                              int tiers = 1, float tierSpacing = 0f, int maxVisible = 0, float propSize = 0.22f)
        {
            this.propTargetSize = propSize;
            this.productIndex = productIndex;
            this.countProvider = countProvider;
            this.topY = topY;
            this.topWidth = topWidth;
            this.topDepth = topDepth;
            this.tiers = Mathf.Max(1, tiers);
            this.tierSpacing = tierSpacing;
            this.maxVisible = maxVisible;

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
            Vector3 native = MeasureSize(probe);
            Destroy(probe);

            float largest = Mathf.Max(native.x, Mathf.Max(native.y, native.z));
            propScale = largest > 0.0001f ? propTargetSize / largest : 1f;
            Vector3 size = native * propScale;

            float stepX = Mathf.Max(0.05f, size.x + Gap);
            float stepZ = Mathf.Max(0.05f, size.z + Gap);

            columns = Mathf.Clamp(Mathf.FloorToInt(topWidth / stepX), 1, MaxColumns);
            rowsPerTier = Mathf.Clamp(Mathf.FloorToInt(topDepth / stepZ), 1, MaxRows);

            int limit = maxVisible > 0 ? maxVisible : InventoryManager.ShelfCapacity;
            capacity = Mathf.Min(columns * rowsPerTier * tiers, limit);

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
            anchor.transform.localScale = Vector3.one * propScale;

            GameObject prop = Instantiate(prefab, anchor.transform);
            prop.transform.localPosition = Vector3.zero;

            Place(anchor, index, MeasureSize(prop));
            return anchor;
        }

        void Place(GameObject anchor, int index, Vector3 size)
        {
            // 한 단을 다 채운 뒤 다음 단으로 내려간다
            int perTier = Mathf.Max(1, columns * rowsPerTier);
            int tier = Mathf.Min(index / perTier, tiers - 1);
            int within = index % perTier;

            int column = within % columns;
            int row = within / columns;

            float stepX = size.x + Gap;
            float stepZ = size.z + Gap;

            float x = (column - (columns - 1) * 0.5f) * stepX;
            float z = -row * stepZ + stepZ * 0.5f;
            float y = topY + tier * tierSpacing;

            anchor.transform.localPosition = new Vector3(x, y, z);

            // 피벗 위치와 무관하게 바닥면이 상판에 정확히 닿게 보정한다
            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float topWorldY = transform.TransformPoint(new Vector3(0f, y, 0f)).y;
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
