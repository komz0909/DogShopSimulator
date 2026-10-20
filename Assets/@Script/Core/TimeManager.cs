using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 하루: 09:00 일어나 준비 → 10:00 문 열기 → 19:00 저절로 영업 종료 → 21:00 까지 정산·발주 → 잠.
    /// 하루(09~21시)를 실시간 5분에 흘린다. 영업은 09:30~19:30(<see cref="Shop.ShopHours"/>).
    /// </summary>
    public class TimeManager : MonoBehaviour, ISaveParticipant
    {
        /// <summary>
        /// 아침에 눈을 뜨는 시각. 전날 발주한 물건이 이때 가게 앞에 도착한다.
        ///
        /// <see cref="OpenHour"/>와 <b>다르다</b> — 이건 하루가 시작하는 시각이고,
        /// 저건 손님이 제값으로 들어오기 시작하는 시각이다. 둘을 하나로 묶으면
        /// 하루치 손님이 열 시간에 퍼져 시간당 유입이 조용히 10% 줄어든다.
        /// </summary>
        public const float DayStartHour = 9f;

        /// <summary>문이 <b>저절로</b> 열리는 시각(09:30). 손님 배분의 기준이기도 하다. 예전엔 [가게 열기]를 눌러야 10시에 열었다.</summary>
        public const float OpenHour = 9.5f;

        /// <summary>영업이 끝나는 시각(19:30). 가게 문이 저절로 닫히고 정산·발주 시간이 된다.</summary>
        public const float CloseHour = 19.5f;

        /// <summary>
        /// 시계가 멈추는 시각. 하루를 저절로 끝내지는 않는다 — 화면 가운데 "잠자러 가기"가 뜨고
        /// (<see cref="UI.SleepPrompt"/>) 눌러야 넘어간다. 그 전에 하던 발주를 마저 할 수 있게.
        /// </summary>
        public const float LastHour = 21f;

        /// <summary>손님 도착량을 나누는 기준. 09:30~19:30 열 시간에 하루치가 다 온다.</summary>
        public const float HoursPerDay = CloseHour - OpenHour;

        /// <summary>
        /// <b>하루 전체</b>(9시 기상 ~ 21시 시계 멈춤, 12시간)를 흘리는 실시간 — 1배속 5분.
        /// 예전엔 영업 구간(10~19시)만 5분이라 준비·마감까지 하면 하루가 7~8분이었다. 템포가 늘어져 줄였다.
        /// </summary>
        public const float RealSecondsPerFullDay = 300f;
        public const float RealSecondsPerGameHour = RealSecondsPerFullDay / (LastHour - DayStartHour);

        /// <summary>영업 구간(09:30~19:30)을 흘리는 실시간(약 4분 10초).</summary>
        public const float RealSecondsPerDay = RealSecondsPerGameHour * HoursPerDay;

        public static TimeManager Instance { get; private set; }

        public float CurrentHour { get; private set; } = DayStartHour;
        /// <summary>손님이 더 오는 시간(영업 종료까지). 영업이 끝났으면 0.</summary>
        public float RemainingHours =>
            Mathf.Max(0f, Shop.ShopHours.LastCustomerHour - CurrentHour);
        public bool IsDayOver { get; private set; }

        /// <summary>오늘 시계를 붙잡아 둔 동안 흘렀을 시간(시연 모드만, 본편은 늘 0).</summary>
        public float HeldHours { get; private set; }

        /// <summary>붙잡힌 시간까지 쳐서 계속 흐르는 시계. 손님 도착처럼 "흐른 만큼" 쌓이는 것은 이걸 본다. 본편에서는 <see cref="CurrentHour"/> 와 같다.</summary>
        public float FlowHour => CurrentHour + HeldHours;
        public int SpeedMultiplier { get; private set; } = 1;

        public event Action<int> OnWholeHourChanged;
        public event Action OnDayEnded;
        public event Action OnDayStarted;

        int lastWholeHour = (int)DayStartHour;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (IsDayOver) return;

            // 시연 모드는 하루가 짧다(1일차 2분 30초, 2·3일차 1분 40초)
            float secondsPerHour = Exhibition.Active && GameManager.Instance != null
                ? Exhibition.SecondsPerGameHour(GameManager.Instance.Day)
                : RealSecondsPerGameHour;
            float step = Time.deltaTime * SpeedMultiplier / secondsPerHour;
            CurrentHour += step;

            // 시연: 오늘 할 일이 남았으면 영업 마감 전에서 시계를 붙잡는다 — 처음 해 보는 사람이 다 못 끝냈는데
            // 문이 닫혀 손님 계산·온라인 주문을 못 하는 일이 없게. 멈춘 동안 흐른 몫은 HeldHours 로 쌓아
            // 손님 도착(FlowHour)은 계속 흐른다
            if (Exhibition.HoldsClock && CurrentHour > Exhibition.HoldHour)
            {
                HeldHours += CurrentHour - Mathf.Max(Exhibition.HoldHour, CurrentHour - step);
                CurrentHour = Mathf.Max(Exhibition.HoldHour, CurrentHour - step);
            }

            int whole = Mathf.FloorToInt(CurrentHour);
            if (whole != lastWholeHour)
            {
                lastWholeHour = whole;
                OnWholeHourChanged?.Invoke(whole);
            }

            // 19시에 영업이 끝나도 하루는 끝나지 않는다. 발주·정리를 한 뒤 <b>자야</b> 넘어간다
            // (Bed / SleepPrompt). 21시에는 시계만 멈춘다 — 하던 발주를 끊지 않고 "잠자러 가기"를 띄운다
            if (CurrentHour >= LastHour) CurrentHour = LastHour;
        }

        /// <summary>
        /// 남은 시간을 버리고 그 자리에서 마감한다. 침대에서 잠자리에 들 때 쓴다.
        /// 18시를 기다린 것과 <b>같은 경로</b>를 타므로 정산·명성·마감 이벤트가 그대로 돈다 —
        /// 일찍 자면 손님을 덜 받아 그날 매출이 줄고, 그 손해가 곧 일찍 자는 값이다.
        /// </summary>
        public void EndDayNow()
        {
            if (IsDayOver) return;

            // 시계는 건드리지 않는다. 19시로 맞추면 20시에 잔 사람의 시간이 거꾸로 간다.
            IsDayOver = true;
            OnDayEnded?.Invoke();
        }

        public void StartNewDay()
        {
            CurrentHour = DayStartHour;
            HeldHours = 0f;
            lastWholeHour = (int)DayStartHour;
            IsDayOver = false;
            OnDayStarted?.Invoke();
        }

        public void SetSpeed(int multiplier) => SpeedMultiplier = Mathf.Clamp(multiplier, 1, 16);

        public void CaptureInto(SaveData data)
        {
            data.currentHour = CurrentHour;
            data.dayOver = IsDayOver;
        }

        /// <summary>시각을 되돌린다. 배속은 저장하지 않는다 — 로드 후엔 1x가 안전하다.</summary>
        public void RestoreFrom(SaveData data)
        {
            // 상한은 <b>CloseHour 가 아니라 LastHour</b> 다. 19시는 장사의 끝이지 하루의 끝이
            // 아니므로, 마감 단계(19~21시)에 저장한 사람이 불러오면 시계가 19시로 되감겼다.
            CurrentHour = Mathf.Clamp(data.currentHour, DayStartHour, LastHour);
            lastWholeHour = Mathf.FloorToInt(CurrentHour);
            IsDayOver = data.dayOver;
            SpeedMultiplier = 1;
        }

        /// <summary>HH:MM. 값이 바뀔 때만 호출할 것 — 문자열을 만든다.</summary>
        public string ClockText
        {
            get
            {
                int h = Mathf.FloorToInt(CurrentHour);
                int m = Mathf.FloorToInt((CurrentHour - h) * 60f);

                // 자정 안전장치가 걸리면 CurrentHour 가 24가 되는데 시계에 24시는 없다.
                // 분을 먼저 뽑고 나서 시만 접는다 — 접은 값으로 분을 구하면 24:00이 00:1440이 된다
                return (h % 24).ToString("00") + ":" + m.ToString("00");
            }
        }
    }
}
