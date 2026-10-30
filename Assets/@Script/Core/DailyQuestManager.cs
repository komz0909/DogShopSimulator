using System;
using DogShop.Core;
using DogShop.Data;
using DogShop.Dogs;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 일일 퀘스트 "오늘의 가게 목표"(2026-10-30). <b>본편</b>에서 튜토리얼을 끝낸 다음 날부터 도그쇼 전날까지 매일 아침 3개.
    ///   가게 칸(손님 계산·진열·온라인 배송·추천 상품 판매) / 강아지 칸(밥·목욕·약·훈련·운동) / 연결 칸(마스코트 인사·사료 판매·추천 상품)
    /// 가게와 강아지를 함께 요구하도록 칸을 나눴다 — 한쪽만 하면 하루 목표를 다 채울 수 없다.
    /// 강아지 칸은 <b>모자란 곳</b>을 짚는다: 건강·청결이 60 아래면 더 낮은 쪽 돌봄(약·밥·목욕),
    /// 괜찮으면 미모·훈련도·어질리티 중 가장 낮은 능력치(그 축 훈련 / 운동). 능력치 보상도 가장 낮은 축에 준다.
    ///
    /// 보상은 <b>퀘스트마다 따로</b> [받기]로 받는다. 종류도 그날그날 다르다: 돈만 / 명성만 / 강아지 능력치만 / 섞어서.
    /// 강아지 칸은 능력치 보상이 잘 나온다. 수치는 날이 갈수록 조금씩 커진다(<see cref="RewardFor"/>).
    /// 하루가 끝날 때 달성해 놓고 안 받은 보상은 자동으로 준다 — 깜빡해서 날리지 않게.
    ///
    /// 진행은 튜토리얼과 같은 <see cref="GameEvents"/> 신호로만 센다(실제로 해낸 자리에서 울린다).
    /// 시연 모드에는 나오지 않는다. 30일 측정 봇이 돌 때는 달성 즉시 받는다.
    /// </summary>
    public class DailyQuestManager : MonoBehaviour, ISaveParticipant
    {
        public enum Kind { Checkout, Display, Online, SellRecommended, SellFood, Feed, Bath, Medicine, Train, Exercise, Greet }
        public enum RewardKind { Money, Reputation, Stat, Mixed }

        public const int PerDay = 3;

        [Serializable]
        public class Quest
        {
            public Kind kind;
            public int target;
            public int progress;
            public bool claimed;
            public RewardKind reward;
            public int money;
            public int reputation;
            public GrowthAxis axis;
            public int stat;
            public int product = -1;   // SellRecommended 이 가리키는 상품(그날 추천 상품)
            public int trainAxis = -1; // Train 이 요구하는 축(GrowthAxis, -1 = 아무 훈련). 두 축 훈련(놀아주기)은 양쪽에 다 친다

            public bool Done => progress >= target;
            public bool ReadyToClaim => Done && !claimed;
        }

        public static DailyQuestManager Instance { get; private set; }
        public event Action OnChanged;
        public int Version { get; private set; }

        /// <summary>오늘 목표가 만들어진 날(0 = 없음).</summary>
        public int QuestDay { get; private set; }
        public readonly Quest[] Quests = new Quest[PerDay];

        /// <summary>지금 판에 띄울 목표가 있나. 시연·튜토리얼 중·도그쇼 날에는 없다.</summary>
        public bool Active => !Exhibition.Active && QuestDay > 0 && QuestDay == Today && Quests[0] != null;
        public int DoneCount { get { int n = 0; foreach (Quest q in Quests) if (q != null && q.Done) n++; return n; } }
        public bool AnyReady { get { foreach (Quest q in Quests) if (q != null && q.ReadyToClaim) return true; return false; } }
        public bool AnyUnclaimed { get { foreach (Quest q in Quests) if (q != null && !q.claimed) return true; return false; } }

        static int Today => GameManager.Instance != null ? GameManager.Instance.Day : 1;

        /// <summary>튜토리얼을 끝낸 날(그다음 날부터 나온다). 0 = 아직.</summary>
        int tutorialDoneDay;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable()
        {
            GameEvents.CustomerCheckedOut += OnCheckedOut;
            GameEvents.ItemDisplayed += OnDisplayed;
            GameEvents.OnlineOrderShipped += OnShipped;
            GameEvents.ProductSold += OnProductSold;
            GameEvents.DogCareDone += OnCareDone;
            GameEvents.DogTrainedAxis += OnTrained;
            GameEvents.DogExercised += OnExercised;
            GameEvents.MascotGreeted += OnGreeted;
            Subscribe();
        }

        bool subscribed;

        void Start() => Subscribe();

        /// <summary>GameManager 가 이 컴포넌트보다 늦게 깨어날 수 있어 Start 에서 한 번 더 붙는다.</summary>
        void Subscribe()
        {
            if (subscribed || GameManager.Instance == null) return;
            GameManager.Instance.OnDaySettled += OnDaySettled;
            subscribed = true;
        }

        void OnDisable()
        {
            GameEvents.CustomerCheckedOut -= OnCheckedOut;
            GameEvents.ItemDisplayed -= OnDisplayed;
            GameEvents.OnlineOrderShipped -= OnShipped;
            GameEvents.ProductSold -= OnProductSold;
            GameEvents.DogCareDone -= OnCareDone;
            GameEvents.DogTrainedAxis -= OnTrained;
            GameEvents.DogExercised -= OnExercised;
            GameEvents.MascotGreeted -= OnGreeted;
            if (subscribed && GameManager.Instance != null) GameManager.Instance.OnDaySettled -= OnDaySettled;
            subscribed = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnCheckedOut() => Count(Kind.Checkout, -1);
        void OnDisplayed() => Count(Kind.Display, -1);
        void OnShipped(int orderId) => Count(Kind.Online, -1);
        void OnTrained(int axis) => Count(Kind.Train, axis);
        void OnExercised() => Count(Kind.Exercise, -1);
        void OnGreeted() => Count(Kind.Greet, -1);

        void OnProductSold(int product)
        {
            Count(Kind.SellRecommended, product);
            if (product == DogCare.FoodIndex || product == DogCare.PremiumFoodIndex) Count(Kind.SellFood, -1);
        }

        void OnCareDone(int care)   // 0 밥, 1 목욕, 2 약
        {
            Count(care == 0 ? Kind.Feed : care == 1 ? Kind.Bath : Kind.Medicine, -1);
        }

        void Count(Kind kind, int product)
        {
            if (!Active) return;
            bool changed = false;
            foreach (Quest q in Quests)
            {
                if (q == null || q.kind != kind || q.Done) continue;
                if (kind == Kind.SellRecommended && q.product != product) continue;
                if (kind == Kind.Train && q.trainAxis >= 0 && !TrainingManager.Covers((GrowthAxis)product, (GrowthAxis)q.trainAxis)) continue;
                q.progress++;
                changed = true;
                if (q.Done)
                {
                    Sound.Play(Sfx.ArrowCorrect);
                    Toast("오늘의 목표 달성! " + Objective(q) + "  [받기]로 보상을 받자");
                    if (Measuring) Claim(Array.IndexOf(Quests, q));
                }
            }
            if (changed) Changed();
        }

        static bool Measuring => Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active;

        void Update()
        {
            if (Exhibition.Active || GameManager.Instance == null) return;
            var tutorial = TutorialQuestManager.Instance;
            bool tutorialDone = tutorial == null || tutorial.IsComplete;
            // 오늘 막 끝냈으면 내일부터, 불러온 세이브처럼 전에 끝나 있었으면 오늘부터
            if (tutorialDone && tutorialDoneDay == 0)
                tutorialDoneDay = tutorial != null && tutorial.FinishedAt < 0f && Today > 1 ? Today - 1 : Today;
            if (!tutorialDone || QuestDay == Today) return;
            if (Today <= tutorialDoneDay || Today >= Show.ChampionshipManager.LastDay) return;   // 시즌마다 도그쇼 날은 쉰다
            MakeDay(Today);
        }

        // ---- 하루 목표 만들기 ----

        void MakeDay(int day)
        {
            var rng = new System.Random(day * 7919 + 17);
            QuestDay = day;
            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            int recommended = InventoryManager.Instance != null ? InventoryManager.Instance.RecommendedToday : -1;

            // 가게 칸
            Kind[] shop = recommended >= 0
                ? new[] { Kind.Checkout, Kind.Display, Kind.Online, Kind.SellRecommended }
                : new[] { Kind.Checkout, Kind.Display, Kind.Online };
            Kind a = shop[rng.Next(shop.Length)];

            // 강아지 칸 — 모자란 곳을 짚는다.
            // 1) 돌봄: 건강·청결 중 60 아래로 떨어진 것, 둘 다면 더 낮은 쪽(건강 40 미만은 약, 그 위는 밥)
            // 2) 돌봄이 괜찮으면 성장: 미모·훈련도·어질리티 중 가장 낮은 축(어질리티는 운동, 나머지는 그 축 훈련)
            GrowthAxis weak = Weakest(hero, rng);
            DogStats st = hero != null ? hero.Stats : null;
            bool sick = st != null && st.Health < 60;
            bool dirty = st != null && st.Cleanliness < 60;
            Kind b;
            int trainAxis = -1;
            if (sick && (!dirty || st.Health <= st.Cleanliness)) b = st.Health < 40 ? Kind.Medicine : Kind.Feed;
            else if (dirty) b = Kind.Bath;
            else if (weak == GrowthAxis.Agility) b = Kind.Exercise;
            else { b = Kind.Train; trainAxis = (int)weak; }

            // 연결 칸 — 가게와 강아지가 같이 걸린 것. 위에서 고른 것과 겹치지 않게
            Kind[] link = { Kind.Greet, Kind.SellFood, Kind.SellRecommended };
            Kind c;
            int guard = 0;
            do { c = link[rng.Next(link.Length)]; }
            while (guard++ < 10 && (c == a || (c == Kind.SellRecommended && recommended < 0)));
            if (c == a || (c == Kind.SellRecommended && recommended < 0)) c = Kind.Greet;

            Quests[0] = Make(a, day, false, recommended, weak, rng);
            Quests[1] = Make(b, day, true, recommended, weak, rng);
            Quests[2] = Make(c, day, false, recommended, weak, rng);
            if (b == Kind.Train) { Quests[1].trainAxis = trainAxis; Quests[1].target = 1; }   // 축을 정한 훈련은 하루 한 번이면 된다
            Changed();
            Toast("오늘의 가게 목표가 나왔다! 오른쪽 위를 확인하자");
        }

        /// <summary>미모·훈련도·어질리티 중 가장 낮은 축. 같으면 그중 아무거나.</summary>
        static GrowthAxis Weakest(Dog hero, System.Random rng)
        {
            GrowthAxis[] axes = { GrowthAxis.Beauty, GrowthAxis.Training, GrowthAxis.Agility };
            if (hero == null) return axes[rng.Next(axes.Length)];
            int[] v = { hero.Stats.Beauty, hero.Stats.Training, hero.Stats.Agility };
            int min = Mathf.Min(v[0], Mathf.Min(v[1], v[2]));
            var tied = new System.Collections.Generic.List<GrowthAxis>();
            for (int i = 0; i < 3; i++) if (v[i] == min) tied.Add(axes[i]);
            return tied[rng.Next(tied.Count)];
        }

        static Quest Make(Kind kind, int day, bool dogSlot, int recommended, GrowthAxis weak, System.Random rng)
        {
            var q = new Quest { kind = kind, target = TargetFor(kind, day) };
            if (kind == Kind.SellRecommended) q.product = recommended;

            // 보상 종류 — 강아지 칸은 능력치가 잘 나온다
            int roll = rng.Next(100);
            q.reward = dogSlot
                ? (roll < 45 ? RewardKind.Stat : roll < 65 ? RewardKind.Mixed : roll < 85 ? RewardKind.Money : RewardKind.Reputation)
                : (roll < 35 ? RewardKind.Money : roll < 60 ? RewardKind.Reputation : roll < 80 ? RewardKind.Mixed : RewardKind.Stat);
            RewardFor(q, day, weak, rng);
            return q;
        }

        /// <summary>목표 수치. 날이 갈수록 조금씩 늘어난다(2일차 → 29일차).</summary>
        static int TargetFor(Kind kind, int day)
        {
            day = Mathf.Min(day, Show.ChampionshipManager.FinalDay);   // 시즌 2 부터는 30일차 크기 그대로
            switch (kind)
            {
                case Kind.Checkout: return 3 + day / 5;          // 3 → 8
                case Kind.Display: return 3 + day / 7;           // 3 → 7
                case Kind.SellRecommended: return 2 + day / 10;  // 2 → 4
                case Kind.SellFood: return 2 + day / 10;
                case Kind.Train: return day < 12 ? 1 : 2;        // 훈련은 돈·칸이 드니 많아야 2번
                case Kind.Greet: return 2 + day / 10;
                default: return 1;                               // 온라인 배송·밥·목욕·약·운동
            }
        }

        /// <summary>
        /// 보상 수치. 하루 매출(67차 측정 평균 약 1,400원)의 10% 안팎이 되게 — 셋을 다 받아도 하루 판도를 바꾸지 않는다.
        /// 돈 60~200원, 명성 2~5, 능력치 +2~4, 섞으면 각각 절반쯤.
        /// </summary>
        static void RewardFor(Quest q, int day, GrowthAxis weak, System.Random rng)
        {
            day = Mathf.Min(day, Show.ChampionshipManager.FinalDay);
            int money = 60 + day * 5;
            int rep = 2 + day / 8;
            int stat = 2 + day / 14;
            q.axis = weak;   // 능력치 보상은 가장 낮은 축을 채운다
            switch (q.reward)
            {
                case RewardKind.Money: q.money = money; break;
                case RewardKind.Reputation: q.reputation = rep; break;
                case RewardKind.Stat: q.stat = stat; break;
                default:
                    q.money = Mathf.RoundToInt(money * 0.5f / 10f) * 10;
                    if (rng.Next(2) == 0) q.reputation = Mathf.Max(1, rep / 2);
                    else q.stat = Mathf.Max(1, stat / 2);
                    break;
            }
        }

        // ---- 보상 ----

        public bool Claim(int index)
        {
            if (index < 0 || index >= Quests.Length) return false;
            Quest q = Quests[index];
            if (q == null || !q.ReadyToClaim) return false;
            q.claimed = true;
            GameManager g = GameManager.Instance;
            if (q.money > 0 && g != null) g.AddMoney(q.money);
            if (q.reputation > 0 && g != null) g.AddReputation(q.reputation);
            if (q.stat > 0)
            {
                Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
                if (hero != null) hero.Stats.AddGrowth(q.axis, q.stat);
            }
            Sound.Play(Sfx.CoinJingle);
            Toast("보상 " + RewardText(q));
            Changed();
            return true;
        }

        /// <summary>
        /// 측정 모드 전용 — 오늘 목표를 전부 달성한 것으로 치고 보상을 받는다. 봇은 진열·훈련을
        /// 퀘스트가 세는 길(GameEvents)로 하지 않으므로, "완벽한 운영" 가정대로 대신 채운다.
        /// </summary>
        public void CompleteAllForMeasurement()
        {
            if (!Active) return;
            for (int i = 0; i < Quests.Length; i++)
            {
                Quest q = Quests[i];
                if (q == null || q.claimed) continue;
                q.progress = q.target;
                Claim(i);
            }
        }

        /// <summary>하루가 끝났다 — 달성하고 안 받은 보상은 대신 받아 준다.</summary>
        void OnDaySettled(int reputationToday)
        {
            if (!Active) return;
            for (int i = 0; i < Quests.Length; i++) if (Quests[i] != null && Quests[i].ReadyToClaim) Claim(i);
        }

        // ---- 글 ----

        public static string Objective(Quest q)
        {
            switch (q.kind)
            {
                case Kind.Checkout: return "손님 " + q.target + "명 계산하기";
                case Kind.Display: return "물건 " + q.target + "번 진열하기";
                case Kind.Online: return "온라인 주문 배송하기";
                case Kind.SellRecommended:
                    string name = InventoryManager.Instance != null && q.product >= 0 ? InventoryManager.Instance.Catalog.Get(q.product).nameKo : "추천 상품";
                    return name + " " + q.target + "개 팔기";
                case Kind.SellFood: return "사료 " + q.target + "개 팔기";
                case Kind.Feed: return "우리 강아지 밥 주기";
                case Kind.Bath: return "강아지 목욕시키기";
                case Kind.Medicine: return "강아지 약 먹이기";
                case Kind.Train: return q.trainAxis >= 0
                    ? AxisName((GrowthAxis)q.trainAxis) + " 올리는 훈련 " + q.target + "번"
                    : "강아지 훈련 " + q.target + "번";
                case Kind.Exercise: return "강아지 운동시키기";
                case Kind.Greet: return "마스코트가 손님 " + q.target + "명 반기기";
            }
            return q.kind.ToString();
        }

        static string AxisName(GrowthAxis axis) => axis == GrowthAxis.Beauty ? "미모" : axis == GrowthAxis.Training ? "훈련도" : "어질리티";

        public static string RewardText(Quest q)
        {
            string s = "";
            if (q.money > 0) s += "+" + q.money + "원";
            if (q.reputation > 0) s += (s.Length > 0 ? ", " : "") + "명성 +" + q.reputation;
            if (q.stat > 0) s += (s.Length > 0 ? ", " : "") + AxisName(q.axis) + " +" + q.stat;
            return s;
        }

        void Changed()
        {
            Version++;
            OnChanged?.Invoke();
        }

        static void Toast(string message)
        {
            var hud = FindAnyObjectByType<UI.GameHud>();
            if (hud != null) hud.Toast(message, new Color(1f, 0.84f, 0.38f));
        }

        // ---- 저장 ----

        public void CaptureInto(SaveData data)
        {
            data.dailyQuestDay = QuestDay;
            data.dailyTutorialDoneDay = tutorialDoneDay;
            data.dailyQuests = new Quest[PerDay];
            for (int i = 0; i < PerDay; i++) data.dailyQuests[i] = Quests[i];
        }

        public void RestoreFrom(SaveData data)
        {
            QuestDay = data.dailyQuestDay;
            tutorialDoneDay = data.dailyTutorialDoneDay;
            for (int i = 0; i < PerDay; i++)
                Quests[i] = data.dailyQuests != null && i < data.dailyQuests.Length ? data.dailyQuests[i] : null;
            // 목표가 없던 세이브거나 비어 있으면 다음 Update 에서 오늘 것을 새로 만든다
            if (Quests[0] == null || Quests[0].target <= 0) QuestDay = 0;
            Changed();
        }
    }
}
