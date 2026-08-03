using UnityEditor;

namespace Lingmai.Editor
{
    public sealed class EnvironmentTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Environment/")) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 8;
            if (assetPath.Contains("_nor_"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else if (assetPath.Contains("_rough_"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
            }
        }

        public static void LogEnvironmentModelMaterials()
        {
            string[] paths =
            {
                "Assets/Resources/Environment/Models/pine_sapling_small/pine_sapling_small_1k.fbx",
                "Assets/Resources/Environment/Models/rock_moss_set_02/rock_moss_set_02_1k.fbx",
                "Assets/Resources/Environment/Models/fern_02/fern_02_1k.fbx",
                "Assets/Resources/Environment/Models/shrub_02/shrub_02_1k.fbx",
                "Assets/Resources/Environment/Models/island_tree_01/island_tree_01_1k.fbx"
            };
            foreach (string path in paths)
            {
                UnityEngine.GameObject model = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                if (model == null) continue;
                foreach (UnityEngine.Renderer renderer in model.GetComponentsInChildren<UnityEngine.Renderer>(true))
                {
                    string names = string.Empty;
                    foreach (UnityEngine.Material material in renderer.sharedMaterials)
                        names += (material != null ? material.name : "<null>") + ";";
                    UnityEngine.Debug.Log("ENV_MODEL_RENDERER path=" + path + " renderer=" + renderer.name + " materials=" + names);
                }
            }
        }
    }
}
