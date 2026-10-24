using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>짧은 효과음. <see cref="SoundLibrary"/> 의 칸과 이름이 같다.</summary>
    public enum Sfx
    {
        DoorOpen, DoorClose,
        DogCall, DogBark,
        ShowSpace, ShowCard, ArrowCorrect, ArrowWrong, Ranking,
        Sleep, Delivery, ShopBuy, Checkout, Packing, OnlineOrder,
        CleanDone, ShowJump, TrackSuccess, TrackFail,
        PosePerfect, PoseGood, PoseMiss, FetchSuccess, FetchFail, HurdleClear, HurdleFail,
        LevelUp, Train, Warning, BoxOpen, Settlement, Feed, Bath, Medicine,
        CratePut, CrateTake, CustomerLost, ShopOpen, ShopClose, Click,
        Countdown, Go, CarMove,
        // 손맛 피드백(2026-10). 클립은 나중에 SoundLibrary 칸에 넣는다 — 비어 있으면 조용하다
        CoinJingle, ItemTok, Sparkle, Fanfare,
    }

    /// <summary>배경음. 한 번에 하나만 흐른다.</summary>
    public enum Bgm { None, Shop, DogShow, Intro, EndingFirst, EndingOther }

    /// <summary>
    /// 소리 창구. 어디서든 <c>Sound.Play(Sfx.Checkout)</c>, <c>Sound.PlayBgm(Bgm.Shop)</c> 한 줄로 부른다.
    ///
    /// 장면에 따로 놓지 않는다 — 게임이 켜질 때 스스로 생겨 장면이 바뀌어도 남는다(메인 메뉴 인트로 → 가게).
    /// 클립 목록은 Resources/SoundLibrary 에 있다. 비어 있는 칸은 조용히 건너뛴다.
    /// </summary>
    public static class Sound
    {
        static SoundPlayer player;
        static SoundLibrary library;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            library = Resources.Load<SoundLibrary>("SoundLibrary");
            if (library == null) { Debug.LogWarning("[Sound] Resources/SoundLibrary 없음 — 소리 없이 진행"); return; }

            var go = new GameObject("Sound");
            UnityEngine.Object.DontDestroyOnLoad(go);
            player = go.AddComponent<SoundPlayer>();
            player.Init(library);

            // "돈이 부족하다", "너무 멀다" 같은 거절 안내는 전부 ActionRunner 한 곳을 지난다 — 거기에 경고음을 단다.
            // 키를 누르고 있어 같은 거절이 연달아 와도 한 번만 울리게 간격을 둔다
            ActionRunner.OnRejected += (action, reason) =>
            {
                if (Time.unscaledTime - lastWarning < WarningGap) return;
                lastWarning = Time.unscaledTime;
                Play(Sfx.Warning);
            };
        }

        const float WarningGap = 0.35f;
        static float lastWarning = -10f;

        public static void Play(Sfx sfx) { if (player != null) player.PlaySfx(sfx); }

        /// <summary>
        /// 그 효과음의 클립과 목록 볼륨. 거리에 따라 크기가 변하는 소리(지나가는 차처럼)는 자기 AudioSource 로 직접 튼다.
        /// 효과음 볼륨 설정은 <see cref="SfxVolume"/> 을 곱해 쓴다.
        /// </summary>
        public static AudioClip ClipOf(Sfx sfx, out float volume)
        {
            volume = 0f;
            if (library == null) return null;
            SoundLibrary.Entry e = library.Find(sfx);
            volume = e.volume;
            return e.clip;
        }

        /// <summary>배경음을 바꾼다. 이미 그 곡이면 처음부터 다시 틀지 않는다.</summary>
        public static void PlayBgm(Bgm bgm, bool loop = true) { if (player != null) player.PlayBgm(bgm, loop); }

        public static void StopBgm() { if (player != null) player.PlayBgm(Bgm.None, true); }

        public static Bgm CurrentBgm => player != null ? player.Current : Bgm.None;

        /// <summary>0~1. 설정창에서 쓴다.</summary>
        public static float SfxVolume
        {
            get => player != null ? player.SfxVolume : 1f;
            set { if (player != null) player.SfxVolume = value; }
        }

        public static float BgmVolume
        {
            get => player != null ? player.BgmVolume : 1f;
            set { if (player != null) player.BgmVolume = value; }
        }
    }

    /// <summary><see cref="Sound"/> 뒤에서 실제로 소리를 내는 오브젝트. 효과음 하나, 배경음 하나.</summary>
    public class SoundPlayer : MonoBehaviour
    {
        const string SfxKey = "sound.sfx", BgmKey = "sound.bgm";
        const float FadeSeconds = 0.6f;

        SoundLibrary library;
        AudioSource sfx, bgm;
        float sfxVolume = 1f, bgmVolume = 1f;
        float fade = 1f;          // 배경음 페이드 배율
        Bgm pending = Bgm.None;   // 페이드아웃이 끝나면 틀 곡
        bool pendingLoop = true;
        bool switching;

        public Bgm Current { get; private set; } = Bgm.None;

        public float SfxVolume
        {
            get => sfxVolume;
            set { if (Mathf.Approximately(value, sfxVolume)) return; sfxVolume = Mathf.Clamp01(value); sfx.volume = sfxVolume; Save(SfxKey, sfxVolume); }
        }

        public float BgmVolume
        {
            get => bgmVolume;
            set { if (Mathf.Approximately(value, bgmVolume)) return; bgmVolume = Mathf.Clamp01(value); Apply(); Save(BgmKey, bgmVolume); }
        }

        public void Init(SoundLibrary library)
        {
            this.library = library;
            sfx = gameObject.AddComponent<AudioSource>();
            bgm = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = bgm.playOnAwake = false;

            sfxVolume = Load(SfxKey);
            bgmVolume = Load(BgmKey);
            sfx.volume = sfxVolume;
            Apply();
        }

        public void PlaySfx(Sfx id)
        {
            SoundLibrary.Entry e = library.Find(id);
            if (e.clip != null) sfx.PlayOneShot(e.clip, e.volume);
        }

        public void PlayBgm(Bgm id, bool loop)
        {
            if (id == Current && !switching) return;
            if (switching && id == pending) return;
            // 바꾸는 도중에 지금 곡을 다시 부르면(컷신을 페이드 중에 건너뜀) 바꾸기를 취소한다 — 처음부터 다시 틀지 않는다
            if (switching && id == Current && bgm.isPlaying)
            {
                switching = false;
                fade = 1f;
                Apply();
                return;
            }
            pending = id;
            pendingLoop = loop;
            switching = true;
            if (!bgm.isPlaying) fade = 0f;   // 조용하면 바로 바꾼다
        }

        void Update()
        {
            if (!switching) return;

            // 지금 곡을 줄이고, 다 줄면 새 곡으로 바꿔 올린다
            fade = Mathf.MoveTowards(fade, 0f, Time.unscaledDeltaTime / FadeSeconds);
            Apply();
            if (fade > 0f) return;

            switching = false;
            Current = pending;
            SoundLibrary.Entry e = library.Find(pending);
            if (pending == Bgm.None || e.clip == null) { bgm.Stop(); bgm.clip = null; return; }

            bgm.clip = e.clip;
            bgm.loop = pendingLoop;
            bgm.Play();
            fade = 1f;
            Apply();
        }

        void Apply()
        {
            float clipVolume = bgm.clip != null ? library.Find(Current).volume : 1f;
            bgm.volume = bgmVolume * clipVolume * fade;
        }

        static float Load(string key)
        {
            try { return PlayerPrefs.GetFloat(key, 1f); } catch (Exception) { return 1f; }
        }

        static void Save(string key, float value)
        {
            try { PlayerPrefs.SetFloat(key, value); } catch (Exception) { }
        }
    }
}
