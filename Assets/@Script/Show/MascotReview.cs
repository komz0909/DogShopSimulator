using DogShop.Core;
using DogShop.Dogs;
using UnityEngine;

namespace DogShop.Show
{
    /// <summary>
    /// 마스코트 등급 심사(2026-11-05). 본편 시즌마다 10일차, 20일차 저녁에 잠자리에 들면 심사장으로 간다.
    /// 10일차는 도그쇼 1. 미모 심사, 20일차는 2. 훈련 심사 한 종목만 — 30일차 도그쇼를 처음 해 보는 일이 없게 미리 해 본다.
    ///
    /// 점수에 따라 마스코트 등급 보너스 점수(<see cref="DogStats.MascotBonus"/>)를 준다. 능력치와 따로 등급 계산에만 더해져
    /// 가게 레벨업을 막던 등급 문턱(40, 150, 260, 400)이 조금 일찍 풀린다(측정에서 Lv3 에 6일, Lv5 에 10일 묶였다).
    /// 도그쇼 난이도는 능력치로만 정하므로 그대로다.
    ///
    /// 시연, 튜토리얼, 무인 측정에는 없다. 심사가 끝나면 하루가 끝나고 정산서에 결과가 한 줄 적힌다.
    /// </summary>
    public static class MascotReview
    {
        /// <summary>심사 날(시즌 안에서 며칠째인지). 시즌 2 는 40일차, 50일차.</summary>
        public const int FirstDay = 10, SecondDay = 20;

        /// <summary>점수(100점 만점) 가운데 보너스로 주는 비율. 20일차는 등급 문턱이 높아(150, 260) 조금 더 준다.</summary>
        public const float FirstRate = 0.3f, SecondRate = 0.4f;

        /// <summary>점수가 낮아도 받는 최소 보너스 — 심사에 나간 것만으로 조금은 오른다.</summary>
        public const int MinBonus = 5;

        /// <summary>그날의 심사 종목(0 미모 심사, 1 훈련 심사). 심사 날이 아니면 -1.</summary>
        public static int EventOn(int day)
        {
            int d = (Mathf.Max(1, day) - 1) % ChampionshipManager.FinalDay + 1;
            return d == FirstDay ? 0 : d == SecondDay ? 1 : -1;
        }

        public static float RateOf(int ev) => ev == 0 ? FirstRate : SecondRate;

        public static string EventName(int ev) => ev == 0 ? "미모 심사" : "훈련 심사";

        public static int BonusFor(int score, float rate) => Mathf.Max(MinBonus, Mathf.RoundToInt(score * rate));

        /// <summary>심사를 마친 날. 같은 날 두 번 하지 않는다. 가게 장면이 새로 열리면 0 으로(<see cref="ResetSession"/>).</summary>
        public static int DoneDay { get; private set; }

        /// <summary>정산서에 적을 한 줄.</summary>
        public static string ResultLine { get; private set; } = "";

        public static void ResetSession()
        {
            DoneDay = 0;
            ResultLine = "";
        }

        static bool Measuring => Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active;

        /// <summary>오늘 밤 심사가 남았나. 그동안 잠자리(침대, 21시 안내)는 [심사 받으러 가기]가 된다.</summary>
        public static bool Pending
        {
            get
            {
                if (Exhibition.Active || Measuring || DogShow.Running) return false;
                GameManager g = GameManager.Instance;
                TimeManager t = TimeManager.Instance;
                if (g == null || t == null || t.IsDayOver) return false;
                if (EventOn(g.Day) < 0 || DoneDay == g.Day) return false;
                if (DogManager.Instance == null || DogManager.Instance.Hero == null) return false;
                return DogShow.Instance != null;
            }
        }

        /// <summary>심사장을 연다. 못 열면 막히지 않게 그냥 하루를 마친다.</summary>
        public static bool Start()
        {
            if (!Pending) return false;
            int ev = EventOn(GameManager.Instance.Day);
            if (DogShow.Instance.BeginReview(ev, RateOf(ev))) return true;

            DoneDay = GameManager.Instance.Day;
            ActionRunner.Reject("심사장을 열 수 없어 오늘을 마친다");
            TimeManager.Instance.EndDayNow();
            return false;
        }

        public static void Record(int day, int ev, int score, int bonus)
        {
            DoneDay = day;
            ResultLine = "마스코트 등급 심사: " + EventName(ev) + " " + score + "점  /  등급 보너스 +" + bonus;
        }
    }
}
