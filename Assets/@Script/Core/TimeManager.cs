using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 하루 09:00~18:00을 실시간 8분에 흘린다. 시간은 자원이 아니다 —
    /// 손님이 도착하는 축이자 마감의 기준일 뿐이다.
    /// </summary>
    public class TimeManager : MonoBehaviour, ISaveParticipant
    {
        public const float OpenHour = 9f;
        public const float CloseHour = 18f;
        public const float HoursPerDay = CloseHour - OpenHour;
        public const float RealSecondsPerDay = 480f;
        public const float RealSecondsPerGameHour = RealSecondsPerDay / HoursPerDay;

        public static TimeManager Instance { get; private set; }

        public float CurrentHour { get; private set; } = OpenHour;
        public float RemainingHours => Mathf.Max(0f, CloseHour - CurrentHour);
        public bool IsDayOver { get; private set; }
        public int SpeedMultiplier { get; private set; } = 1;

        public event Action<int> OnWholeHourChanged;
        public event Action OnDayEnded;
        public event Action OnDayStarted;

        int lastWholeHour = (int)OpenHour;

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

            if (CurrentHour >= CloseHour)
            {
                CurrentHour = CloseHour;
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

            CurrentHour = CloseHour;
            IsDayOver = true;
            OnDayEnded?.Invoke();
        }

        public void StartNewDay()
        {
            CurrentHour = OpenHour;
            lastWholeHour = (int)OpenHour;
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
            CurrentHour = Mathf.Clamp(data.currentHour, OpenHour, CloseHour);
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
                return h.ToString("00") + ":" + m.ToString("00");
            }
        }
    }
}
