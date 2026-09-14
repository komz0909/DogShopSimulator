using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace DogShop.EditorTools
{
    /// <summary>
    /// VARCO에서 받은 프롭의 임포트 설정을 자동으로 맞춘다.
    /// 대상은 <c>Assets/@Resources/AIProps/</c> 아래 파일만.
    ///
    /// 파일명에 목표 높이를 cm로 넣는다: <c>P_DogFoodBag_h35.fbx</c> → 0.35m.
    /// 표를 따로 관리하지 않아도 되고 파일명만 보면 크기가 읽힌다.
    /// </summary>
    public class AIPropPostprocessor : AssetPostprocessor
    {
        public const string RootFolder = "Assets/@Resources/AIProps/";
        public const float DefaultHeightMeters = 0.30f;
        public const int MaxTextureSize = 2048;   // 라벨 디테일이 살아남는 최소치. 생성은 4096으로 받고 여기서 줄인다

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(RootFolder)) return;

            ModelImporter importer = (ModelImporter)assetImporter;

            // useFileScale은 절대 끄지 말 것 — FBX의 cm 단위 보정이 사라져 35배로 커진다.
            importer.useFileScale = true;
            importer.globalScale = ParseHeightMeters(assetPath);

            // 축 변환을 메시에 굽는다. 굽지 않으면 루트에 (270, 0, 0) 보정 회전이 남아,
            // 배치 코드가 회전을 identity로 덮어쓰는 순간 프롭이 눕는다.
            importer.bakeAxisConversion = true;

            // 머티리얼을 외부 .mat로 빼야 ORM 배선이 재임포트 후에도 살아남는다.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;

            // 프롭은 정적 오브젝트다
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.isReadable = false;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(RootFolder)) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

            // 노멀맵은 타입을 지정해야 한다. Default로 두면 컬러 텍스처로 취급되어 음영이 깨진다.
            if (name.Contains("normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else if (name.Contains("orm") || name.Contains("metallic") || name.Contains("roughness"))
            {
                // ORM은 색이 아니라 데이터다
                importer.sRGBTexture = false;
            }

            // 생성은 4096으로 받아 라벨을 선명하게 굽고, 게임에는 2048로 줄여 쓴다
            if (importer.maxTextureSize > MaxTextureSize) importer.maxTextureSize = MaxTextureSize;
        }

        /// <summary>파일명의 <c>_h35</c> 같은 접미사를 미터로 읽는다. 없으면 기본값.</summary>
        public static float ParseHeightMeters(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            int marker = name.LastIndexOf("_h", System.StringComparison.Ordinal);
            if (marker < 0) return DefaultHeightMeters;

            string digits = name.Substring(marker + 2);
            int centimeters;
            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out centimeters)) return DefaultHeightMeters;
            if (centimeters <= 0) return DefaultHeightMeters;

            return centimeters / 100f;
        }
    }
}
