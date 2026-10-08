using DogShop.Core;
using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 밤 시간 안내. <b>22시에 경고, 23시에 잠자기 버튼</b>을 띄운다.
    ///
    /// 자는 길이 침대(E)뿐이면 플레이어가 시계를 안 보고 일하다 자정 안전장치에 걸려
    /// 아무 예고 없이 하루가 끊겼다. 22시에 한 번 알리고, 23시엔 어디서든 누를 수 있는
    /// 버튼을 띄운다. 침대에서 자는 것과 같은 경로(<see cref="TimeManager.EndDayNow"/>)라
    /// 마감 화면이 그대로 뜬다. 버튼을 무시해도 자정에는 시계가 멈추며 하루가 끝난다.
    /// </summary>
    public class SleepPrompt : MonoBehaviour, IPointerMenu
    {
        public const float WarnHour = 22f;
        public const float SleepHour = 23f;

        const float Width = 320f;
        const float RowHeight = 30f;
        const float Pad = 12f;

        public bool IsOpen { get; private set; }

        Rect panel;

        void Update()
        {
            TimeManager time = TimeManager.Instance;
            bool show = time != null && !time.IsDayOver && time.CurrentHour >= SleepHour;

            // 23시 버튼은 커서를 풀어야 누를 수 있다(1인칭은 커서가 잠겨 있다)
            if (show != IsOpen)
            {
                IsOpen = show;
                PointerMenus.SetOpen(this, show);
            }
        }

        void OnDestroy()
        {
            if (IsOpen) PointerMenus.SetOpen(this, false);
        }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            return panel.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));
        }

        void OnGUI()
        {
            TimeManager time = TimeManager.Instance;
            if (time == null || time.IsDayOver || time.CurrentHour < WarnHour) return;

            if (!IsOpen)
            {
                // 22~23시: 화면 위쪽에 경고만. 일을 막지 않는다.
                // 영업 버튼과 그 밑 안내 한 줄(y ~60)보다 아래에 둔다 — 겹치면 둘 다 안 읽힌다
                Rect warn = new Rect((Screen.width - 440f) * 0.5f, 84f, 440f, RowHeight + 8f);
                GUI.Box(warn, GUIContent.none, UiSkin.Panel_);
                GUI.Label(new Rect(warn.x + Pad, warn.y + 4f, warn.width - Pad * 2f, RowHeight),
                    time.ClockText + ", 잘 시간이 다가온다. 23시에 잠자리에 든다", UiSkin.Label);
                return;
            }

            float h = Pad * 3f + RowHeight * 3f;
            panel = new Rect((Screen.width - Width) * 0.5f, (Screen.height - h) * 0.4f, Width, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + Pad;
            float w = Width - Pad * 2f;
            float y = panel.y + Pad;

            GUI.Label(new Rect(x, y, w, RowHeight), time.ClockText + ", 잘 시간이다", UiSkin.Title);
            y += RowHeight;
            GUI.Label(new Rect(x, y, w, RowHeight), "자정이 되면 그대로 하루가 끝난다", UiSkin.Label);
            y += RowHeight + Pad;

            if (GUI.Button(new Rect(x, y, w, RowHeight + 6f), "잠자기", UiSkin.Button(UiSkin.Green)))
                time.EndDayNow();
        }
    }
}
