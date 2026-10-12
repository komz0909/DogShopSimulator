using System;
using DogShop.Core;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 문을 언제 열고 닫을지. 하루는 <b>준비중 → 영업중 → 영업 종료</b> 셋으로 나뉜다.
    ///
    /// - <b>준비중</b> 09시에 일어나 진열·정리를 한다. [가게 열기]를 누르면 <b>10시에 문을 연다</b>(예약).
    ///   10시가 지나서 누르면 바로 연다. 누르지 않으면 열리지 않는다 — 늦게 열면 그만큼 손님을 잃는다
    /// - <b>영업중</b> 10~19시 손님이 고르게 온다. <b>19시에 저절로 문을 닫는다</b>
    /// - <b>영업 종료</b> 손님이 더 오지 않는다. 21시까지 정산·발주를 하고, 19시부터 잘 수 있다(<see cref="Bed"/>)
    ///
    /// 예전엔 08시부터 열 수 있고 18~21시에 손님이 잦아들며 닫기 버튼을 직접 눌렀다.
    /// 하루를 길게 끄는 것보다 <b>정해진 영업시간 안에 잘 파는 것</b>이 남도록 바꿨다.
    /// </summary>
    public class ShopHours : MonoBehaviour, ISaveParticipant
    {
        /// <summary>이 시각부터 [가게 열기]를 누를 수 있다(하루 시작과 같다).</summary>
        public const float OpeningHour = TimeManager.DayStartHour;

        /// <summary>문이 실제로 열리는 시각. 그 전에 누르면 이때까지 기다린다.</summary>
        public const float OpenHour = TimeManager.OpenHour;

        /// <summary>이 시각에 저절로 문을 닫는다. 손님도 이때 끊긴다.</summary>
        public const float CloseHour = TimeManager.CloseHour;

        /// <summary>이 시각이면 손님이 끊긴다(영업 종료와 같다).</summary>
        public const float LastCustomerHour = CloseHour;

        public enum Phase { Preparing, Open, Closed }

        public static ShopHours Instance { get; private set; }

        public Phase Current { get; private set; } = Phase.Preparing;
        public bool IsOpen => Current == Phase.Open;

        /// <summary>10시 전에 [가게 열기]를 눌러 둔 상태. 10시가 되면 연다.</summary>
        public bool Reserved { get; private set; }

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
            Reserved = false;
            remindedLate = false;
            OnPhaseChanged?.Invoke();
        }

        public bool CanOpen(out string reason)
        {
            if (Current == Phase.Open) { reason = "이미 영업 중이다"; return false; }
            if (Current == Phase.Closed) { reason = "오늘 장사는 끝났다"; return false; }
            if (Reserved) { reason = "10:00 에 문을 연다"; return false; }
            if (TimeManager.Instance.CurrentHour >= CloseHour) { reason = "영업시간이 지났다"; return false; }
            reason = null;
            return true;
        }

        /// <summary>[가게 열기]. 10시 전이면 예약만 하고, 10시가 지났으면 바로 연다.</summary>
        public bool Open()
        {
            string reason;
            if (!CanOpen(out reason)) return false;

            if (TimeManager.Instance.CurrentHour < OpenHour)
            {
                Reserved = true;
                Notice("오픈 준비 끝, 10:00 에 문을 연다");
                OnPhaseChanged?.Invoke();
                return true;
            }

            OpenNow();
            return true;
        }

        void OpenNow()
        {
            Reserved = false;
            Current = Phase.Open;
            OnPhaseChanged?.Invoke();
        }

        /// <summary>영업 종료. 19시에 저절로 부른다(무인 측정도 같은 길).</summary>
        public bool Close()
        {
            if (Current != Phase.Open) return false;
            Current = Phase.Closed;
            OnPhaseChanged?.Invoke();
            return true;
        }

        string notice = "";
        float noticeUntil;
        bool remindedLate;

        void Notice(string text)
        {
            notice = text;
            noticeUntil = Time.unscaledTime + 4f;
        }

        void Update()
        {
            TimeManager time = TimeManager.Instance;
            if (time == null || time.IsDayOver) return;
            float hour = time.CurrentHour;

            if (Current == Phase.Preparing && Reserved && hour >= OpenHour)
            {
                OpenNow();
                Notice("10:00, 가게 문을 열었다");
            }
            else if (Current == Phase.Preparing && !Reserved && hour >= OpenHour && hour < CloseHour && !remindedLate)
            {
                // 깜빡했을 때 한 번 알린다 — 열기 전엔 손님이 오지 않는다
                remindedLate = true;
                Notice("10:00 이 지났다. [가게 열기]를 눌러야 손님이 온다");
            }

            if (hour >= CloseHour && Current != Phase.Closed)
            {
                Reserved = false;
                Current = Phase.Closed;
                OnPhaseChanged?.Invoke();
                Notice("19:00, 영업 종료. 21시까지 정산과 발주를 하자");
            }
        }

        /// <summary>지금 손님이 오는 비율. 영업 중(10~19시)이면 1, 아니면 0.</summary>
        public float ArrivalFactor => IsOpen && TimeManager.Instance.CurrentHour < CloseHour ? 1f : 0f;

        public string StatusText
        {
            get
            {
                switch (Current)
                {
                    case Phase.Open: return "영업중 (19:00 에 문을 닫는다)";
                    case Phase.Closed: return "영업 종료  정산하고 발주한 뒤 잠자리로 (21:00 까지)";
                    default:
                        if (Reserved) return "오픈 준비 끝 (10:00 에 문을 연다)";
                        return TimeManager.Instance.CurrentHour < OpenHour
                            ? "준비중 (가게 열기를 누르면 10:00 에 연다)"
                            : "준비중 (가게 열기를 눌러야 손님이 온다)";
                }
            }
        }

        // ---- 세이브 ----

        public void CaptureInto(SaveData data)
        {
            data.shopPhase = (int)Current;
            data.shopReserved = Reserved;
        }

        public void RestoreFrom(SaveData data)
        {
            Current = (Phase)Mathf.Clamp(data.shopPhase, 0, 2);
            Reserved = Current == Phase.Preparing && data.shopReserved;
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

            // 버튼은 준비중(예약 전)에만. 영업 중·종료 뒤엔 상태 글만 둔다
            string reason;
            bool button = Current == Phase.Preparing && CanOpen(out reason);
            if (button && GUI.Button(new Rect(x, y, Width, Height), "가게 열기", UiSkin.Button(UiSkin.Green))) Open();

            // 넉넉한 칸에 가운데로 찍는다. 글자 폭에 칸을 맞추면 줄바꿈으로 뭉갠다.
            float ty = button ? y + Height + 5f : y + 8f;
            string text = Time.unscaledTime < noticeUntil ? notice : StatusText;
            GUI.Label(new Rect((Screen.width - 520f) * 0.5f, ty, 520f, 22f), text, UiSkin.Caption);
        }
    }
}
