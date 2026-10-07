using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    /// <summary>
    /// 매장 공간을 다시 굽는 곳. <b>NavMesh를 건드리는 유일한 자리다.</b>
    ///
    /// 가구는 <see cref="NavMeshObstacle"/> 카빙으로 실시간에 길을 막으므로 다시 구울 필요가 없다.
    /// 다시 구워야 하는 것은 <b>구울 때만 반영되는 것</b> 둘뿐이다 —
    /// 바닥·벽을 옮기거나 키웠을 때, 그리고 <see cref="NavMeshModifierVolume"/>(직원 구역)이
    /// 움직였을 때. 계산대를 옮기면 그 뒤 직원 주머니가 따라 움직이므로 후자에 해당한다.
    ///
    /// 굽는 동안 손님·반려견이 타고 있던 길은 끊긴다. 그래서 굽기 전에 목적지를 적어 두고,
    /// 굽고 나서 가장 가까운 면으로 옮긴 뒤 같은 목적지를 다시 준다 —
    /// 안 그러면 "NavMesh 위에 있지 않다" 경고가 쏟아지고 손님이 그 자리에 굳는다.
    /// </summary>
    public class ShopSpace : MonoBehaviour
    {
        public static ShopSpace Instance { get; private set; }

        [SerializeField] NavMeshSurface surface;

        [Header("판매장 — 가운데(문)에서 양쪽으로 늘어나는 부분")]
        [SerializeField] Transform floor;        // Floor
        [SerializeField] Transform rightWall;    // Wall_Right
        [SerializeField] Transform frontRight;   // Wall_FrontB — 출입문 오른쪽 앞벽
        [SerializeField] Transform leftWall;     // Wall_Left
        [SerializeField] Transform frontLeft;    // Wall_FrontA — 출입문 왼쪽 앞벽
        [SerializeField] Transform backLeft;     // Wall_Back_L — 창고 문 왼쪽 뒷벽(왼쪽으로 늘어난 만큼 따라 늘어난다)

        [Header("앞마당 — 가게 폭을 따라 넓어진다")]
        [SerializeField] Transform forecourtFloor;   // Forecourt_Floor
        [SerializeField] Transform fenceLeft;        // Forecourt_Fence_L
        [SerializeField] Transform fenceRight;       // Forecourt_Fence_R
        [SerializeField] Transform edgeWest;         // 거리 경계벽(앞마당 왼쪽 인도 경계)
        [SerializeField] Transform edgeEast;         // 거리 경계벽(앞마당 오른쪽 인도 경계)

        /// <summary>
        /// 매장 천장과 지붕. 바닥만 늘이면 넓어진 쪽이 뚜껑 없이 뚫린다.
        /// 비워 두어도 동작한다 — 지붕이 없는 씬에서도 확장은 그대로 된다.
        /// </summary>
        [SerializeField] Transform ceiling;      // Ceiling_Shop
        [SerializeField] Transform roof;         // Gable_Shop

        /// <summary>
        /// 매장 천장 조명. 최대 폭(12m)까지 미리 깔아 두고, <b>지금 폭 바깥의 것은 끈다</b> —
        /// 안 끄면 아직 없는 확장 구역 자리, 즉 가게 밖 허공에서 불이 켜진다.
        /// </summary>
        [SerializeField] Transform shopLights;

        /// <summary>판매장 세로(z). 늘리지 않는다 — 앞은 앞마당, 뒤는 창고라 막혀 있다.</summary>
        const float Depth = 6f;

        /// <summary>벽 두께. 씬의 모든 벽이 같다.</summary>
        const float WallThickness = 0.2f;

        /// <summary>출입문 오른쪽 기둥이 시작하는 x. 문틈(x 3~5)은 넓혀도 그대로 둔다.</summary>
        const float DoorRightEdge = 5f;
        const float DoorLeftEdge = 3f;

        /// <summary>지붕이 벽 밖으로 나오는 처마 길이. 지붕을 세울 때 쓴 값과 같아야 한다.</summary>
        const float RoofOverhang = 0.35f;

        /// <summary>지금 판매장 가로 길이. 바닥에서 직접 읽는다 — 저장할 필요가 없다.</summary>
        public float Width => floor != null ? floor.localScale.x : 8f;

        /// <summary>
        /// 판매장 가운데 x. <b>출입문(x 3~5) 한가운데</b>다 — 가게는 여기를 기준으로 양쪽으로 넓어진다
        /// (8m: x 0~8 → 9.5m: -0.75~8.75 → 12m: -2~10). 그래서 문·앞마당·배송 자리는 언제나 가게 정중앙이다.
        /// </summary>
        public const float CenterX = 4f;

        public float LeftX => CenterX - Width * 0.5f;
        public float RightX => CenterX + Width * 0.5f;

        /// <summary>판매장 바닥 범위(x LeftX~RightX, z 0~6). 가구를 놓을 자리를 찾을 때 쓴다.</summary>
        public Rect FloorRect => new Rect(LeftX, 0f, Width, Depth);

        /// <summary>다시 굽는 동안 잃어버리지 않으려고 적어 두는 것.</summary>
        struct Traveler
        {
            public NavMeshAgent agent;
            public Vector3 destination;
            public bool hadPath;
        }

        readonly List<Traveler> travelers = new List<Traveler>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (surface == null) surface = FindFirstObjectByType<NavMeshSurface>();
        }

        void Start() => ToggleLights(LeftX, RightX);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 앞마당을 가게 정면 폭에 맞춘다. 문·배송 자리는 가운데 그대로고 양옆 울타리만 벌어진다.
        /// 거리 경계벽(투명)도 울타리 끝에 맞춰 줄여서, 앞마당 옆으로 빠지는 틈이 생기지 않게 한다.
        /// </summary>
        void FitForecourt(float left, float right)
        {
            if (forecourtFloor != null)
            {
                forecourtFloor.localScale = new Vector3(right - left, forecourtFloor.localScale.y, forecourtFloor.localScale.z);
                forecourtFloor.position = new Vector3(CenterX, forecourtFloor.position.y, forecourtFloor.position.z);
            }
            if (fenceLeft != null) fenceLeft.position = new Vector3(left - 0.1f, fenceLeft.position.y, fenceLeft.position.z);
            if (fenceRight != null) fenceRight.position = new Vector3(right + 0.1f, fenceRight.position.y, fenceRight.position.z);

            if (edgeWest != null)
            {
                float x0 = edgeWest.position.x - edgeWest.localScale.x * 0.5f, x1 = left - 0.2f;
                edgeWest.localScale = new Vector3(x1 - x0, edgeWest.localScale.y, edgeWest.localScale.z);
                edgeWest.position = new Vector3((x0 + x1) * 0.5f, edgeWest.position.y, edgeWest.position.z);
            }
            if (edgeEast != null)
            {
                float x1 = edgeEast.position.x + edgeEast.localScale.x * 0.5f, x0 = right + 0.2f;
                edgeEast.localScale = new Vector3(x1 - x0, edgeEast.localScale.y, edgeEast.localScale.z);
                edgeEast.position = new Vector3((x0 + x1) * 0.5f, edgeEast.position.y, edgeEast.position.z);
            }
        }

        /// <summary>매장 안에 드는 조명만 켠다. 조명 반경이 벽을 넘어도 되지만, 조명 자체가 벽 밖이면 끈다.</summary>
        void ToggleLights(float left, float right)
        {
            if (shopLights == null) return;
            foreach (Transform lamp in shopLights)
                lamp.gameObject.SetActive(lamp.position.x > left && lamp.position.x < right);
        }

        /// <summary>
        /// 그 레벨의 넓이로 판매장을 맞춘다. 늘어난 만큼 바닥이 커지고
        /// 오른쪽 벽과 앞벽 오른쪽 기둥이 따라 밀려난다.
        ///
        /// 뒤쪽(z=6.1)은 손댈 필요가 없다 — 옆방 앞벽(Room_Wall_Front, x 8~12.4)이
        /// 이미 그 자리를 막고 있어서 매장 뒷벽 노릇을 그대로 해 준다.
        ///
        /// 줄이지는 않는다. 가구를 이미 놓아 둔 자리가 벽 바깥으로 밀려나면
        /// 되돌릴 방법이 없다 — 가게는 커지기만 한다.
        /// </summary>
        public bool ApplyWidth(float width)
        {
            if (floor == null) return false;

            width = Mathf.Clamp(width, 8f, 12f);
            if (width <= Width + 0.01f) return false;   // 이미 그만큼 넓다

            // 문(x 4)을 가운데 두고 양쪽으로 넓힌다
            float left = CenterX - width * 0.5f;
            float right = CenterX + width * 0.5f;

            floor.localScale = new Vector3(width, floor.localScale.y, Depth);
            floor.position = new Vector3(CenterX, floor.position.y, Depth * 0.5f);

            if (rightWall != null)
                rightWall.position = new Vector3(right + WallThickness * 0.5f, rightWall.position.y, rightWall.position.z);
            if (leftWall != null)
                leftWall.position = new Vector3(left - WallThickness * 0.5f, leftWall.position.y, leftWall.position.z);

            if (frontRight != null)
            {
                float span = right - DoorRightEdge;
                frontRight.localScale = new Vector3(span, frontRight.localScale.y, WallThickness);
                frontRight.position = new Vector3(DoorRightEdge + span * 0.5f, frontRight.position.y, frontRight.position.z);
            }
            if (frontLeft != null)
            {
                float span = DoorLeftEdge - left;
                frontLeft.localScale = new Vector3(span, frontLeft.localScale.y, WallThickness);
                frontLeft.position = new Vector3(left + span * 0.5f, frontLeft.position.y, frontLeft.position.z);
            }

            // 뒷벽 왼쪽도 왼쪽 끝까지 따라 늘인다. 오른쪽은 옆방 앞벽(x 8~12.4)이 이미 막고 있다
            if (backLeft != null)
            {
                float edge = backLeft.position.x + backLeft.localScale.x * 0.5f;   // 창고 문 쪽 끝(x 3.2)은 그대로
                float span = edge - left;
                backLeft.localScale = new Vector3(span, backLeft.localScale.y, backLeft.localScale.z);
                backLeft.position = new Vector3(left + span * 0.5f, backLeft.position.y, backLeft.position.z);
            }

            ToggleLights(left, right);
            FitForecourt(left, right);

            // 뚜껑도 같이 늘인다. 바닥만 넓히면 늘어난 쪽이 하늘로 뚫려 보인다
            if (ceiling != null)
            {
                ceiling.localScale = new Vector3(width, ceiling.localScale.y, ceiling.localScale.z);
                ceiling.position = new Vector3(CenterX, ceiling.position.y, ceiling.position.z);
            }

            // 맞배지붕은 처마만큼 더 길다. 비율을 그대로 유지한 채 폭만 따라간다
            if (roof != null)
            {
                foreach (Transform slope in roof)
                {
                    slope.localScale = new Vector3(width + RoofOverhang * 2f, slope.localScale.y, slope.localScale.z);
                    slope.localPosition = new Vector3(CenterX, slope.localPosition.y, slope.localPosition.z);
                }
            }

            Rebake();
            return true;
        }

        /// <summary>
        /// 바닥·벽·직원 구역이 바뀐 뒤 부른다. 동기로 굽는다 —
        /// 이 게임의 매장은 8×6에서 12×6 남짓이라 한 프레임 안에 끝나고,
        /// 비동기로 굽는 동안 손님이 낡은 면 위를 걷는 편이 더 나쁘다.
        /// </summary>
        public void Rebake()
        {
            if (surface == null) return;

            Remember();
            surface.BuildNavMesh();
            Restore();
        }

        void Remember()
        {
            travelers.Clear();

            foreach (NavMeshAgent agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                if (agent == null || !agent.isActiveAndEnabled) continue;

                travelers.Add(new Traveler
                {
                    agent = agent,
                    // hasPath 가 아니라 목적지를 적는다. 굽고 나면 경로는 어차피 버려진다
                    destination = agent.hasPath || agent.pathPending ? agent.destination : agent.transform.position,
                    hadPath = agent.hasPath || agent.pathPending,
                });
            }
        }

        void Restore()
        {
            for (int i = 0; i < travelers.Count; i++)
            {
                NavMeshAgent agent = travelers[i].agent;
                if (agent == null || !agent.isActiveAndEnabled) continue;

                // 서 있던 자리가 새 면 밖일 수 있다(벽이 밀려나며 바닥이 바뀐 자리).
                // 자기 영역 안에서 가장 가까운 면으로 끌어다 놓는다
                NavMeshHit hit;
                if (NavMesh.SamplePosition(agent.transform.position, out hit, 4f, agent.areaMask))
                    agent.Warp(hit.position);

                if (!travelers[i].hadPath || !agent.isOnNavMesh) continue;

                if (NavMesh.SamplePosition(travelers[i].destination, out hit, 4f, agent.areaMask))
                    agent.SetDestination(hit.position);
            }

            travelers.Clear();
        }
    }
}
