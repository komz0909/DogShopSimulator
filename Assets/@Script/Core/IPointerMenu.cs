using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DogShop.Core
{
    /// <summary>
    /// 화면에 열릴 수 있는 클릭 메뉴. 메뉴가 여러 개일 때 서로의 영역을 확인해
    /// 메뉴 안을 클릭했는데 뒤에 있는 오브젝트가 잡히는 일을 막는다.
    /// </summary>
    public interface IPointerMenu
    {
        bool IsOpen { get; }
        bool ContainsPoint(Vector2 screenPos);
    }

    /// <summary>
    /// 열려 있는 메뉴를 추적한다. 카메라는 이걸 보고 회전을 멈추고 커서를 풀어준다.
    /// GameObject가 달라도 집계되므로 전체 화면 패널(마감 이벤트)까지 포함된다.
    /// </summary>
    public static class PointerMenus
    {
        static readonly HashSet<object> openMenus = new HashSet<object>();

        public static bool AnyOpen => openMenus.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear() => openMenus.Clear();

        public static void SetOpen(object menu, bool isOpen)
        {
            if (isOpen) openMenus.Add(menu);
            else openMenus.Remove(menu);
        }

        /// <summary>
        /// 조준 위치. 커서가 잠긴 1인칭에서는 <b>화면 중앙</b>, 커서가 살아 있는 3인칭에서는 마우스 위치다.
        /// 조준 좌표를 여기 한 곳에서만 만들기 때문에 시점을 바꿔도 상호작용 코드는 그대로다.
        /// </summary>
        public static Vector2 PickPosition()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }
    }
}
