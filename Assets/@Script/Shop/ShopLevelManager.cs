using System;
using DogShop.Core;
using DogShop.Data;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>가게 레벨 1~10. 명성 문턱 + 재화 + 주인공견 스탯 게이트를 모두 만족해야 올라간다.</summary>
    public class ShopLevelManager : MonoBehaviour, ISaveParticipant
    {
        public static ShopLevelManager Instance { get; private set; }

        [SerializeField] ShopLevelTable table;

        public int Level { get; private set; } = 1;

        /// <summary>주인공견 (미모 + 훈련도). DogManager가 D9-13에 채운다.</summary>
        public int HeroGrowthStat { get; set; }

        public event Action<int> OnLevelUp;

        public ShopLevelDef Current => table.Get(Level);
        public bool IsMaxLevel => Level >= table.MaxLevel;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public ShopLevelDef Next => IsMaxLevel ? null : table.Get(Level + 1);

        public bool CanLevelUp(out string reason)
        {
            if (IsMaxLevel) { reason = "최대 레벨"; return false; }

            ShopLevelDef next = Next;
            GameManager gm = GameManager.Instance;

            if (gm.Reputation < next.requiredReputation)
            {
                reason = "명성 부족 — " + gm.Reputation + " / " + next.requiredReputation;
                return false;
            }
            if (next.heroStatGate > 0 && HeroGrowthStat < next.heroStatGate)
            {
                reason = "주인공견 성장 스탯 부족 — " + HeroGrowthStat + " / " + next.heroStatGate;
                return false;
            }
            if (gm.Money < next.upgradeCost)
            {
                reason = "재화 부족 — " + gm.Money + " / " + next.upgradeCost;
                return false;
            }

            reason = null;
            return true;
        }

        public bool TryLevelUp()
        {
            string reason;
            if (!CanLevelUp(out reason)) return false;
            if (!GameManager.Instance.TrySpend(Next.upgradeCost)) return false;

            Level++;

            // 승급 당일은 리드타임을 없앤다. 손님이 한 번에 늘고 카테고리가 열리는 날인데
            // 하루를 기다리면 그 수요를 받을 재고가 존재할 수 없다.
            // RestoreFrom도 OnLevelUp을 쏘므로 이벤트가 아니라 여기서 직접 부른다 —
            // 세이브를 불러올 때마다 특급 입고가 공짜로 붙으면 안 된다.
            if (InventoryManager.Instance != null) InventoryManager.Instance.BeginRushDelivery();

            OnLevelUp?.Invoke(Level);
            return true;
        }

        public void CaptureInto(SaveData data) => data.shopLevel = Level;

        public void RestoreFrom(SaveData data)
        {
            int target = Mathf.Clamp(data.shopLevel, 1, table.MaxLevel);
            if (target == Level) return;

            Level = target;
            OnLevelUp?.Invoke(Level);
        }

        /// <summary>레벨업을 액션으로 감싸 거절 사유를 UI로 흘린다.</summary>
        public sealed class UpgradeAction : IPlayerAction
        {
            public bool CanExecute(out string reason) => Instance.CanLevelUp(out reason);
            public void Execute() => Instance.TryLevelUp();
        }
    }
}
