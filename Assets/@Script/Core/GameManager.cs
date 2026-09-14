using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>재화·명성·날짜·당일 매출을 보유한다. 명성은 마감 시 당일 매출에서 환산된다.</summary>
    public class GameManager : MonoBehaviour
    {
        /// <summary>판매 명성 = 당일 매출 / 이 값.</summary>
        public const int RevenuePerReputation = 100;

        public static GameManager Instance { get; private set; }

        public int Money { get; private set; } = 500;
        public int Reputation { get; private set; }
        public int Day { get; private set; } = 1;
        public int DailyRevenue { get; private set; }

        public event Action<int> OnMoneyChanged;
        public event Action<int> OnReputationChanged;
        public event Action<int> OnDayChanged;
        public event Action<int> OnDaySettled;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayEnded += HandleDayEnded;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayEnded -= HandleDayEnded;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 마감 시 명성만 정산하고 날짜는 넘기지 않는다.
        /// 마감 후 이벤트 카드를 고른 뒤 EveningEventMenu가 BeginNextDay를 호출한다.
        /// </summary>
        void HandleDayEnded()
        {
            int gained = DailyRevenue / RevenuePerReputation;
            if (gained > 0) AddReputation(gained);

            OnDaySettled?.Invoke(gained);
        }

        public void BeginNextDay()
        {
            if (!TimeManager.Instance.IsDayOver) return;

            Day++;
            DailyRevenue = 0;
            OnDayChanged?.Invoke(Day);
            TimeManager.Instance.StartNewDay();
        }

        /// <summary>손님 1명이 상품 1개를 구매했다. 돈과 당일 매출을 함께 올린다.</summary>
        public void RegisterSale(int retail)
        {
            if (retail <= 0) return;
            Money += retail;
            DailyRevenue += retail;
            OnMoneyChanged?.Invoke(Money);
        }

        public void AddMoney(int amount)
        {
            if (amount == 0) return;
            Money += amount;
            OnMoneyChanged?.Invoke(Money);
        }

        public bool TrySpend(int cost)
        {
            if (cost < 0 || cost > Money) return false;
            Money -= cost;
            OnMoneyChanged?.Invoke(Money);
            return true;
        }

        public void AddReputation(int amount)
        {
            if (amount <= 0) return;
            Reputation += amount;
            OnReputationChanged?.Invoke(Reputation);
        }

        public void RestoreFrom(SaveData data)
        {
            Money = data.money;
            Reputation = data.reputation;
            Day = Mathf.Max(1, data.day);
            DailyRevenue = Mathf.Max(0, data.dailyRevenue);
            OnMoneyChanged?.Invoke(Money);
            OnReputationChanged?.Invoke(Reputation);
            OnDayChanged?.Invoke(Day);
        }
    }
}
