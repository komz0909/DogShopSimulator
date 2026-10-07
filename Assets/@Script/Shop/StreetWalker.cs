using System;
using System.Collections.Generic;
using DogShop.Core;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 거리에서 정해진 점들을 따라 걷는다. NavMesh 는 가게·앞마당에만 구워져 있어서
    /// 인도·차도 위는 길찾기 없이 점을 이어 걷는다(거리에는 피할 것도 없다).
    ///
    /// 손님은 이걸로 인도에서 문 앞까지 걸어 들어오고(도착하면 <see cref="CustomerManager"/>가
    /// NavMeshAgent 로 넘겨받는다), 나갈 때는 문에서 인도로 걸어 나가 거리 끝에서 사라진다.
    /// 지나가는 행인(<see cref="StreetLife"/>)도 같은 걸 쓴다.
    ///
    /// 걷는 애니메이션은 <see cref="Player.CharacterAnimatorDriver"/>가 위치 변화에서 알아서 붙인다.
    /// </summary>
    public class StreetWalker : MonoBehaviour
    {
        /// <summary>배속을 따라가는 상한 — 손님(NavMeshAgent)과 같다.</summary>
        const int MaxSpeedMultiplier = 5;
        const float TurnSpeed = 540f;

        readonly List<Vector3> path = new List<Vector3>();
        int next;
        float speed = 1.4f;
        Action done;
        bool destroyAtEnd;

        public bool Walking => next < path.Count;

        /// <summary>점들을 따라 걷는다. 끝나면 <paramref name="onDone"/>을 부르거나(있으면) 스스로 사라진다.</summary>
        public void Walk(IList<Vector3> points, float metersPerSecond, Action onDone, bool destroyWhenDone)
        {
            path.Clear();
            path.AddRange(points);
            next = 0;
            speed = metersPerSecond;
            done = onDone;
            destroyAtEnd = destroyWhenDone;
            enabled = true;
        }

        void Update()
        {
            if (next >= path.Count) return;

            int mult = TimeManager.Instance != null ? Mathf.Clamp(TimeManager.Instance.SpeedMultiplier, 1, MaxSpeedMultiplier) : 1;
            float step = speed * mult * Time.deltaTime;

            Vector3 p = transform.position;
            Vector3 target = path[next];
            Vector3 to = target - p;
            to.y = 0f;

            if (to.magnitude <= step)
            {
                transform.position = new Vector3(target.x, target.y, target.z);
                next++;
                if (next >= path.Count) Finish();
                return;
            }

            Vector3 dir = to.normalized;
            transform.position = p + dir * step + Vector3.up * (target.y - p.y) * Mathf.Clamp01(step / to.magnitude);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), TurnSpeed * Time.deltaTime);
        }

        void Finish()
        {
            enabled = false;
            Action callback = done;
            done = null;
            callback?.Invoke();
            if (destroyAtEnd) Destroy(gameObject);
        }
    }
}
