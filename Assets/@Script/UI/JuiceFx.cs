using System.Collections;
using System.Collections.Generic;
using DogShop.Core;
using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// <b>손맛 피드백</b> 창구. 계산 "+N원", 진열 "톡" 튕김, 청소 반짝, 레벨업 축하 화면을 한 곳에서 그린다.
    /// 어디서든 <c>JuiceFx.FloatText(...)</c> 처럼 한 줄로 부른다. 처음 부를 때 스스로 생기고 장면과 함께 사라진다.
    ///
    /// 파티클 셰이더는 빌드에서 빠질 수 있어 쓰지 않는다 — 반짝이는 OnGUI 로 직접 그린다(<see cref="Dogs.MascotGreeter"/> 와 같은 방식).
    /// 소리는 새 칸(CoinJingle·ItemTok·Sparkle·Fanfare)을 같이 부른다. 클립이 비어 있으면 조용히 넘어간다.
    /// </summary>
    public class JuiceFx : MonoBehaviour
    {
        const float FloatSeconds = 1.3f;
        const float SparkleSeconds = 0.7f;
        const float LevelUpSeconds = 4.5f;

        // screen 이면 world 대신 sp(GUI 화면 좌표, 왼쪽 위 0)에 그린다 — 메뉴·HUD 위에 띄우는 것
        struct Floater { public Vector3 world; public Vector2 sp; public bool screen; public string text; public Color color; public float born; }
        struct Burst { public Vector3 world; public Vector2 sp; public bool screen; public float born; }

        static JuiceFx instance;

        readonly List<Floater> floaters = new List<Floater>();
        readonly List<Burst> bursts = new List<Burst>();

        int levelUpLevel;
        string levelUpUnlock;
        readonly List<string> levelUpProducts = new List<string>();
        float levelUpAt = -100f;

        GUIStyle floatStyle, bigTitle, body;

        static JuiceFx Get()
        {
            if (instance == null) instance = new GameObject("JuiceFx").AddComponent<JuiceFx>();
            return instance;
        }

        void OnDestroy() { if (instance == this) instance = null; }

        // ---- 부르는 쪽 ----

        /// <summary>그 자리에서 위로 떠오르며 흐려지는 글("+120원").</summary>
        public static void FloatText(Vector3 world, string text, Color color) =>
            Get().floaters.Add(new Floater { world = world, text = text, color = color, born = Time.unscaledTime });

        /// <summary>물건을 "톡" 튕긴다 — 눌렸다가 커졌다가 제자리.</summary>
        public static void Punch(Transform target, float amount = 0.3f, float seconds = 0.3f)
        {
            if (target == null) return;
            Get().StartCoroutine(PunchRoutine(target, amount, seconds));
        }

        /// <summary>그 자리에 반짝이가 퍼지며 사라진다. 이미 제 효과음이 있는 자리(도그쇼 판정)는 quiet 로 반짝 소리를 뺀다.</summary>
        public static void Sparkle(Vector3 world, bool quiet = false)
        {
            Get().bursts.Add(new Burst { world = world, born = Time.unscaledTime });
            if (!quiet) Sound.Play(Sfx.Sparkle);
        }

        /// <summary>화면 좌표(GUI, 왼쪽 위 0) 버전 — 할 일 칸·발주 창처럼 화면에 붙은 것 위에 띄운다.</summary>
        public static void SparkleScreen(Vector2 gui, bool quiet = false)
        {
            Get().bursts.Add(new Burst { sp = gui, screen = true, born = Time.unscaledTime });
            if (!quiet) Sound.Play(Sfx.Sparkle);
        }

        public static void FloatTextScreen(Vector2 gui, string text, Color color) =>
            Get().floaters.Add(new Floater { sp = gui, screen = true, text = text, color = color, born = Time.unscaledTime });

        /// <summary>
        /// 지금 화면을 그리는 카메라. 도그쇼는 Main 이 아닌 무대 카메라로 그리므로 Camera.main 만 보면 위치가 어긋난다 —
        /// 켜져 있는 화면용 카메라 중 가장 위(depth)에 그리는 것을 쓴다.
        /// </summary>
        static Camera ViewCamera()
        {
            Camera best = null;
            foreach (Camera c in Camera.allCameras)
                if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
            return best != null ? best : Camera.main;
        }

        /// <summary>레벨업 축하 화면. 새로 열린 상품 이름을 같이 띄운다.</summary>
        public static void LevelUp(int level, string unlock, IEnumerable<string> newProducts)
        {
            JuiceFx fx = Get();
            fx.levelUpLevel = level;
            fx.levelUpUnlock = unlock;
            fx.levelUpProducts.Clear();
            if (newProducts != null) fx.levelUpProducts.AddRange(newProducts);
            fx.levelUpAt = Time.unscaledTime;
            Sound.Play(Sfx.Fanfare);
        }

        static IEnumerator PunchRoutine(Transform target, float amount, float seconds)
        {
            Vector3 rest = target.localScale;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (target == null) yield break;
                float k = t / seconds;
                // 처음엔 납작하게 눌렸다가(y↓) 튀어 오르며 커지고 잦아든다
                float wave = Mathf.Sin(k * Mathf.PI * 2f) * (1f - k);
                target.localScale = new Vector3(rest.x * (1f - wave * amount * 0.5f), rest.y * (1f + wave * amount), rest.z * (1f - wave * amount * 0.5f));
                yield return null;
            }
            if (target != null) target.localScale = rest;
        }

        // ---- 그리기 ----

        static Texture2D star;

        /// <summary>
        /// 네 갈래 반짝이 그림. GUI 를 45° 돌려 마름모를 그리면 에디터 화면 배율에서 회전 중심이 어긋나
        /// 엉뚱한 곳에 흩어진다 — 돌리지 않고 모양을 그림에 직접 굽는다.
        /// </summary>
        static Texture2D Star
        {
            get
            {
                if (star != null) return star;
                // 바르코로 뽑은 광택 별(2026-10). 없으면 아래에서 직접 굽는다
                star = Resources.Load<Texture2D>("FX_Sparkle");
                if (star != null) return star;
                const int N = 32;
                star = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = Mathf.Abs((x + 0.5f) / N * 2f - 1f), v = Mathf.Abs((y + 0.5f) / N * 2f - 1f);
                        // 오목한 별(아스트로이드) + 가운데 은은한 빛
                        float shape = 1f - (Mathf.Sqrt(u) + Mathf.Sqrt(v));
                        float glow = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) * 1.6f) * 0.5f;
                        px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(shape * 4f, glow)));
                    }
                star.SetPixels(px);
                star.Apply();
                return star;
            }
        }

        void OnGUI()
        {
            GUI.depth = -100;   // 진열대 이름표 같은 다른 OnGUI 글보다 위
            if (Event.current.type != EventType.Repaint) return;
            if (floatStyle == null || floatStyle.font != UiSkin.Font)
            {
                floatStyle = new GUIStyle(UiSkin.Title) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
                bigTitle = new GUIStyle(UiSkin.Title) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                body = new GUIStyle(UiSkin.Label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }

            Color keep = GUI.color;
            Camera cam = ViewCamera();
            float now = Time.unscaledTime;

            DrawBursts(cam, now);
            DrawFloaters(cam, now);

            DrawLevelUp(now);
            GUI.color = keep;
        }

        void DrawFloaters(Camera cam, float now)
        {
            float s = UiSkin.ScreenScale;
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                Floater f = floaters[i];
                float t = now - f.born;
                if (t > FloatSeconds) { floaters.RemoveAt(i); continue; }

                Vector2 gp;
                if (f.screen) gp = f.sp - Vector2.up * (t * 50f * s);
                else
                {
                    if (cam == null) continue;
                    Vector3 p = cam.WorldToScreenPoint(f.world + Vector3.up * (t * 0.6f));
                    if (p.z <= 0f) continue;
                    gp = new Vector2(p.x, Screen.height - p.y);
                }

                float a = Mathf.Clamp01((FloatSeconds - t) / 0.4f);
                // 처음 0.12초는 크게 튀어나왔다가 제 크기로
                float pop = 1f + 0.5f * Mathf.Clamp01(1f - t / 0.12f);
                floatStyle.fontSize = Mathf.RoundToInt(26f * s * pop);
                var r = new Rect(gp.x - 150f * s, gp.y - 22f * s, 300f * s, 44f * s);
                Shadowed(r, f.text, floatStyle, new Color(f.color.r, f.color.g, f.color.b, a), a);
            }
        }

        void DrawBursts(Camera cam, float now)
        {
            float s = UiSkin.ScreenScale;
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                Burst b = bursts[i];
                float t = now - b.born;
                if (t > SparkleSeconds) { bursts.RemoveAt(i); continue; }

                Vector2 center;
                if (b.screen) center = b.sp;
                else
                {
                    if (cam == null) continue;
                    Vector3 c = cam.WorldToScreenPoint(b.world);
                    if (c.z <= 0f) continue;
                    center = new Vector2(c.x, Screen.height - c.y);
                }
                float k = t / SparkleSeconds;

                // 바깥으로 퍼지는 큰 마름모 8개 + 그 사이 작은 점 8개. 크기는 솟았다가 줄어든다
                for (int j = 0; j < 16; j++)
                {
                    bool big = j % 2 == 0;
                    float ang = j * Mathf.PI * 2f / 16f + k * 0.6f;
                    float r = (big ? 70f : 45f) * s * Mathf.Sqrt(k);
                    Vector2 p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.55f) * r - Vector2.up * (k * 20f * s);
                    float size = (big ? 30f : 16f) * s * Mathf.Sin(Mathf.Min(1f, k * 1.3f) * Mathf.PI);
                    if (size <= 0.5f) continue;
                    GUI.color = new Color(1f, 1f, 1f, big ? 1f - k * 0.5f : 1f - k);   // 그림이 이미 노랗다 — 알파만
                    GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Star);
                }
            }
        }

        void DrawLevelUp(float now)
        {
            float t = now - levelUpAt;
            if (t > LevelUpSeconds) return;

            float a = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((LevelUpSeconds - t) / 0.5f);
            // 처음엔 크게 튀어나왔다가 제 크기로 내려앉는다
            float pop = 1f + 0.25f * Mathf.Max(0f, 1f - t / 0.25f) * Mathf.Sin(Mathf.Min(1f, t / 0.25f) * Mathf.PI);

            const float W = 620f;
            float H = 100f + (string.IsNullOrEmpty(levelUpUnlock) ? 0f : 64f) + (levelUpProducts.Count > 0 ? 44f : 0f);
            float s = UiSkin.ScaleFor(W, H) * pop;

            Matrix4x4 keep = GUI.matrix;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.36f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(center.x, center.y, 0f), Quaternion.identity, new Vector3(s, s, 1f));

            var panel = new Rect(-W * 0.5f, -H * 0.5f, W, H);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Box(panel, GUIContent.none, UiSkin.Panel_);

            // 패널 둘레를 도는 반짝이
            for (int i = 0; i < 10; i++)
            {
                float ang = i * Mathf.PI * 2f / 10f + t * 0.9f;
                Vector2 p = new Vector2(Mathf.Cos(ang) * (W * 0.5f + 14f), Mathf.Sin(ang) * (H * 0.5f + 14f));
                float size = 14f + 14f * Mathf.Abs(Mathf.Sin(t * 5f + i * 1.3f));
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Star);
            }

            float y = panel.y + 18f;
            Shadowed(new Rect(panel.x, y, W, 56f), "가게 레벨 " + levelUpLevel + "!", bigTitle, new Color(1f, 0.86f, 0.35f, a), a);
            y += 62f;
            body.normal.textColor = new Color(0.92f, 0.94f, 0.95f, a);
            if (!string.IsNullOrEmpty(levelUpUnlock))
            {
                GUI.Label(new Rect(panel.x + 20f, y, W - 40f, 60f), levelUpUnlock, body);
                y += 64f;
            }
            if (levelUpProducts.Count > 0)
            {
                body.normal.textColor = new Color(0.62f, 0.9f, 0.7f, a);
                GUI.Label(new Rect(panel.x + 20f, y, W - 40f, 36f), "새 상품: " + string.Join(", ", levelUpProducts), body);
            }

            GUI.matrix = keep;
        }

        /// <summary>밝은 바닥에서도 읽히게 진한 그림자를 먼저 깔고 글을 쓴다.</summary>
        static void Shadowed(Rect r, string text, GUIStyle style, Color face, float alpha)
        {
            Color keepText = style.normal.textColor;
            GUI.color = Color.white;
            style.normal.textColor = new Color(0.2f, 0.11f, 0.03f, alpha);
            for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2)
                if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, style);
            style.normal.textColor = face;
            GUI.Label(r, text, style);
            style.normal.textColor = keepText;
        }
    }
}
