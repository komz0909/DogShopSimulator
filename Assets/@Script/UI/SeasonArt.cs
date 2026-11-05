using System.Collections.Generic;
using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 시즌 결산판, 다음 시즌 강아지 고르기, 우승 기록 창이 같이 쓰는 그림과 그리기 도구(2026-11-04).
    /// 판, 카드, 리본, 트로피는 바르코로 뽑아 초록 배경을 지운 그림이고 UI/Resources/Season 에 있다.
    /// 버튼은 메인 메뉴의 노란 확인 버튼과 뼈다귀 그림을 그대로 쓴다. 처음 쓸 때 읽는다.
    /// </summary>
    public static class SeasonArt
    {
        static Texture2D board, record, card, cardOn, ribbon, trophy, gold, silver, bronze, confirm, bone, closePaw, sparkle, rays;

        static Texture2D Load(ref Texture2D slot, string path)
        {
            if (slot == null) slot = Resources.Load<Texture2D>(path);
            return slot;
        }

        public static Texture2D Board { get { return Load(ref board, "Season/T_UI_SeasonBoard"); } }
        public static Texture2D RecordCard { get { return Load(ref record, "Season/T_UI_RecordCard"); } }
        public static Texture2D PickCard { get { return Load(ref card, "Season/T_UI_PickCard"); } }
        public static Texture2D PickCardOn { get { return Load(ref cardOn, "Season/T_UI_PickCardOn"); } }
        public static Texture2D Ribbon { get { return Load(ref ribbon, "Season/T_UI_TitleRibbon"); } }
        public static Texture2D Trophy { get { return Load(ref trophy, "Season/T_UI_Trophy"); } }
        public static Texture2D ClosePaw { get { return Load(ref closePaw, "T_UI_ClosePaw"); } }
        public static Texture2D Sparkle { get { return Load(ref sparkle, "FX_Sparkle"); } }
        static Texture2D Confirm { get { return Load(ref confirm, "UI_ConfirmButton"); } }
        static Texture2D Bone { get { return Load(ref bone, "UI_Bone"); } }

        /// <summary>순위 그림. 1위는 트로피, 2위와 3위는 은메달과 동메달, 그 밖은 null.</summary>
        public static Texture2D RankIcon(int rank)
        {
            if (rank == 1) return Trophy;
            if (rank == 2) return Load(ref silver, "T_UI_Medal_Silver");
            if (rank == 3) return Load(ref bronze, "T_UI_Medal_Bronze");
            return null;
        }

        public static Texture2D GoldMedal { get { return Load(ref gold, "T_UI_Medal_Gold"); } }

        // 명예의 전당(2026-11-05, 바르코): 왕관 월계관 장식 판, 기록 한 줄 명판, 1위 금빛 명판
        static Texture2D hallBoard, hallPlaque, hallPlaqueGold;
        public static Texture2D HallBoard { get { return Load(ref hallBoard, "Season/T_UI_HallBoard"); } }
        public static Texture2D HallPlaque { get { return Load(ref hallPlaque, "Season/T_UI_HallPlaque"); } }
        public static Texture2D HallPlaqueGold { get { return Load(ref hallPlaqueGold, "Season/T_UI_HallPlaqueGold"); } }

        public static readonly Color Ink = new Color(0.33f, 0.2f, 0.1f);
        public static readonly Color SoftInk = new Color(0.52f, 0.38f, 0.26f);
        public static readonly Color Gold = new Color(1f, 0.78f, 0.18f);
        public static readonly Color DeepGold = new Color(0.78f, 0.5f, 0.04f);
        public static readonly Color Outline = new Color(0.36f, 0.13f, 0.06f);
        public static readonly Color Cream = new Color(1f, 0.97f, 0.88f);

        // ---- 글씨 ----

        static readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();

        /// <summary>BMJUA 굵은 글씨. 크기, 정렬, 색, 줄바꿈마다 한 번 만들어 둔다.</summary>
        public static GUIStyle Text(int size, TextAnchor anchor, Color color, bool wrap = false)
        {
            string key = size + "|" + (int)anchor + "|" + ColorUtility.ToHtmlStringRGBA(color) + "|" + wrap;
            GUIStyle st;
            if (styles.TryGetValue(key, out st) && st.font == UiSkin.Font) return st;

            st = new GUIStyle { font = UiSkin.Font, fontSize = size, fontStyle = FontStyle.Bold, alignment = anchor, wordWrap = wrap };
            st.normal.textColor = color;
            styles[key] = st;
            return st;
        }

        /// <summary>한 줄에 들어가도록 글씨 크기를 2씩 줄인다.</summary>
        public static int Fit(string text, int size, float maxWidth, int min = 14)
        {
            var content = new GUIContent(text);
            while (size > min && Text(size, TextAnchor.MiddleCenter, Color.white).CalcSize(content).x > maxWidth) size -= 2;
            return size;
        }

        /// <summary>테두리 두른 글씨. 리본과 어두운 배경 위 글씨가 또렷하게.</summary>
        public static void Outlined(Rect r, string text, GUIStyle style, Color outline, float width = 2f)
        {
            Color keep = style.normal.textColor;
            style.normal.textColor = new Color(outline.r, outline.g, outline.b, outline.a * GUI.color.a);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * width, r.y + Mathf.Sin(a) * width, r.width, r.height), text, style);
            }
            style.normal.textColor = keep;
            GUI.Label(r, text, style);
        }

        // ---- 움직임 ----

        /// <summary>창이 열리고 얼마나 됐는지에 따라 0.85 에서 1 로 살짝 튀어나오는 배율.</summary>
        public static float Pop(float openedAt)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.32f);
            const float c = 1.70158f;
            float e = 1f + (c + 1f) * Mathf.Pow(t - 1f, 3f) + c * Mathf.Pow(t - 1f, 2f);
            return Mathf.LerpUnclamped(0.85f, 1f, e);
        }

        public static float Fade(float openedAt)
        {
            return Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.2f);
        }

        /// <summary>0 에서 1 로 차오르는 값(막대 채우기). delay 뒤 0.6초.</summary>
        public static float Grow(float openedAt, float delay)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt - delay) / 0.6f);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        // ---- 그림 조각 ----

        /// <summary>뒤를 어둡게 덮고, 가운데에 천천히 도는 햇살을 깐다. 우승이면 금빛으로 진하게.</summary>
        public static void Backdrop(float sw, float sh, bool golden, float openedAt)
        {
            float a = Fade(openedAt);
            GUI.color = new Color(0.05f, 0.04f, 0.08f, 0.74f * a);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);

            Matrix4x4 keep = GUI.matrix;
            float size = Mathf.Max(sw, sh) * 1.35f;
            var c = new Vector2(sw * 0.5f, sh * 0.5f);
            GUIUtility.RotateAroundPivot(Time.unscaledTime * 6f, c);
            GUI.color = golden ? new Color(1f, 0.8f, 0.28f, 0.42f * a) : new Color(1f, 0.93f, 0.75f, 0.17f * a);
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), Rays);
            GUI.matrix = keep;
            GUI.color = Color.white;
        }

        /// <summary>빨간 리본에 흰 제목. 글씨가 길면 줄여서 리본 안에 맞춘다.</summary>
        public static void RibbonTitle(Rect r, string text, int size)
        {
            if (Ribbon != null) GUI.DrawTexture(r, Ribbon, ScaleMode.StretchToFill, true);
            else GUI.Box(r, GUIContent.none, UiSkin.Tag(new Color(0.9f, 0.3f, 0.28f)));
            var t = new Rect(r.x + r.width * 0.16f, r.y + r.height * 0.12f, r.width * 0.68f, r.height * 0.62f);
            int fit = Fit(text, size, t.width);
            Outlined(t, text, Text(fit, TextAnchor.MiddleCenter, Color.white), Outline, 2.5f);
        }

        /// <summary>
        /// 그림 버튼. primary 는 노란 확인 버튼(진한 갈색 글씨), 아니면 주황 뼈다귀(흰 글씨).
        /// 마우스를 올리면 살짝 커진다. 누르면 true.
        /// </summary>
        public static bool Button(Rect r, string text, bool primary, int size = 30)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            float k = hover ? 1.05f : 1f;
            var d = new Rect(r.center.x - r.width * k * 0.5f, r.center.y - r.height * k * 0.5f, r.width * k, r.height * k);

            Texture2D tex = primary ? Confirm : Bone;
            if (tex != null)
            {
                if (primary) UiSkin.Draw9(d, tex, tex.height * 0.5f, tex.height * 0.3f, tex.height * 0.5f, tex.height * 0.3f, d.height / tex.height);
                else UiSkin.Draw9(d, tex, tex.width * 0.3f, tex.height * 0.25f, tex.width * 0.3f, tex.height * 0.25f, d.height / tex.height);
            }
            else GUI.Box(d, GUIContent.none, UiSkin.Button(primary ? UiSkin.Green : UiSkin.Coral));

            float inset = primary ? d.height * 0.45f : d.width * 0.2f;
            var t = new Rect(d.x + inset, d.y, d.width - inset * 2f, d.height * (primary ? 0.94f : 0.9f));
            int fit = Fit(text, Mathf.RoundToInt(size * k), t.width);
            if (primary)
            {
                var st = Text(fit, TextAnchor.MiddleCenter, Ink);
                Outlined(t, text, st, new Color(1f, 0.98f, 0.85f, 0.85f), 1.5f);
            }
            else Outlined(t, text, Text(fit, TextAnchor.MiddleCenter, Color.white), Outline, 2f);

            return UiSkin.Click(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>둥근 막대. t 는 0~1.</summary>
        public static void Bar(Rect r, float t, Color fill)
        {
            GUI.Box(r, GUIContent.none, UiSkin.Tag(new Color(0.86f, 0.78f, 0.64f)));
            t = Mathf.Clamp01(t);
            if (t <= 0f) return;
            float inner = r.height - 6f;
            var f = new Rect(r.x + 3f, r.y + 3f, Mathf.Max(inner, (r.width - 6f) * t), inner);
            GUI.Box(f, GUIContent.none, UiSkin.Tag(fill));
        }

        /// <summary>도는 햇살 그림. 14갈래, 가장자리로 갈수록 옅어진다. 처음 한 번 만든다.</summary>
        static Texture2D Rays
        {
            get
            {
                if (rays != null) return rays;
                const int N = 256;
                rays = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f - N * 0.5f) / (N * 0.5f), dy = (y + 0.5f - N * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ray = 0.5f + 0.5f * Mathf.Cos(Mathf.Atan2(dy, dx) * 14f);
                    float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, ray));
                    float fade = Mathf.Clamp01(1f - r);
                    float a = (0.2f + 0.8f * s) * fade * fade;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                rays.SetPixels32(px);
                rays.Apply();
                return rays;
            }
        }
    }
}
