using System.Collections.Generic;
using DogShop.Core;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 거리를 오가는 행인. 손님과 같은 겉모습으로 양쪽 인도를 걸어 지나간다 —
    /// 가게 손님은 이 사람들 사이에서 인도를 걸어와 들어오므로(<see cref="CustomerManager"/>),
    /// "지나가던 사람이 들어온다"처럼 보인다.
    ///
    /// 장식이다. 길찾기·충돌·명성 어디에도 끼지 않는다. 밤엔 줄어든다.
    /// </summary>
    public class StreetLife : MonoBehaviour
    {
        [SerializeField] int dayCount = 10;
        [SerializeField] int nightCount = 3;

        /// <summary>거리 양 끝(x). 이 밖은 안개 속이라 여기서 나타나고 사라진다.</summary>
        const float WestX = -40f;
        const float EastX = 49f;

        /// <summary>행인 줄(z). 가게 쪽 인도 두 줄(손님 줄보다 차도 쪽), 건너편 인도 두 줄.</summary>
        static readonly float[] Lanes = { -6.7f, -7.3f, -18.0f, -18.7f };

        readonly List<StreetWalker> walkers = new List<StreetWalker>();
        float spawnTimer;

        void Start()
        {
            // 처음부터 거리가 비어 있지 않게 중간중간에 미리 세워 둔다
            for (int i = 0; i < Wanted(); i++) Spawn(true);
        }

        int Wanted()
        {
            float hour = TimeManager.Instance != null ? TimeManager.Instance.CurrentHour : 12f;
            return hour >= 20.5f ? nightCount : dayCount;
        }

        void Update()
        {
            walkers.RemoveAll(w => w == null);
            if (walkers.Count >= Wanted()) return;

            spawnTimer -= Time.deltaTime;
            if (spawnTimer > 0f) return;
            spawnTimer = Random.Range(0.8f, 2.5f);
            Spawn(false);
        }

        void Spawn(bool anywhere)
        {
            CustomerManager cm = CustomerManager.Instance;
            if (cm == null || cm.AppearanceCount == 0) return;

            int lane = Random.Range(0, Lanes.Length);
            bool east = (lane % 2) == 0;   // 줄마다 방향을 정해 마주 오는 사람끼리 겹치지 않게
            float z = Lanes[lane] + Random.Range(-0.12f, 0.12f);
            float fromX = east ? WestX : EastX;
            float toX = east ? EastX : WestX;
            float startX = anywhere ? Random.Range(WestX + 5f, EastX - 5f) : fromX;

            var go = new GameObject("Pedestrian");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(startX, 0f, z);
            go.transform.rotation = Quaternion.LookRotation(east ? Vector3.right : Vector3.left);

            GameObject model = Instantiate(cm.AppearanceAt(Random.Range(0, cm.AppearanceCount)), go.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            go.AddComponent<Player.CharacterAnimatorDriver>();

            StreetWalker walker = go.AddComponent<StreetWalker>();
            walker.Walk(new[] { new Vector3(toX, 0f, z) }, Random.Range(1.1f, 1.6f), null, true);
            walkers.Add(walker);
        }
    }
}
