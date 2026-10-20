using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DogShop.Core
{
    /// <summary>
    /// 전시 부스 스태프용 키. <b>시연 모드에서만</b> 듣는다 — 본편에서 잘못 눌러 진행을 날리지 않게.
    ///   F12: 어떤 화면에서든(정산·잠자기·도그쇼·컷신 중이어도) 곧바로 시연 견종 고르기 화면으로.
    ///        체험자가 하다 말고 가거나 이상한 데서 막혔을 때 다음 사람을 바로 받는다.
    ///
    /// 장면마다 놓지 않는다 — 게임이 켜질 때 스스로 생겨 장면이 바뀌어도 남는다(<see cref="Sound"/> 와 같은 방식).
    /// </summary>
    public class StaffKeys : MonoBehaviour
    {
        const string MenuScene = "MainMenu";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("StaffKeys");
            DontDestroyOnLoad(go);
            go.AddComponent<StaffKeys>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.f12Key.wasPressedThisFrame) return;
            if (!Exhibition.Active) return;
            if (SceneManager.GetActiveScene().name == MenuScene) return;
            BackToPicker();
        }

        /// <summary>
        /// 시연을 버리고 견종 고르기로. 시연은 저장하지 않으니 잃을 것이 없다.
        /// 컷신·설정 창이 멈춰 둔 시간, 열린 창 목록, 잠긴 커서를 직접 풀어 준다 — 그 창들이 정리할 틈 없이 장면째 사라지기 때문이다.
        /// </summary>
        public static void BackToPicker()
        {
            Time.timeScale = 1f;
            PointerMenus.ForgetAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Sound.StopBgm();
            Exhibition.ReturnToPicker = true;
            SceneManager.LoadScene(MenuScene);
        }
    }
}
