using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 재화·명성·날짜·당일 매출을 보유한다. 명성은 <b>손님이 들어올 때마다 연령대만큼</b> 쌓인다
    /// (<see cref="Shop.CustomerManager.ReputationToday"/>) — 마감에 몰아서 주지 않는다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>
        /// 시작 자금. L1 하루 재고비가 310원이라 500원은 여유가 하루치도 안 됐고,
        /// L2(하루 재고비 450원) 승격을 감당할 운전자본을 모으기 전에 손님을 놓쳤다.
        /// 실측 4회에서 반복된 병목이라 L2 이틀치인 900원으로 올렸다. D27 조정 대상.
        /// </summary>
        [SerializeField] int startingMoney = 900;

        public int Money { get; private set; }
        public int Reputation { get; private set; }
        public int Day { get; private set; } = 1;
        public int DailyRevenue { get; private set; }

        /// <summary>돈이 어디로 나갔나 — 마감 정산서가 "오늘 쓴 돈"을 갈래별로 보여 준다.</summary>
        public enum SpendKind { Stock, Furniture, Dog, Other }
        readonly int[] spentToday = new int[4];
        public int SpentToday(SpendKind kind) => spentToday[(int)kind];
        public int SpentTodayTotal { get { int s = 0; foreach (int v in spentToday) s += v; return s; } }

        /// <summary>판매가 아닌 수입(퀘스트 보상 등). 오늘 번 돈 = 매출 + 이것.</summary>
        public int BonusToday { get; private set; }

        /// <summary>오늘 아침의 명성. 마감 때 "오늘 오른 명성" = 지금 − 이것(손님·온라인 주문·취소 벌점까지 다 들어간다).</summary>
        public int ReputationAtDayStart { get; private set; }

        public event Action<int> OnMoneyChanged;
        public event Action<int> OnReputationChanged;
        public event Action<int> OnDayChanged;
        public event Action<int> OnDaySettled;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Money = Exhibition.Active ? Exhibition.StartMoney : startingMoney;   // 시연 모드는 넉넉히 시작한다
            if (Exhibition.Active) Reputation = Exhibition.StartReputation;     // 시연은 다음 레벨 문턱 바로 아래에서
            ReputationAtDayStart = Reputation;
        }

        void Start()
        {
            TimeManager.Instance.OnDayEnded += HandleDayEnded;
            if (Exhibition.Active && GetComponent<ExhibitionSetup>() == null) gameObject.AddComponent<ExhibitionSetup>();   // 시연 첫 화면 차리기
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayEnded -= HandleDayEnded;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 마감 화면을 띄울 뿐 날짜는 넘기지 않는다. 명성은 낮 동안 손님마다 이미 붙었으므로
        /// 여기서는 "오늘 손님이 쌓아 준 명성"만 넘긴다. 마감 화면에서 다음 날로 넘어가면
        /// EveningEventMenu 가 BeginNextDay 를 부른다.
        /// </summary>
        void HandleDayEnded()
        {
            Shop.CustomerManager customers = Shop.CustomerManager.Instance;
            OnDaySettled?.Invoke(customers != null ? customers.ReputationToday : 0);
        }

        /// <summary>테스트용. 날짜만 바꾼다(도그쇼 바로 가기 — DogShow.DebugStart).</summary>
        public void DebugSetDay(int day)
        {
            Day = Mathf.Max(1, day);
            OnDayChanged?.Invoke(Day);
        }

        public void BeginNextDay()
        {
            if (!TimeManager.Instance.IsDayOver) return;

            Day++;
            DailyRevenue = 0;
            BonusToday = 0;
            for (int i = 0; i < spentToday.Length; i++) spentToday[i] = 0;
            ReputationAtDayStart = Reputation;
            OnDayChanged?.Invoke(Day);
            TimeManager.Instance.StartNewDay();
        }

        /// <summary>손님 1명이 상품 1개를 구매했다. 돈과 당일 매출을 함께 올린다.</summary>
        public void RegisterSale(int retail)
        {
            if (retail <= 0) return;
            Money += retail;
            DailyRevenue += retail;
            Exhibition.RecordSale(retail);
            OnMoneyChanged?.Invoke(Money);
        }

        public void AddMoney(int amount)
        {
            if (amount == 0) return;
            Money += amount;
            if (amount > 0) BonusToday += amount;
            OnMoneyChanged?.Invoke(Money);
        }

        public bool TrySpend(int cost, SpendKind kind = SpendKind.Other)
        {
            if (cost < 0 || cost > Money) return false;
            Money -= cost;
            spentToday[(int)kind] += cost;
            OnMoneyChanged?.Invoke(Money);
            return true;
        }

        /// <summary>음수를 넣으면 깎인다. 0 아래로는 내려가지 않는다.</summary>
        public void AddReputation(int amount)
        {
            if (amount == 0) return;

            int next = Mathf.Max(0, Reputation + amount);
            // 시연은 시작 명성(Lv 8 문턱) 밑으로 내려가지 않는다 — 1일차를 일찍 끝내며 손님을 놓치면 -4 로 2일차 레벨업이 막혔다(10-30 빌드 점검)
            if (Exhibition.Active) next = Mathf.Max(next, Exhibition.StartReputation);
            if (next == Reputation) return;

            Reputation = next;
            OnReputationChanged?.Invoke(Reputation);
        }

        public void RestoreFrom(SaveData data)
        {
            Money = data.money;
            Reputation = data.reputation;
            Day = Mathf.Max(1, data.day);
            DailyRevenue = Mathf.Max(0, data.dailyRevenue);
            BonusToday = Mathf.Max(0, data.bonusToday);
            for (int i = 0; i < spentToday.Length; i++)
                spentToday[i] = data.spentToday != null && i < data.spentToday.Length ? data.spentToday[i] : 0;
            // 없던 시절 세이브(-1)는 오늘 손님 명성만큼 빼서 어림한다
            ReputationAtDayStart = data.reputationDayStart >= 0 ? data.reputationDayStart : Mathf.Max(0, Reputation - data.reputationToday);
            OnMoneyChanged?.Invoke(Money);
            OnReputationChanged?.Invoke(Reputation);
            OnDayChanged?.Invoke(Day);
        }
    }
}
