using UnityEngine;

namespace DogShop.UI
{
    /// <summary>
    /// 게임 안 모든 IMGUI 글자를 한 폰트로 묶는다.
    ///
    /// <see cref="UiSkin"/>가 굽는 스타일은 폰트를 직접 들고 있지만, 창 대부분은
    /// <c>new GUIStyle(GUI.skin.label)</c> 처럼 <b>유니티 기본 스킨</b>에서 스타일을 떠 온다.
    /// 기본 스킨의 폰트는 Arial 이라 한글이 나오긴 해도 다른 창과 글꼴이 어긋난다 —
    /// 계산대 창은 배달아 폰트인데 강아지 메뉴는 Arial 인 식이다.
    ///
    /// 그래서 <c>GUI.skin.font</c> 를 직접 갈아 끼운다. 스킨은 전역이므로 한 번 바꾸면
    /// 그 뒤의 모든 OnGUI 가 따라온다. <b>실행 순서를 맨 앞으로</b> 당겨 두는 이유가 이것이다 —
    /// 다른 창이 먼저 그려지면 그 프레임은 옛 폰트로 나온다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class UiFontBinder : MonoBehaviour
    {
        [SerializeField] Font font;

        void Awake()
        {
            if (font != null) UiSkin.Font = font;
        }

        void OnGUI()
        {
            if (font == null) return;

            UiSkin.Font = font;
            if (GUI.skin != null && GUI.skin.font != font) GUI.skin.font = font;
        }
    }
}
