using UnityEngine;
using UnityEngine.AI;

namespace DogShop.Shop
{
    public enum CustomerState { ToShelf, ToCounter, Waiting, ToExit }

    /// <summary>
    /// 손님 1명. 이동만 담당하고 상태 전이는 CustomerManager가 몰아서 처리한다
    /// (상점 로직이 한 곳에 모여 있어야 밸런싱이 쉽다).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Customer : MonoBehaviour
    {
        const float ArriveRadius = 0.6f;

        NavMeshAgent agent;
        float baseSpeed;
        Vector3 destination;

        public CustomerState State { get; set; }
        public int WantedProduct { get; set; } = -1;
        public bool HasItem { get; set; }
        public float WaitRemaining { get; set; }
        public string Label { get; set; } = "";

        /// <summary>이동 상태에 머문 시간. 길이 막혔을 때 큐를 영구 점유하지 않게 하는 안전장치.</summary>
        public float TravelTime { get; set; }

        /// <summary>
        /// 에이전트의 remainingDistance는 경로 계산 전에 0을 돌려주는 구간이 있어
        /// 도착 판정에 쓰면 문 앞에서 즉시 "도착"해버린다. 실제 평면 거리로 판정한다.
        /// </summary>
        public bool Arrived
        {
            get
            {
                Vector3 delta = transform.position - destination;
                delta.y = 0f;
                return delta.sqrMagnitude <= ArriveRadius * ArriveRadius;
            }
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            baseSpeed = agent.speed;
            destination = transform.position;
        }

        public void MoveTo(Vector3 target)
        {
            destination = target;
            TravelTime = 0f;
            if (agent == null || !agent.isOnNavMesh) return;
            agent.SetDestination(target);
        }

        /// <summary>배속을 따라간다 — 16배속에서 손님도 16배로 몰려온다.</summary>
        public void ApplySpeed(int multiplier)
        {
            if (agent == null) return;
            agent.speed = baseSpeed * multiplier;
        }
    }
}
