using System;
using UnityEditor;
using UnityEngine;

namespace Code2Create.UnitySandbox.LocalDemo
{
    // batchmode専用。Start後の初期化と重力・当たり判定を実際のPlay Modeで確かめる。
    [InitializeOnLoad]
    public static class DemoPlaySmoke
    {
        private const string Running = "LocalSandbox.PlaySmoke.Running";
        private static double startedAt;
        private static bool sawError;

        static DemoPlaySmoke()
        {
            if (!SessionState.GetBool(Running, false)) return;
            startedAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("この検証はbatchmode専用です。");
            DemoEditor.CreateScene();
            SessionState.SetBool(Running, true);
            startedAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string message, string trace, LogType kind)
        {
            if (kind == LogType.Exception || kind == LogType.Error || kind == LogType.Assert) sawError = true;
        }

        private static void Tick()
        {
            double elapsed = EditorApplication.timeSinceStartup - startedAt;
            if (elapsed > 45) { Finish(false, "Play Mode timeout"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || elapsed < 3) return;
            try
            {
                var demo = UnityEngine.Object.FindFirstObjectByType<LocalSandboxDemo>();
                if (demo == null || demo.Blocks == null || demo.View == null) throw new Exception("Start initialization");
                var body = demo.GetComponentInChildren<CharacterController>();
                if (body == null || DemoCoordinates.Cell(body.transform.position).y != 63) throw new Exception("feet did not settle on y=63");
                var before = body.transform.position;
                body.Move(Vector3.forward);
                body.Move(Vector3.down * 2);
                if (body.transform.position.z - before.z < .9f || DemoCoordinates.Cell(body.transform.position).y != 63)
                    throw new Exception("walking or fixed ground collision");
                if (Mathf.Abs(demo.View.transform.position.y - body.transform.position.y - 1.6f) > .001f)
                    throw new Exception("eye offset from feet");
                if (sawError) throw new Exception("runtime error in log");
                Finish(true, "Start initialization, feet y=63, walking, ground collision, eye offset");
            }
            catch (Exception error) { Finish(false, error.Message); }
        }

        private static void Finish(bool passed, string reason)
        {
            SessionState.SetBool(Running, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Debug.Log("LOCAL_SANDBOX_PLAY_SMOKE_" + (passed ? "PASS: " : "FAIL: ") + reason);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
