using System.Collections.Generic;
using System.IO;
using System.Text;
using DogShop.Data;
using UnityEditor;
using UnityEngine;

namespace DogShop.EditorTools
{
    /// <summary>
    /// 상점창에 띄울 물건 사진을 굽는다. <b>그림을 따로 그리지 않고 실제 모델을 찍는다</b> —
    /// 창에서 고른 것과 진열대에 올라가는 것이 같아야 하고, 프롭을 갈아끼우면 사진도 같이 바뀐다.
    ///
    /// 배경은 두 번 찍어서 지운다. 검정 배경 한 장과 흰 배경 한 장을 찍으면 밝아진 만큼이
    /// 그대로 비친 배경이라, URP의 카메라 알파가 어떻게 나오든 상관없이 경계가 깨끗하게 떨어진다.
    /// 색 하나를 골라 빼는 방식은 반투명한 가장자리에 그 색 테두리를 남긴다.
    ///
    /// 조명은 씬 조명을 잠시 끄고 여기서 만든 두 개만 쓴다. 그래야 씬의 시간대나 조명 설정을
    /// 건드려도 사진이 늘 같은 밝기로 나온다.
    /// </summary>
    public static class ThumbnailBaker
    {
        const string OutputFolder = "Assets/@Resources/UI/Thumb";
        const string CatalogPath = "Assets/@Resources/Data/ProductCatalog.asset";
        const string FurniturePath = "Assets/@Resources/Data/FurnitureCatalog.asset";

        /// <summary>사진 한 변. 카드가 120px 남짓이라 두 배로 굽고 줄여 쓴다.</summary>
        const int Size = 192;

        /// <summary>물건을 담을 여백. 1이면 꽉 차고, 크면 주변이 빈다.</summary>
        const float Margin = 1.08f;

        /// <summary>씬에서 멀리 떨어진 촬영장. 가게 안의 물건이 사진에 끼어들지 않게 한다.</summary>
        static readonly Vector3 Stage = new Vector3(0f, 500f, 0f);

        [MenuItem("DogShop/상품 사진 굽기")]
        public static void BakeMenu() => Debug.Log(Bake());

        public static string Bake()
        {
            // 씬 조명을 껐다 켜고 씬에 임시 물체를 세우므로, 플레이 중에 돌리면 그 장면을 망친다
            if (Application.isPlaying) return "플레이 중에는 굽지 않는다 — 정지하고 다시 실행할 것";

            Directory.CreateDirectory(OutputFolder);

            StringBuilder report = new StringBuilder();
            List<string> imported = new List<string>();

            List<Light> dimmed = DimSceneLights();
            GameObject rig = BuildLightRig();

            try
            {
                report.AppendLine(BakeProducts(imported));
                report.AppendLine(BakeFurniture(imported));
            }
            finally
            {
                Object.DestroyImmediate(rig);
                for (int i = 0; i < dimmed.Count; i++) dimmed[i].enabled = true;
            }

            AssetDatabase.Refresh();
            for (int i = 0; i < imported.Count; i++) ApplyImportSettings(imported[i]);

            report.AppendLine(Wire());
            return report.ToString();
        }

        // ---- 찍는 대상 ----

        static string BakeProducts(List<string> imported)
        {
            ProductCatalog catalog = AssetDatabase.LoadAssetAtPath<ProductCatalog>(CatalogPath);
            if (catalog == null) return "ProductCatalog 없음: " + CatalogPath;

            int done = 0, missing = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                GameObject source = catalog.Get(i).propPrefab;
                if (source == null) { missing++; continue; }

                string path = Shoot(source);
                if (path == null) { missing++; continue; }

                imported.Add(path);
                done++;
            }
            return "상품 사진 " + done + "장" + (missing > 0 ? " (프롭 없음 " + missing + ")" : "");
        }

