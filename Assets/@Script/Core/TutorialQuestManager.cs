using System;
using DogShop.Core;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 1일차 튜토리얼 퀘스트 다섯 개. <b>정해진 순서로 하나씩</b> — 지금 퀘스트만 진행되고,
    /// 목표를 채우면 HUD 의 [퀘스트 완료]를 눌러 50원을 받고 다음 퀘스트가 켜진다(<see cref="Claim"/>).
    /// 다섯 개를 다 끝내면 다시 시작하지 않는다(저장된다).
    ///
    /// 진행은 <see cref="GameEvents"/> 로만 센다 — 각 시스템이 일을 <b>실제로 끝낸</b> 자리에서 울리는 신호라,
    /// 창을 열거나 대상을 조준만 한 것으로는 깨지지 않는다.
    ///   1 창고 물건 상자에 담기 — 창고 선반에서 운반 상자로 1개(E)
    ///   2 진열하기 — 상자에서 진열대로 1개(R)
    ///   3 POS 로 발주하기 — 계산대 발주 창에서 상품 발주 결제
    ///   4 온라인 주문 배송 — 모니터 수락, 포장대에서 담기·테이프 포장, 배송
    ///   5 강아지 훈련하기 — 훈련 한 번(창만 열면 안 됨)
    ///
    /// <b>시연 모드</b>에서는 같은 판(HUD 오른쪽 위)에 <b>그날의 시연 할 일</b>(<see cref="DemoDays"/>)을 띄운다.
    /// 본편 튜토리얼과 시연 안내가 화면에 둘 다 떠서 무엇을 따라야 할지 헷갈렸다.
    /// 시연 할 일은 보상 없이 해내는 즉시 다음 줄로 넘어가고, 날이 바뀌면 그날 목록으로 새로 시작한다.
    ///
    /// 4번을 위해 <b>1일차 12시에 온라인 주문 하나를 보장</b>해 넣는다(확률과 무관, 한 번).
    /// 12시가 지나서 켜졌으면 곧바로 넣는다. 그 주문이 처리 중인 동안엔 확률 주문이 안 굴러 겹치지 않는다.
    /// 4번이 지금 퀘스트인데 그 주문이 취소됐거나(하루가 끝남) 4번 전에 미리 배송해 버렸으면
    /// 영업시간에 하나를 다시 넣는다 — 놓쳐서 튜토리얼이 막히지 않게. 동시에 둘이 생기지는 않는다.
    /// </summary>
    public class TutorialQuestManager : MonoBehaviour, ISaveParticipant
    {
        public const int Reward = 50;
        /// <summary>보장 온라인 주문이 들어오는 1일차 시각.</summary>
        const float TutorialOrderHour = 12f;
        /// <summary>본편 튜토리얼에서 온라인 주문 퀘스트의 번호(0부터).</summary>
        public const int OnlineOrderStep = 3;
        const float CheckEvery = 0.5f;

        /// <summary>퀘스트가 세는 일. 같은 신호를 본편 튜토리얼과 시연 할 일이 함께 쓴다.</summary>
        public enum Goal { Crate, Display, Order, Online, Train, Checkout, Care, Exercise, Health, Mascot, LevelUp }

        public sealed class Quest
        {
            public readonly Goal goal;
            public readonly string objective;
            public readonly string detail;
            public readonly int target;

            public Quest(Goal goal, string objective, string detail, int target = 1)
            {
                this.goal = goal;
                this.objective = objective;
                this.detail = detail;
                this.target = target;
            }
        }

        static readonly Quest[] Quests =
        {
            new Quest(Goal.Crate, "창고 물건 상자에 담기",
                "상자를 보고 E 로 들어 올린 뒤, 창고 선반을 보고 E 를 눌러 물건을 상자에 담자."),
            new Quest(Goal.Display, "진열하기",
                "상자를 든 채 진열대 가까이에서 R 을 눌러 상자 속 물건을 진열대에 올리자."),
            new Quest(Goal.Order, "POS 로 발주하기",
                "계산대의 POS 기계를 E 로 열어 가게에 팔 물건을 담고 주문하자. 손님 계산이 아니라 물건 발주다."),
            new Quest(Goal.Online, "온라인 주문 배송",
                "12시에 첫 온라인 주문이 들어온다. 계산대 옆 모니터에서 주문을 수락하고, 창고 포장대에서 물건을 담아 테이프로 포장한 뒤 배송하자."),
            new Quest(Goal.Train, "강아지 훈련하기",
                "강아지를 보고 E 를 눌러 상태를 확인하고, 훈련을 하나 골라 끝까지 해 보자."),
        };

        /// <summary>
        /// 시연 하루마다의 할 일. 1일차 가게 운영 → 2일차 강아지 키우기 → 3일차 도그쇼 준비.
        /// 3일차 마지막 줄이 끝나면 왼쪽 안내판의 [도그쇼 시작]으로 간다.
        /// </summary>
        static readonly Quest[][] DemoDays =
        {
            new[]
            {
                new Quest(Goal.Crate, "창고 물건 상자에 담기",
                    "상자를 보고 E 로 들어 올린 뒤, 창고 선반을 보고 E 를 눌러 물건을 상자에 담자."),
                new Quest(Goal.Display, "진열하기",
                    "상자를 든 채 진열대 가까이에서 R 을 눌러 상자 속 물건을 진열대에 올리자."),
                new Quest(Goal.Checkout, "손님 계산하기",
                    "물건을 고른 손님이 계산대 앞에 줄을 선다. 맨 앞 손님을 보고 E 를 눌러 계산하자.", 2),
                new Quest(Goal.Online, "온라인 주문 배송",
                    "12시에 온라인 주문이 들어온다. 계산대 옆 모니터에서 수락하고, 창고 포장대에서 물건을 담아 테이프로 포장한 뒤 배송하자."),
            },
            new[]
            {
                new Quest(Goal.Care, "강아지 돌보기",
                    "강아지를 보고 E 를 눌러 상태창을 열고, 밥, 목욕, 약 중 하나를 해 주자. 청결이나 건강이 40 아래면 훈련해도 자라지 않는다."),
                new Quest(Goal.Train, "강아지 훈련하기",
                    "상태창 훈련 칸에서 골라 훈련시키자. 미모와 훈련도가 오르고, 자란 강아지는 손님을 반겨 명성을 올려 준다.", 2),
                new Quest(Goal.Mascot, "마스코트 등급 올리기",
                    "미모, 훈련도, 어질리티를 합쳐 260이 되면 '스타 강아지'로 승급한다. 모자라면 훈련을 더 하자."),
                new Quest(Goal.LevelUp, "가게 레벨업",
                    "가게를 키우려면 명성과 함께 마스코트 등급이 필요하다. 계산대 POS 를 E 로 열고 [Lv8 레벨업]을 누르자."),
                new Quest(Goal.Exercise, "강아지 운동시키기",
                    "상태창 운동 칸에서 하나를 골라 시키자. 운동은 하루 한 번, 어질리티가 오른다."),
            },
            new[]
            {
                new Quest(Goal.Train, "마지막 훈련",
                    "도그쇼 전 마지막 훈련이다. 능력치가 높을수록 도그쇼 종목이 쉬워진다."),
                new Quest(Goal.Health, "건강 70 이상 만들기",
                    "도그쇼는 건강 70 이상이어야 나갈 수 있다. 상태창에서 밥이나 약으로 건강을 올리자."),
            },
        };

        public static TutorialQuestManager Instance { get; private set; }

        /// <summary>지금 시연 할 일을 보여 주는가(시연 모드). 아니면 본편 튜토리얼.</summary>
        public static bool Demo => Exhibition.Active;

        /// <summary>시연 할 일 목록이 맞춰져 있는 날. 날이 바뀌면 목록을 새로 시작한다.</summary>
        int demoDay = -1;

        static int Today => GameManager.Instance != null ? GameManager.Instance.Day : 1;

        Quest[] List => Demo ? DemoDays[Mathf.Clamp(Today, 1, DemoDays.Length) - 1] : Quests;

        public int QuestCount => List.Length;
        /// <summary>지금 퀘스트 번호(0부터). <see cref="QuestCount"/> 면 다 끝났다.</summary>
        public int Step { get; private set; }
        public int Progress { get; private set; }
        public bool IsComplete => Step >= List.Length;
        public Quest Current => IsComplete ? null : List[Step];

        /// <summary>마지막 퀘스트를 끝낸 시각(unscaled). 화면이 "튜토리얼 완료"를 잠깐 보여 주는 데 쓴다. 음수면 이번 플레이에서 안 끝냈다.</summary>
        public float FinishedAt { get; private set; } = -1f;

        /// <summary>바뀔 때마다 오른다 — HUD 가 다시 그릴지 비교한다.</summary>
        public int Version { get; private set; }
        public event Action OnChanged;

        bool orderIssued;
        float checkTimer;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable()
        {
            GameEvents.StorageToCrate += OnStorageToCrate;
            GameEvents.ItemDisplayed += OnItemDisplayed;
            GameEvents.StockOrdered += OnStockOrdered;
            GameEvents.OnlineOrderShipped += OnOnlineShipped;
            GameEvents.DogTrained += OnDogTrained;
            GameEvents.CustomerCheckedOut += OnCheckedOut;
            GameEvents.DogCared += OnDogCared;
            GameEvents.DogExercised += OnDogExercised;
            GameEvents.ShopLeveledUp += OnShopLeveledUp;
        }

        void OnDisable()
        {
            GameEvents.StorageToCrate -= OnStorageToCrate;
            GameEvents.ItemDisplayed -= OnItemDisplayed;
            GameEvents.StockOrdered -= OnStockOrdered;
            GameEvents.OnlineOrderShipped -= OnOnlineShipped;
            GameEvents.DogTrained -= OnDogTrained;
            GameEvents.CustomerCheckedOut -= OnCheckedOut;
            GameEvents.DogCared -= OnDogCared;
            GameEvents.DogExercised -= OnDogExercised;
            GameEvents.ShopLeveledUp -= OnShopLeveledUp;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnStorageToCrate() => Advance(Goal.Crate);
        void OnItemDisplayed() => Advance(Goal.Display);
        void OnStockOrdered() => Advance(Goal.Order);
        void OnOnlineShipped(int orderId) => Advance(Goal.Online);
        void OnDogTrained() => Advance(Goal.Train);
        void OnCheckedOut() => Advance(Goal.Checkout);
        void OnDogCared() => Advance(Goal.Care);
        void OnDogExercised() => Advance(Goal.Exercise);
        void OnShopLeveledUp() => Advance(Goal.LevelUp);

        /// <summary>목표는 채웠고 [퀘스트 완료]를 눌러 보상을 받기만 하면 되는 상태. 시연 할 일은 보상 없이 바로 넘어가므로 늘 false.</summary>
        public bool ReadyToClaim => !Demo && !IsComplete && Progress >= List[Step].target;

        /// <summary>그 퀘스트가 <b>지금</b> 퀘스트일 때만 센다 — 미리 해 둔 일은 쳐 주지 않는다.</summary>
        void Advance(Goal goal)
        {
            if (IsComplete || ReadyToClaim || List[Step].goal != goal) return;
            Progress++;
            if (Demo)
            {
                if (Progress >= List[Step].target) NextDemoStep();
                else Changed();
                return;
            }
            if (ReadyToClaim) Toast("퀘스트 달성! 오른쪽 위 [퀘스트 완료]를 눌러 보상을 받자");
            Changed();
        }

        /// <summary>시연 할 일 하나를 끝냈다 — 보상 없이 곧바로 다음 줄로.</summary>
        void NextDemoStep()
        {
            Step++;
            Progress = 0;
            if (IsComplete)
            {
                FinishedAt = Time.unscaledTime;
                Sound.Play(Sfx.TrackSuccess);
                Toast(Exhibition.IsShowDay(Today)
                    ? "준비 완료! 왼쪽 [도그쇼 시작]을 누르자 (G)"
                    : "오늘 할 일 완료! 잠시 뒤 다음 날로 넘어간다");
            }
            else
            {
                Sound.Play(Sfx.ArrowCorrect);
                Toast("완료!  다음 할 일: " + List[Step].objective);
            }
            Changed();
        }

        /// <summary>HUD 판 머리글.</summary>
        public string Header
        {
            get
            {
                if (Demo)
                {
                    string head = "시연 " + Mathf.Clamp(Today, 1, Exhibition.Days) + "일차  오늘 할 일";
                    return IsComplete ? head : head + " " + (Step + 1) + "/" + QuestCount;
                }
                if (IsComplete) return "튜토리얼";
                return "튜토리얼 " + (Step + 1) + "/" + QuestCount + (ReadyToClaim ? "   달성!" : "   +" + Reward + "원");
            }
        }

        /// <summary>다 끝냈을 때 판에 띄우는 한 줄과 그 아래 설명(시연만, 본편은 설명 없음).</summary>
        public string CompleteText => Demo ? (Exhibition.IsShowDay(Today) ? "준비 완료!" : "오늘 할 일 완료!") : "튜토리얼 완료!";
        public string CompleteDetail => !Demo ? null
            : Exhibition.IsShowDay(Today) ? "왼쪽 [도그쇼 시작]을 누르거나 G 를 누르자."
            : "잠시 뒤 다음 날로 넘어간다. 바로 가려면 왼쪽 버튼을 누르자.";

        /// <summary>
        /// [퀘스트 완료] — 보상 50원을 받고 다음 퀘스트로. 달성했을 때만 된다.
        /// 바로 넘어가면 심심해서 한 번 눌러 받는 손맛을 둔다. 단계가 한 방향으로만 오르므로 같은 보상을 두 번 받을 길이 없다.
        /// </summary>
        public bool Claim()
        {
            if (!ReadyToClaim) return false;
            Step++;
            Progress = 0;
            if (GameManager.Instance != null) GameManager.Instance.AddMoney(Reward);

            if (IsComplete)
            {
                FinishedAt = Time.unscaledTime;
                Toast("튜토리얼 완료! +" + Reward + "원");
            }
            else Toast("보상 +" + Reward + "원  다음 퀘스트: " + Quests[Step].objective);
            Changed();
            return true;
        }

        void Changed()
        {
            Version++;
            OnChanged?.Invoke();
        }

        void Update()
        {
            if (Demo && Today != demoDay) StartDemoDay();

            checkTimer -= Time.unscaledDeltaTime;
            if (checkTimer > 0f) return;
            checkTimer = CheckEvery;
            if (Demo) CheckStateGoals();
            EnsureTutorialOrder();
        }

        /// <summary>날이 바뀌었다 — 그날의 시연 할 일을 처음부터.</summary>
        void StartDemoDay()
        {
            demoDay = Today;
            Step = 0;
            Progress = 0;
            FinishedAt = -1f;
            Changed();
        }

        /// <summary>
        /// "건강 70 이상", "마스코트 등급", "가게 레벨업" 은 하는 일이 아니라 상태라 신호 대신 직접 본다.
        /// 레벨업을 신호로만 세면, 할 일 순서를 앞질러(돌보기 전에 훈련으로 승급하고 바로 레벨업) 해 버린 관람객은
        /// 다시 레벨업할 수 없어 그 줄에 영영 멈췄다 — 다음 줄인 운동도 "미션 때 열림"이라 같이 잠겼다.
        /// </summary>
        void CheckStateGoals()
        {
            if (IsComplete) return;
            Goal goal = List[Step].goal;
            if (goal == Goal.Health)
            {
                Dogs.Dog hero = Dogs.DogManager.Instance != null ? Dogs.DogManager.Instance.Hero : null;
                if (hero != null && hero.Stats.Health >= Show.ChampionshipManager.HealthGate) Advance(Goal.Health);
            }
            else if (goal == Goal.Mascot && MascotGateMet) Advance(Goal.Mascot);
            else if (goal == Goal.LevelUp && LeveledUpThisRun) Advance(Goal.LevelUp);
        }

        /// <summary>시연에서 가게를 이미 한 번 이상 키웠나(시작 레벨보다 높다). 본편 튜토리얼에는 레벨업 할 일이 없다.</summary>
        static bool LeveledUpThisRun =>
            Exhibition.Active && ShopLevelManager.Instance != null && ShopLevelManager.Instance.Level > Exhibition.StartLevel;

        /// <summary>다음 가게 레벨이 요구하는 마스코트 등급을 채웠나. 요구가 없거나 최대 레벨이면 채운 것으로 본다.</summary>
        static bool MascotGateMet
        {
            get
            {
                ShopLevelManager levels = ShopLevelManager.Instance;
                if (levels == null) return false;
                Data.ShopLevelDef next = levels.Next;
                return next == null || next.heroStatGate <= 0 || levels.HeroGrowthStat >= next.heroStatGate;
            }
        }

        /// <summary>
        /// 시연에서 그 일(훈련·운동)을 아직 잠가 둬야 하나. 할 일에 나오기 <b>전에</b> 해 버리면
        /// 하루 횟수(운동 하루 한 번, 같은 훈련 하루 한 번)를 미리 써서, 정작 그 할 일이 떴을 때 다음 날까지 막혔다.
        ///   오늘 목록에 뒤쪽으로 남아 있으면 → 그 차례가 올 때까지 잠금
        ///   오늘 목록엔 없고 다음 날 목록에 있으면 → 오늘은 잠금(1일차의 훈련·운동)
        ///   오늘 할 일을 다 끝냈거나 더 나올 일이 없으면 → 자유
        /// </summary>
        public static bool DemoLocked(Goal goal, out string reason)
        {
            reason = null;
            TutorialQuestManager q = Instance;
            if (!Demo || q == null || q.IsComplete) return false;

            int day = Mathf.Clamp(Today, 1, DemoDays.Length);
            Quest[] today = DemoDays[day - 1];
            for (int i = 0; i < today.Length; i++)
            {
                if (today[i].goal != goal) continue;
                if (i <= q.Step) return false;
                reason = "미션 때 열림";
                return true;
            }
            for (int d = day; d < DemoDays.Length; d++)
                foreach (Quest later in DemoDays[d])
                    if (later.goal == goal) { reason = "미션 때 열림"; return true; }
            return false;
        }

        /// <summary>지금 목록에서 온라인 주문 할 일의 번호. 없으면 -1.</summary>
        int OnlineIndex
        {
            get
            {
                Quest[] list = List;
                for (int i = 0; i < list.Length; i++) if (list[i].goal == Goal.Online) return i;
                return -1;
            }
        }

        /// <summary>"온라인 주문 배송" 퀘스트가 아직 안 끝났나 — 그동안은 튜토리얼 주문이 확률 주문을 막는다.</summary>
        public bool WaitsForOnlineOrder
        {
            get
            {
                int online = OnlineIndex;
                return !IsComplete && online >= 0 && Step <= online && !(Step == online && ReadyToClaim);
            }
        }

        /// <summary>보장 온라인 주문이 필요하면 넣는다. 매니저가 아직 없으면 다음에 다시 본다.</summary>
        void EnsureTutorialOrder()
        {
            if (!WaitsForOnlineOrder) return;

            OnlineOrderManager online = OnlineOrderManager.Instance;
            TimeManager time = TimeManager.Instance;
            GameManager game = GameManager.Instance;
            if (online == null || time == null || game == null) return;
            if (time.IsDayOver || online.TutorialOrderPending) return;
            // 도그쇼 날처럼 가게가 꺼져 있으면(ShopHours 를 끈다) 넣지 않는다 — 아무도 못 보고 취소된다
            if (ShopHours.Instance == null || !ShopHours.Instance.isActiveAndEnabled) return;
            // 시연 3일차(도그쇼 날)는 가게를 열지 않으니 튜토리얼 주문도 넣지 않는다
            if (Exhibition.IsShowDay(game.Day)) return;

            float hour = time.CurrentHour;
            bool due = !orderIssued
                ? hour >= (game.Day <= 1 ? TutorialOrderHour : TimeManager.OpenHour)
                : Step == OnlineIndex && hour >= TimeManager.OpenHour;   // 놓친 경우 다시
            if (!due) return;

            if (online.AddTutorialOrder()) orderIssued = true;
        }

        static void Toast(string message)
        {
            var hud = FindAnyObjectByType<UI.GameHud>();
            if (hud != null) hud.Toast(message, new Color(1f, 0.84f, 0.38f));
        }

        // ---- 저장 ----

        public void CaptureInto(SaveData data)
        {
            data.tutorialStep = Step;
            data.tutorialProgress = Progress;
            data.tutorialOrderIssued = orderIssued;
        }

        public void RestoreFrom(SaveData data)
        {
            // 튜토리얼이 생기기 전 세이브: 2일차 이상이면 이미 가게를 돌려 본 것이라 끝난 것으로 본다
            if (data.version < 8 && data.day > 1)
            {
                Step = Quests.Length;
                Progress = 0;
                orderIssued = true;
            }
            else
            {
                Step = Mathf.Clamp(data.tutorialStep, 0, Quests.Length);
                Progress = Step >= Quests.Length ? 0 : Mathf.Clamp(data.tutorialProgress, 0, Quests[Step].target);   // target = 보상 대기
                orderIssued = data.tutorialOrderIssued;
            }
            FinishedAt = -1f;
            checkTimer = 0f;
            Changed();
        }
    }
}
