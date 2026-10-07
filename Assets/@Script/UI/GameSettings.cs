using DogShop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.UI
{
    /// <summary>
    /// 게임 안 설정 창. <b>ESC 로 열고 닫는다</b>(발바닥으로도 닫힌다). 창은 메인 메뉴와 같은
    /// <see cref="SettingsPanel"/> 이다.
    ///
    /// 열려 있는 동안 시간은 멈춘다 — 장사 중에 소리를 줄이려다 손님을 놓치면 안 된다.
    /// 컷신이 떠 있을 때는 열지 않는다. 둘 다 <c>Time.timeScale</c> 을 만지므로 겹치면
    /// 닫는 순서에 따라 시간이 멈춘 채로 남는다.
    /// </summary>
    public class GameSettings : MonoBehaviour, IPointerMenu
    {
        [SerializeField] Texture2D pawImage;

        public bool IsOpen { get; private set; }

        float savedTimeScale = 1f;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            if (IsOpen) Close();
            // 다른 창(강아지 창 등)이 열려 있거나 그 창이 방금 ESC 로 닫혔으면 설정 창을 열지 않는다 — ESC 한 번은 창 하나만 닫는다
            else if (PointerMenus.EscapeTaken || PointerMenus.AnyOpenExcept(this)) return;
            else if (Show.Cutscene.Instance == null || !Show.Cutscene.Instance.IsOpen) Open();
        }

        void Open()
        {
            IsOpen = true;
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
            if (SettingsPanel.Draw(pawImage)) Close();
        }
    }
}
