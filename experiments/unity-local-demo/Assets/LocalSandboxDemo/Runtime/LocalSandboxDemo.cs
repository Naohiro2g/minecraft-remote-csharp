using UnityEngine;

namespace Code2Create.UnitySandbox.LocalDemo
{
    [DisallowMultipleComponent]
    public sealed class LocalSandboxDemo : MonoBehaviour
    {
        [SerializeField] private Vector3Int displayOrigin = new Vector3Int(100, 0, 100);
        [SerializeField, Range(1, 20)] private int columnHeight = 10;
        [SerializeField] private Font hudFont;
        private DemoPalette palette;
        private DemoBlocks blocks;
        private GameObject generated;
        private LocalSandboxWalker walker;
        private GUIStyle textStyle;
        private string layoutName;

        internal DemoBlocks Blocks => blocks;
        internal Camera View => walker.View;
        internal Vector3Int DisplayOrigin => displayOrigin;

        private void Start() => Build();

        internal void Build()
        {
            if (generated != null) return;
            palette = new DemoPalette();
            generated = new GameObject("Local demo (generated)");
            generated.transform.SetParent(transform, false);
            var ground = DemoBlocks.Cube("Fixed ground: top cell y=62", generated.transform,
                new Vector3(0, 31.5f, 0), new Vector3(400, 63, 400), palette.GrassTop);
            // 地面全体に1セル用の境界線を引き延ばさない。
            var groundMaterial = new Material(palette.GrassTop) { mainTexture = null, name = "Demo ground" };
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            ground.AddComponent<DemoOwnedMaterial>().Value = groundMaterial;

            for (int i = 0; i < 9; i++)
            {
                var origin = DemoCoordinates.PlotOrigin(i);
                var color = i == 4 ? new Color(1, .8f, .2f) : i == 8 ? new Color(.2f, .8f, 1) : new Color(.85f, .9f, .75f);
                var p = (Vector3)origin + Vector3.up * 63.015f;
                Line("Plot " + i, color, .08f, new[] {
                    p + new Vector3(-50, 0, -50), p + new Vector3(50, 0, -50),
                    p + new Vector3(50, 0, 50), p + new Vector3(-50, 0, 50), p + new Vector3(-50, 0, -50)
                });
                Line("Origin " + i + " X", color, .12f, new[] { p + Vector3.left * 2, p + Vector3.right * 2 });
                Line("Origin " + i + " Z", color, .12f, new[] { p + Vector3.back * 2, p + Vector3.forward * 2 });
            }
            var sunObject = new GameObject("Demo sun");
            sunObject.transform.SetParent(generated.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(48, -30, 0);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;

            blocks = new DemoBlocks(generated.transform, palette);
            Populate(false);
            var player = new GameObject("Feet");
            player.transform.SetParent(generated.transform, false);
            player.layer = 2; // Ignore Raycast: 自分の身体を照準に含めない。
            walker = player.AddComponent<LocalSandboxWalker>();
            walker.Initialize(displayOrigin);
        }

        internal void Populate(bool alternate)
        {
            blocks.Clear();
            layoutName = alternate ? "壁・階段" : "材質比較：列と柱";
            var origin = new Vector3Int(100, 63, 100); // 配置用。HUDの原点設定とは独立。
            for (int m = 0; m < 6; m++)
            {
                var kind = (DemoMaterial)m;
                var start = origin + new Vector3Int(-36 + m * 12, 0, 0);
                if (!alternate)
                {
                    for (int x = 0; x < 10; x++) blocks.Put(start + new Vector3Int(x, 0, 0), kind);
                    for (int y = 0; y < columnHeight; y++) blocks.Put(start + new Vector3Int(4, y, 4), kind);
                }
                else
                {
                    for (int x = 0; x < 5; x++)
                    for (int y = 0; y < 3; y++) blocks.Put(start + new Vector3Int(x, y, 0), kind);
                    for (int step = 0; step < 4; step++)
                    for (int y = 0; y <= step; y++) blocks.Put(start + new Vector3Int(step, y, 5), kind);
                }
            }
            Physics.SyncTransforms();
        }

        private void Line(string name, Color color, float width, Vector3[] points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(generated.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = palette.Guide;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) Populate(false);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Populate(true);
#endif
        }

        private void OnGUI()
        {
            if (walker == null) return;
            if (textStyle == null)
            {
                if (hudFont == null) hudFont = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK JP", "Yu Gothic", "Hiragino Sans", "Arial" }, 18);
                textStyle = new GUIStyle(GUI.skin.label) { font = hudFont, fontSize = 18, wordWrap = true };
                textStyle.normal.textColor = Color.white;
            }
            float width = Mathf.Min(650, Screen.width - 24);
            float panelHeight = Mathf.Min(Screen.height - 24, width < 560 ? 340 : 245);
            GUI.Box(new Rect(12, 12, width, panelHeight), GUIContent.none);
            GUILayout.BeginArea(new Rect(24, 20, width - 24, panelHeight - 16));
            GUILayout.Label(layoutName + "  /  " + blocks.Count + "個", textStyle);
            GUILayout.Label("材質：金 → 鉄 → シーランタン → ガラス → ダイヤモンド → 草", textStyle);
            var feet = DemoCoordinates.Cell(walker.transform.position);
            GUILayout.Label("足  世界 " + feet + "  原点から " + DemoCoordinates.Relative(feet, displayOrigin), textStyle);
            var ray = View.ViewportPointToRay(new Vector3(.5f, .5f));
            string target = "—";
            if (Physics.Raycast(ray, out var hit, 120f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var cell = DemoCoordinates.HitCell(hit);
                target = "世界 " + cell + "  原点から " + DemoCoordinates.Relative(cell, displayOrigin);
            }
            GUILayout.Label("照準  " + target, textStyle);
            GUILayout.Label("表示原点 " + displayOrigin + "  /  中央区画4：金色、区画8：水色", textStyle);
#if ENABLE_LEGACY_INPUT_MANAGER
            GUILayout.Label("クリックで見回す / Escで解放 / WASD・矢印で歩く / Rで戻る / 1・2で場面切替", textStyle);
#else
            GUILayout.Label("入力の設定：Active Input HandlingをInput Manager (Old)またはBothへ変更してください。", textStyle);
#endif
            GUILayout.EndArea();
            GUI.Label(new Rect(Screen.width / 2f - 8, Screen.height / 2f - 14, 24, 28), "+", textStyle);
        }

        private void OnDestroy()
        {
            DemoPalette.Release(generated);
            palette?.Dispose();
        }
    }

    internal sealed class DemoOwnedMaterial : MonoBehaviour
    {
        internal Material Value;
        private void OnDestroy() => DemoPalette.Release(Value);
    }
}
