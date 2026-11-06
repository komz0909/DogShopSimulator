using DogShop.Core;
using DogShop.Dogs;
using DogShop.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Show
{
    /// <summary>
    /// 우승 액자에 E — 그 시즌 챔피언의 기록 카드(2026-11-04, 바르코 그림으로 다시 그림).
    /// 금테 카드 위에 초상화와 이름, 능력치 막대(그 시즌 만점 기준), 마스코트 명성과 도그쇼 점수,
    /// 아래 빨간 리본에 능력치 합. 시즌끼리 견주어 보라고. ESC, 창 밖 클릭, 빨간 발바닥으로 닫는다.
    /// </summary>
    public class ChampionRecordPanel : MonoBehaviour, IPointerMenu
    {
        // 카드 그림 비율(1018:1485)
        const float Width = 600f, Height = 875f;
        const int HealthFull = 100;

        static ChampionRecordPanel instance;
        ChampionshipManager.ChampionRecord record;
        float openedAt;

        public bool IsOpen { get; private set; }

        public static void Open(ChampionshipManager.ChampionRecord r)
        {
            if (r == null) return;
            if (instance == null) instance = new GameObject("ChampionRecordPanel").AddComponent<ChampionRecordPanel>();
            instance.record = r;
            instance.IsOpen = true;
            instance.openedAt = Time.unscaledTime;
            PointerMenus.SetOpen(instance, true);
            Sound.Play(Sfx.Sparkle);
        }

        public void Close()
        {
            IsOpen = false;
            PointerMenus.SetOpen(this, false);
        }

        void OnDestroy()
        {
            if (IsOpen) PointerMenus.SetOpen(this, false);
            if (instance == this) instance = null;
        }

        static float UiScale { get { return UiSkin.ScaleFor(Width, Height); } }

        static Rect WindowRect()
        {
            float s = UiScale;
            return new Rect((Screen.width / s - Width) * 0.5f, (Screen.height / s - Height) * 0.5f, Width, Height);
        }

        public bool ContainsPoint(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            float s = UiScale;
            Rect r = WindowRect();
            return r.Contains(new Vector2(screenPos.x / s, (Screen.height - screenPos.y) / s));
        }

        void Update()
        {
            if (!IsOpen) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { PointerMenus.TakeEscape(); Close(); return; }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !ContainsPoint(PointerMenus.PickPosition())) Close();
        }

        static readonly Color Beauty = new Color(0.96f, 0.52f, 0.62f);
        static readonly Color Training = new Color(0.42f, 0.63f, 0.92f);
        static readonly Color Agility = new Color(0.46f, 0.78f, 0.42f);
        static readonly Color Health = new Color(0.96f, 0.5f, 0.34f);

        void OnGUI()
        {
            if (!IsOpen || record == null) return;
            Matrix4x4 keep = GUI.matrix;
            float s = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.depth = -60;

            Rect win = WindowRect();
            float pop = SeasonArt.Pop(openedAt);
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), win.center);
            GUI.color = new Color(1f, 1f, 1f, SeasonArt.Fade(openedAt));

            Texture2D art = SeasonArt.RecordCard;
            if (art != null) GUI.DrawTexture(win, art, ScaleMode.StretchToFill, true);
            else GUI.Box(win, GUIContent.none, UiSkin.Tag(new Color(0.98f, 0.94f, 0.85f)));
            float W = win.width, H = win.height;
            float x = win.x + W * 0.14f, w = W * 0.72f, y = win.y + H * 0.178f;

            // 닫기: 오른쪽 위 귀퉁이 발바닥 장식 자리에 빨간 발바닥
            if (UiSkin.PawClose(new Rect(win.x + W * 0.886f - 30f, win.y + H * 0.142f - 30f, 60f, 60f), SeasonArt.ClosePaw)) { Close(); GUI.color = Color.white; GUI.matrix = keep; return; }

            SeasonArt.Outlined(new Rect(x, y, w, 50f), "시즌 " + record.season + " 챔피언",
                SeasonArt.Text(38, TextAnchor.MiddleCenter, SeasonArt.Gold), SeasonArt.Outline, 2.5f);
            y += 60f;

            // ---- 초상화와 이름 ----
            int total = record.beauty + record.training + record.agility;
            int grade = MascotGrade.GradeOf(total);
            Texture2D portrait = ChampionWall.PortraitOf(record.breedIndex);
            var pic = new Rect(x, y, 150f, 150f);
            GUI.Box(pic, GUIContent.none, UiSkin.Tag(new Color(0.84f, 0.93f, 0.99f)));
            if (portrait != null) GUI.DrawTexture(new Rect(pic.x + 8f, pic.y + 8f, pic.width - 16f, pic.height - 16f), portrait, ScaleMode.ScaleToFit, true);
            Texture2D trophy = SeasonArt.Trophy;
            if (trophy != null) GUI.DrawTexture(new Rect(pic.xMax - 44f, pic.yMax - 46f, 54f, 54f), trophy, ScaleMode.ScaleToFit, true);

            float nx = pic.xMax + 18f, nw = x + w - nx;
            string breed = DogManager.Instance != null ? DogManager.Instance.BreedName(record.breedIndex) : "";
            GUI.Label(new Rect(nx, y + 6f, nw, 46f), record.dogName, SeasonArt.Text(SeasonArt.Fit(record.dogName, 36, nw), TextAnchor.MiddleLeft, SeasonArt.Ink));
            GUI.Label(new Rect(nx, y + 52f, nw, 30f), breed, SeasonArt.Text(22, TextAnchor.MiddleLeft, SeasonArt.SoftInk));
            Texture2D badge = MascotGrade.Badge(grade);
            float gx = nx;
            if (badge != null) { GUI.DrawTexture(new Rect(nx, y + 92f, 46f, 46f), badge, ScaleMode.ScaleToFit, true); gx += 52f; }
            GUI.Label(new Rect(gx, y + 92f, x + w - gx, 46f), MascotGrade.Names[grade], SeasonArt.Text(22, TextAnchor.MiddleLeft, SeasonArt.DeepGold));
            y += 166f;

            bool old = total == 0 && record.mascotFame == 0 && record.showScore == 0;
            if (old)
            {
                GUI.Label(new Rect(x, y + 20f, w, 120f), "이 액자는 기록을 남기기 전에 걸렸다.\n다음 우승부터 능력치와 마스코트 명성이 남는다.",
                    SeasonArt.Text(21, TextAnchor.UpperCenter, SeasonArt.SoftInk, true));
                Ribbon(win, "시즌 " + record.season + " 우승");
                GUI.color = Color.white;
                GUI.matrix = keep;
                return;
            }

            // ---- 능력치 막대(그 시즌 만점 기준) ----
            int full = Exhibition.Active ? ChampionshipManager.ExhibitionStatFull : ChampionshipManager.StatFullFor(record.season);   // 시연판은 240 그대로
            StatRow(ref y, x, w, "미모", record.beauty, full, Beauty, 0.15f);
            StatRow(ref y, x, w, "훈련도", record.training, full, Training, 0.25f);
            StatRow(ref y, x, w, "어질리티", record.agility, full, Agility, 0.35f);
            StatRow(ref y, x, w, "건강", record.health, HealthFull, Health, 0.45f);
            y += 8f;

            // ---- 마스코트 명성, 도그쇼 점수 ----
            float half = (w - 14f) * 0.5f;
            ScoreBox(new Rect(x, y, half, 104f), SeasonArt.Sparkle, new Color(1f, 0.82f, 0.25f), "마스코트 명성", record.mascotFame.ToString("N0"));
            ScoreBox(new Rect(x + half + 14f, y, half, 104f), SeasonArt.GoldMedal, Color.white, "도그쇼 점수", record.showScore + "점");
            y += 114f;
            if (record.shopFame > 0)
            {
                GUI.Label(new Rect(x, y, w, 30f), "그 시즌 가게 명성 " + record.shopFame.ToString("N0"), SeasonArt.Text(22, TextAnchor.MiddleCenter, SeasonArt.DeepGold));
                y += 30f;
            }
            GUI.Label(new Rect(x, y, w, 24f), "마스코트 명성: 손님을 반겨 번 명성의 합", SeasonArt.Text(16, TextAnchor.MiddleCenter, SeasonArt.SoftInk));

            Ribbon(win, "능력치 합 " + total);
            GUI.color = Color.white;
            GUI.matrix = keep;
        }

        /// <summary>카드 아래 빨간 리본 위 글씨.</summary>
        static void Ribbon(Rect win, string text)
        {
            var r = new Rect(win.x + win.width * 0.24f, win.y + win.height * 0.875f, win.width * 0.52f, win.height * 0.06f);
            int fit = SeasonArt.Fit(text, 28, r.width);
            SeasonArt.Outlined(r, text, SeasonArt.Text(fit, TextAnchor.MiddleCenter, Color.white), SeasonArt.Outline, 2f);
        }

        void StatRow(ref float y, float x, float w, string label, int value, int full, Color color, float delay)
        {
            const float LabelW = 96f, ValueW = 64f;
            GUI.Label(new Rect(x, y, LabelW, 34f), label, SeasonArt.Text(22, TextAnchor.MiddleLeft, SeasonArt.Ink));
            float t = full > 0 ? value / (float)full : 0f;
            SeasonArt.Bar(new Rect(x + LabelW, y + 6f, w - LabelW - ValueW - 6f, 22f), t * SeasonArt.Grow(openedAt, delay), color);
            GUI.Label(new Rect(x + w - ValueW, y, ValueW, 34f), value.ToString(), SeasonArt.Text(24, TextAnchor.MiddleRight, SeasonArt.Ink));
            y += 44f;
        }

        static void ScoreBox(Rect r, Texture2D icon, Color iconTint, string label, string value)
        {
            GUI.Box(r, GUIContent.none, UiSkin.Tag(SeasonArt.Cream));
            float ix = r.x + 10f;
            if (icon != null)
            {
                Color k = GUI.color;
                GUI.color = new Color(iconTint.r, iconTint.g, iconTint.b, k.a);
                GUI.DrawTexture(new Rect(ix, r.center.y - 30f, 60f, 60f), icon, ScaleMode.ScaleToFit, true);
                GUI.color = k;
                ix += 64f;
            }
            float tw = r.xMax - ix - 8f;
            GUI.Label(new Rect(ix, r.y + 12f, tw, 26f), label, SeasonArt.Text(SeasonArt.Fit(label, 18, tw), TextAnchor.MiddleCenter, SeasonArt.SoftInk));
            GUI.Label(new Rect(ix, r.y + 40f, tw, 46f), value, SeasonArt.Text(SeasonArt.Fit(value, 34, tw), TextAnchor.MiddleCenter, SeasonArt.DeepGold));
        }
    }
}
