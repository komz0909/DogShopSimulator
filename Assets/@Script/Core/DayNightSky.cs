using UnityEngine;
using UnityEngine.Rendering;

namespace DogShop.Core
{
    /// <summary>
    /// 게임 시각에 따라 하늘·햇빛·주변광·안개를 바꾼다. 아침(08) → 한낮 → 오후 → 노을(18~19) → 저녁 → 밤(21~24).
    ///
    /// 하늘은 <c>DogShop/GradientSky</c> 셰이더다. 기본 Procedural 스카이박스는 해가 지평선 아래로 가면
    /// 누런 녹색이 되거나 새까매져서 저녁·밤을 그릴 수 없었다. 열쇠 시각마다 천정·지평선·바닥 색과
    /// 해·달·별 세기를 정해 두고 사이를 부드럽게 섞는다.
    ///
    /// 주변광도 같은 열쇠에서 <b>직접</b> 정한다(Trilight). 하늘에서 굽게 두면 밤에 가게 안까지 시커메졌다.
    /// 밤에는 햇빛을 <b>달 쪽에서 오는 약한 푸른 빛</b>으로 돌린다 — 해를 지평선 아래에 둔 채로 비추면 땅 밑에서 빛이 올라온다.
    /// </summary>
    public class DayNightSky : MonoBehaviour
    {
        [SerializeField] Light sun;
        [SerializeField] Material skyMaterial;

        /// <summary>해가 지나가는 방위 중심(도). 아침엔 여기서 -70, 저녁엔 +70.</summary>
        [SerializeField] float sunYaw = 330f;

        struct Key
        {
            public float hour, pitch, light, stars, moon;
            public Color lightColor, top, horizon, bottom, ambSky, ambEq, ambGround;
        }

        static Key K(float hour, float pitch, float light, Color lightColor, Color top, Color horizon, Color bottom,
                     Color ambSky, Color ambEq, Color ambGround, float stars, float moon)
            => new Key { hour = hour, pitch = pitch, light = light, lightColor = lightColor, top = top, horizon = horizon, bottom = bottom,
                         ambSky = ambSky, ambEq = ambEq, ambGround = ambGround, stars = stars, moon = moon };

        static Color C(float r, float g, float b) => new Color(r, g, b);

        static readonly Key[] Keys =
        {
            //   시각    해높이 빛세기 빛색               천정               지평선              바닥                 주변광 하늘         적도               땅                  별    달
            K( 8.0f,  22f, 1.05f, C(1f, .87f, .72f), C(.38f,.60f,.88f), C(.88f,.90f,.90f), C(.55f,.58f,.58f), C(.62f,.67f,.76f), C(.58f,.58f,.56f), C(.34f,.32f,.29f), 0f, 0f),
            K(12.0f,  58f, 1.35f, C(1f, .96f, .88f), C(.24f,.50f,.90f), C(.76f,.86f,.96f), C(.55f,.60f,.62f), C(.66f,.72f,.82f), C(.60f,.62f,.62f), C(.36f,.34f,.30f), 0f, 0f),
            K(16.0f,  34f, 1.20f, C(1f, .90f, .76f), C(.30f,.52f,.86f), C(.90f,.86f,.78f), C(.55f,.56f,.55f), C(.64f,.66f,.72f), C(.62f,.58f,.52f), C(.36f,.32f,.28f), 0f, 0f),
            K(18.3f,  10f, 0.85f, C(1f, .62f, .38f), C(.32f,.40f,.70f), C(1f, .64f, .38f), C(.45f,.38f,.36f), C(.52f,.48f,.58f), C(.70f,.52f,.40f), C(.30f,.24f,.22f), 0f, 0f),
            K(19.4f,  -4f, 0.18f, C(.62f,.58f,.90f), C(.20f,.18f,.42f), C(.92f,.52f,.56f), C(.25f,.20f,.28f), C(.30f,.28f,.44f), C(.42f,.30f,.36f), C(.14f,.12f,.15f), .3f, .5f),
            K(21.0f, -18f, 0.14f, C(.55f,.64f,.95f), C(.04f,.06f,.16f), C(.13f,.16f,.32f), C(.06f,.07f,.10f), C(.20f,.24f,.38f), C(.16f,.18f,.26f), C(.08f,.08f,.10f), 1f, 1f),
            K(24.0f, -30f, 0.12f, C(.55f,.64f,.95f), C(.03f,.04f,.12f), C(.10f,.12f,.25f), C(.05f,.05f,.08f), C(.18f,.21f,.34f), C(.14f,.15f,.22f), C(.07f,.07f,.09f), 1f, 1f),
        };

