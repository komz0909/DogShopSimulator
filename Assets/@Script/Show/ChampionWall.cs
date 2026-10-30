using System.Collections.Generic;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 명예의 벽(2026-10-30). 시즌 챔피언십에서 우승할 때마다 가게 뒷벽에 액자가 하나씩 걸린다.
    /// 액자 안에는 그 시즌 우승견의 견종 그림(견종 고르기와 같은 초상화), 아래 이름판에는 "시즌 N 챔피언 / 이름".
    ///
    /// 액자 그림은 바르코(T_ChampionFrame — 가운데 구멍과 바깥은 투명). 재질은 씬에 연결된 에셋을 쓴다 —
    /// 코드에서 셰이더를 새로 만들면 빌드에서 그 변형이 빠질 수 있다.
    /// 효과(우승 상금, 판매가 보너스)는 <see cref="ChampionshipManager"/> 가 갖는다. 여기는 보이기만 한다.
    /// </summary>
    public class ChampionWall : MonoBehaviour
    {
        [SerializeField] Material frameMaterial;     // M_ChampionFrame (URP Lit, 알파 잘라내기)
        [SerializeField] Material portraitMaterial;  // M_ChampionPortrait (URP Lit, 알파 잘라내기, 그림은 견종마다 바꿔 낀다)
        [SerializeField] Material backingMaterial;   // M_ChampionBacking (구멍 뒤 하늘색 바탕)
        [SerializeField] Texture2D[] breedPortraits = new Texture2D[0];   // DogManager 견종 순서
        [SerializeField] Font font;

        /// <summary>첫 액자 자리. 뒷벽(z 6.0) 안쪽, 진열대 위(1.55m)와 천장 들보(2.88m) 사이.</summary>
        [SerializeField] Vector3 firstPosition = new Vector3(0.6f, 2.2f, 5.985f);
        [SerializeField] float spacing = 0.88f;
        [SerializeField] int maxFrames = 7;   // 뒷벽(0~6.4m)에 들어가는 만큼. 넘으면 최근 것부터

        const float FrameHeight = 0.78f;   // 게임 카메라 거리에서도 이름판이 읽히게
        const float FrameAspect = 828f / 1018f;   // 액자 그림 가로/세로

        // 액자 그림 안의 자리(아래가 0). 바르코 그림에서 잰 값
        static readonly Rect Hole = new Rect(0.188f, 0.185f, 0.622f, 0.605f);
        static readonly Rect Plaque = new Rect(0.251f, 0.03f, 0.497f, 0.13f);

        readonly List<GameObject> frames = new List<GameObject>();
        readonly List<Material> owned = new List<Material>();
        int shown = -1;

        void Update()
        {
            ChampionshipManager champ = ChampionshipManager.Instance;
            if (champ == null || champ.Champions.Count == shown) return;
            Rebuild(champ.Champions);
        }

        void OnDestroy()
        {
            foreach (Material m in owned) if (m != null) Destroy(m);
        }

        void Rebuild(IReadOnlyList<ChampionshipManager.ChampionRecord> champions)
        {
            foreach (GameObject f in frames) if (f != null) Destroy(f);
            frames.Clear();
            shown = champions.Count;

            int from = Mathf.Max(0, champions.Count - maxFrames);
            for (int i = from; i < champions.Count; i++)
                frames.Add(Build(champions[i], firstPosition + Vector3.right * spacing * (i - from)));
        }

        GameObject Build(ChampionshipManager.ChampionRecord record, Vector3 position)
        {
            var root = new GameObject("ChampionFrame_S" + record.season);
            root.transform.SetParent(transform, false);
            root.transform.position = position;

            float h = FrameHeight, w = FrameHeight * FrameAspect;

            // 구멍 자리(액자 가운데 기준)
            Vector2 holeCenter = new Vector2((Hole.center.x - 0.5f) * w, (Hole.center.y - 0.5f) * h);
            Vector2 holeSize = new Vector2(Hole.width * w, Hole.height * h) * 1.04f;   // 테두리 안쪽까지 살짝 덮게

            Quad(root.transform, "Backing", backingMaterial, holeCenter, holeSize, 0.006f);

            Texture2D portrait = record.breedIndex >= 0 && record.breedIndex < breedPortraits.Length ? breedPortraits[record.breedIndex] : null;
            if (portrait != null && portraitMaterial != null)
            {
                var mat = new Material(portraitMaterial) { mainTexture = portrait };
                // 정사각 그림을 세로로 긴 구멍에 꽉 채운다 — 좌우를 잘라 낸다
                float aspect = holeSize.x / holeSize.y;
                mat.mainTextureScale = new Vector2(aspect, 1f);
                mat.mainTextureOffset = new Vector2((1f - aspect) * 0.5f, 0f);
                owned.Add(mat);
                Quad(root.transform, "Portrait", mat, holeCenter, holeSize, 0.003f);
            }

            Quad(root.transform, "Frame", frameMaterial, Vector2.zero, new Vector2(w, h), 0f);

            // 이름판 글씨 — 두 줄(시즌, 이름)
            Vector2 plaqueCenter = new Vector2((Plaque.center.x - 0.5f) * w, (Plaque.center.y - 0.5f) * h);
            Text(root.transform, "시즌 " + record.season + " 챔피언\n" + record.dogName, plaqueCenter, Plaque.height * h);
            return root;
        }

        static void Quad(Transform parent, string name, Material material, Vector2 center, Vector2 size, float back)
        {
            if (material == null) return;
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = new Vector3(center.x, center.y, back);   // 벽 쪽(+z)으로 조금씩 뒤에
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Text(Transform parent, string text, Vector2 center, float height)
        {
            if (font == null) return;
            var go = new GameObject("Name");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, -0.004f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.lineSpacing = 0.9f;
            tm.color = new Color(0.36f, 0.22f, 0.1f);
            // 글자 높이가 이름판 높이의 40% 쯤(두 줄이 들어가게)
            tm.characterSize = height * 0.44f / (64f * 0.1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}
