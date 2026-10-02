using System;
using UnityEngine;

namespace DogShop.Data
{
    /// <summary>가게 레벨 1단계의 정의. Plan.md의 10레벨 곡선과 1:1 대응한다.</summary>
    [Serializable]
    public class ShopLevelDef
    {
        public int requiredReputation;
        public int upgradeCost;
        public int customersPerDay = 6;
        public int basketPriceTarget = 70;
        public int trainingSlots = 2;

        /// <summary>
        /// 운반 상자의 칸 수. 레벨 보상으로 커진다 — 한 번에 더 많이 나르면
        /// 창고와 진열대 사이를 덜 왕복한다. 하루가 5분뿐이라 그 왕복이 곧 시간이다.
        /// </summary>
        public int crateSlots = 8;

        public int heroStatGate;

        /// <summary>
        /// 판매장 바닥의 가로(x) 길이. 세로(z 0~6)와 창고는 그대로 두고 <b>오른쪽으로만</b> 늘린다.
        ///
        /// 오른쪽이 유일하게 빈 방향이다 — 앞(z&lt;0)은 배달 앞마당, 뒤(z&gt;6)는 창고,
        /// 왼쪽(x&lt;0)은 벽 바깥이다. 옆방은 z 6.1 부터 시작하므로 x 8~12.3 / z 0~6 구간과 겹치지 않는다.
        /// 12.0 을 넘기면 옆방 오른쪽 벽(x 12.3)과 부딪친다.
        /// </summary>
        [Range(8f, 12f)] public float floorWidth = 8f;

        public string unlockKo = "";
    }

    [CreateAssetMenu(menuName = "DogShop/Shop Level Table", fileName = "ShopLevelTable")]
    public class ShopLevelTable : ScriptableObject
    {
        [SerializeField] ShopLevelDef[] levels = new ShopLevelDef[0];

        public int MaxLevel => levels.Length;
        public ShopLevelDef Get(int level) => levels[Mathf.Clamp(level, 1, levels.Length) - 1];
    }
}
