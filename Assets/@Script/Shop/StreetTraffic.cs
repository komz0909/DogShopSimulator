using System.Collections.Generic;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 가게 앞 차도를 지나다니는 차. 많이 띄우지 않는다 — 양쪽 차선을 합쳐 몇 대가
    /// 길을 따라 한쪽 끝에서 반대쪽 끝까지 달리고, 끝을 지나면 처음 자리에서 다시 나온다.
    ///
    /// 예전엔 차 아홉 대가 차도에 서 있기만 했다. 가게 쪽 차선은 +x, 건너편 차선은 -x 로 달린다
    /// (원래 세워 둔 차들이 그 방향을 보고 있었다). 차 모델은 여러 개를 넣어 두면 나올 때마다 하나를 고른다.
    ///
    /// 시간 배속은 따르지 않는다 — x4 에서 차가 시속 100km 로 지나가면 이상하다. 설정 창(timeScale 0)에선 멈춘다.
    /// 주인은 차도에 못 들어간다(가게 쪽 연석의 투명 벽), 강아지는 NavMesh 가 앞마당에만 있어서 원래 못 간다.
    /// </summary>
    public class StreetTraffic : MonoBehaviour
    {
        [Tooltip("지나다닐 차 모델들. 나올 때마다 하나를 고른다")]
        [SerializeField] GameObject[] carPrefabs = new GameObject[0];

        [Tooltip("차선마다 몇 대")]
        [SerializeField] int carsPerLane = 2;

        [SerializeField] float carScale = 0.85f;

        /// <summary>차선(z)과 달리는 방향. 원래 세워 둔 차 자리 그대로다.</summary>
        static readonly (float z, float dir)[] Lanes = { (-9.8f, 1f), (-15.2f, -1f) };

        /// <summary>차가 나타나고 사라지는 x — 거리 끝 건물 뒤.</summary>
        const float WestX = -48f, EastX = 58f;

        const float MinSpeed = 4.5f, MaxSpeed = 7f;   // m/s(시속 16~25km, 동네 골목)
        /// <summary>앞차와 이만큼보다 가까우면 앞차 속도에 맞춘다.</summary>
        const float FollowGap = 7f;

        class Car
        {
            public Transform body;
            public int lane;
            public float speed;
        }

        readonly List<Car> cars = new List<Car>();

        void Start()
        {
            if (carPrefabs.Length == 0) return;
            float length = EastX - WestX;
            for (int lane = 0; lane < Lanes.Length; lane++)
            {
                for (int i = 0; i < carsPerLane; i++)
                {
                    var car = new Car { lane = lane };
                    // 처음엔 길 위에 고르게 흩어 둔다 — 열자마자 차가 한꺼번에 끝에서 나오지 않게
                    float x = WestX + length * (i + Random.Range(0.15f, 0.85f)) / carsPerLane;
                    Spawn(car, x);
                    cars.Add(car);
                }
            }
        }

        void Spawn(Car car, float x)
        {
            if (car.body != null) Destroy(car.body.gameObject);
            GameObject prefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
            GameObject go = Instantiate(prefab, transform);
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);   // 차도엔 아무도 못 들어온다
            go.transform.localScale = Vector3.one * carScale;
            var (z, dir) = Lanes[car.lane];
            go.transform.position = new Vector3(x, 0f, z);
            go.transform.rotation = Quaternion.Euler(0f, dir > 0f ? 90f : 270f, 0f);
            car.body = go.transform;
            car.speed = Random.Range(MinSpeed, MaxSpeed);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            foreach (Car car in cars)
            {
                if (car.body == null) continue;
                float dir = Lanes[car.lane].dir;
                float x = car.body.position.x;

                // 같은 차선에서 바로 앞차를 찾아 너무 붙으면 속도를 맞춘다
                float speed = car.speed;
                foreach (Car other in cars)
                {
                    if (other == car || other.lane != car.lane || other.body == null) continue;
                    float ahead = (other.body.position.x - x) * dir;
                    if (ahead > 0f && ahead < FollowGap) speed = Mathf.Min(speed, other.speed * Mathf.Clamp01(ahead / FollowGap));
                }

                x += dir * speed * dt;
                car.body.position = new Vector3(x, 0f, Lanes[car.lane].z);

                // 끝을 지나면 반대쪽 끝에서 다시 — 모델과 속도는 새로 고른다
                if (dir > 0f && x > EastX) Spawn(car, WestX);
                else if (dir < 0f && x < WestX) Spawn(car, EastX);
            }
        }
    }
}
