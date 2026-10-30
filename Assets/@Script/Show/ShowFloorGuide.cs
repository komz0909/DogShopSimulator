using System.Collections.Generic;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 도그쇼 바닥 안내(바르코로 뽑은 그림, 2026-10-30).
    ///   ④ 장애물 달리기: 강아지 발밑에서 다음 장애물 입구까지 화살표를 줄지어 깔고, 입구에는 지나갈 방향으로 큰 화살표 하나.
    ///      처음 하는 사람이 어디로 가야 할지 몰라 시간을 다 쓰던 문제(10-30 빌드 점검).
    ///   ③ 어질리티: 허들·링마다 "여기서 Space 를 누르면 넘는다" 구간에 초록 패드. 강아지가 구간에 들어오면 밝아진다.
    /// 그림은 Resources 의 머티리얼(M_ShowCourseArrow, M_ShowJumpZone)을 복제해 쓴다 — 머티리얼 에셋이 있어야 빌드에 투명 셰이더가 남는다.
    /// 무대(stage) 아래에 붙여 두므로 장면이 바뀌면(F12 등) 같이 사라진다. 다 쓰면 <see cref="Clear"/>.
    /// </summary>
    public class ShowFloorGuide
    {
        const float Lift = 0.025f;           // 바닥에서 띄우는 높이(바닥과 겹쳐 깜빡이지 않게)
        const int MaxTrail = 7;              // 줄 화살표 최대 수
        const float TrailStart = 0.75f;      // 강아지에서 첫 화살표까지
        const float TrailGap = 0.85f;        // 화살표 사이
        const float TrailStopShort = 0.55f;  // 입구 화살표와 겹치지 않게 이만큼 앞에서 멈춘다
        static readonly Vector2 TrailSize = new Vector2(0.42f, 0.54f);   // 가로, 세로(그림 666x852 비율)
        static readonly Vector2 EntrySize = new Vector2(0.66f, 0.85f);
        const float PadWidth = 1.0f;         // 점프 패드 가로(달리는 방향과 직각)

        readonly Transform root;
        readonly List<Material> made = new List<Material>();
        readonly List<Renderer> trail = new List<Renderer>();
        Renderer entry;

        struct Pad { public Renderer r; public float start, end, at; }
        readonly List<Pad> pads = new List<Pad>();

        public ShowFloorGuide(Transform stage, string name)
        {
            root = new GameObject(name).transform;
            root.SetParent(stage, false);
        }

        public void Clear()
        {
            foreach (Material m in made) if (m != null) Object.Destroy(m);
            made.Clear();
            trail.Clear();
            pads.Clear();
            entry = null;
            if (root != null) Object.Destroy(root.gameObject);
        }

        // ---- ④ 장애물 달리기 ----

        /// <summary>
        /// 매 프레임. <paramref name="show"/> 가 false(장애물을 하는 중, 위브 리듬 중, 다 끝남)면 모두 숨긴다.
        /// <paramref name="entryPoint"/> 는 다음 장애물 입구(월드), <paramref name="entryForward"/> 는 지나가는 방향.
        /// </summary>
        public void UpdateCourse(Vector3 dog, bool show, Vector3 entryPoint, Vector3 entryForward, float floorY)
        {
            if (trail.Count == 0) BuildCourse();

            if (!show)
            {
                foreach (Renderer r in trail) r.enabled = false;
                if (entry != null) entry.enabled = false;
                return;
            }

            float t = Time.time;
            Vector3 flatDog = new Vector3(dog.x, floorY + Lift, dog.z);
            Vector3 flatEntry = new Vector3(entryPoint.x, floorY + Lift, entryPoint.z);
            Vector3 to = flatEntry - flatDog;
            float dist = to.magnitude;
            Vector3 dir = dist > 1e-3f ? to / dist : entryForward;

            for (int i = 0; i < trail.Count; i++)
            {
                float d = TrailStart + i * TrailGap;
                bool on = d < dist - TrailStopShort;
                trail[i].enabled = on;
                if (!on) continue;
                Place(trail[i].transform, flatDog + dir * d, dir, TrailSize);
                // 발밑에서 입구 쪽으로 흐르는 물결 — 어느 쪽으로 가는지 한눈에
                float wave = 0.5f + 0.5f * Mathf.Sin(t * 6f - i * 0.9f);
                SetAlpha(trail[i], Mathf.Lerp(0.55f, 1f, wave));
            }

            entry.enabled = true;
            Vector3 ef = new Vector3(entryForward.x, 0f, entryForward.z).normalized;
            float bob = 1f + 0.06f * Mathf.Sin(t * 4f);
            Place(entry.transform, flatEntry, ef, EntrySize * bob);
            SetAlpha(entry, 0.85f + 0.15f * Mathf.Sin(t * 4f));
        }

        void BuildCourse()
        {
            Material src = Resources.Load<Material>("M_ShowCourseArrow");
            for (int i = 0; i < MaxTrail; i++) trail.Add(MakeDecal("Arrow_" + i, src));
            entry = MakeDecal("Arrow_Entry", src);
        }

        // ---- ③ 어질리티 ----

        /// <summary>
        /// 점프 패드 하나. 달리는 길 위 <paramref name="start"/>~<paramref name="end"/>(트랙 거리)에서 누르면 <paramref name="at"/>(허들·링)을 넘는다.
        /// <paramref name="center"/>·<paramref name="forward"/> 는 월드 좌표.
        /// </summary>
        public void AddJumpPad(Vector3 center, Vector3 forward, float start, float end, float at)
        {
            Material src = Resources.Load<Material>("M_ShowJumpZone");
            Renderer r = MakeDecal("JumpPad_" + pads.Count, src);
            Place(r.transform, center + Vector3.up * Lift, forward, new Vector2(PadWidth, Mathf.Max(0.3f, end - start)));
            pads.Add(new Pad { r = r, start = start, end = end, at = at });
        }

        /// <summary>매 프레임. <paramref name="pos"/> 는 강아지의 트랙 거리.</summary>
        public void UpdatePads(float pos)
        {
            float t = Time.time;
            foreach (Pad p in pads)
            {
                float a;
                if (pos > p.at) a = Mathf.Clamp01(1f - (pos - p.at) / 1.2f) * 0.5f;          // 지나갔으면 흐려진다
                else if (pos >= p.start && pos <= p.end) a = 1f;                               // 지금이 누를 때
                else if (pos > p.start - 4f) a = 0.6f + 0.15f * Mathf.Sin(t * 8f);            // 다가오는 중 — 깜빡여서 눈에 띄게
                else a = 0.45f;
                SetAlpha(p.r, a);
                bool hot = pos >= p.start && pos <= p.end;
                Vector3 s = p.r.transform.localScale;
                float want = hot ? 1.12f : 1f;
                // 가로만 살짝 부풀린다(세로는 구간 길이라 그대로)
                p.r.transform.localScale = new Vector3(Mathf.Lerp(s.x, PadWidth * want, 0.35f), s.y, 1f);
            }
        }

        // ---- 공통 ----

        Renderer MakeDecal(string name, Material src)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(root, false);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (src != null)
            {
                var m = new Material(src);
                made.Add(m);
                r.sharedMaterial = m;
            }
            r.enabled = false;
            return r;
        }

        /// <summary>바닥에 눕힌다 — 그림 위쪽이 <paramref name="forward"/>, 크기는 (가로, 세로) m. 높이는 부르는 쪽이 Lift 까지 넣어 준다.</summary>
        static void Place(Transform t, Vector3 world, Vector3 forward, Vector2 size)
        {
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            t.position = world;
            // Quad 는 로컬 -Z 쪽에서 보이고 그림 위쪽이 로컬 +Y — 로컬 Z 를 아래로, Y 를 진행 방향으로
            t.rotation = Quaternion.LookRotation(Vector3.down, forward);
            t.localScale = new Vector3(size.x, size.y, 1f);
            t.GetComponent<Renderer>().enabled = true;
        }

        static void SetAlpha(Renderer r, float a)
        {
            Material m = r.sharedMaterial;
            if (m == null) return;
            Color c = m.GetColor("_BaseColor");
            c.a = a;
            m.SetColor("_BaseColor", c);
        }
    }
}
