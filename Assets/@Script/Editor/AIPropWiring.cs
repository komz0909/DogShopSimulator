using System.Text;
using DogShop.Data;
using UnityEditor;
using UnityEngine;

namespace DogShop.EditorTools
{
    /// <summary>
    /// AIProps 폴더의 FBX를 ProductCatalog의 상품에 연결한다.
    /// 파일명 접두어로 매칭하므로 상품이 늘어도 이 표만 고치면 된다.
    /// </summary>
    public static class AIPropWiring
    {
        /// <summary>상품 인덱스 → 프롭 파일명 접두어. ProductCatalog의 순서와 맞춰야 한다.</summary>
        static readonly string[] PropByProductIndex =
        {
            "P_DogFoodBag",     // 0 기본 사료
            "P_Shampoo",        // 1 샴푸
            "P_MedicineCase",   // 2 기본 약품 (붉은 구급 케이스 + 흰 십자)
            "P_CollarRing",     // 3 목줄 (링 + 버클판)
            "P_TreatPouch",     // 4 간식
            "P_ToyBone",        // 5 장난감
            "P_PremiumFood",    // 6 고급 사료
            "P_GiftBox",        // 7 고급 간식 세트
            "P_ImportedFood",   // 8 수입 사료
            "P_GroomingCase"    // 9 프리미엄 미용키트
            // P_ToyBall은 상품에 연결하지 않는다 — 바닥 장식이나 강아지 소품용으로 남겨둔다
        };

        [MenuItem("DogShop/AI 프롭을 상품에 연결")]
        public static void WireMenu() => Debug.Log(Wire());

        public static string Wire()
        {
            const string catalogPath = "Assets/@Resources/Data/ProductCatalog.asset";
            ProductCatalog catalog = AssetDatabase.LoadAssetAtPath<ProductCatalog>(catalogPath);
            if (catalog == null) return "ProductCatalog 없음: " + catalogPath;

            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty products = serialized.FindProperty("products");
            if (products == null) return "products 배열을 찾을 수 없다";

            StringBuilder report = new StringBuilder();
            report.AppendLine("프롭 연결 — 상품 " + products.arraySize + "개");

            int wired = 0, missing = 0;

            for (int i = 0; i < products.arraySize; i++)
            {
                SerializedProperty product = products.GetArrayElementAtIndex(i);
                SerializedProperty nameKo = product.FindPropertyRelative("nameKo");
                SerializedProperty propField = product.FindPropertyRelative("propPrefab");
                if (propField == null) continue;

                string label = nameKo != null ? nameKo.stringValue : ("#" + i);

                if (i >= PropByProductIndex.Length)
                {
                    report.AppendLine("  " + label + "  — 매칭 표에 없음");
                    missing++;
                    continue;
                }

                GameObject found = FindProp(PropByProductIndex[i]);
                propField.objectReferenceValue = found;

                if (found == null)
                {
                    report.AppendLine("  " + label + "  — " + PropByProductIndex[i] + "* FBX 없음");
                    missing++;
                    continue;
                }

                // 부피가 큰 상품은 상자에서 2칸을 차지한다
                float largest = MeasureLargestDimension(found);
                int slots = largest >= BulkyThresholdMeters ? 2 : 1;

                SerializedProperty slotField = product.FindPropertyRelative("slotCost");
                if (slotField != null) slotField.intValue = slots;

                report.AppendLine("  " + label.PadRight(14) + found.name.PadRight(22)
                    + "최대변 " + largest.ToString("0.00") + "m   " + slots + "칸");
                wired++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            report.AppendLine();
            report.AppendLine("연결 " + wired + "개 / 미연결 " + missing + "개");
            return report.ToString();
        }

        /// <summary>이 크기를 넘으면 상자에서 2칸을 차지한다. 사료 봉지 3종이 여기 걸린다.</summary>
        const float BulkyThresholdMeters = 0.40f;

        /// <summary>회전을 보존한 상태의 최대 변 길이. 프롭 루트에는 축 변환 보정 회전이 들어 있다.</summary>
        static float MeasureLargestDimension(GameObject prefab)
        {
            GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.transform.position = Vector3.zero;

            Renderer[] renderers = probe.GetComponentsInChildren<Renderer>(true);
            float largest = 0f;
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            }

            Object.DestroyImmediate(probe);
            return largest;
        }

        /// <summary>파일명이 접두어로 시작하는 FBX를 찾는다(높이 접미어 _h35는 무시).</summary>
        static GameObject FindProp(string prefix)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/@Resources/AIProps" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }
    }
}
