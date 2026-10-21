using DogShop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.UI
{
    /// <summary>
    /// 시연 모드 안내판(왼쪽, 시계 아래). 오늘이 3일 중 며칠째인지, 오늘 할 일 두 줄, 버튼 하나.
    ///   1·2일차: 오늘 할 일을 다 하면 3초 뒤 저절로 다음 날로. 할 일이 남았을 때의 [오늘 마치기]는 스태프용(한 번 더 눌러야 마친다)
    ///   3일차:   [도그쇼 시작] (G) — 본편 30일차와 같은 도그쇼 시작 화면을 연다
    /// 본편에서는 아무것도 그리지 않는다. 창(강아지 상태창·상점 등)이 열려 있거나 쇼·컷신·마감 중에도 숨는다.
    /// </summary>
    public class ExhibitionGuide : MonoBehaviour
    {
        // 1080p 설계 좌표. 시계 판(왼쪽 위 25~90) 바로 아래
        const float X = 25f, Y = 102f, Width = 470f;
        const float Pad = 14f;

        /// <summary>[오늘 마치기]를 한 번 누른 시각. 이 안에 다시 누르면 마친다.</summary>
        float confirmUntil = -1f;
        const float ConfirmSeconds = 3f;

        GUIStyle title, body, button;

        static readonly string[][] Goals =
        {
            new[] { "가게 운영",
                    "오른쪽 위 '오늘 할 일'을 하나씩 따라 하자",
                    "강아지를 클릭하면 상태창이 열린다 (훈련은 2일차부터)" },
            new[] { "마스코트 키우기",
                    "오른쪽 위 '오늘 할 일'을 하나씩 따라 하자",
                    "자란 강아지는 손님을 반겨 명성을 올려 준다" },
            new[] { "도그쇼 날",
                    "오른쪽 위 '오늘 할 일'을 마치고 도그쇼에 나가자",
                    "능력치가 높을수록 종목이 쉬워진다 (건강 70 이상)" },
        };

        /// <summary>오늘 할 일을 다 끝낸 뒤 저절로 다음 날로 넘어가는 시각(unscaled). 음수면 기다리는 중이 아니다.</summary>
        float autoAt = -1f;

        /// <summary>할 일을 다 끝내고 다음 날로 넘어가기까지 기다리는 시간(초). 완료 알림을 읽을 틈.</summary>
        const float AutoNextSeconds = 3f;

        void Update()
        {
            // 창(강아지 상태창 등)이 열려 있으면 기다림을 멈춘다 — 운동 창을 닫기도 전에 정산이 뜨지 않게.
            // 창을 닫으면 처음부터 다시 센다
            if (!Visible) { autoAt = -1f; return; }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame && Exhibition.ShowPending) Exhibition.StartShow();
            TickAutoNext();
        }

        /// <summary>시간이 아니라 할 일로 하루가 끝난다 — 1·2일차 할 일을 다 하면 잠깐 뒤 다음 날로.</summary>
        void TickAutoNext()
        {
            Shop.TutorialQuestManager quests = Shop.TutorialQuestManager.Instance;
            bool done = quests != null && quests.IsComplete
                        && GameManager.Instance != null && GameManager.Instance.Day < Exhibition.Days && !Exhibition.ShowPending;
            if (!done) { autoAt = -1f; return; }
            if (autoAt < 0f) autoAt = Time.unscaledTime + AutoNextSeconds;
            else if (Time.unscaledTime >= autoAt) { autoAt = -1f; EndToday(); }
        }

        static bool Visible
        {
            get
            {
                if (!Exhibition.Active) return false;
                if (Show.DogShow.Running) return false;
                if (Show.Cutscene.Instance != null && Show.Cutscene.Instance.IsOpen) return false;
                TimeManager time = TimeManager.Instance;
                if (time == null || time.IsDayOver) return false;
                return !PointerMenus.AnyOpen;
            }
        }

        void OnGUI()
        {
            if (!Visible || GameManager.Instance == null) return;
            Styles();

            Matrix4x4 keep = GUI.matrix;
            float s = UiSkin.ScreenScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            int day = Mathf.Clamp(GameManager.Instance.Day, 1, Exhibition.Days);
            string[] goal = Goals[day - 1];
            bool showDay = Exhibition.ShowPending;

            const float lineH = 26f, btnH = 44f;
            float h = Pad * 2f + 34f + lineH * 2f + 8f + btnH;
            var panel = new Rect(X, Y, Width, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            float x = panel.x + Pad, w = Width - Pad * 2f, y = panel.y + Pad;
            GUI.Label(new Rect(x, y, w, 34f), "시연 " + day + "일차 / " + Exhibition.Days + "   " + goal[0], title);
            y += 34f;
            GUI.Label(new Rect(x, y, w, lineH), goal[1], body);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), goal[2], body);
            y += lineH + 8f;

            var btn = new Rect(x, y, w, btnH);
            if (showDay)
            {
                if (UiSkin.Click(btn, "도그쇼 시작  (G)", button)) Exhibition.StartShow();
            }
            else if (day < Exhibition.Days)
            {
                // 오늘 할 일을 다 끝냈으면 한 번에 넘어간다 — 확인은 할 일이 남았을 때 실수로 넘기지 않게 하려는 것이다
                Shop.TutorialQuestManager quests = Shop.TutorialQuestManager.Instance;
                bool done = quests != null && quests.IsComplete;
                bool confirming = Time.unscaledTime < confirmUntil;
                int left = autoAt > 0f ? Mathf.Max(1, Mathf.CeilToInt(autoAt - Time.unscaledTime)) : (int)AutoNextSeconds;
                string label = done ? left + "초 뒤 다음 날로  (바로 가기)"
                    : confirming ? "한 번 더 누르면 " + (day + 1) + "일차로" : "오늘 마치기";
                if (UiSkin.Click(btn, label, button))
                {
                    if (done || confirming) EndToday();
                    else confirmUntil = Time.unscaledTime + ConfirmSeconds;
                }
            }

            GUI.matrix = keep;
        }

        /// <summary>침대에서 자는 것과 같은 길 — 마감 정산이 뜨고 거기서 다음 날로 넘어간다.</summary>
        void EndToday()
        {
            confirmUntil = -1f;
            Sound.Play(Sfx.Sleep);
            TimeManager.Instance.EndDayNow();
        }

        void Styles()
        {
            if (title != null && title.font == UiSkin.Font) return;
            title = new GUIStyle(UiSkin.Title) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
            body = new GUIStyle(UiSkin.Label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            button = new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 20 };
        }
    }
}
