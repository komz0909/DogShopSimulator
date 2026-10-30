using DogShop.Core;
using DogShop.Shop;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// <b>마스코트 손님 반응</b>. 영업 중 가끔 강아지가 둘러보는 손님에게 뛰어가 반응하고, 손님은 멈춰서 말한다.
    /// 그때 명성이 조금 오른다 — 강아지를 키우면 가게가 같이 크는 매일의 고리.
    ///
    /// 반응은 능력치에 따라 열린다(<see cref="Unlock"/> 이상):
    ///   미모 → 꼬리 흔들며 반짝반짝 · 훈련도 → 손님 앞에서 얌전히 앉기 · 어질리티 → 손님 둘레를 한 바퀴 뛰기.
    ///   열린 게 없으면 꼬리 흔들기. 청결·건강이 성장 정지 수준이면 오히려 손님이 걱정한다(명성 −1).
    ///
    /// 매출은 건드리지 않는다(훈련 경제 균형). 하루에 얻는 명성에 상한(<see cref="DailyCap"/>)을 두어
    /// 강아지만 키우고 가게를 놀리는 길이 생기지 않게 한다. 손님 한 명에게는 한 번만.
    /// </summary>
    public class MascotGreeter : MonoBehaviour
    {
        /// <summary>하루에 손님 반응으로 얻을 수 있는 명성.</summary>
        public const int DailyCap = 15;

        /// <summary>하루에 깎일 수 있는 명성(아픈·더러운 강아지). 돌보기를 빼먹으면 손해지만 끝없이 깎이진 않게.</summary>
        public const int DailyPenaltyCap = 5;

        /// <summary>능력치 반응이 열리는 값(만점 240 기준).</summary>
        public const int Unlock = 70;

        /// <summary>인사 사이 쉬는 시간(실시간 초). 너무 잦으면 강아지가 손님만 쫓아다닌다.</summary>
        static readonly Vector2 Cooldown = new Vector2(10f, 18f);

        /// <summary>이 거리 안의 손님에게만 간다(m).</summary>
        const float SeekRange = 6f;

        /// <summary>강아지가 닿을 때까지 손님이 기다려 주는 최대 시간(초).</summary>
        const float MaxHold = 6f;

        /// <summary>닿은 뒤 손님이 멈춰서 보는 시간(초). 한 바퀴 뛰기는 더 길다.</summary>
        const float AfterHold = 2.4f;
        const float CircleSeconds = 2.4f;

        public enum Reaction { Basic, Beauty, Training, Agility, Unwell }

        static readonly string[] BasicLines = { "귀엽다~", "안녕, 강아지야!", "꼬리 흔드는 것 좀 봐" };
        static readonly string[] BeautyLines = { "털이 반짝반짝!", "어머 예뻐라", "모델 같아!" };
        static readonly string[] TrainingLines = { "앉아도 하네!", "말 잘 듣는다~", "똑똑하다!" };
        static readonly string[] AgilityLines = { "엄청 빨라!", "우와, 날쌔다!", "신나 보여!" };

        int earnedToday, lostToday;
        float nextTryAt;

        /// <summary>떠 있는 말풍선과 명성 표시 하나.</summary>
        Customer bubbleWho;
        string bubbleText;
        int bubbleRep;
        Reaction bubbleKind;
        float bubbleAt = -10f;
        const float BubbleShow = 2.6f;

        GUIStyle bubbleStyle, repStyle;

        void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted += ResetDay;
            if (TimeManager.Instance != null) TimeManager.Instance.OnWholeHourChanged += SimulateForMeasurement;
            nextTryAt = Time.time + Random.Range(Cooldown.x, Cooldown.y);
        }

        void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayStarted -= ResetDay;
            if (TimeManager.Instance != null) TimeManager.Instance.OnWholeHourChanged -= SimulateForMeasurement;
        }

        void ResetDay() { earnedToday = 0; lostToday = 0; }

        /// <summary>
        /// 무인 측정(16배속)용. 강아지는 실제 속도로 걸어 하루(19초)에 한두 번밖에 못 닿으니,
        /// 영업 중 매 정시에 반응 한 번을 계산으로만 낸다 — 1배속 플레이에서 하루 10~15번 닿는 것과 같은 몫.
        /// </summary>
        void SimulateForMeasurement(int hour)
        {
            if (Debugging.MeasurementMode.Instance == null || !Debugging.MeasurementMode.Instance.Active) return;
            if (ShopHours.Instance == null || !ShopHours.Instance.IsOpen) return;
            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (hero == null) return;
            Reaction kind = Choose(hero.Stats);
            if (kind == Reaction.Unwell)
            {
                if (lostToday >= DailyPenaltyCap) return;
                lostToday++;
                GameManager.Instance.AddReputation(-1);
                return;
            }
            int rep = kind == Reaction.Basic ? 1 : 2;
            if (MascotGrade.GradeOf(hero.Stats.MascotTotal) >= MascotGrade.Thresholds.Length - 1) rep++;
            rep = Mathf.Min(rep, DailyCap - earnedToday);
            if (rep <= 0) return;
            earnedToday += rep;
            GameManager.Instance.AddReputation(rep);
            GameEvents.RaiseMascotGreeted();
        }

        void Update()
        {
            if (Time.time < nextTryAt) return;
            if (Debugging.MeasurementMode.Instance != null && Debugging.MeasurementMode.Instance.Active) return;   // 측정 중엔 정시 계산(SimulateForMeasurement)만
            nextTryAt = Time.time + 1f;   // 조건이 안 맞으면 1초 뒤에 다시 본다

            if (Show.DogShow.Running) return;
            if (ShopHours.Instance == null || !ShopHours.Instance.IsOpen) return;
            if (TimeManager.Instance == null || TimeManager.Instance.IsDayOver) return;

            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (hero == null) return;
            bool unwell = hero.Stats.GrowthBlocked;
            if (unwell ? lostToday >= DailyPenaltyCap : earnedToday >= DailyCap) return;

            DogRoamer roamer = hero.GetComponent<DogRoamer>();
            if (roamer == null || !roamer.enabled || roamer.Busy) return;

            Customer target = PickCustomer(hero.transform.position);
            if (target == null) return;

            // 손님이 강아지를 알아보고 멈춰 기다린다. 강아지가 닿으면 반응
            target.Greeted = true;
            target.HoldFor(MaxHold, hero.transform);
            Customer who = target;
            bool started = roamer.Greet(who.transform, () => React(hero, roamer, who), MaxHold);
            if (!started) { target.HoldFor(0f, null); return; }
            nextTryAt = Time.time + Random.Range(Cooldown.x, Cooldown.y);
        }

        /// <summary>물건을 고르러(둘러보러) 가는 중이고, 아직 인사 안 받았고, 가까운 손님. 줄 선 손님은 건드리지 않는다(계산 흐름).</summary>
        static Customer PickCustomer(Vector3 dog)
        {
            CustomerManager cm = CustomerManager.Instance;
            if (cm == null) return null;
            Customer best = null;
            float bestDist = SeekRange;
            for (int i = 0; ; i++)
            {
                Customer c = cm.ActiveAt(i);
                if (c == null) break;
                if (c.State != CustomerState.ToShelf || c.Greeted || c.Holding) continue;
                float d = Vector3.Distance(dog, c.transform.position);
                if (d < bestDist) { best = c; bestDist = d; }
            }
            return best;
        }

        /// <summary>지금 능력치로 고를 반응. 열린 것 중 하나를 무작위로 — 매번 같은 반응이면 금방 질린다.</summary>
        public static Reaction Choose(DogStats s)
        {
            if (s.GrowthBlocked) return Reaction.Unwell;
            var open = new System.Collections.Generic.List<Reaction>(3);
            if (s.Beauty >= Unlock) open.Add(Reaction.Beauty);
            if (s.Training >= Unlock) open.Add(Reaction.Training);
            if (s.Agility >= Unlock) open.Add(Reaction.Agility);
            return open.Count == 0 ? Reaction.Basic : open[Random.Range(0, open.Count)];
        }

        void React(Dog hero, DogRoamer roamer, Customer who)
        {
            if (who == null) return;
            DogStats s = hero.Stats;
            Reaction kind = Choose(s);
            int grade = MascotGrade.GradeOf(s.MascotTotal);
            int rep;
            string line;

            switch (kind)
            {
                case Reaction.Unwell:
                    roamer.Show(DogAnim.Angry, 2.2f, who.transform);
                    line = s.Cleanliness < DogStats.GrowthBlockThreshold ? "어... 냄새가 나요" : "강아지가 아파 보여요";
                    rep = -Mathf.Min(1, DailyPenaltyCap - lostToday);
                    break;
                case Reaction.Training:
                    roamer.Show(DogAnim.Sit, AfterHold, who.transform);
                    line = Pick(TrainingLines);
                    rep = 2;
                    break;
                case Reaction.Agility:
                    roamer.RunAround(who.transform, CircleSeconds);
                    line = Pick(AgilityLines);
                    rep = 2;
                    break;
                case Reaction.Beauty:
                    roamer.Show(DogAnim.WagTail, AfterHold, who.transform);
                    line = Pick(BeautyLines);
                    rep = 2;
                    break;
                default:
                    roamer.Show(DogAnim.WagTail, AfterHold, who.transform);
                    line = Pick(BasicLines);
                    rep = 1;
                    break;
            }

            who.HoldFor(kind == Reaction.Agility ? CircleSeconds + 1f : AfterHold, hero.transform);

            if (rep > 0)
            {
                if (grade >= MascotGrade.Thresholds.Length - 1) rep++;   // 챔피언 후보는 다들 알아본다
                rep = Mathf.Min(rep, DailyCap - earnedToday);
                earnedToday += rep;
                Sound.Play(Sfx.DogBark);
            }
            else if (rep < 0)
            {
                lostToday += -rep;
                Sound.Play(Sfx.Warning);
            }
            if (rep != 0) GameManager.Instance.AddReputation(rep);
            if (rep > 0) GameEvents.RaiseMascotGreeted();

            bubbleWho = who;
            bubbleText = line;
            bubbleRep = rep;
            bubbleKind = kind;
            bubbleAt = Time.time;
        }

        static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];

        void OnGUI()
        {
            float since = Time.time - bubbleAt;
            if (since > BubbleShow || bubbleWho == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (bubbleStyle == null || bubbleStyle.font != UI.UiSkin.Font)
            {
                bubbleStyle = new GUIStyle(UI.UiSkin.Tag(UI.UiSkin.Cream)) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                repStyle = new GUIStyle(UI.UiSkin.Title) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            }

            float a = Mathf.Clamp01((BubbleShow - since) / 0.4f);   // 끝에 흐려진다
            Color keep = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);

            // 손님 머리 위 말풍선 — 걱정하는 말은 붉은 기
            Vector3 head = cam.WorldToScreenPoint(bubbleWho.transform.position + Vector3.up * (bubbleWho.LabelHeight + 0.15f));
            if (head.z > 0f)
            {
                if (bubbleKind == Reaction.Unwell) GUI.color = new Color(1f, 0.82f, 0.8f, a);
                float w = bubbleStyle.CalcSize(new GUIContent(bubbleText)).x + 28f;
                GUI.Box(new Rect(head.x - w * 0.5f, Screen.height - head.y - 36f, w, 34f), bubbleText, bubbleStyle);
                GUI.color = new Color(1f, 1f, 1f, a);
            }

            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (hero == null) { GUI.color = keep; return; }

            // 미모 반응: 강아지 둘레에 반짝이
            if (bubbleKind == Reaction.Beauty)
            {
                Vector3 c = cam.WorldToScreenPoint(hero.transform.position + Vector3.up * 0.45f);
                if (c.z > 0f) DrawSparkles(new Vector2(c.x, Screen.height - c.y), since, a);
            }

            // 강아지 머리 위로 떠오르는 명성 표시(감점은 붉게)
            if (bubbleRep != 0)
            {
                Vector3 p = cam.WorldToScreenPoint(hero.transform.position + Vector3.up * (0.9f + since * 0.35f));
                if (p.z > 0f)
                {
                    string text = bubbleRep > 0 ? "명성 +" + bubbleRep : "명성 " + bubbleRep;
                    Color face = bubbleRep > 0 ? new Color(1f, 0.82f, 0.3f, a) : new Color(1f, 0.45f, 0.4f, a);
                    var rr = new Rect(p.x - 80f, Screen.height - p.y - 16f, 160f, 32f);
                    // 밝은 바닥에서도 읽히게 진한 그림자를 먼저 깐다
                    repStyle.normal.textColor = new Color(0.25f, 0.13f, 0.03f, a);
                    for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2)
                        if (dx != 0 || dy != 0) GUI.Label(new Rect(rr.x + dx, rr.y + dy, rr.width, rr.height), text, repStyle);
                    repStyle.normal.textColor = face;
                    GUI.Label(rr, text, repStyle);
                }
            }
            GUI.color = keep;
        }

        /// <summary>반짝이 — 작은 마름모 몇 개가 강아지 둘레에서 번갈아 커졌다 작아진다(글꼴에 ✦ 가 없어 직접 그린다).</summary>
        static void DrawSparkles(Vector2 center, float t, float alpha)
        {
            const int Count = 6;
            Matrix4x4 keep = GUI.matrix;
            for (int i = 0; i < Count; i++)
            {
                float ang = i * Mathf.PI * 2f / Count + t * 0.8f;
                float r = 46f + 10f * Mathf.Sin(t * 3f + i);
                Vector2 p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f) * r;
                float size = 6f + 7f * Mathf.Abs(Mathf.Sin(t * 6f + i * 1.7f));
                GUI.color = new Color(1f, 0.95f, 0.55f, alpha);
                GUIUtility.RotateAroundPivot(45f, p);
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Texture2D.whiteTexture);
                GUI.matrix = keep;
            }
        }
    }
}
