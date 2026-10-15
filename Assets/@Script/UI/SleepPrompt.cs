using DogShop.Core;
using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 밤 시간 안내. <b>20:30 에 한 번 알리고, 21:00 엔 화면 가운데 [잠자러 가기]</b>를 띄운다.
    ///
    /// 21시에 시계가 멈춘다(<see cref="TimeManager.LastHour"/>) — 하루를 저절로 끊지 않고, 하던 발주를
    /// 마치고 이 버튼을 누르면 넘어간다. 침대에서 자는 것과 같은 경로(<see cref="TimeManager.EndDayNow"/>)라
    /// 마감 화면이 그대로 뜬다. 19시부터는 침대에서 먼저 잘 수도 있다.
    /// </summary>
    public class SleepPrompt : MonoBehaviour, IPointerMenu
    {
        public const float WarnHour = 20.5f;
        public const float SleepHour = TimeManager.LastHour;

        const float Width = 380f;
        const float RowHeight = 30f;
        const float Pad = 14f;

        public bool IsOpen { get; private set; }

        Rect panel;

        void Update()
        {
            TimeManager time = TimeManager.Instance;
            bool show = time != null && !time.IsDayOver && time.CurrentHour >= SleepHour;

            // 버튼은 커서를 풀어야 누를 수 있다(3인칭은 커서가 잠겨 있다)
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
                // 20:30~21:00: 화면 위쪽에 알림만. 일을 막지 않는다.
                // 영업 상태 글(y ~22)보다 아래에 둔다 — 겹치면 둘 다 안 읽힌다
                Rect warn = new Rect((Screen.width - 440f) * 0.5f, 84f, 440f, RowHeight + 8f);
                GUI.Box(warn, GUIContent.none, UiSkin.Panel_);
                GUI.Label(new Rect(warn.x + Pad, warn.y + 4f, warn.width - Pad * 2f, RowHeight),
                    time.ClockText + ", 21시면 잠자러 간다. 발주를 마무리하자", UiSkin.Label);
                return;
            }

            GUI.depth = -50;
            float h = Pad * 3f + RowHeight * 3f + 8f;
            panel = new Rect((Screen.width - Width) * 0.5f, (Screen.height - h) * 0.5f, Width, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + Pad;
            float w = Width - Pad * 2f;
            float y = panel.y + Pad;

            var title = new GUIStyle(UiSkin.Title) { alignment = TextAnchor.MiddleCenter };
            var body = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(x, y, w, RowHeight), time.ClockText + ", 잠자러 갈 시간이다", title);
            y += RowHeight;
            GUI.Label(new Rect(x, y, w, RowHeight), "오늘 하루 수고했다. 내일 09:00 에 일어난다", body);
            y += RowHeight + Pad;

            // 시연 3일차엔 잠 대신 도그쇼 — 쇼 없이 4일차로 넘어가지 않게
            if (Exhibition.ShowPending)
            {
                if (UiSkin.Click(new Rect(x, y, w, RowHeight + 8f), "도그쇼 시작", UiSkin.Button(UiSkin.Green))) Exhibition.StartShow();
                return;
            }

            if (UiSkin.Click(new Rect(x, y, w, RowHeight + 8f), "잠자러 가기", UiSkin.Button(UiSkin.Green)))
            {
                Sound.Play(Sfx.Sleep);
                time.EndDayNow();
            }
        }
    }
}
