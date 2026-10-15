using DogShop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.UI
{
    /// <summary>
    /// 시연 모드 안내판(왼쪽, 시계 아래). 오늘이 3일 중 며칠째인지, 오늘 할 일 두 줄, 버튼 하나.
    ///   1·2일차: [오늘 마치기] — 짧은 시연에서 저녁까지 기다리지 않게(한 번 더 눌러야 마친다, 실수 방지)
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
                    "오른쪽 위 튜토리얼을 따라 진열하고 손님을 받아 보자",
                    "강아지를 클릭하면 상태창(돌보기, 훈련)이 열린다" },
            new[] { "강아지 돌보기, 훈련",
                    "강아지를 클릭해 밥, 목욕, 훈련, 운동을 해 보자",
                    "청결이나 건강이 40 아래면 훈련해도 자라지 않는다" },
            new[] { "도그쇼 날",
                    "마지막으로 밥, 목욕, 훈련을 하고 도그쇼에 나가자",
                    "미모, 훈련도, 어질리티가 높을수록 종목이 쉬워진다 (건강 70 이상)" },
        };

        void Update()
        {
            if (!Visible) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame && Exhibition.ShowPending) Exhibition.StartShow();
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
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
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
                bool confirming = Time.unscaledTime < confirmUntil;
                string label = confirming ? "한 번 더 누르면 " + (day + 1) + "일차로" : "오늘 마치기";
                if (UiSkin.Click(btn, label, button))
                {
                    if (confirming) EndToday();
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
