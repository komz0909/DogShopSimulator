using System;
using DogShop.Core;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 문을 언제 열고 닫을지. 하루는 <b>준비중 → 영업중 → 영업 종료</b> 셋으로 나뉜다. 전부 <b>저절로</b> 넘어간다.
    ///
    /// - <b>준비중</b> 09시에 일어나 진열·정리를 한다. <b>09:30 에 문이 저절로 열린다</b>
    /// - <b>영업중</b> 09:30~19:30 손님이 고르게 온다. <b>19:30 에 저절로 문을 닫는다</b>
    /// - <b>영업 종료</b> 손님이 더 오지 않는다. 21시까지 정리·발주를 하고 [잠자러 가기](<see cref="UI.SleepPrompt"/>)로 넘어간다.
    ///   그 전에도 19:30 부터는 침대에서 잘 수 있다(<see cref="Bed"/>)
    ///
    /// 예전엔 [가게 열기]를 눌러야 10시에 열었다 — 잊으면 손님을 통째로 잃는 함정이라 없앴다.
    /// </summary>
    public class ShopHours : MonoBehaviour, ISaveParticipant
    {
        /// <summary>하루가 시작하는 시각.</summary>
        public const float OpeningHour = TimeManager.DayStartHour;

        /// <summary>문이 저절로 열리는 시각(09:30).</summary>
        public const float OpenHour = TimeManager.OpenHour;

        /// <summary>이 시각에 저절로 문을 닫는다(19:30). 손님도 이때 끊긴다.</summary>
        public const float CloseHour = TimeManager.CloseHour;

        /// <summary>이 시각이면 손님이 끊긴다(영업 종료와 같다).</summary>
        public const float LastCustomerHour = CloseHour;

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

        /// <summary>지금 바로 열 수 있나(무인 측정용). 문은 09:30 에 저절로 열리므로 그 전엔 못 연다.</summary>
        public bool CanOpen(out string reason)
        {
            if (Current == Phase.Open) { reason = "이미 영업 중이다"; return false; }
            if (Current == Phase.Closed) { reason = "오늘 장사는 끝났다"; return false; }
            float hour = TimeManager.Instance.CurrentHour;
            if (hour < OpenHour) { reason = "09:30 에 문이 열린다"; return false; }
            if (hour >= CloseHour) { reason = "영업시간이 지났다"; return false; }
            reason = null;
            return true;
        }

        public bool Open()
        {
            string reason;
            if (!CanOpen(out reason)) return false;
            OpenNow();
            return true;
        }

        void OpenNow()
        {
            Current = Phase.Open;
            Sound.Play(Sfx.ShopOpen);
            OnPhaseChanged?.Invoke();
        }

        /// <summary>영업 종료. 19:30 에 저절로 부른다(무인 측정도 같은 길).</summary>
        public bool Close()
        {
            if (Current != Phase.Open) return false;
            Current = Phase.Closed;
            Sound.Play(Sfx.ShopClose);
            OnPhaseChanged?.Invoke();
            return true;
        }

        string notice = "";
        float noticeUntil;

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

            // 시연 모드 3일차(도그쇼 날)는 가게를 열지 않는다 — 손님 없이 강아지 준비만 하고 쇼로 간다
            if (Current == Phase.Preparing && hour >= OpenHour && hour < CloseHour && !ShowDayToday)
            {
                OpenNow();
                Notice("09:30, 가게 문을 열었다");
            }

            if (hour >= CloseHour && Current != Phase.Closed)
            {
                Current = Phase.Closed;
                Sound.Play(Sfx.ShopClose);
                OnPhaseChanged?.Invoke();
                Notice("19:30, 영업 종료. 21시까지 정리하고 발주하자");
            }
        }

        /// <summary>지금 손님이 오는 비율. 영업 중(09:30~19:30)이면 1, 아니면 0.</summary>
        static bool ShowDayToday => GameManager.Instance != null && Exhibition.IsShowDay(GameManager.Instance.Day);

        public float ArrivalFactor => IsOpen && TimeManager.Instance.CurrentHour < CloseHour ? 1f : 0f;

        public string StatusText
        {
            get
            {
                if (ShowDayToday) return "오늘은 도그쇼! 가게는 쉰다";
                switch (Current)
                {
                    case Phase.Open: return "영업중 (19:30 에 문을 닫는다)";
                    case Phase.Closed: return "영업 종료  정리하고 발주한 뒤 잠자리로 (21:00 까지)";
                    default: return "준비중 (09:30 에 문이 열린다)";
                }
            }
        }

        // ---- 세이브 ----

        public void CaptureInto(SaveData data)
        {
            data.shopPhase = (int)Current;
            data.shopReserved = false;   // 예약은 없어졌다(옛 세이브 호환용 칸)
        }

        public void RestoreFrom(SaveData data)
        {
            Current = (Phase)Mathf.Clamp(data.shopPhase, 0, 2);
            OnPhaseChanged?.Invoke();
        }

        // ---- 화면 ----

        GUIStyle statusStyle;

        void OnGUI()
        {
            if (TimeManager.Instance == null || TimeManager.Instance.IsDayOver) return;
            // 상점 같은 창이 떠 있으면 숨긴다 — 창 윗변(간판) 위에 글자가 겹쳐 보였다
            if (PointerMenus.AnyOpen) return;

            // 버튼은 없다 — 상태 한 줄만. 넉넉한 칸에 가운데로 찍는다(글자 폭에 칸을 맞추면 줄바꿈으로 뭉갠다)
            string text = Time.unscaledTime < noticeUntil ? notice : StatusText;
            if (statusStyle == null || statusStyle.font != UiSkin.Font)
                statusStyle = new GUIStyle(UiSkin.Caption) { fontSize = 20 };   // 밝은 거리 위에서 16 은 잘 안 읽혔다
            GUI.Label(new Rect((Screen.width - 640f) * 0.5f, 20f, 640f, 28f), text, statusStyle);
        }
    }
}
