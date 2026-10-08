using System.Collections.Generic;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 도그쇼 ④ 장애물 달리기 코스. 점프는 ③ 어질리티가 맡으므로 여기는 <b>WASD 로 달리기만</b> 한다.
    ///
    /// 코스는 무대 앞쪽 빈 바닥(허들 레인 앞)에 S 자로 깐다.
    ///   윗줄(→)  A-프레임, 터널, 도그 워크
    ///   아랫줄(←) 위브 폴 12개, 시소, 테이블(위에서 멈춰 버티기)
    /// 순서대로만 인정한다. 다음 장애물 하나만 판정하고, 나머지는 지나가도 세지 않는다.
    /// 닿기 전에는 자유롭게 다니고, 닿는 순간 시작된다. 시작한 장애물은 뒤로 못 돌아가고,
    /// 떨어지거나 벗어나면 다시 하지 않고 감점으로 끝낸다.
    ///
    /// 오르막은 콜라이더 표면을 위에서 쏘아 높이를 얻는다 — 바르코 모델 모양이 제각각이라
    /// 경사를 수식으로 박지 않고 실제 표면을 따라간다. 모델이 비어 있으면 기본 도형으로 세운다.
    /// </summary>
    public class ObstacleCourse : MonoBehaviour
    {
        public enum Kind { AFrame, Tunnel, DogWalk, Weave, Seesaw, Table }

        [Header("바르코 모델(비우면 기본 도형)")]
        [SerializeField] GameObject aFrameModel;
        [SerializeField] GameObject tunnelModel;
        [SerializeField] GameObject dogWalkModel;
        [SerializeField] GameObject weavePoleModel;   // 봉 하나. 12개를 정확한 간격으로 세운다
        [SerializeField] GameObject seesawModel;
        [SerializeField] GameObject tableModel;

        public const int PoleCount = 12;
        /// <summary>봉 간격. 손으로 모는 거라 0.5m 는 너무 빽빽했다.</summary>
        const float PoleGap = 0.75f;
        public const float TableHold = 3f;
        /// <summary>시소 끝에서 이만큼 멈춰 있어야 판이 넘어간다.</summary>
        public const float SeesawWait = 0.9f;

        // 감점(점). 빠른 도착 점수에서 뺀다
        // 떨어지거나 벗어나면 다시 하지 않고 그 장애물은 감점으로 끝낸다(닿은 뒤엔 뒤로 못 돌아가니까).
        // 한 번에 크게 깎이면 불쾌하다는 의견으로 작게 잡았다. 위브는 봉·지그재그를 합쳐 건너뛴 것(-SkipPenalty) 이상 깎지 않는다
        public const int FallPenalty = 5, SkipPenalty = 8, TouchPenalty = 2, WeaveMissPenalty = 1;
        public int Falls { get; private set; }
        public int Skips { get; private set; }
        public int Touches { get; private set; }
        public int WeaveMisses { get; private set; }
        public int PenaltyPoints { get; private set; }
        /// <summary>봉에 닿았다고 보는 거리(강아지 몸 반폭 + 봉 반지름).</summary>
        const float TouchRadius = 0.2f;

        public class Obstacle
        {
            public Kind kind;
            public string nameKo;
            public string hint;        // 공략 한 줄(안내 화면·달리는 중 위쪽 글)
            public Vector3 center;     // 무대 기준
            public float yaw;          // 로컬 +z 가 진행 방향
            public Vector3 size;       // x 폭, y 높이, z 길이
            public Transform root;
            public Transform tilt;     // 시소 판
            public bool entered, done;
            public bool failed;          // 떨어졌거나 벗어나서 감점으로 끝났다
            public float maxZ;           // 닿은 뒤 가장 멀리 간 곳(로컬 z) — 여기보다 뒤로는 못 간다
            public int penalty;          // 이 장애물에서 깎인 점수(위브 상한용)
            public float score;
            public float stay;
            public float lastZ = float.NegativeInfinity;
            public int lastSide, alternations;
            public float seesawAngle;
            public bool tipped;          // 시소가 반대로 넘어갔다
            public float endWait;        // 시소 끝에서 멈춰 있던 시간
            public float lastY;          // 지난 프레임 발밑 높이 — 높은 데서 벗어나면 떨어진 것
            public readonly List<Transform> poles = new List<Transform>();
            public float[] wobble;       // 봉 흔들림(닿으면 1 에서 줄어든다)
            public bool[] touching;
        }

        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public int Next { get; private set; }
        public bool Finished => Next >= Obstacles.Count;

        /// <summary>출발점(무대 기준)과 처음 바라볼 방향.</summary>
        public static readonly Vector3 StartPoint = new Vector3(-8.6f, 0f, -7.3f);
        public static readonly Vector3 StartFacing = Vector3.right;

        /// <summary>강아지가 다닐 수 있는 칸(무대 기준). 벽·관중석 쪽으로 새지 않게 막는다.</summary>
        public static readonly Rect Area = Rect.MinMaxRect(-9.2f, -11.9f, 9.2f, -6.1f);

        Transform stage;
        readonly HashSet<Collider> surfaces = new HashSet<Collider>();
        readonly List<Material> made = new List<Material>();

        public void Build(Transform stageRoot)
        {
            Clear();
            stage = stageRoot;
            Add(Kind.AFrame, "A-프레임", new Vector3(-5.5f, 0f, -7.3f), 90f, new Vector3(1.0f, 0.9f, 3.0f));
            // 터널·도그 워크는 손으로 몰기엔 좁았다 — 터널 지름 0.75→1.0, 다리 폭 0.5→0.75
            Add(Kind.Tunnel, "터널", new Vector3(-1.0f, 0f, -7.3f), 90f, new Vector3(1.0f, 1.0f, 3.0f));
            Add(Kind.DogWalk, "도그 워크", new Vector3(4.5f, 0f, -7.3f), 90f, new Vector3(0.75f, 0.6f, 4.2f));
            Add(Kind.Weave, "위브", new Vector3(3.5f, 0f, -10.6f), -90f, new Vector3(1.4f, 0.9f, PoleCount * PoleGap));
            Add(Kind.Seesaw, "시소", new Vector3(-3.6f, 0f, -10.6f), -90f, new Vector3(0.5f, 0.4f, 3.0f));
            Add(Kind.Table, "테이블", new Vector3(-7.3f, 0f, -10.6f), -90f, new Vector3(1.0f, 0.35f, 1.0f));
            Next = 0;
            Falls = Touches = WeaveMisses = Skips = 0;
            PenaltyPoints = 0;
        }

        // ---- 뒤로 못 가기 ----
        // 장애물에 닿기 전에는 자유롭게 다닌다. 다음 장애물에 몸이 닿는 순간 그 장애물이 시작되고,
        // 끝날 때까지는 장애물 진행 방향(로컬 +z)으로 지나온 곳보다 뒤로는 못 간다.

        /// <summary>뒤로 이만큼까지는 물러날 수 있다(방향 틀 때 걸리지 않게).</summary>
        const float BackSlack = 0.3f;

        /// <summary>지금 하고 있는(닿아서 시작된) 장애물. 없으면 null.</summary>
        public Obstacle Active => !Finished && Obstacles[Next].entered ? Obstacles[Next] : null;

        /// <summary>
        /// 이번 프레임 이동(무대 기준)을 거른다. 장애물을 하는 중이면 뒤로 가는 몫만 지운다 — 옆으로는 자유.
        /// 입구 밖으로 뒷걸음질해 나가지도 못한다.
        /// </summary>
        public Vector3 ConstrainForward(Vector3 from, Vector3 to)
        {
            Obstacle o = Active;
            if (o == null) return to;
            float limit = Mathf.Max(o.maxZ - BackSlack, -Front(o) + 0.05f);
            if (LocalStage(o, to).z < limit)
            {
                Vector3 fwd = Quaternion.Euler(0f, o.yaw, 0f) * Vector3.forward;
                Vector3 d = to - from;
                float back = Vector3.Dot(d, fwd);
                if (back < 0f) d -= fwd * back;
                to = from + d;
            }
            return to;
        }

        /// <summary>입구 쪽 끝(로컬 -z 방향 거리). 테이블은 계단까지.</summary>
        static float Front(Obstacle o) => o.size.z * 0.5f + (o.kind == Kind.Table ? StepCount * StepDepth : 0f);

        /// <summary>강아지가 장애물 칸 안에 들어와 있나(로컬 좌표). 닿는 순간 그 장애물이 시작된다.</summary>
        static bool Touching(Obstacle o, Vector3 lp)
        {
            float halfW = o.kind == Kind.Weave ? 0.6f : o.size.x * 0.5f + 0.05f;
            return Mathf.Abs(lp.x) < halfW && lp.z > -Front(o) && lp.z < o.size.z * 0.5f;
        }

        public void Clear()
        {
            foreach (Obstacle o in Obstacles) if (o.root != null) Destroy(o.root.gameObject);
            Obstacles.Clear();
            surfaces.Clear();
            foreach (Material m in made) if (m != null) Destroy(m);
            made.Clear();
        }

        // ---- 세우기 ----

        void Add(Kind kind, string nameKo, Vector3 center, float yaw, Vector3 size)
        {
            var o = new Obstacle { kind = kind, nameKo = nameKo, center = center, yaw = yaw, size = size, hint = HintOf(kind) };
            var root = new GameObject("Course_" + kind).transform;
            root.SetParent(stage, false);
            root.localPosition = center;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            o.root = root;

            // 바르코 모델은 보이기만 한다. 밟는 표면은 같은 칸에 투명한 판으로 깐다 —
            // 모델 메시는 발판 틈·뒤집힌 면 때문에 위에서 쏜 광선이 군데군데 빠져 강아지가 바닥으로 꺼졌다
            switch (kind)
            {
                case Kind.AFrame:
                    ghost = Fit(aFrameModel, root, size);
                    BuildAFrame(root, size);
                    break;
                case Kind.Tunnel:
                    if (!Fit(tunnelModel, root, size)) BuildTunnel(root, size);
                    break;
                case Kind.DogWalk:
                    ghost = Fit(dogWalkModel, root, size);
                    BuildDogWalk(root, size);
                    break;
                case Kind.Weave:
                    BuildWeave(o, root, size);
                    break;
                case Kind.Seesaw:
                {
                    // 판은 받침 높이에서 기운다 — 회전 중심을 받침 꼭대기에 둔다
                    var pivot = new GameObject("Pivot").transform;
                    pivot.SetParent(root, false);
                    pivot.localPosition = new Vector3(0f, size.y, 0f);
                    o.tilt = pivot;
                    ghost = Fit(seesawModel, root, new Vector3(size.x, size.y + 0.06f, size.z));
                    // 모델은 "Board"(판)와 "Base"(받침)로 나뉘어 있다 — 판만 회전축에 붙여 기울이고 받침은 바닥에 둔다
                    Transform board = ghost ? FindDeep(root, "Board") : null;
                    if (board != null) board.SetParent(pivot, true);
                    else if (ghost) root.Find("Model").SetParent(pivot, true);
                    BuildSeesaw(root, pivot, size);
                    o.seesawAngle = -SeesawMax(size);
                    pivot.localRotation = Quaternion.Euler(o.seesawAngle, 0f, 0f);
                    break;
                }
                case Kind.Table:
                    ghost = Fit(tableModel, root, size);
                    BuildTable(root, size);
                    ghost = false;
                    BuildSteps(root, size);   // 점프가 없으니 들어오는 쪽에 계단을 붙인다
                    break;
            }
            ghost = false;
            Obstacles.Add(o);
        }

        static string HintOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.AFrame: return "A자 경사를 올라 넘어가기. 옆으로 떨어지면 감점";
                case Kind.Tunnel: return "터널 입구로 들어가 반대쪽으로 빠져나오기";
                case Kind.DogWalk: return "좁은 다리를 끝까지 건너기. 떨어지면 감점";
                case Kind.Weave: return "봉 사이를 지그재그로. 봉에 닿거나 칸을 빼먹으면 감점";
                case Kind.Seesaw: return "끝까지 올라가 잠깐 멈추면 판이 내려간다. 먼저 내리면 감점";
                default: return "계단으로 올라가 " + TableHold.ToString("0") + "초 동안 멈추기";
            }
        }

        static float SeesawMax(Vector3 size) => Mathf.Atan2(size.y, size.z * 0.5f) * Mathf.Rad2Deg;

        /// <summary>
        /// 모델을 칸 크기(폭·높이·길이)에 맞춰 늘려 앉힌다. 모델이 옆으로 길면(x 가 더 길면) 90도 돌린다.
        /// 바닥 가운데가 <paramref name="parent"/> 원점(+bottom)에 오게 옮긴다.
        /// </summary>
        bool Fit(GameObject model, Transform parent, Vector3 size, float bottom = 0f)
        {
            if (model == null) return false;
            GameObject inst = Instantiate(model, parent);
            inst.name = "Model";
            // FBX 뿌리의 축 변환 회전(270° X)은 그대로 둔다 — 지우면 A-프레임·테이블·봉이 옆으로 눕는다
            Quaternion own = model.transform.localRotation;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = own;
            inst.transform.localScale = Vector3.one;

            Bounds b = LocalBounds(inst.transform, parent);
            if (b.size.x > b.size.z * 1.15f)
            {
                inst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * own;
                b = LocalBounds(inst.transform, parent);
            }
            // 0 나누기만 막는다 — 센티미터 단위 FBX(터널)는 배율 1 에서 0.008m 라 0.01 로 막으면 덜 커졌다
            Vector3 k = new Vector3(size.x / Mathf.Max(1e-5f, b.size.x), size.y / Mathf.Max(1e-5f, b.size.y), size.z / Mathf.Max(1e-5f, b.size.z));
            // 배율은 부모 축 기준으로 구했다 — 모델 로컬 축마다 부모의 어느 축에 놓였는지 찾아 옮겨 준다
            Quaternion rot = inst.transform.localRotation;
            Vector3 scale = Vector3.one;
            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = Vector3.zero; axis[i] = 1f;
                Vector3 a = rot * axis;
                int j = Mathf.Abs(a.x) >= Mathf.Abs(a.y) && Mathf.Abs(a.x) >= Mathf.Abs(a.z) ? 0 : Mathf.Abs(a.y) >= Mathf.Abs(a.z) ? 1 : 2;
                scale[i] = k[j];
            }
            inst.transform.localScale = scale;
            b = LocalBounds(inst.transform, parent);
            inst.transform.localPosition = new Vector3(-b.center.x, bottom - b.min.y, -b.center.z);

            foreach (Collider c in inst.GetComponentsInChildren<Collider>()) Destroy(c);
            return true;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>()) if (c.name == name) return c;
            return null;
        }

        static Bounds LocalBounds(Transform t, Transform space)
        {
            Bounds b = new Bounds(); bool first = true;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                Bounds w = r.bounds;
                Vector3 mn = w.min, mx = w.max;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = space.InverseTransformPoint(new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        // 기본 도형(바르코 모델이 오기 전)
        static readonly Color Red = new Color(0.86f, 0.27f, 0.25f), Yellow = new Color(0.98f, 0.82f, 0.25f),
                              Blue = new Color(0.3f, 0.55f, 0.85f), White = new Color(0.95f, 0.95f, 0.92f);

        Material Mat(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(s != null ? s : Shader.Find("Standard")) { color = c };
            made.Add(m);
            return m;
        }

        /// <summary>참이면 기본 도형을 <b>보이지 않는 표면</b>으로만 만든다(바르코 모델이 겉모습을 맡을 때).</summary>
        bool ghost;

        Transform Box(Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Color c, bool surface)
        {
            if (ghost && !surface) return null;   // 모델이 있으면 다리·받침 같은 장식 도형은 필요 없다
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            Renderer rend = go.GetComponent<Renderer>();
            if (ghost) rend.enabled = false;
            else rend.sharedMaterial = Mat(c);
            Collider col = go.GetComponent<Collider>();
            if (surface) surfaces.Add(col); else Destroy(col);
            return go.transform;
        }

        void Ramp(Transform parent, float z0, float y0, float z1, float y1, float width, Color c)
        {
            float len = Vector2.Distance(new Vector2(z0, y0), new Vector2(z1, y1));
            float ang = -Mathf.Atan2(y1 - y0, z1 - z0) * Mathf.Rad2Deg;
            Box(parent, new Vector3(0f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(width, 0.05f, len), new Vector3(ang, 0f, 0f), c, true);
        }

        void BuildAFrame(Transform root, Vector3 s)
        {
            Ramp(root, -s.z * 0.5f, 0f, 0f, s.y, s.x, Red);
            Ramp(root, 0f, s.y, s.z * 0.5f, 0f, s.x, Red);
        }

        void BuildDogWalk(Transform root, Vector3 s)
        {
            float third = s.z / 3f;
            Ramp(root, -s.z * 0.5f, 0f, -s.z * 0.5f + third, s.y, s.x, Red);
            Box(root, new Vector3(0f, s.y, 0f), new Vector3(s.x, 0.05f, third), Vector3.zero, Yellow, true);
            Ramp(root, s.z * 0.5f - third, s.y, s.z * 0.5f, 0f, s.x, Red);
            Box(root, new Vector3(0f, s.y * 0.5f, -third * 0.5f), new Vector3(0.06f, s.y, 0.06f), Vector3.zero, Blue, false);
            Box(root, new Vector3(0f, s.y * 0.5f, third * 0.5f), new Vector3(0.06f, s.y, 0.06f), Vector3.zero, Blue, false);
        }

        void BuildTunnel(Transform root, Vector3 s)
        {
            int rings = 10;
            for (int i = 0; i < rings; i++)
            {
                float z = Mathf.Lerp(-s.z * 0.5f, s.z * 0.5f, i / (float)(rings - 1));
                Box(root, new Vector3(-s.x * 0.5f, s.y * 0.5f, z), new Vector3(0.05f, s.y, s.z / rings), Vector3.zero, i % 2 == 0 ? Red : White, false);
                Box(root, new Vector3(s.x * 0.5f, s.y * 0.5f, z), new Vector3(0.05f, s.y, s.z / rings), Vector3.zero, i % 2 == 0 ? Red : White, false);
                Box(root, new Vector3(0f, s.y, z), new Vector3(s.x, 0.05f, s.z / rings), Vector3.zero, i % 2 == 0 ? Red : White, false);
            }
        }

        void BuildWeave(Obstacle o, Transform root, Vector3 s)
        {
            for (int i = 0; i < PoleCount; i++)
            {
                // 봉마다 바닥에 축을 둔다 — 닿으면 그 축으로 흔들린다
                var holder = new GameObject("Pole_" + i).transform;
                holder.SetParent(root, false);
                holder.localPosition = new Vector3(0f, 0f, PoleZ(s, i));
                if (weavePoleModel == null || !Fit(weavePoleModel, holder, new Vector3(0.12f, s.y, 0.12f)))
                    Box(holder, new Vector3(0f, s.y * 0.5f, 0f), new Vector3(0.05f, s.y, 0.05f), Vector3.zero, i % 2 == 0 ? Blue : Yellow, false);
                o.poles.Add(holder);
            }
            o.wobble = new float[PoleCount];
            o.touching = new bool[PoleCount];
        }

        public static float PoleZ(Vector3 size, int i) => -size.z * 0.5f + PoleGap * 0.5f + i * PoleGap;

        void BuildSeesaw(Transform root, Transform pivot, Vector3 s)
        {
            Box(root, new Vector3(0f, s.y * 0.5f, 0f), new Vector3(0.3f, s.y, 0.3f), Vector3.zero, Blue, false);
            Box(pivot, new Vector3(0f, 0.03f, 0f), new Vector3(s.x, 0.05f, s.z), Vector3.zero, Red, true);
        }

        void BuildTable(Transform root, Vector3 s)
        {
            Box(root, new Vector3(0f, s.y * 0.5f, 0f), new Vector3(s.x, s.y, s.z), Vector3.zero, Blue, true);
        }

        public const int StepCount = 3;
        const float StepDepth = 0.26f;
        static readonly Color Wood = new Color(0.78f, 0.58f, 0.36f);

        /// <summary>테이블 입구 쪽(로컬 -z) 계단. 높이가 조금씩 올라 표면을 따라 걸어 올라간다.</summary>
        void BuildSteps(Transform root, Vector3 s)
        {
            for (int i = 0; i < StepCount; i++)
            {
                float h = s.y * (i + 1) / (StepCount + 1);
                float z = -s.z * 0.5f - StepDepth * (StepCount - i - 0.5f);
                Box(root, new Vector3(0f, h * 0.5f, z), new Vector3(s.x * 0.8f, h, StepDepth), Vector3.zero, i % 2 == 0 ? Wood : Yellow, true);
            }
        }

        // ---- 판정 ----

        /// <summary>
        /// 발 아래 장애물 표면 높이(월드). 없으면 무대 바닥. 강아지 자기 콜라이더는 무시하려고
        /// 장애물 표면만 골라 가장 높은 것을 쓴다.
        /// </summary>
        public float SurfaceY(Vector3 world, float floorY, out Vector3 normal)
        {
            normal = Vector3.up;
            float best = floorY;
            Physics.SyncTransforms();   // 시소 판이 이번 프레임에 기울었을 수 있다
            var ray = new Ray(new Vector3(world.x, floorY + 3f, world.z), Vector3.down);
            foreach (RaycastHit h in Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!surfaces.Contains(h.collider)) continue;
                if (h.point.y > best) { best = h.point.y; normal = h.normal; }
            }
            return best;
        }

        public Vector3 Local(Obstacle o, Vector3 world) => LocalStage(o, stage.InverseTransformPoint(world));

        /// <summary>무대 기준 점 → 장애물 로컬(+z 가 진행 방향).</summary>
        static Vector3 LocalStage(Obstacle o, Vector3 stagePoint) =>
            Quaternion.Inverse(Quaternion.Euler(0f, o.yaw, 0f)) * (stagePoint - o.center);

        /// <summary>장애물 입구(무대 밖 월드 좌표). 화면에 "다음" 표시를 띄우는 자리.</summary>
        public Vector3 EntryWorld(Obstacle o)
        {
            Vector3 local = new Vector3(0f, 0f, -o.size.z * 0.5f - (o.kind == Kind.Table ? StepCount * StepDepth + 0.2f : 0.3f));
            return stage.TransformPoint(o.center + Quaternion.Euler(0f, o.yaw, 0f) * local);
        }

        /// <summary>
        /// 한 프레임 판정. 끝난(통과든 감점이든) 장애물이 생기면 그 장애물을 돌려준다.
        /// 다음 장애물에 닿으면 시작되고, 다시 하기는 없다 — 떨어지거나 옆으로 벗어나면 감점으로 끝나고 다음으로 넘어간다.
        /// 순서를 건너뛰고 뒤 장애물에 먼저 닿으면 앞의 것들은 건너뛴 걸로 감점한다.
        /// <paramref name="warn"/> 은 감점 없는 안내, <paramref name="penalty"/> 는 감점 알림이다.
        /// </summary>
        public Obstacle Tick(Vector3 dogWorld, float dogY, float floorY, float dt, bool still, out string warn, out string penalty)
        {
            warn = null;
            penalty = null;
            foreach (Obstacle s in Obstacles)
            {
                if (s.kind == Kind.Seesaw) TickSeesaw(s, dogWorld, dogY, floorY, still, dt);
                if (s.kind == Kind.Weave)
                {
                    // 봉은 언제 닿아도 흔들린다. 감점은 위브를 하는 중일 때만
                    TouchPoles(s, dogWorld, Active == s, ref penalty);
                    TickWobble(s, dt);
                }
            }
            if (Finished) return null;

            Obstacle o = Obstacles[Next];
            if (!o.entered)
            {
                for (int j = Next + 1; j < Obstacles.Count; j++)
                {
                    if (!Touching(Obstacles[j], Local(Obstacles[j], dogWorld))) continue;
                    int n = j - Next;
                    while (Next < j)
                    {
                        Obstacle k = Obstacles[Next];
                        Skips++;
                        PenaltyPoints += SkipPenalty;
                        k.penalty += SkipPenalty;
                        k.failed = true;
                        Complete(k, 0f);
                    }
                    penalty = (n == 1 ? o.nameKo : o.nameKo + " 외 " + (n - 1) + "개") + " 건너뜀 -" + n * SkipPenalty;
                    o = Obstacles[Next];
                    break;
                }
            }

            Vector3 lp = Local(o, dogWorld);
            float halfL = o.size.z * 0.5f;
            bool inside = Touching(o, lp);
            if (!o.entered)
            {
                if (!inside) return null;
                o.entered = true;   // 닿았다 — 시작
                o.maxZ = lp.z;
                o.lastY = dogY;
                o.lastZ = lp.z;
                o.lastSide = 0;
                o.alternations = 0;
                o.stay = 0f;
            }
            o.maxZ = Mathf.Max(o.maxZ, lp.z);

            switch (o.kind)
            {
                case Kind.Table:
                {
                    float top = stage.TransformPoint(o.center + Vector3.up * o.size.y).y;
                    bool on = Mathf.Abs(lp.x) < o.size.x * 0.5f && Mathf.Abs(lp.z) < halfL && dogY > top - 0.08f;
                    o.stay = on && still ? o.stay + dt : 0f;
                    if (o.stay >= TableHold) return Complete(o, 1f);
                    if (!inside)
                    {
                        Falls++;
                        return Fail(o, FallPenalty, "테이블에서 내려왔다 -" + FallPenalty, ref penalty);
                    }
                    break;
                }
                case Kind.Weave:
                {
                    // 끝으로 나오든 옆으로 빠지든 위브는 거기서 끝 — 못 한 칸은 빼먹은 걸로
                    if (!inside) return FinishWeave(o, ref penalty);
                    // 봉을 지날 때마다 강아지가 봉의 어느 쪽에 있었나 — 번갈아 바뀌면 지그재그
                    for (int i = 0; i < PoleCount; i++)
                    {
                        float pz = PoleZ(o.size, i);
                        if (o.lastZ < pz && lp.z >= pz)
                        {
                            int side = lp.x >= 0f ? 1 : -1;
                            if (o.lastSide != 0 && side == -o.lastSide) o.alternations++;
                            o.lastSide = side;
                        }
                    }
                    o.lastZ = lp.z;
                    break;
                }
                default:
                {
                    bool climbs = o.kind != Kind.Tunnel;
                    bool wasHigh = o.lastY > floorY + 0.12f;
                    o.lastY = dogY;
                    // 올라가는 장애물 한가운데서 바닥까지 내려왔다 = 떨어졌다
                    if (climbs && wasHigh && dogY < floorY + 0.05f && Mathf.Abs(lp.z) < halfL - 0.4f)
                    {
                        Falls++;
                        return Fail(o, FallPenalty, o.nameKo + "에서 떨어졌다 -" + FallPenalty, ref penalty);
                    }
                    if (inside) break;
                    if (lp.z >= halfL)
                    {
                        // 시소는 끝에서 기다려 판이 내려간 뒤에 내려와야 한다
                        if (o.kind == Kind.Seesaw && !o.tipped)
                        {
                            Falls++;
                            return Fail(o, FallPenalty, "시소가 내려가기 전에 뛰어내렸다 -" + FallPenalty, ref penalty);
                        }
                        return Complete(o, 1f);
                    }
                    // 끝까지 안 가고 옆으로 빠져나갔다
                    Falls++;
                    return Fail(o, FallPenalty, o.nameKo + (wasHigh ? "에서 떨어졌다 -" : "에서 벗어났다 -") + FallPenalty, ref penalty);
                }
            }
            return null;
        }

        /// <summary>봉에 몸이 닿으면 흔들고, 위브 중이면 감점 — 한 번 닿을 때마다 한 번(붙어 있는 동안은 더 세지 않는다).</summary>
        void TouchPoles(Obstacle o, Vector3 dogWorld, bool counts, ref string penalty)
        {
            if (o.done || o.touching == null) return;
            Vector3 lp = Local(o, dogWorld);
            for (int i = 0; i < PoleCount; i++)
            {
                float dx = lp.x, dz = lp.z - PoleZ(o.size, i);
                bool touch = dx * dx + dz * dz < TouchRadius * TouchRadius;
                if (touch && !o.touching[i])
                {
                    o.wobble[i] = 1f;
                    if (counts)
                    {
                        Touches++;
                        int pts = Mathf.Min(TouchPenalty, Mathf.Max(0, SkipPenalty - o.penalty));
                        PenaltyPoints += pts;
                        o.penalty += pts;
                        penalty = "봉에 닿았다" + (pts > 0 ? " -" + pts : "");
                    }
                }
                o.touching[i] = touch;
            }
        }

        /// <summary>위브 끝. 빼먹은 칸만큼 감점하되 봉 감점과 합쳐 건너뛴 것(-SkipPenalty)보다 크게 깎지는 않는다.</summary>
        Obstacle FinishWeave(Obstacle o, ref string penalty)
        {
            int misses = PoleCount - 1 - o.alternations;
            if (misses <= 0) return Complete(o, 1f);
            WeaveMisses += misses;
            int pts = Mathf.Min(misses * WeaveMissPenalty, Mathf.Max(0, SkipPenalty - o.penalty));
            return Fail(o, pts, "지그재그 " + misses + "칸 빼먹음" + (pts > 0 ? " -" + pts : ""), ref penalty, misses < PoleCount - 1);
        }

        /// <summary>감점하고 그 장애물을 끝낸다. <paramref name="partial"/> 이면 일부는 해냈으니 실패 표시는 하지 않는다.</summary>
        Obstacle Fail(Obstacle o, int points, string msg, ref string penalty, bool partial = false)
        {
            PenaltyPoints += points;
            o.penalty += points;
            penalty = msg;
            o.failed = !partial;
            return Complete(o, partial ? 0.5f : 0f);
        }

        /// <summary>시소 끝에서 기다린 비율(0~1). 화면에 "기다리기" 막대를 그릴 때 쓴다.</summary>
        public float SeesawProgress(Obstacle o) => o.tipped ? 1f : Mathf.Clamp01(o.endWait / SeesawWait);

        void TickWobble(Obstacle o, float dt)
        {
            if (o.wobble == null) return;
            for (int i = 0; i < o.poles.Count; i++)
            {
                o.wobble[i] = Mathf.MoveTowards(o.wobble[i], 0f, dt * 1.6f);
                o.poles[i].localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 26f + i) * 14f * o.wobble[i], 0f, 0f);
            }
        }

        Obstacle Complete(Obstacle o, float score)
        {
            o.done = true;
            o.score = score;
            Next++;
            return o;
        }

        void TickSeesaw(Obstacle o, Vector3 dogWorld, float dogY, float floorY, bool still, float dt)
        {
            if (o.tilt == null) return;
            Vector3 lp = Local(o, dogWorld);
            float halfL = o.size.z * 0.5f;
            bool onBoard = Mathf.Abs(lp.x) < o.size.x * 0.5f + 0.1f && Mathf.Abs(lp.z) < halfL && dogY > floorY + 0.05f;
            // 처음엔 입구 쪽 끝이 땅에 닿아 있다. 끝(들린 쪽)까지 가서 잠깐 멈춰 있어야 반대로 넘어간다
            if (!o.tipped)
            {
                bool atEnd = onBoard && lp.z > halfL - 0.8f;
                o.endWait = atEnd && still ? o.endWait + dt : (atEnd ? o.endWait : 0f);
                if (o.endWait >= SeesawWait) o.tipped = true;
            }
            float max = SeesawMax(o.size);
            float want = o.tipped ? max : -max;
            o.seesawAngle = Mathf.MoveTowards(o.seesawAngle, want, 45f * dt);
            o.tilt.localRotation = Quaternion.Euler(o.seesawAngle, 0f, 0f);
        }

        void OnDestroy() => Clear();
    }
}
