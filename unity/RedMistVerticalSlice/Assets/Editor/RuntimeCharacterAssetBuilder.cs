using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Lingmai.Editor
{
    public static class RuntimeCharacterAssetBuilder
    {
        private const string Root = "Assets/Resources/Characters/UnityStandard";
        private const string PeopleRoot = "Assets/Resources/Characters/PeopleSansPeople";

        public static void Prepare()
        {
            ConfigureModel(Root + "/DefaultMale.fbx", false);
            ConfigureModel(Root + "/DefaultFemale.fbx", false);
            ConfigureModel(Root + "/MaleIdle.fbx", true);
            ConfigureModel(Root + "/FemaleIdle.fbx", true);
            ConfigureModel(PeopleRoot + "/PSP_Person.fbx", false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            CreateOrUpdateController(Root + "/MaleIdle.fbx", Root + "/MaleIdle.controller");
            CreateOrUpdateController(Root + "/FemaleIdle.fbx", Root + "/FemaleIdle.controller");
            AssetDatabase.SaveAssets();

            LogModel("DefaultMale", Root + "/DefaultMale.fbx");
            LogModel("DefaultFemale", Root + "/DefaultFemale.fbx");
            LogModel("PeopleSansPeople", PeopleRoot + "/PSP_Person.fbx");
        }

        private static void ConfigureModel(string path, bool animationOnly)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing Unity character asset: " + path);

            bool changed = importer.animationType != ModelImporterAnimationType.Human ||
                           importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                           importer.importAnimation != animationOnly;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = animationOnly;

            if (animationOnly)
            {
                ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    clip.loopTime = true;
                    clip.loopPose = true;
                    clip.keepOriginalOrientation = true;
                    clip.keepOriginalPositionY = true;
                    clip.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = clips;
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
        }

        private static void CreateOrUpdateController(string animationPath, string controllerPath)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(animationPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
            if (clip == null) throw new InvalidOperationException("No animation clip found in " + animationPath);

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states.ToArray()) machine.RemoveState(child.state);
            AnimatorState state = machine.AddState("Living idle");
            state.motion = clip;
            state.speed = 0.92f;
            machine.defaultState = state;
            EditorUtility.SetDirty(controller);
        }

        private static void LogModel(string label, string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Unable to load character model: " + path);
            int skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            int mesh = prefab.GetComponentsInChildren<MeshRenderer>(true).Length;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Debug.Log("RED_MIST_CHARACTER_READY label=" + label + " skinned=" + skinned + " mesh=" + mesh +
                      " avatar=" + (avatar != null && avatar.isValid && avatar.isHuman));
        }
    }
}
