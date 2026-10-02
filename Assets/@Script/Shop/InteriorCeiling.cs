using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 실내에 들어와도 <b>숨기지 않는 천장 요소</b>. 지붕(<see cref="RoofVisibility"/>)은
    /// 실내에서 그림자만 남기는데, 이 표시가 붙은 것은 그 대상에서 빠진다.
    ///
    /// 3인칭 카메라는 기본 피치에서 벽 높이(3.0m)쯤인 3.04m 에 서서 내려다보므로,
    /// 천장을 판으로 그리면 매장이 통째로 가려진다. 그래서 두 가지만 남긴다.
    ///
    /// - <b>아래를 향한 천장면</b> — 면은 뒷면을 그리지 않으므로 위에서는 비쳐 보이고
    ///   1인칭이나 카메라를 낮게 숙였을 때만 보인다
    /// - <b>들보</b> — 폭 15cm 라 화면을 거의 안 가리면서, 위에서 봐도 펜던트가
    ///   매달린 곳이 보인다. 천장면만으로는 평소 시점에서 램프가 허공에 떠 보였다
    ///
    /// 둘 다 콜라이더가 없어 카메라 SphereCast 에 안 걸린다.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class InteriorCeiling : MonoBehaviour
    {
    }
}
