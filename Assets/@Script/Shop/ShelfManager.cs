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
            data.shelfSlotCount = new int[total];

            int offset = 0;
            for (int i = 0; i < tables.Count; i++)
            {
                tables[i].CaptureInto(data.shelfSlotProduct, data.shelfSlotCount, offset);
                offset += tables[i].SlotCount;
            }
        }

        public void RestoreFrom(DogShop.Core.SaveData data)
        {
            if (data.shelfSlotProduct == null || data.shelfSlotCount == null) return;

            int offset = 0;
            for (int i = 0; i < tables.Count; i++)
            {
                tables[i].RestoreFrom(data.shelfSlotProduct, data.shelfSlotCount, offset);
                offset += tables[i].SlotCount;
            }
        }

        /// <summary>입구에서 멀수록 덜 팔린다. 배치가 자유로워졌으니 거리로만 판정한다.</summary>
        float ProximityFor(Vector3 position)
        {
            float far = 6f;   // 매장 대각선의 대략 절반. 이보다 멀면 최저 배수
            float t = Mathf.Clamp01(Vector3.Distance(new Vector3(position.x, 0f, position.z), Entrance) / far);
            return Mathf.Lerp(NearMultiplier, FarMultiplier, t);
        }

        /// <summary>그 상품이 <b>실제로 올려져 있는</b> 진열대. 칸이 비면 그 상품은 찾는 사람도 없다.</summary>
        public ShelfTable FindFor(int productIndex)
        {
            for (int i = 0; i < tables.Count; i++)
                if (tables[i].TotalOf(productIndex) > 0) return tables[i];
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

        public float ProximityMultiplier(int productIndex)
        {
            ShelfTable table = FindFor(productIndex);
            return table != null ? table.ProximityMultiplier : 0f;
        }
    }
}
