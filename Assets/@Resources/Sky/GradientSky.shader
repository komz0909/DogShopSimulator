// 시간대별 하늘(DayNightSky 가 값을 넣는다). 기본 Procedural 스카이박스는 해가 지평선 아래로 가면
// 하늘이 누런 녹색이 되거나 새까매져서 저녁·밤을 그릴 수 없었다 — 색을 직접 정하는 그라데이션으로 바꿨다.
//   하늘: 천정 ↔ 지평선 그라데이션, 땅 쪽: 지평선 ↔ 바닥
//   해: 원반 + 주변 번짐, 달: 해 반대편 원반(밤에만), 별: 방향 해시로 흩뿌린 점(밤에만)
Shader "DogShop/GradientSky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.25, 0.5, 0.9, 1)
        _HorizonColor ("Horizon", Color) = (0.8, 0.86, 0.95, 1)
        _BottomColor ("Ground", Color) = (0.45, 0.5, 0.52, 1)
        _HorizonPower ("Horizon falloff", Range(0.1, 4)) = 0.6
        _SunDir ("Sun direction (to sun)", Vector) = (0, 0.5, 1, 0)
        _SunColor ("Sun color", Color) = (1, 0.95, 0.85, 1)
        _SunSize ("Sun size", Range(0.0005, 0.05)) = 0.004
        _SunGlow ("Sun glow", Range(0, 2)) = 0.6
        _MoonIntensity ("Moon", Range(0, 1)) = 0
        _StarIntensity ("Stars", Range(0, 2)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TopColor, _HorizonColor, _BottomColor, _SunColor, _SunDir;
            float _HorizonPower, _SunSize, _SunGlow, _MoonIntensity, _StarIntensity;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash (float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                float3 col = y >= 0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(y), _HorizonPower))
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, pow(saturate(-y * 4), 0.5));

                float3 sunDir = normalize(_SunDir.xyz);

                // 해: 원반 + 번짐(지평선 위에서만)
                float s = dot(d, sunDir);
                float disc = smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, s);
                float glow = pow(saturate(s), 48) * _SunGlow + pow(saturate(s), 6) * _SunGlow * 0.25;
                float above = saturate(sunDir.y * 8 + 0.5);
                col += _SunColor.rgb * (disc * 2 + glow) * above * step(0, y);

                // 달: 해 반대편
                float3 moonDir = normalize(float3(-sunDir.x, abs(sunDir.y) + 0.35, -sunDir.z));
                float m = dot(d, moonDir);
                float moon = smoothstep(0.9993, 0.9996, m);
                col += float3(0.95, 0.95, 0.85) * (moon * 1.2 + pow(saturate(m), 200) * 0.25) * _MoonIntensity * step(0, y);

                // 별: 하늘 위쪽에 드문드문
                float3 cell = floor(d * 180);
                float h = hash(cell);
                float star = step(0.9965, h) * saturate(y * 3);
                col += star * _StarIntensity * (0.6 + 0.4 * hash(cell + 7));

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