        Material sky;
        Material savedSkybox;
        AmbientMode savedAmbient;

        void Start()
        {
            if (sun == null) sun = RenderSettings.sun;

            savedSkybox = RenderSettings.skybox;
            savedAmbient = RenderSettings.ambientMode;
            if (skyMaterial != null)
            {
                // 복제해서 쓴다 — 에셋을 고치면 플레이를 멈춰도 마지막 시각이 남는다
                sky = new Material(skyMaterial) { name = skyMaterial.name + " (DayNight)" };
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;

            // 먼 곳을 지평선 색으로 흐린다. 가게 안·도그쇼 무대(15m 안쪽)는 닿지 않는다
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 170f;

            Apply(CurrentHour());
        }

        static float CurrentHour() => TimeManager.Instance != null ? TimeManager.Instance.CurrentHour : 12f;

        void Update() => Apply(CurrentHour());

        void Apply(float hour)
        {
            hour = Mathf.Clamp(hour, Keys[0].hour, Keys[Keys.Length - 1].hour);
            int i = 0;
            while (i < Keys.Length - 2 && hour > Keys[i + 1].hour) i++;
            Key a = Keys[i], b = Keys[i + 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.hour, b.hour, hour));

            float pitch = Mathf.Lerp(a.pitch, b.pitch, t);
            float yaw = sunYaw + Mathf.Lerp(-70f, 70f, Mathf.InverseLerp(8f, 20f, hour));
            Quaternion sunRot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 toSun = -(sunRot * Vector3.forward);

            if (sun != null)
            {
                // 해가 지면 빛은 달 쪽(반대편 하늘)에서 비스듬히 온다
                sun.transform.rotation = pitch >= 0f ? sunRot : Quaternion.Euler(Mathf.Clamp(-pitch * 1.6f, 18f, 40f), yaw + 180f, 0f);
                sun.color = Color.Lerp(a.lightColor, b.lightColor, t);
                sun.intensity = Mathf.Lerp(a.light, b.light, t);
            }

            if (sky != null)
            {
                sky.SetColor("_TopColor", Color.Lerp(a.top, b.top, t));
                sky.SetColor("_HorizonColor", Color.Lerp(a.horizon, b.horizon, t));
                sky.SetColor("_BottomColor", Color.Lerp(a.bottom, b.bottom, t));
                sky.SetVector("_SunDir", toSun);
                sky.SetColor("_SunColor", Color.Lerp(a.lightColor, b.lightColor, t));
                sky.SetFloat("_StarIntensity", Mathf.Lerp(a.stars, b.stars, t));
                sky.SetFloat("_MoonIntensity", Mathf.Lerp(a.moon, b.moon, t));
            }

            RenderSettings.ambientSkyColor = Color.Lerp(a.ambSky, b.ambSky, t);
            RenderSettings.ambientEquatorColor = Color.Lerp(a.ambEq, b.ambEq, t);
            RenderSettings.ambientGroundColor = Color.Lerp(a.ambGround, b.ambGround, t);
            RenderSettings.fogColor = Color.Lerp(a.horizon, b.horizon, t);
        }

        void OnDestroy()
        {
            if (sky != null)
            {
                if (RenderSettings.skybox == sky) RenderSettings.skybox = savedSkybox;
                RenderSettings.ambientMode = savedAmbient;
                Destroy(sky);
            }
        }
    }
}
