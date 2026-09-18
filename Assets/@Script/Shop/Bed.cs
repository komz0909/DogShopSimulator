using DogShop.Core;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 잠자리. 조준하고 E를 누르면 확인창이 뜨고, 확인하면 하루를 끝내고 다음 날 아침이 된다.
    ///
    /// 실수로 하루를 날리지 않게 <b>반드시 한 번 더 묻는다</b> — 되돌릴 수 없는 유일한
    /// 상호작용이다. 남은 시간과 그날 매출을 같이 보여 줘서, 지금 자면 무엇을 버리는지
    /// 알고 누르게 한다.
    /// </summary>
    public class Bed : MonoBehaviour, IPointerMenu
    {
        const float Width = 340f;
        const float RowHeight = 30f;
        const float Pad = 12f;

        [SerializeField] string displayName = "잠자리";

        Vector2 anchor;
        string headline = "";
        string detail = "";

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

            headline = "Day " + game.Day + " — 잠자리에 들까?";

            // 이미 마감한 뒤라면 버릴 것이 없다
            detail = time.IsDayOver
                ? "영업이 끝났다. 자고 나면 Day " + (game.Day + 1) + " 아침이다."
                : "아직 " + time.ClockText + "다. 지금 자면 남은 "
                  + time.RemainingHours.ToString("0.#") + "시간의 손님을 받지 못한다."
                  + (customers != null ? "   오늘 매출 " + customers.RevenueToday + "원" : "");

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

            // 18시를 기다린 것과 같은 경로. 마감 이벤트 카드가 뜨고 거기서 다음 날로 넘어간다.
            TimeManager.Instance.EndDayNow();
        }

        void OnGUI()
        {
            if (!IsOpen) return;

            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(1f, 0.9f, 0.5f);
            GUIStyle dim = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            dim.normal.textColor = new Color(0.78f, 0.8f, 0.76f);
            GUIStyle row = new GUIStyle(GUI.skin.button) { fontSize = 14 };

            float h = Height();
            GUI.Box(new Rect(anchor.x, anchor.y, Width, h), GUIContent.none);

            float x = anchor.x + Pad;
            float w = Width - Pad * 2f;
            float y = anchor.y + Pad;

            GUI.Label(new Rect(x, y, w, RowHeight), headline, title);
            y += RowHeight;
            GUI.Label(new Rect(x, y, w, RowHeight * 1.6f), detail, dim);
            y += RowHeight * 1.7f;

            float half = (w - Pad) * 0.5f;
            if (GUI.Button(new Rect(x, y, half, RowHeight - 2f), "잠자기", row)) Sleep();
            if (GUI.Button(new Rect(x + half + Pad, y, half, RowHeight - 2f), "취소", row)) Close();
        }
    }
}
