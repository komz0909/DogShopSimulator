using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 주인공견 머리 위에 떠 있는 이름표. 메인 화면에서 지어 준 이름(비우면 견종 이름)을 쓴다.
    ///
    /// 화면에 그리는 IMGUI 라 지금 화면을 그리는 카메라(가게 카메라든 도그쇼 카메라든)를 따라간다.
    /// 멀어지면 작아지다가 사라지고, 벽 너머에 있으면 감춘다 — 벽을 뚫고 이름만 둥둥 떠 있으면 어색했다.
    /// </summary>
    public class DogNameTag : MonoBehaviour
    {
        [SerializeField] Texture2D plate;

        /// <summary>이름 글자가 들어갈 자리(이름표 이미지 안의 비율, 위에서부터). 안쪽 판 — 왼쪽 발바닥 장식을 피한다.</summary>
        [SerializeField] Rect textArea = new Rect(0.15f, 0.18f, 0.71f, 0.66f);

        const float MaxDistance = 13f;
        const float FadeFrom = 10f;
        const float BaseWidth = 190f;   // 3m 거리에서의 폭(px)

        GUIStyle style;
        Dog measuredDog;
        float headHeight = 0.5f;
        readonly RaycastHit[] hits = new RaycastHit[8];

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || plate == null) return;
            if (Show.Cutscene.Instance != null && Show.Cutscene.Instance.IsOpen) return;
            if (Show.DogShow.Running) return;   // 미니게임 판정 링·말풍선을 가린다

            Dog dog = DogManager.Instance != null ? DogManager.Instance.Hero : null;
            if (dog == null) return;

            Camera cam = ViewCamera();
            if (cam == null) return;

            if (measuredDog != dog) { headHeight = MeasureHeight(dog); measuredDog = dog; }

            // 머리 위에서 살짝 위아래로 떠다닌다
            float bob = Mathf.Sin(Time.unscaledTime * 2.2f) * 0.035f;
            Vector3 anchor = dog.transform.position + Vector3.up * (headHeight + 0.12f + bob);
            Vector3 sp = cam.WorldToScreenPoint(anchor);
            if (sp.z <= 0.3f || sp.z > MaxDistance) return;
            if (Hidden(cam.transform.position, anchor, dog)) return;

            float scale = Mathf.Clamp(3f / sp.z, 0.45f, 0.85f);   // 바로 옆에 있을 때 화면을 가리지 않게
            float w = BaseWidth * scale;
            float h = w * plate.height / plate.width;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);

            float alpha = 1f - Mathf.InverseLerp(FadeFrom, MaxDistance, sp.z);
            GUI.depth = 50;   // HUD·창보다 뒤에 그린다
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, plate, ScaleMode.StretchToFill);

            if (style == null)
            {
                style = new GUIStyle(UI.UiSkin.Caption) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                style.normal.textColor = new Color(0.36f, 0.22f, 0.12f);
            }
            style.fontSize = Mathf.Max(10, Mathf.RoundToInt(30f * scale));
            var tr = new Rect(r.x + r.width * textArea.x, r.y + r.height * textArea.y,
                              r.width * textArea.width, r.height * textArea.height);
            GUI.Label(tr, dog.DisplayName, style);
            GUI.color = Color.white;
        }

        /// <summary>지금 화면을 그리는 카메라 — 켜져 있고 화면에 그리는 것 중 depth 가 가장 높은 것.</summary>
        static Camera ViewCamera()
        {
            Camera best = null;
            foreach (Camera c in Camera.allCameras)
                if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
            return best;
        }

        /// <summary>카메라와 이름표 사이를 강아지·주인이 아닌 무언가(벽·선반)가 막고 있는가.</summary>
        bool Hidden(Vector3 from, Vector3 to, Dog dog)
        {
            Vector3 d = to - from;
            int n = Physics.RaycastNonAlloc(from, d.normalized, hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Transform t = hits[i].transform;
                if (t.IsChildOf(dog.transform)) continue;
                if (t.GetComponentInParent<Player.PlayerCarry>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 머리 꼭대기 높이. 스킨 메시의 경계 상자는 넉넉하게 잡혀 있어 이름표가 붕 떠 보였다 —
        /// 지금 자세의 메시를 구워 가장 높은 점을 잰다.
        /// </summary>
        static float MeasureHeight(Dog dog)
        {
            float top = float.MinValue;
            var baked = new Mesh();
            foreach (SkinnedMeshRenderer smr in dog.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(baked, true);
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                foreach (Vector3 v in baked.vertices) top = Mathf.Max(top, m.MultiplyPoint3x4(v).y);
            }
            Destroy(baked);
            return top > float.MinValue ? top - dog.transform.position.y : 0.5f;
        }
    }
}
