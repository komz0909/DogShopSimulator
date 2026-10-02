using System;
using UnityEngine;

namespace DogShop.Shop
{
    /// <summary>
    /// 운반 상자의 겉모양. 칸 수가 늘면 <b>상자가 바뀐다</b> — 나무 → 블랙 플라스틱 → 원목.
    ///
    /// 레벨 번호가 아니라 <see cref="CarryCrate.SlotCapacity"/>로 고른다. 레벨 표에서
    /// 칸 수를 옮기면 모양이 저절로 따라가므로, 둘이 어긋날 일이 없다.
    ///
    /// 모델마다 원본 크기와 방향이 제각각이라(VARCO 메시는 미터, 기본 상자는 센티미터)
    /// 단계마다 배율·회전을 따로 들고 있다. 손에 들었을 때 가로 폭이 같아야
    /// 레벨업했을 때 "같은 물건이 좋아졌다"로 읽힌다.
    /// </summary>
    public class CrateLook : MonoBehaviour
    {
        [Serializable]
        public class Tier
        {
            /// <summary>칸 수가 이 이상이면 이 모양을 쓴다.</summary>
            public int minSlots = 8;
            public Mesh mesh;
            public Material material;
            public Vector3 localScale = Vector3.one;
            public Vector3 localEuler;
            public Vector3 localPosition;
        }

        [SerializeField] MeshFilter body;
        [SerializeField] MeshRenderer bodyRenderer;
        [SerializeField] Tier[] tiers = new Tier[0];

        int applied = -1;

        void Start()
        {
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.OnLevelUp += HandleLevel;
            Refresh();
        }

        void OnDestroy()
        {
            if (ShopLevelManager.Instance != null) ShopLevelManager.Instance.OnLevelUp -= HandleLevel;
        }

        void HandleLevel(int level) => Refresh();

        public void Refresh()
        {
            if (body == null || tiers.Length == 0) return;

            int slots = CarryCrate.SlotCapacity;
            int pick = 0;
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i].minSlots <= slots && tiers[i].minSlots >= tiers[pick].minSlots) pick = i;

            if (pick == applied) return;
            applied = pick;

            Tier t = tiers[pick];
            if (t.mesh != null) body.sharedMesh = t.mesh;
            if (t.material != null && bodyRenderer != null) bodyRenderer.sharedMaterial = t.material;
            body.transform.localScale = t.localScale;
            body.transform.localEulerAngles = t.localEuler;
            body.transform.localPosition = t.localPosition;
        }
    }
}
