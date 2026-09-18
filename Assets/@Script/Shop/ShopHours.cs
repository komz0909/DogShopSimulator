using System;
using DogShop.Core;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 문을 언제 열고 닫을지. 하루는 <b>준비중 → 영업중 → 마감</b> 셋으로 나뉜다.
    ///
    /// 예전에는 09시에 저절로 장사가 시작되고 18시에 하루가 끝났다. 플레이어가 고를 것이
    /// 없었고, 마감 뒤에 발주·정리를 할 시간도 없었다. 이제 여는 것도 닫는 것도 손으로 한다.
    ///
    /// - <b>준비중</b> 09시부터 열 수 있다. 늦게 열면 그 시간 손님을 그냥 놓친다
    /// - <b>영업중</b> 손님이 도착한다. 18시가 지나면 점점 줄어 20시에 끊긴다
    /// - <b>마감</b> 손님이 더 오지 않는다. 발주하고 물건을 정리한 뒤 침대에서 잔다
    ///
    /// 하루를 끝내는 것은 <b>잠자리</b>다(<see cref="Bed"/>). 닫는 것과 자는 것을 나눈 이유는
    /// 마감 후 정리 시간을 주기 위해서다.
    /// </summary>
    public class ShopHours : MonoBehaviour, ISaveParticipant
    {
        /// <summary>이 시각부터 문을 열 수 있다.</summary>
        public const float OpeningHour = 9f;

        /// <summary>이 시각부터 손님이 줄기 시작한다.</summary>
        public const float WindDownHour = 18f;

        /// <summary>이 시각이면 손님이 끊긴다.</summary>
        public const float LastCustomerHour = 20f;

        public enum Phase { Preparing, Open, Closed }

        public static ShopHours Instance { get; private set; }

        public Phase Current { get; private set; } = Phase.Preparing;
        public bool IsOpen => Current == Phase.Open;

        public event Action OnPhaseChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            TimeManager.Instance.OnDayStarted += HandleDayStarted;
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= HandleDayStarted;
            if (Instance == this) Instance = null;
        }

        void HandleDayStarted()
        {
            Current = Phase.Preparing;
            OnPhaseChanged?.Invoke();
        }

        public bool CanOpen(out string reason)
        {
            if (Current == Phase.Open) { reason = "이미 영업 중이다"; return false; }
            if (Current == Phase.Closed) { reason = "오늘 장사는 끝났다"; return false; }
            if (TimeManager.Instance.CurrentHour < OpeningHour)
            {
                reason = "09:00 부터 열 수 있다";
                return false;
            }
            reason = null;
            return true;
        }

        public bool Open()
        {
            string reason;
            if (!CanOpen(out reason)) return false;

            Current = Phase.Open;
            OnPhaseChanged?.Invoke();
            return true;
        }

        public bool Close()
        {
            if (Current != Phase.Open) return false;

            Current = Phase.Closed;
            OnPhaseChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 지금 손님이 오는 비율. 영업 중이 아니면 0이고, 18시를 넘기면 20시까지 선형으로 준다.
        /// 늦게까지 열어 두면 손님을 조금 더 받지만 그만큼 하루가 길어진다.
        /// </summary>
        public float ArrivalFactor
        {
            get
            {
                if (!IsOpen) return 0f;

                float hour = TimeManager.Instance.CurrentHour;
                if (hour < WindDownHour) return 1f;
                if (hour >= LastCustomerHour) return 0f;

                return 1f - (hour - WindDownHour) / (LastCustomerHour - WindDownHour);
            }
        }

        public string StatusText
        {
            get
            {
                switch (Current)
                {
                    case Phase.Open: return TimeManager.Instance.CurrentHour >= WindDownHour ? "영업중 (손님 줄어드는 중)" : "영업중";
                    case Phase.Closed: return "마감  발주하고 정리한 뒤 잠자리로";
                    default: return TimeManager.Instance.CurrentHour < OpeningHour ? "준비중 (09:00 부터 열 수 있다)" : "준비중";
                }
            }
        }

        // ---- 세이브 ----

        public void CaptureInto(SaveData data) => data.shopPhase = (int)Current;

        public void RestoreFrom(SaveData data)
        {
            Current = (Phase)Mathf.Clamp(data.shopPhase, 0, 2);
            OnPhaseChanged?.Invoke();
        }

        // ---- 화면 ----

        const float Width = 210f;
        const float Height = 40f;

        void OnGUI()
        {
            if (TimeManager.Instance == null || TimeManager.Instance.IsDayOver) return;

            float x = (Screen.width - Width) * 0.5f;
            float y = 14f;

            string reason;
            bool canOpen = CanOpen(out reason);

            if (Current == Phase.Open)
            {
                if (GUI.Button(new Rect(x, y, Width, Height), "가게 닫기", UiSkin.Button(UiSkin.Coral))) Close();
            }
            else if (Current == Phase.Preparing)
            {
                GUI.enabled = canOpen;
                if (GUI.Button(new Rect(x, y, Width, Height), "가게 열기", UiSkin.Button(UiSkin.Green))) Open();
                GUI.enabled = true;
            }

            // 넉넉한 칸에 가운데로 찍는다. 글자 폭에 칸을 맞추면 줄바꿈으로 뭉갠다
            GUI.Label(new Rect((Screen.width - 520f) * 0.5f, y + Height + 5f, 520f, 22f), StatusText, UiSkin.Caption);
        }
    }
}
