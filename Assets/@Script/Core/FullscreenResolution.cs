using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 전체화면으로 켜졌는데 렌더 해상도가 모니터보다 작으면 모니터 해상도로 맞춘다.
    ///
    /// 유니티는 마지막에 쓴 창 크기를 레지스트리에 저장해 두고 다음 실행에 그대로 쓴다.
    /// 창모드로 작게 띄운 적이 있는 PC 에서 런처로 전체화면 실행하면, 작은 해상도로 그린 화면을
    /// 모니터 크기로 늘려 보여 줘서 전체가 흐릿해진다(지스타 시연 피드백, 2026-11-10).
    /// </summary>
    static class FullscreenResolution
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (Application.isEditor) return;
            if (Screen.fullScreenMode == FullScreenMode.Windowed) return;

            DisplayInfo display = Screen.mainWindowDisplayInfo;
            int w = display.width, h = display.height;
            if (w <= 0 || h <= 0) return;
            if (Screen.width == w && Screen.height == h) return;

            Screen.SetResolution(w, h, Screen.fullScreenMode);
        }
    }
}