        static string BakeFurniture(List<string> imported)
        {
            FurnitureCatalog catalog = AssetDatabase.LoadAssetAtPath<FurnitureCatalog>(FurniturePath);
            if (catalog == null) return "FurnitureCatalog 없음 — 가구 사진은 건너뛴다";

            int done = 0, missing = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                GameObject source = catalog.Get(i).prefab;
                if (source == null) { missing++; continue; }

                string path = Shoot(source, catalog.Get(i).material, catalog.Get(i).iconYaw);
                if (path == null) { missing++; continue; }

                imported.Add(path);
                done++;
            }
            return "가구 사진 " + done + "장" + (missing > 0 ? " (모델 없음 " + missing + ")" : "");
        }

        // ---- 촬영 ----

        static string Shoot(GameObject source, Material material = null, float yaw = 0f)
        {
            GameObject instance = Object.Instantiate(source);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = Stage;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;

                if (material != null)
                    for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = material;

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                Texture2D shot = Capture(bounds);
                string path = OutputFolder + "/Thumb_" + Slug(source.name) + ".png";
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                return path;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// 같은 자리에서 배경만 바꿔 두 장 찍고 합친다. 흰 배경에서 밝아진 만큼이 비쳐 보인
        /// 배경이므로 알파는 1에서 그 차이를 뺀 값이고, 색은 검정 배경 쪽을 알파로 나누면 된다.
        /// </summary>
        static Texture2D Capture(Bounds bounds)
        {
            GameObject camGo = new GameObject("__thumbCam") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;                       // 아이콘은 원근이 없어야 크기 비교가 된다
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 60f;
            cam.allowHDR = false;

            float radius = bounds.extents.magnitude;

            Vector3 view = new Vector3(0.62f, 0.48f, -1f).normalized;   // 살짝 위에서 본 3/4 각도
            camGo.transform.position = bounds.center + view * (radius * 4f + 1f);
            camGo.transform.LookAt(bounds.center);

            // 화면에 실제로 차지하는 크기로 맞춘다. 외접구 반지름으로 잡으면 길쭉한 물건일수록
            // 남는 여백이 커져서, 사료 봉지와 목줄이 카드 안에서 서로 다른 크기로 보인다.
            cam.orthographicSize = Mathf.Max(0.05f, ScreenExtent(bounds.extents, camGo.transform) * Margin);

            RenderTexture rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            Texture2D onBlack = RenderOnce(cam, rt, Color.black);
            Texture2D onWhite = RenderOnce(cam, rt, Color.white);

            Color[] black = onBlack.GetPixels();
            Color[] white = onWhite.GetPixels();
            Color[] pixels = new Color[black.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                float lifted = Mathf.Max(Mathf.Max(white[i].r - black[i].r, white[i].g - black[i].g),
                                         white[i].b - black[i].b);
                float alpha = Mathf.Clamp01(1f - lifted);

                if (alpha <= 0.004f) { pixels[i] = Color.clear; continue; }

                pixels[i] = new Color(
                    Mathf.Clamp01(black[i].r / alpha),
                    Mathf.Clamp01(black[i].g / alpha),
                    Mathf.Clamp01(black[i].b / alpha),
                    alpha);
            }

            Texture2D result = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            result.SetPixels(pixels);
            result.Apply();

            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(camGo);
            rt.Release();
            Object.DestroyImmediate(rt);
            return result;
        }

        /// <summary>
        /// 이 각도에서 봤을 때 상자가 화면에서 차지하는 반폭·반높이 중 큰 쪽.
        /// 회전한 축에 상자의 세 변을 각각 투영해 더한 값이 그 방향의 반지름이다.
        /// </summary>
        static float ScreenExtent(Vector3 extents, Transform cam)
        {
            Vector3 right = cam.right;
            Vector3 up = cam.up;

            float halfWidth = Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y + Mathf.Abs(right.z) * extents.z;
            float halfHeight = Mathf.Abs(up.x) * extents.x + Mathf.Abs(up.y) * extents.y + Mathf.Abs(up.z) * extents.z;

            return Mathf.Max(halfWidth, halfHeight);   // 사진이 정사각형이라 긴 쪽에 맞춘다
        }

        static Texture2D RenderOnce(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }

        // ---- 조명 ----

        static List<Light> DimSceneLights()
        {
            List<Light> dimmed = new List<Light>();
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i].enabled) continue;
                lights[i].enabled = false;
                dimmed.Add(lights[i]);
            }
            return dimmed;
        }

        static GameObject BuildLightRig()
        {
            GameObject rig = new GameObject("__thumbLights") { hideFlags = HideFlags.HideAndDontSave };
            rig.transform.position = Stage;

            AddLight(rig, new Vector3(35f, -35f, 0f), 1.30f, new Color(1f, 0.98f, 0.94f));   // 주광, 왼쪽 위
            AddLight(rig, new Vector3(20f, 145f, 0f), 0.60f, new Color(0.86f, 0.90f, 1f));   // 보조광, 뒤쪽
            return rig;
        }

        static void AddLight(GameObject parent, Vector3 euler, float intensity, Color color)
        {
            GameObject go = new GameObject("light") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(parent.transform, false);
            go.transform.rotation = Quaternion.Euler(euler);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;   // 그림자는 아이콘 안에서 형태만 어지럽힌다
        }

        // ---- 파일 ----

        /// <summary>
        /// P_DogFoodBag_h45 → DogFoodBag. 높이 접미어를 이름에 남기면 모델을 다시 뽑을 때마다
        /// 안 쓰는 사진 파일이 하나씩 쌓인다.
        /// </summary>
        static string Slug(string assetName)
        {
            string name = assetName;
            if (name.StartsWith("P_")) name = name.Substring(2);

            int tail = name.LastIndexOf("_h", System.StringComparison.Ordinal);
            if (tail > 0) name = name.Substring(0, tail);

            return name;
        }

        static void ApplyImportSettings(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Size;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }

        // ---- 카탈로그에 물리기 ----

        static string Wire()
        {
            int wired = 0;
            wired += WireInto(CatalogPath, "products", "propPrefab");
            wired += WireInto(FurniturePath, "items", "prefab");

            AssetDatabase.SaveAssets();
            return "카탈로그에 사진 " + wired + "장 연결";
        }

        static int WireInto(string assetPath, string arrayName, string sourceField)
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null) return 0;

            SerializedObject serialized = new SerializedObject(asset);
            SerializedProperty array = serialized.FindProperty(arrayName);
            if (array == null) return 0;

            int wired = 0;
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty entry = array.GetArrayElementAtIndex(i);
                GameObject source = entry.FindPropertyRelative(sourceField).objectReferenceValue as GameObject;
                if (source == null) continue;

                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    OutputFolder + "/Thumb_" + Slug(source.name) + ".png");
                if (icon == null) continue;

                entry.FindPropertyRelative("icon").objectReferenceValue = icon;
                wired++;
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return wired;
        }
    }
}
