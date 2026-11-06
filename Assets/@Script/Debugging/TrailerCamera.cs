using UnityEngine;

namespace DogShop.Debugging
{
    /// <summary>
    /// 소개 영상 촬영용 카메라(2026-11-06). 게임 기능이 아니다 — 에디터에서 장면을 찍을 때만 만든다.
    /// 지금 화면 카메라를 복사해(후처리 설정까지 그대로) 두 자리 사이를 천천히 움직이며 한 곳을 바라본다.
    /// </summary>
    public class TrailerCamera : MonoBehaviour
    {
        public static TrailerCamera Instance { get; private set; }

        Camera cam;
        Vector3 fromPos, toPos, fromLook, toLook;
        float start, duration = 1f, fov;

        public static void Shot(Vector3 fromPos, Vector3 toPos, Vector3 fromLook, Vector3 toLook, float seconds, float fov = 50f)
        {
            if (Instance == null)
            {
                Camera src = Camera.main;
                if (src == null) return;
                GameObject go = Instantiate(src.gameObject);
                go.name = "TrailerCamera";
                go.tag = "TrailerCam";   // 녹화가 이 카메라만 찍는다(화면 위 글·창은 빠진다)
                foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
                    if (mb.GetType().Name != "UniversalAdditionalCameraData") DestroyImmediate(mb);
                foreach (AudioListener al in go.GetComponents<AudioListener>()) DestroyImmediate(al);
                for (int i = go.transform.childCount - 1; i >= 0; i--) DestroyImmediate(go.transform.GetChild(i).gameObject);
                Instance = go.AddComponent<TrailerCamera>();
                Instance.cam = go.GetComponent<Camera>();
                Instance.cam.depth = src.depth + 10f;
            }
            Instance.fromPos = fromPos; Instance.toPos = toPos;
            Instance.fromLook = fromLook; Instance.toLook = toLook;
            Instance.duration = Mathf.Max(0.1f, seconds);
            Instance.fov = fov;
            Instance.start = Time.unscaledTime;
            Instance.Apply(0f);
        }

        /// <summary>계산 줄 맨 앞 손님부터 차례로 계산한다(계산대 장면).</summary>
        public static void ServeQueue(float interval, float seconds)
        {
            if (Instance != null) Instance.StartCoroutine(Instance.Serve(interval, seconds));
        }

        System.Collections.IEnumerator Serve(float interval, float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                Shop.CustomerManager cm = Shop.CustomerManager.Instance;
                if (cm != null)
                {
                    Shop.Customer c = cm.QueuedAt(0);
                    if (c != null && c.State == Shop.CustomerState.Waiting) cm.Checkout(c);
                }
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        public static void Off()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            Instance = null;
        }

        void LateUpdate() => Apply(Mathf.Clamp01((Time.unscaledTime - start) / duration));

        void Apply(float t)
        {
            float s = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(fromPos, toPos, s);
            transform.rotation = Quaternion.LookRotation(Vector3.Lerp(fromLook, toLook, s) - transform.position);
            if (cam != null) cam.fieldOfView = fov;
        }
    }
}
