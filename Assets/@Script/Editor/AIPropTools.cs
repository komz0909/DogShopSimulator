using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DogShop.EditorTools
{
    /// <summary>
    /// VARCO 프롭의 텍스처 추출과 머티리얼 정리. 임포트 콜백 안에서 하면 재임포트 루프가 나므로
    /// 명시적으로 호출한다.
    ///
    /// 임포트 설정(스케일·노멀맵 타입·해상도)은 AIPropPostprocessor가 자동으로 처리한다.
    /// </summary>
    public static class AIPropTools
    {

        [MenuItem("DogShop/AI 프롭 후처리 + 리포트")]
        public static void ProcessAllMenu() => Debug.Log(ProcessAll());

        /// <summary>AIProps 폴더의 모든 FBX를 후처리하고 리포트를 돌려준다.</summary>
        public static string ProcessAll()
        {
            string root = AIPropPostprocessor.RootFolder.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root)) return "폴더 없음: " + root;

            List<string> models = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { root }))
                models.Add(AssetDatabase.GUIDToAssetPath(guid));

            if (models.Count == 0) return "FBX 없음: " + root;

            // 1단계: 임베드 텍스처를 각 FBX가 있는 <b>자기 폴더</b>로 뽑는다.
            // 모든 프롭의 텍스처 이름이 diffuse/normal/orm으로 같아서, 한 폴더에 모으면
            // 서로를 덮어쓰고 프롭이 남의 텍스처를 입는다. 폴더를 나눠야 충돌이 없다.
            foreach (string path in models)
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                importer.ExtractTextures(folder);
            }
            AssetDatabase.Refresh();

            // 2단계: 텍스처가 생긴 뒤 다시 임포트하면 BaseMap/BumpMap이 자동 연결된다.
            foreach (string path in models)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            // 3단계: 머티리얼 값 고정 + 검증 리포트
            StringBuilder report = new StringBuilder();
            report.AppendLine("AI 프롭 후처리 — " + models.Count + "개");
            report.AppendLine();

            foreach (string path in models)
                report.AppendLine(WireAndReport(path));

            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        static string WireAndReport(string path)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (model == null) return "  " + fileName + "  — 임포트 실패 (GLB는 Unity가 못 읽는다. FBX로 받을 것)";

            int triangles = 0;
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                Mesh mesh = sub as Mesh;
                if (mesh != null) triangles += mesh.triangles.Length / 3;
            }

            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            int missingTexture = 0;
            int baseMapSize = 0;

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;

                    Texture baseMap = material.GetTexture("_BaseMap");
                    if (baseMap == null) missingTexture++;
                    else baseMapSize = Mathf.Max(baseMapSize, baseMap.width);

                    // ORM은 연결하지 않는다. URP는 _OcclusionMap의 G채널을 읽지만
                    // ORM 표준의 G는 roughness라서, 그냥 물리면 엉뚱한 채널로 음영을 깎아 색이 바랜다.
                    // 스타일라이즈 무광 프롭은 AI가 구워준 음영이 들어간 diffuse 한 장이면 충분하다.
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);

                    EditorUtility.SetDirty(material);
                }
            }

            string warning = "";
            if (triangles > 6500) warning += "  ※폴리곤 과다";
            if (missingTexture > 0) warning += "  ※BaseMap 없음 " + missingTexture + "개";
            if (baseMapSize > 0 && baseMapSize < 2048) warning += "  ※텍스처 " + baseMapSize + " — 라벨이 뭉개진다";

            return "  " + fileName
                 + "   tris " + triangles
                 + "   크기 " + bounds.size.ToString("F2")
                 + "   목표높이 " + AIPropPostprocessor.ParseHeightMeters(path).ToString("0.00") + "m"
                 + "   BaseMap " + baseMapSize
                 + warning;
        }

    }
}
