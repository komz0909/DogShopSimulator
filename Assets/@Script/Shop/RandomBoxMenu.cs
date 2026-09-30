using DogShop.Data;
using DogShop.UI;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 상자를 깐 결과를 보여 주는 작은 창.
    ///
    /// 뽑기의 재미는 <b>결과를 보는 순간</b>에 있다. 창고 숫자만 조용히 올라가면
    /// 400원을 쓴 보람이 어디에도 남지 않으므로, 확인을 누를 때까지 창을 띄워 둔다.
    /// </summary>
    public class RandomBoxMenu : MonoBehaviour
    {
        const float Width = 340f;
        const float Height = 320f;

        /// <summary>반짝임 한 바퀴에 걸리는 시간(초).</summary>
        const float SparklePeriod = 0.9f;

        /// <summary>제목 옆에 붙일 상자 그림.</summary>
        [SerializeField] Texture2D boxIcon;

        Texture2D glow;

        void Awake()
        {
            glow = new Texture2D(1, 1);
            glow.SetPixel(0, 0, Color.white);
            glow.Apply();
        }

        void OnDestroy()
        {
            if (glow != null) Destroy(glow);
        }

        void OnGUI()
        {
            RandomBox box = RandomBox.Instance;
            if (box == null || !box.HasResult) return;

            var rect = new Rect((Screen.width - Width) * 0.5f, (Screen.height - Height) * 0.5f, Width, Height);

            ItemGrade grade = box.ResultGrade;
            Color tint = ItemGrades.ColorOf(grade);

            DrawSparkle(rect, grade, tint);

            GUI.Box(rect, GUIContent.none, UiSkin.Panel_);

            var header = new GUIStyle(UiSkin.Title) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            if (boxIcon != null)
                GUI.DrawTexture(new Rect(rect.x + 96f, rect.y + 6f, 34f, 34f), boxIcon, ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(rect.x + 136f, rect.y + 12f, rect.width - 140f, 26f), "상자를 열었다", header);

            // 무엇이 나왔는지 사진으로 보여 준다. 상점 창과 같은 사진을 쓴다 —
            // 글자만 있으면 상자를 깐 보람이 숫자 한 줄로 끝난다
            DrawPhoto(new Rect(rect.center.x - 62f, rect.y + 42f, 124f, 124f), box.ResultProduct, tint, grade);

            // 등급을 크게. 이 창에서 가장 먼저 눈에 들어와야 하는 글자다
            var big = new GUIStyle(UiSkin.Caption)
            {
                fontSize = 46,
                alignment = TextAnchor.MiddleCenter
            };
            big.normal.textColor = tint;
            GUI.Label(new Rect(rect.x, rect.y + 172f, rect.width, 56f), ItemGrades.NameOf(grade), big);

            var line = new GUIStyle(UiSkin.Caption) { fontSize = 17 };
            GUI.Label(new Rect(rect.x, rect.y + 226f, rect.width, 24f), box.ResultLine(), line);

            var note = new GUIStyle(UiSkin.Caption) { fontSize = 13 };
            note.normal.textColor = new Color(0.72f, 0.75f, 0.78f);
            GUI.Label(new Rect(rect.x, rect.y + 248f, rect.width, 22f), "창고에 넣었다", note);

            if (GUI.Button(new Rect(rect.x + rect.width * 0.5f - 60f, rect.yMax - 52f, 120f, 36f),
                    "확인", UiSkin.Button(UiSkin.Green)))
                box.Acknowledge();
        }

        /// <summary>
        /// 나온 물건의 사진. 등급이 높으면 사진 뒤에도 등급 색 판을 깔아,
        /// 글자를 안 읽어도 좋은 게 나왔다는 것이 보이게 한다.
        /// </summary>
        void DrawPhoto(Rect frame, int productIndex, Color tint, ItemGrade grade)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null || productIndex < 0) return;

            Color prev = GUI.color;
            GUI.color = new Color(tint.r, tint.g, tint.b, ItemGrades.Sparkles(grade) ? 0.28f : 0.14f);
            GUI.DrawTexture(frame, glow);
            GUI.color = prev;

            Texture2D icon = inv.Catalog.Get(productIndex).icon;
            if (icon != null) GUI.DrawTexture(frame, icon, ScaleMode.ScaleToFit, true);
        }

        /// <summary>
        /// 높은 등급일수록 티가 나게 창 뒤에 빛을 깐다.
        /// B 아래로는 그리지 않는다 — 다 반짝이면 반짝임이 신호 노릇을 못 한다.
        /// </summary>
        void DrawSparkle(Rect rect, ItemGrade grade, Color tint)
        {
            if (!ItemGrades.Sparkles(grade)) return;

            // 등급이 올라갈수록 더 크게, 더 세게 맥동한다
            float step = (int)grade - (int)ItemGrades.SparkleFrom;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / SparklePeriod));

            int rings = 3 + Mathf.RoundToInt(step * 2f);
            for (int i = rings; i >= 1; i--)
            {
                float spread = 10f * i * (1f + step * 0.35f) * (0.85f + pulse * 0.3f);
                float alpha = (0.16f + step * 0.06f) * (1f - i / (float)(rings + 1)) * (0.6f + pulse * 0.6f);

                Color prev = GUI.color;
                GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
                GUI.DrawTexture(new Rect(rect.x - spread, rect.y - spread,
                    rect.width + spread * 2f, rect.height + spread * 2f), glow);
                GUI.color = prev;
            }
        }
    }
}
