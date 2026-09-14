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
        public int dogSlots = 2;
        public int trainingSlots = 2;
        public int heroStatGate;
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
