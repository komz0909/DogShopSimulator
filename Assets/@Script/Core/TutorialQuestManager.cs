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
        /// <summary>온라인 주문 퀘스트의 번호(0부터).</summary>
        public const int OnlineOrderStep = 3;
        const float CheckEvery = 0.5f;

        public sealed class Quest
        {
            public readonly string objective;
            public readonly string detail;
            public readonly int target;

            public Quest(string objective, string detail, int target = 1)
            {
                this.objective = objective;
                this.detail = detail;
                this.target = target;
            }
        }

        static readonly Quest[] Quests =
        {
            new Quest("창고 물건 상자에 담기",
                "상자를 보고 E 로 들어 올린 뒤, 창고 선반을 보고 E 를 눌러 물건을 상자에 담자."),
            new Quest("진열하기",
                "상자를 든 채 진열대 가까이에서 R 을 눌러 상자 속 물건을 진열대에 올리자."),
            new Quest("POS 로 발주하기",
                "계산대의 POS 기계를 E 로 열어 가게에 팔 물건을 담고 주문하자. 손님 계산이 아니라 물건 발주다."),
            new Quest("온라인 주문 배송",
                "12시에 첫 온라인 주문이 들어온다. 계산대 옆 모니터에서 주문을 수락하고, 창고 포장대에서 물건을 담아 테이프로 포장한 뒤 배송하자."),
            new Quest("강아지 훈련하기",
                "강아지를 보고 E 를 눌러 상태를 확인하고, 훈련을 하나 골라 끝까지 해 보자."),
        };

        public static TutorialQuestManager Instance { get; private set; }

        public int QuestCount => Quests.Length;
        /// <summary>지금 퀘스트 번호(0부터). <see cref="QuestCount"/> 면 다 끝났다.</summary>
        public int Step { get; private set; }
        public int Progress { get; private set; }
        public bool IsComplete => Step >= Quests.Length;
        public Quest Current => IsComplete ? null : Quests[Step];

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
        }

        void OnDisable()
        {
            GameEvents.StorageToCrate -= OnStorageToCrate;
            GameEvents.ItemDisplayed -= OnItemDisplayed;
            GameEvents.StockOrdered -= OnStockOrdered;
            GameEvents.OnlineOrderShipped -= OnOnlineShipped;
            GameEvents.DogTrained -= OnDogTrained;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnStorageToCrate() => Advance(0);
        void OnItemDisplayed() => Advance(1);
        void OnStockOrdered() => Advance(2);
        void OnOnlineShipped(int orderId) => Advance(OnlineOrderStep);
        void OnDogTrained() => Advance(4);

        /// <summary>목표는 채웠고 [퀘스트 완료]를 눌러 보상을 받기만 하면 되는 상태.</summary>
        public bool ReadyToClaim => !IsComplete && Progress >= Quests[Step].target;

        /// <summary>그 퀘스트가 <b>지금</b> 퀘스트일 때만 센다 — 미리 해 둔 일은 쳐 주지 않는다.</summary>
        void Advance(int step)
        {
            if (Step != step || IsComplete || ReadyToClaim) return;
            Progress++;
            if (ReadyToClaim) Toast("퀘스트 달성! 오른쪽 위 [퀘스트 완료]를 눌러 보상을 받자");
            Changed();
        }

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
            checkTimer -= Time.unscaledDeltaTime;
            if (checkTimer > 0f) return;
            checkTimer = CheckEvery;
            EnsureTutorialOrder();
        }

        /// <summary>"온라인 주문 배송" 퀘스트가 아직 안 끝났나 — 그동안은 튜토리얼 주문이 확률 주문을 막는다.</summary>
        public bool WaitsForOnlineOrder => !IsComplete && Step <= OnlineOrderStep && !(Step == OnlineOrderStep && ReadyToClaim);

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
                : Step == OnlineOrderStep && hour >= TimeManager.OpenHour;   // 놓친 경우 다시
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
                Progress = IsComplete ? 0 : Mathf.Clamp(data.tutorialProgress, 0, Quests[Step].target);   // target = 보상 대기
                orderIssued = data.tutorialOrderIssued;
            }
            FinishedAt = -1f;
            checkTimer = 0f;
            Changed();
        }
    }
}
