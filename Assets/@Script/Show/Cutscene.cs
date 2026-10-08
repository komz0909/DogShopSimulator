using System;
using System.Collections;
using System.Collections.Generic;
using DogShop.Core;
using DogShop.Dogs;
using DogShop.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Show
{
    /// <summary>
    /// 오프닝·엔딩 컷신. <b>정지 일러스트 + 글 한 단락</b>을 넘겨 본다 — 3D 컷신은 만들지 않는다(Plan.md "엔딩").
    ///
    /// - 오프닝: 메인 화면에서 <b>새 게임</b>으로 들어왔을 때 가게가 뜨자마자 3장
    /// - 엔딩: D30 챔피언십 결과 화면에서 "게임 종료"를 누르면 3장, 끝나면 타이틀로
    ///
    /// 강아지가 나오는 장은 <b>견종마다 그림이 따로</b> 있을 수 있다(<see cref="Slide.images"/> 를
    /// 견종 번호로 고른다). 그 견종 그림이 없으면 첫 장을 쓴다.
    ///
    /// 컷신이 떠 있는 동안 시간은 멈춘다(<c>Time.timeScale = 0</c>) — 오프닝을 읽는 사이
    /// 08시가 흘러가 첫 손님을 놓치면 안 된다.
    /// </summary>
    public class Cutscene : MonoBehaviour, IPointerMenu
    {
        [Serializable]
        public class Slide
        {
            [Tooltip("견종 번호 순서. 강아지가 안 나오는 장은 한 장만 두면 된다")]
            public Texture2D[] images = new Texture2D[0];

            [TextArea(2, 4)]
            [Tooltip("{breed} {place} {grade} {level} {money} 를 엔딩에서 채운다")]
            public string text = "";
        }

        [SerializeField] Slide[] opening = new Slide[0];
        [SerializeField] Slide[] ending = new Slide[0];

        [Header("우승 못 한 엔딩")]
        [Tooltip("엔딩에서 우승 그림 대신 쓸 장(0부터)")]
        [SerializeField] int endingResultSlide = 1;
        [Tooltip("견종 번호 순서. 주인과 강아지가 웃으며 '다음 대회 화이팅' 하는 그림")]
        [SerializeField] Texture2D[] endingNotWonImages = new Texture2D[0];

        [SerializeField] string titleScene = "MainMenu";

        /// <summary>넘기기 입력을 이 시간(실시간 초) 동안 무시한다 — 연타로 장을 건너뛰지 않게.</summary>
        const float MinShow = 0.35f;

        const float Pad = 24f;

        public static Cutscene Instance { get; private set; }

        public bool IsOpen => playing != null;

        Slide[] playing;
        int index;
        float shownAt;
        float savedTimeScale = 1f;
        Action onDone;
        Dictionary<string, string> tokens;

        GUIStyle textStyle;
        GUIStyle hintStyle;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        IEnumerator Start()
        {
            // 이어하기·에디터에서 바로 누른 플레이·무인 측정에는 띄우지 않는다
            if (!GameStart.FromMenu || GameStart.Continue) yield break;
            // 주인공 강아지는 DogManager.Start 에서 생기고 이름을 받는다. Start 순서는 정해져 있지 않아서,
            // 같은 프레임에 글을 만들면 메인 화면에서 지은 이름 대신 "강아지"로 불릴 수 있었다 — 한 프레임 기다린다
            yield return null;
            Play(opening, BreedTokens(), null);
        }

        void OnDestroy()
        {
            if (playing != null) Finish(false);
            if (Instance == this) Instance = null;
        }

        /// <summary>엔딩을 튼다. <paramref name="values"/> 의 키가 글의 {키} 를 채운다.</summary>
        public void PlayEnding(Dictionary<string, string> values)
        {
            Dictionary<string, string> all = BreedTokens();
            if (values != null) foreach (var kv in values) all[kv.Key] = kv.Value;
            notWon = all.TryGetValue("won", out string won) && won != "1";

            Play(ending, all, () => UnityEngine.SceneManagement.SceneManager.LoadScene(titleScene));
        }

        static Dictionary<string, string> BreedTokens()
        {
            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            return new Dictionary<string, string> { { "breed", hero != null ? hero.DisplayName : "강아지" } };   // 이름을 지어 줬으면 이름으로 부른다
        }

        void Play(Slide[] slides, Dictionary<string, string> values, Action done)
        {
            if (slides == null || slides.Length == 0) { done?.Invoke(); return; }

            playing = slides;
            index = 0;
            tokens = values;
            onDone = done;
            shownAt = Time.unscaledTime;

            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            PointerMenus.SetOpen(this, true);
        }

        void Next()
        {
            if (Time.unscaledTime - shownAt < MinShow) return;

            index++;
            shownAt = Time.unscaledTime;
            if (index >= playing.Length) Finish(true);
        }

        void Finish(bool runCallback)
        {
            playing = null;
            Time.timeScale = savedTimeScale;
            PointerMenus.SetOpen(this, false);

            Action done = onDone;
            onDone = null;
            if (runCallback) done?.Invoke();
        }

        void Update()
        {
            if (playing == null) return;

            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
                Next();
        }

        public bool ContainsPoint(Vector2 screenPos) => playing != null;   // 화면 전체를 덮는다

        string Fill(string text)
        {
            if (tokens == null || string.IsNullOrEmpty(text)) return text;
            foreach (var kv in tokens) text = text.Replace("{" + kv.Key + "}", kv.Value);
            return text;
        }

        /// <summary>이번 엔딩이 우승을 못 한 판이다 — 결과 장의 트로피 그림을 바꿔 끼운다.</summary>
        bool notWon;

        Texture2D ImageOf(Slide slide)
        {
            Dog hero = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            int breed = hero != null ? hero.BreedIndex : 0;

            if (notWon && playing == ending && index == endingResultSlide && endingNotWonImages.Length > 0)
            {
                Texture2D alt = breed >= 0 && breed < endingNotWonImages.Length ? endingNotWonImages[breed] : null;
                return alt != null ? alt : endingNotWonImages[0];
            }

            if (slide.images == null || slide.images.Length == 0) return null;
            Texture2D picked = breed >= 0 && breed < slide.images.Length ? slide.images[breed] : null;
            return picked != null ? picked : slide.images[0];
        }

        void OnGUI()
        {
            if (playing == null) return;

            if (textStyle == null)
            {
                textStyle = new GUIStyle(UiSkin.Title) { fontSize = 24, wordWrap = true, alignment = TextAnchor.MiddleCenter };
                textStyle.normal.textColor = Color.white;
                hintStyle = new GUIStyle(UiSkin.Caption) { alignment = TextAnchor.MiddleRight };
            }

            GUI.depth = -100;
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);

            GUI.color = Color.black;
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = Color.white;

            Slide slide = playing[index];
            Texture2D image = ImageOf(slide);
            if (image != null) GUI.DrawTexture(screen, image, ScaleMode.ScaleAndCrop);

            // 아래쪽 글 띠. 그림이 밝아도 읽히게 반투명 검정을 깐다
            float band = Mathf.Max(150f, Screen.height * 0.2f);
            Rect bandRect = new Rect(0f, Screen.height - band, Screen.width, band);
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(bandRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(Pad * 2f, bandRect.y + Pad * 0.5f, Screen.width - Pad * 4f, band - Pad * 1.6f),
                Fill(slide.text), textStyle);

            GUI.Label(new Rect(Pad, Screen.height - Pad * 1.4f, Screen.width - Pad * 2f, Pad),
                (index + 1) + " / " + playing.Length + "     클릭/Space: 다음", hintStyle);

            if (GUI.Button(new Rect(Screen.width - 120f - Pad, Pad, 120f, 36f), "건너뛰기", UiSkin.Button(UiSkin.Cream)))
            {
                Finish(true);
                return;
            }

            // 화면 아무 데나 클릭해도 넘어간다(건너뛰기 버튼은 위에서 먼저 먹는다)
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Next();
                e.Use();
            }
        }
    }
}
