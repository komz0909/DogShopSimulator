using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 하루 09:00~18:00을 실시간 5분에 흘린다. 시간은 <b>자원이다</b> —
    /// 일찍 열고 늦게까지 버틸수록 손님을 더 받는다(<see cref="Shop.ShopHours"/>).
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
        public const float DayStartHour = 8f;

        /// <summary>손님 유입이 온전해지는 시각. 손님 배분의 기준이기도 하다.</summary>
        public const float OpenHour = 9f;

        /// <summary>장사 시간의 끝. 이 시각이 지나면 손님이 줄지만 하루가 끝나지는 않는다.</summary>
        public const float CloseHour = 18f;

        /// <summary>시계가 멈추는 시각. 플레이어가 자지 않고 버틸 때를 위한 안전장치다.</summary>
        public const float LastHour = 24f;

        /// <summary>
        /// 손님 도착량을 나누는 기준. 09~18시 아홉 시간에 하루치가 다 온다.
        /// 08시나 18시 바깥에서 받는 손님은 여기에 <b>얹히는</b> 몫이다.
        /// </summary>
        public const float HoursPerDay = CloseHour - OpenHour;

        /// <summary>09~18시를 흘리는 실시간. 하루 전체가 아니라 <b>영업 구간</b>의 길이다.</summary>
        public const float RealSecondsPerDay = 300f;
        public const float RealSecondsPerGameHour = RealSecondsPerDay / HoursPerDay;

        public static TimeManager Instance { get; private set; }

        public float CurrentHour { get; private set; } = DayStartHour;
        /// <summary>
        /// 지금 자면 버리는 <b>손님이 오는 시간</b>. 18시가 아니라 손님이 끊기는 시각까지다 —
        /// 18시로 재면 18:30에 "남은 0시간"이라고 하면서 실제로는 세 시간을 버리게 된다.
        /// </summary>
        public float RemainingHours =>
            Mathf.Max(0f, Shop.ShopHours.LastCustomerHour - CurrentHour);
        public bool IsDayOver { get; private set; }
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

            CurrentHour += Time.deltaTime * SpeedMultiplier / RealSecondsPerGameHour;

            int whole = Mathf.FloorToInt(CurrentHour);
            if (whole != lastWholeHour)
            {
                lastWholeHour = whole;
                OnWholeHourChanged?.Invoke(whole);
            }

            // 18시가 지나도 하루는 끝나지 않는다. 문을 닫고 발주·정리를 한 뒤
            // <b>침대에서 자야</b> 넘어간다(ShopHours / Bed). 여기서는 시계가 영원히
            // 도는 것만 막는다 — 자정이면 강제로 재운다.
            if (CurrentHour >= LastHour)
            {
                CurrentHour = LastHour;
                IsDayOver = true;
                OnDayEnded?.Invoke();
            }
        }

        /// <summary>
        /// 남은 시간을 버리고 그 자리에서 마감한다. 침대에서 잠자리에 들 때 쓴다.
        /// 18시를 기다린 것과 <b>같은 경로</b>를 타므로 정산·명성·마감 이벤트가 그대로 돈다 —
        /// 일찍 자면 손님을 덜 받아 그날 매출이 줄고, 그 손해가 곧 일찍 자는 값이다.
        /// </summary>
        public void EndDayNow()
        {
            if (IsDayOver) return;

            // 시계는 건드리지 않는다. 18시로 맞추면 21시에 잔 사람의 시간이 거꾸로 간다.
            IsDayOver = true;
            OnDayEnded?.Invoke();
        }

        public void StartNewDay()
        {
            CurrentHour = DayStartHour;
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
            // 상한은 <b>CloseHour 가 아니라 LastHour</b> 다. 18시는 장사의 끝이지 하루의 끝이
            // 아니므로, 마감 단계(18~24시)에 저장한 사람이 불러오면 시계가 18시로 되감겼다.
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
