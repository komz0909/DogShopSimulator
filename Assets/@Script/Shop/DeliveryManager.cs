using System.Collections.Generic;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 앞마당의 배달 더미 배치. 아침에 온 물건이 여기 놓이고, 플레이어가 상자로 나른다.
    ///
    /// 창고 선반(<see cref="StorageManager"/>)과 같은 구조다 — 재고는 전부
    /// <see cref="InventoryManager"/>가 들고 있고 여기서는 <b>보여 주기만</b> 한다.
    /// 그래서 세이브에 참여하지 않는다: 불러오면 배달 수량을 다시 읽어 더미를 다시 세운다.
    ///
    /// 더미는 <b>배달이 있는 상품만</b> 왼쪽부터 채워 세운다. 상품마다 자리를 고정하면
    /// 사료 하나 시킨 날에도 앞마당이 빈 팔레트로 가득 찬다.
    /// </summary>
    public class DeliveryManager : MonoBehaviour
    {
        /// <summary>앞마당은 x 0~8 / z -4.5~0 이다. 문(x 3~5) 앞을 비워 두고 양옆으로 세운다.</summary>
        const float FirstX = 0.9f;
        const float SpacingX = 1.45f;
        const int PerRow = 5;
        const float FrontRowZ = -1.3f;
        const float BackRowZ = -2.9f;

        /// <summary>팔레트 위에 상품이 놓이는 로컬 높이 = 널빤지 윗면.</summary>
        const float PalletTopY = 0.12f;
        const float PalletWidth = 0.95f;
        const float PalletDepth = 0.75f;

        /// <summary>한 더미에 보일 최대 개수. 2단으로 쌓아 12개까지 보여 준다.</summary>
        const int Tiers = 2;
        const float TierSpacing = 0.30f;
        const int MaxVisible = 12;

        public static DeliveryManager Instance { get; private set; }

        [SerializeField] GameObject palletPrefab;

        readonly List<DeliveryStack> stacks = new List<DeliveryStack>();

        bool dirty = true;

        public int Count => stacks.Count;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged += MarkDirty;
            Rebuild();
        }

        void OnDestroy()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnStockChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        void MarkDirty() => dirty = true;

        /// <summary>
        /// 재고가 바뀔 때마다 즉시 다시 세우지 않는다. 하나 집을 때마다 전부 부수고 새로 만들면
        /// 집는 순간 조준하던 콜라이더가 사라져 다음 E가 허공을 짚는다.
        /// </summary>
        void LateUpdate()
        {
            if (!dirty) return;

            dirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            InventoryManager inventory = InventoryManager.Instance;
            if (palletPrefab == null || inventory == null) return;

            ProductCatalog catalog = inventory.Catalog;

            // 배달이 남은 상품만, 카탈로그 순서대로 자리를 잡는다
            int placed = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (inventory.DeliveredOf(i) <= 0) continue;

                DeliveryStack stack = FindFor(i);
                if (stack == null) stack = Create(i, catalog.Get(i).nameKo);

                stack.transform.localPosition = PositionOf(placed);
                placed++;
            }

            // 다 나른 더미는 치운다
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                if (stacks[i] != null && inventory.DeliveredOf(stacks[i].ProductIndex) > 0) continue;

                if (stacks[i] != null) Destroy(stacks[i].gameObject);
                stacks.RemoveAt(i);
            }
        }

        static Vector3 PositionOf(int slot)
        {
            int column = slot % PerRow;
            float z = slot < PerRow ? FrontRowZ : BackRowZ;
            return new Vector3(FirstX + column * SpacingX, 0f, z);
        }

        DeliveryStack Create(int productIndex, string nameKo)
        {
            GameObject instance = Instantiate(palletPrefab, transform);
            instance.name = "Delivery_" + nameKo;

            DeliveryStack stack = instance.GetComponent<DeliveryStack>();
            stack.Bind(productIndex);

            int captured = productIndex;
            ProductSlotDisplay display = instance.GetComponent<ProductSlotDisplay>();
            if (display != null)
                display.Configure(captured,
                    () => InventoryManager.Instance.DeliveredOf(captured),
                    PalletTopY, PalletWidth, PalletDepth,
                    Tiers, TierSpacing, MaxVisible);

            stacks.Add(stack);
            return stack;
        }

        public DeliveryStack FindFor(int productIndex)
        {
            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i] != null && stacks[i].ProductIndex == productIndex) return stacks[i];
            return null;
        }
    }
}
