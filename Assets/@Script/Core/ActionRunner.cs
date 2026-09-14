using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>액션 실행의 단일 통로. 성공·거절 피드백이 여기 한 곳에서만 발생한다.</summary>
    public static class ActionRunner
    {
        public static event Action<IPlayerAction> OnExecuted;
        public static event Action<IPlayerAction, string> OnRejected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscribers()
        {
            OnExecuted = null;
            OnRejected = null;
        }

        /// <summary>액션 없이 사유만 알린다 — 거리 부족처럼 액션을 만들 필요가 없는 경우.</summary>
        public static void Reject(string reason) => OnRejected?.Invoke(null, reason);

        public static bool TryRun(IPlayerAction action)
        {
            if (action == null) return false;

            TimeManager time = TimeManager.Instance;
            if (time != null && time.IsDayOver)
            {
                OnRejected?.Invoke(action, "영업 종료");
                return false;
            }

            string reason;
            if (!action.CanExecute(out reason))
            {
                OnRejected?.Invoke(action, reason);
                return false;
            }

            action.Execute();
            OnExecuted?.Invoke(action);
            return true;
        }
    }
}
