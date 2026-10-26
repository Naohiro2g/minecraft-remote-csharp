using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Code2Create.UnitySandbox.LocalDemo
{
    public static class DemoEditor
    {
        [MenuItem("Tools/Local Sandbox/Create Demo Scene")]
        public static void CreateScene()
        {
            CheckPipeline();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Local Sandbox Demo").AddComponent<LocalSandboxDemo>();
            Directory.CreateDirectory("Assets/LocalSandboxDemo/Scenes");
            EditorSceneManager.SaveScene(scene, AssetDatabase.GenerateUniqueAssetPath("Assets/LocalSandboxDemo/Scenes/LocalSandboxDemo.unity"));
            Selection.activeObject = GameObject.Find("Local Sandbox Demo");
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(100, 64, 100), Quaternion.Euler(25, 0, 0), 25);
            Debug.Log("デモシーンを作成しました。Playで開始します。");
        }

        [MenuItem("Tools/Local Sandbox/Validate Demo")]
        public static void Validate()
        {
            CheckPipeline();
            // 利用者の編集中のシーンを保存・破棄せず、独立したsceneで検証する。
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Validation demo");
                SceneManager.MoveGameObjectToScene(root, scene);
                var demo = root.AddComponent<LocalSandboxDemo>();
                demo.Build();
                Require(DemoCoordinates.Cell(new Vector3(-.01f, 63, -1.01f)) == new Vector3Int(-1, 63, -2), "negative floor");
                Require(DemoCoordinates.Center(new Vector3Int(-1, 62, -1)) == new Vector3(-.5f, 62.5f, -.5f), "cell center");
                Require(DemoCoordinates.PlotOrigin(0) == new Vector3Int(-100, 0, -100), "plot 0");
                Require(DemoCoordinates.PlotOrigin(4) == Vector3Int.zero, "plot 4");
                Require(DemoCoordinates.PlotOrigin(8) == new Vector3Int(100, 0, 100), "plot 8");
                Require(DemoCoordinates.Relative(new Vector3Int(120, 63, 130), demo.DisplayOrigin) == new Vector3Int(20, 63, 30), "relative coordinates");
                Require(demo.Blocks.Count == 120, "six materials, ten in row and ten in column");
                Physics.SyncTransforms();
                var physics = scene.GetPhysicsScene();
                Require(physics.Raycast(new Vector3(-.01f, 80, -1.01f), Vector3.down, out var ground, 30), "ground collider");
                Require(DemoCoordinates.HitCell(ground) == new Vector3Int(-1, 62, -2), "ground top y=62");
                var body = demo.GetComponentInChildren<CharacterController>();
                body.Move(Vector3.down * 2);
                Require(DemoCoordinates.Cell(body.transform.position).y == 63, "feet at ground: " + body.transform.position);

                var cell = new Vector3Int(-10, 64, -10);
                demo.Blocks.Put(cell, DemoMaterial.Gold);
                var replacement = demo.Blocks.Put(cell, DemoMaterial.Iron);
                Require(demo.Blocks.Count == 121, "replace keeps count");
                Physics.SyncTransforms();
                foreach (var normal in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                {
                    Require(physics.Raycast(DemoCoordinates.Center(cell) + normal * 2, -normal, out var hit, 2), "six faces ray");
                    Require(hit.collider.gameObject == replacement, "replacement collider");
                    Require(DemoCoordinates.HitCell(hit) == cell, "six faces cell");
                }
                Require(demo.Blocks.Remove(cell) && !demo.Blocks.Remove(cell), "remove and missing cell");
                demo.Populate(true);
                Require(demo.Blocks.Count == 150, "wall and staircase");
                demo.Populate(false);
                Require(demo.Blocks.Count == 120, "scene reset");
                Debug.Log("LOCAL_SANDBOX_VALIDATION_PASS: negative/relative coordinates, plots, six hit faces, ground, replace/remove, scene reset");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // -executeMethod ...DemoEditor.CapturePreview -demoPreview /absolute/path.png
        public static void CapturePreview()
        {
            CheckPipeline();
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D image = null;
            var previousTarget = RenderTexture.active;
            try
            {
                var root = new GameObject("Preview");
                SceneManager.MoveGameObjectToScene(root, scene);
                var demo = root.AddComponent<LocalSandboxDemo>();
                demo.Build();
                demo.View.scene = scene;
                demo.View.transform.position = new Vector3(100, 90, 55);
                demo.View.transform.LookAt(new Vector3(100, 66, 103));
                target = new RenderTexture(1440, 900, 24);
                demo.View.targetTexture = target;
                demo.View.Render();
                RenderTexture.active = target;
                image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
                image.Apply();
                var args = Environment.GetCommandLineArgs();
                int flag = Array.IndexOf(args, "-demoPreview");
                if (flag < 0 || flag + 1 >= args.Length) throw new ArgumentException("-demoPreview <path.png> が必要です。");
                File.WriteAllBytes(args[flag + 1], image.EncodeToPNG());
                Debug.Log("LOCAL_SANDBOX_PREVIEW_SAVED: " + args[flag + 1]);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void CheckPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
                throw new InvalidOperationException("この試作は3D (Built-in Render Pipeline)用です。");
        }

        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("Local sandbox validation failed: " + check);
        }
    }
}
