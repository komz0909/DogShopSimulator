using DogShop.Core;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 잠자리. 조준하고 E를 누르면 확인창이 뜨고, 확인하면 하루를 끝내고 다음 날 아침이 된다.
    /// 잘 수 있는 건 <b>19:30(영업 종료)부터</b>다. 그 전엔 "아직 잘 시간이 아니다"만 알린다.
    ///
    /// 실수로 하루를 날리지 않게 <b>반드시 한 번 더 묻는다</b> — 되돌릴 수 없는 유일한
    /// 상호작용이다. 남은 시간과 그날 매출을 같이 보여 줘서, 지금 자면 무엇을 버리는지
    /// 알고 누르게 한다.
    /// </summary>
    public class Bed : MonoBehaviour, IPointerMenu
    {
        const float Width = 390f;
        const float RowHeight = 34f;
        const float Pad = 12f;

        [SerializeField] string displayName = "잠자리";

        Vector2 anchor;
        string headline = "";
        string detail = "";
        bool tooEarly;

        public bool IsOpen { get; private set; }
        public string DisplayName => displayName;

        public void Open(Vector2 screenPos)
        {
            TimeManager time = TimeManager.Instance;
            GameManager game = GameManager.Instance;
            CustomerManager customers = CustomerManager.Instance;
            if (time == null || game == null) return;

            anchor = new Vector2(
                Mathf.Clamp(screenPos.x, Pad, Screen.width - Width - Pad),
                Mathf.Clamp(Screen.height - screenPos.y, Pad, Screen.height - Height() - Pad));

            // 잠은 19시(영업 종료)부터 — 낮에 자서 하루를 건너뛰지 않게
            // 시연 모드는 언제든 잔다 — 짧은 시연에서 저녁까지 기다리게 하지 않는다
            tooEarly = !Exhibition.Active && !time.IsDayOver && time.CurrentHour < TimeManager.CloseHour;
            if (Exhibition.ShowPending)
            {
                headline = "도그쇼에 나갈까?";
                detail = "오늘은 도그쇼 날이다. 밥·목욕·훈련으로 준비를 마쳤으면 무대로 가자.";
            }
            else if (tooEarly)
            {
                headline = "아직 잘 시간이 아니다";
                detail = "지금 " + time.ClockText + ". 19:30 에 영업이 끝나면 잘 수 있다.";
            }
            else
            {
                headline = "Day " + game.Day + " 잠자리에 들까?";
                detail = "영업이 끝났다. 발주는 마쳤나? 자고 나면 Day " + (game.Day + 1) + " 아침이다."
                       + (customers != null ? "   오늘 매출 " + customers.RevenueToday + "원" : "");
            }

            IsOpen = true;
            PointerMenus.SetOpen(this, true);
        }

        public void Close()
        {
            IsOpen = false;
            PointerMenus.SetOpen(this, false);
        }

        void OnDestroy()
        {
            if (IsOpen) PointerMenus.SetOpen(this, false);
        }

        /// <summary>ESC 로 닫는다(같은 ESC 로 설정 창이 열리지 않게 표시).</summary>
        void Update()
        {
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                PointerMenus.TakeEscape();
            }
        }

        static float Height() => Pad * 3f + RowHeight * 4f;

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!IsOpen) return false;

            float guiY = Screen.height - screenPos.y;
            return guiY >= anchor.y && guiY <= anchor.y + Height()
                && screenPos.x >= anchor.x && screenPos.x <= anchor.x + Width;
        }

        void Sleep()
        {
            Close();

            // 시연 3일차: 자는 대신 도그쇼로 간다(쇼가 끝나야 하루가 끝난다)
            if (Exhibition.ShowPending) { Exhibition.StartShow(); return; }

            // 21시 잠자러 가기(SleepPrompt)와 같은 경로. 마감 화면이 뜨고 거기서 다음 날로 넘어간다.
            Sound.Play(Sfx.Sleep);
            TimeManager.Instance.EndDayNow();
        }

        void OnGUI()
        {
            if (!IsOpen) return;

            float h = Height();
            GUI.Box(new Rect(anchor.x, anchor.y, Width, h), GUIContent.none, UiSkin.Panel_);

            float x = anchor.x + Pad;
            float w = Width - Pad * 2f;
            float y = anchor.y + Pad;

            GUI.Label(new Rect(x, y, w, RowHeight), headline, UiSkin.Title);
            y += RowHeight;
            GUI.Label(new Rect(x, y, w, RowHeight * 1.6f), detail, UiSkin.Label);
            y += RowHeight * 1.7f;

            if (tooEarly)
            {
                if (UiSkin.Click(new Rect(x, y, w, RowHeight + 6f), "닫기", UiSkin.Button(UiSkin.Cream))) Close();
                return;
            }

            float half = (w - Pad) * 0.5f;
            if (UiSkin.Click(new Rect(x, y, half, RowHeight + 6f), Exhibition.ShowPending ? "도그쇼 시작" : "잠자기", UiSkin.Button(UiSkin.Green))) Sleep();
            if (UiSkin.Click(new Rect(x + half + Pad, y, half, RowHeight + 6f), "취소", UiSkin.Button(UiSkin.Cream))) Close();
        }
    }
}
