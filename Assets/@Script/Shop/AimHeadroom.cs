using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 진열대·창고 선반 <b>맨 윗판 위에 올라간 물건</b>까지 조준에 잡히게 하는 트리거 상자.
    ///
    /// 몸통 콜라이더는 윗판 높이에서 끝난다(아일랜드 진열대 0.81m, 창고 선반 1.67m). 그래서 윗판에 놓인 물건을
    /// 겨누고 E/R 를 누르면 조준선이 진열대를 넘어가 뒤의 벽·다른 진열대에 맞아 아무 일도 없었다.
    /// 물건이 서는 공간(윗판 위 <see cref="Height"/>)을 트리거로 덮는다 — 트리거라 걸음·배치 검사(트리거 무시)에는 끼지 않고,
    /// 트리거까지 맞히는 조준(<c>PlayerInteraction.Aim</c>)에만 걸린다.
    /// </summary>
    public static class AimHeadroom
    {
        /// <summary>윗판 위로 덮는 높이. 진열 크기(0.30)에 여유를 조금.</summary>
        public const float Height = 0.35f;

        /// <summary>Awake 에서 한 번 부른다(콜라이더 크기가 정해진 뒤).</summary>
        /// <param name="body">몸통 콜라이더(같은 오브젝트, 로컬 좌표).</param>
        /// <param name="topLocalY">맨 윗판의 로컬 높이 — 물건이 그 위에 선다.</param>
        public static void Add(BoxCollider body, float topLocalY)
        {
            if (body == null) return;
            float bodyTop = body.center.y + body.size.y * 0.5f;
            if (topLocalY + Height <= bodyTop + 0.01f) return;   // 이미 몸통이 물건 높이까지 덮는다

            Transform owner = body.transform;
            var go = new GameObject("AimHeadroom");
            go.transform.SetParent(owner, false);
            go.layer = owner.gameObject.layer;

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            float bottom = Mathf.Max(topLocalY, bodyTop - 0.02f);
            float top = topLocalY + Height;
            box.center = new Vector3(body.center.x, (bottom + top) * 0.5f, body.center.z);
            box.size = new Vector3(body.size.x, top - bottom, body.size.z);
        }
    }
}
