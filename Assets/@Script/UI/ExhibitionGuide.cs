using DogShop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.UI
{
    /// <summary>
    /// 시연 모드 안내판(왼쪽, 시계 아래). 오늘이 3일 중 며칠째인지, 오늘 할 일 두 줄, 버튼 하나.
    ///   1·2일차: 오늘 할 일을 다 하면 3초 뒤 저절로 다음 날로(버튼 없음)
    ///   3일차:   [도그쇼 시작] (G) — 본편 30일차와 같은 도그쇼 시작 화면을 연다
    /// 본편에서는 아무것도 그리지 않는다. 창(강아지 상태창·상점 등)이 열려 있거나 쇼·컷신·마감 중에도 숨는다.
    /// </summary>
    public class ExhibitionGuide : MonoBehaviour
    {
        // 1080p 설계 좌표. 시계 판(왼쪽 위 25~90) 바로 아래
        const float X = 25f, Y = 102f, Width = 530f;
        const float Pad = 14f;

        GUIStyle title, body, button, countdown;

        /// <summary>안내판이 차지한 화면 사각형(픽셀, 위가 0). 안 보이면 크기 0. 진열대 이름표가 이 위에 겹쳐 그려지지 않게 쓴다.</summary>
        public static Rect ScreenRect { get; private set; }

        void OnDisable() => ScreenRect = new Rect();

        static readonly string[][] Goals =
        {
            new[] { "가게 운영",
                    "오른쪽 위 '오늘 할 일'을 하나씩 따라 하자",
                    "T 로 강아지를 부르고 E 로 상태창 열기 (훈련은 2일차부터)" },
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

        /// <summary>튜토리얼 2일차를 다 마치고 타이틀로 가기까지(2026-11-05). "본게임으로 들어가보자!"를 읽을 틈.</summary>
        const float FinishSeconds = 5f;

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
            bool finishing = GameManager.Instance != null && Exhibition.IsTutorialLastDay(GameManager.Instance.Day);
            bool done = quests != null && quests.IsComplete
                        && GameManager.Instance != null && (GameManager.Instance.Day < Exhibition.Days || finishing) && !Exhibition.ShowPending;
            if (!done) { autoAt = -1f; return; }
            if (autoAt < 0f) autoAt = Time.unscaledTime + (finishing ? FinishSeconds : AutoNextSeconds);
            else if (Time.unscaledTime >= autoAt) { autoAt = -1f; if (finishing) FinishTutorial(); else EndToday(); }
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
            if (!Visible || GameManager.Instance == null) { ScreenRect = new Rect(); return; }
            Styles();

            Matrix4x4 keep = GUI.matrix;
            float s = UiSkin.ScreenScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            int day = Mathf.Clamp(GameManager.Instance.Day, 1, Exhibition.Days);
            string[] goal = Goals[day - 1];
            bool showDay = Exhibition.ShowPending;
            // [오늘 마치기] 버튼은 뺐다(2026-11-04 직접 플레이 의견 — 할 일을 순서대로 하면 저절로 넘어가니 필요 없다).
            // 할 일을 다 하면 버튼 자리에 '몇 초 뒤 다음 날로'만 띄운다. 3일차 [도그쇼 시작]은 쇼 입구라 그대로 둔다
            Shop.TutorialQuestManager quests = Shop.TutorialQuestManager.Instance;
            bool finishing = Exhibition.IsTutorialLastDay(GameManager.Instance.Day);
            bool counting = !showDay && (day < Exhibition.Days || finishing) && autoAt > 0f && quests != null && quests.IsComplete;

            const float lineH = 28f, btnH = 46f;
            float h = Pad * 2f + 34f + lineH * 2f + (showDay ? 8f + btnH : 0f) + (counting ? (finishing ? lineH * 2f : lineH) + 4f : 0f);
            var panel = new Rect(X, Y, Width, h);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);
            ScreenRect = new Rect(panel.x * s, panel.y * s, panel.width * s, panel.height * s);

            float x = panel.x + Pad, w = Width - Pad * 2f, y = panel.y + Pad;
            GUI.Label(new Rect(x, y, w, 34f), Exhibition.ModeName + " " + day + "일차 / " + Exhibition.Days + "   " + goal[0], title);
            y += 34f;
            GUI.Label(new Rect(x, y, w, lineH), goal[1], body);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), goal[2], body);
            y += lineH;

            if (showDay)
            {
                if (UiSkin.Click(new Rect(x, y + 8f, w, btnH), "도그쇼 시작  (G)", button)) Exhibition.StartShow();
            }
            else if (counting)
            {
                int left = Mathf.Max(1, Mathf.CeilToInt(autoAt - Time.unscaledTime));
                if (finishing)
                {
                    GUI.Label(new Rect(x, y + 4f, w, lineH), "튜토리얼 완료! 본게임으로 들어가보자!", countdown);
                    GUI.Label(new Rect(x, y + 4f + lineH, w, lineH), left + "초 뒤 타이틀 화면으로, [게임 시작]을 누르자", countdown);
                }
                else GUI.Label(new Rect(x, y + 4f, w, lineH), "오늘 할 일 완료!  " + left + "초 뒤 " + (day + 1) + "일차로 넘어간다", countdown);
            }

            GUI.matrix = keep;
        }

        /// <summary>침대에서 자는 것과 같은 길 — 마감 정산이 뜨고 거기서 다음 날로 넘어간다.</summary>
        /// <summary>튜토리얼을 다 마쳤다 — 타이틀 화면으로(견종 고르기로 바로 가지 않는다). 거기서 [게임 시작]으로 본편을 시작한다.</summary>
        void FinishTutorial()
        {
            Sound.Play(Sfx.Fanfare);
            Exhibition.ReturnToPicker = false;
            if (Show.Cutscene.Instance != null) Show.Cutscene.Instance.ReturnToTitle();
            else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        void EndToday()
        {
            Sound.Play(Sfx.Sleep);
            TimeManager.Instance.EndDayNow();
        }

        void Styles()
        {
            if (title != null && title.font == UiSkin.Font) return;
            title = new GUIStyle(UiSkin.Title) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
            body = new GUIStyle(UiSkin.Label) { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            button = new GUIStyle(UiSkin.Button(UiSkin.Green)) { fontSize = 21 };
            countdown = new GUIStyle(body);
            countdown.normal.textColor = new Color(0.62f, 0.9f, 0.7f);
        }
    }
}
