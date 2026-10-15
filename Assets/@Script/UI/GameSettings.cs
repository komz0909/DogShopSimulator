using DogShop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.UI
{
    /// <summary>
    /// 게임 안 ESC 메뉴. <b>ESC 로 열고 닫는다</b>. 먼저 일시 정지 메뉴(계속 진행 / 게임 설정 / 게임 종료)가 뜨고,
    /// 게임 설정은 메인 메뉴와 같은 <see cref="SettingsPanel"/> 이다.
    ///
    /// 열려 있는 동안 시간은 멈춘다 — 장사 중에 소리를 줄이려다 손님을 놓치면 안 된다.
    /// 컷신이 떠 있을 때는 열지 않는다. 둘 다 <c>Time.timeScale</c> 을 만지므로 겹치면
    /// 닫는 순서에 따라 시간이 멈춘 채로 남는다.
    /// </summary>
    public class GameSettings : MonoBehaviour, IPointerMenu
    {
        [SerializeField] Texture2D closeImage;     // 닫기 귀퉁이 발바닥 (T_UI_SettingsClosePaw)
        [SerializeField] Texture2D frameImage;     // 설정 창 틀 (T_UI_SettingsFrame)

        public bool IsOpen { get; private set; }

        float savedTimeScale = 1f;

        /// <summary>ESC 를 누르면 먼저 일시 정지 메뉴(계속 진행 / 게임 설정 / 게임 종료)가 뜬다. 설정·종료 확인은 그 아래 단계다.</summary>
        enum Page { Pause, Settings, ConfirmQuit }
        Page page;

        static readonly string[] PauseButtons = { "계속 진행", "게임 설정", "게임 종료" };
        static readonly string[] QuitButtons = { "저장하고 종료", "그냥 종료", "취소" };
        static readonly string[] DemoQuitButtons = { "메인 화면으로", "게임 종료", "취소" };

        /// <summary>빌드 설정 0번 = 메인 화면.</summary>
        const int MainMenuScene = 0;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            // 아래 단계에서 ESC 는 한 단계 위(일시 정지 메뉴)로, 메뉴에서 ESC 는 게임으로
            if (IsOpen && page != Page.Pause) page = Page.Pause;
            else if (IsOpen) Close();
            // 다른 창(강아지 창 등)이 열려 있거나 그 창이 방금 ESC 로 닫혔으면 설정 창을 열지 않는다 — ESC 한 번은 창 하나만 닫는다
            else if (PointerMenus.EscapeTaken || PointerMenus.AnyOpenExcept(this)) return;
            else if (Show.Cutscene.Instance == null || !Show.Cutscene.Instance.IsOpen) Open();
        }

        void Open()
        {
            IsOpen = true;
            page = Page.Pause;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            PointerMenus.SetOpen(this, true);
        }

        void Close()
        {
            IsOpen = false;
            Time.timeScale = savedTimeScale;
            PointerMenus.SetOpen(this, false);
        }

        void OnDestroy()
        {
            if (IsOpen) Close();
        }

        public bool ContainsPoint(Vector2 screenPos) => IsOpen;   // 화면 전체를 덮는다

        void OnGUI()
        {
            if (!IsOpen) return;

            // HUD 와 다른 창보다 위에 그리고, 클릭도 먼저 받는다(GUI.depth 가 낮을수록 위)
            GUI.depth = -90;
            switch (page)
            {
                case Page.Pause:
                {
                    int pick = SettingsPanel.DrawMenu("일시 정지", null, PauseButtons, closeImage, frameImage);
                    if (pick == 0 || pick == -2) Close();
                    else if (pick == 1) page = Page.Settings;
                    else if (pick == 2) page = Page.ConfirmQuit;
                    break;
                }
                case Page.Settings:
                    // 설정 창의 발바닥(닫기)은 일시 정지 메뉴로 돌아간다
                    if (SettingsPanel.Draw(closeImage, frameImage)) page = Page.Pause;
                    break;
                case Page.ConfirmQuit:
                {
                    // 시연 모드는 저장이 없다 — 대신 메인 화면으로 돌아가 다음 사람이 처음부터 시작할 수 있게 한다
                    if (Exhibition.Active)
                    {
                        int demoPick = SettingsPanel.DrawMenu("시연 종료", "시연 진행은 저장되지 않는다",
                            DemoQuitButtons, closeImage, frameImage);
                        if (demoPick == 0) { Close(); UnityEngine.SceneManagement.SceneManager.LoadScene(MainMenuScene); }
                        else if (demoPick == 1) Quit();
                        else if (demoPick == 2 || demoPick == -2) page = Page.Pause;
                        break;
                    }

                    // 자동 저장이 없다 — 마지막 저장(F5) 뒤의 진행을 그냥 날리지 않게 한 번 묻는다
                    int pick = SettingsPanel.DrawMenu("게임 종료",
                        "저장하지 않으면 마지막 저장(F5) 뒤의 진행은 사라진다",
                        QuitButtons, closeImage, frameImage);
                    if (pick == 0) { if (SaveManager.Instance != null) SaveManager.Instance.Save(); Quit(); }
                    else if (pick == 1) Quit();
                    else if (pick == 2 || pick == -2) page = Page.Pause;
                    break;
                }
            }
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
