using System;
using UnityEngine;

namespace DogShop.Data
{
    /// <summary>
    /// 가게에 놓는 물건 1종. <b>상품 카탈로그와 따로 둔다</b> — 상품 카탈로그는 수요가중치와
    /// 창고 배열의 길이를 정하는 자리라, 팔지 않는 가구를 끼워 넣으면 손님의 상품 선택 확률과
    /// 하루 재고 비용 계산이 조용히 틀어진다.
    /// </summary>
    [Serializable]
    public class FurnitureDef
    {
        public string nameKo = "";
        public int price = 300;
        [Min(1)] public int unlockLevel = 1;

        /// <summary>사진을 찍을 모델. 부품이 없는 맨 모델이어도 된다.</summary>
        public GameObject prefab;

        /// <summary>
        /// 사서 배달될 때 실제로 세울 프리팹. <see cref="prefab"/>과 나눈 이유는
        /// <b>사진은 모델만 있으면 되지만 놓이는 물건은 콜라이더·진열 칸·배치 부품이 필요하기</b> 때문이다.
        /// 비어 있으면 그 가구는 아직 살 수 없다.
        /// </summary>
        public GameObject placedPrefab;

        /// <summary>
        /// 모델에 입힐 재질. FBX를 그대로 꺼내면 <b>임포터가 만든 흰 기본 재질</b>이 붙어 있어
        /// 사진이 전부 하얗게 나온다 — 씬에서 실제로 쓰는 재질을 여기에 물려 둔다.
        /// 비워 두면 모델에 붙어 있는 것을 그대로 쓴다.
        /// </summary>
        public Material material;

        /// <summary>상점창에 뜨는 사진. 상품과 같은 방식으로 굽는다.</summary>
        public Texture2D icon;

        /// <summary>
        /// 사진 찍을 때 <b>모델 회전에 얹을</b> 각도. 기본 각도에서는 뒷면이 카메라를 보는
        /// 모델이 있다 — 아일랜드 진열대는 뼈다귀 그림이 한쪽 면에만 있어 180도 돌려야 보인다.
        /// 덮어쓰지 않고 곱하므로 FBX 축 보정이 살아 있다.
        /// </summary>
        public Vector3 iconRotation;

        /// <summary>창에 한 줄 붙는 설명. 무엇에 쓰는 물건인지.</summary>
        public string note = "";
    }

    [CreateAssetMenu(menuName = "DogShop/Furniture Catalog", fileName = "FurnitureCatalog")]
    public class FurnitureCatalog : ScriptableObject
    {
        [SerializeField] FurnitureDef[] items = new FurnitureDef[0];

        public int Count => items.Length;
        public FurnitureDef Get(int index) => items[index];
    }
}
