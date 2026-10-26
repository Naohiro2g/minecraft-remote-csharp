using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Code2Create.UnitySandbox.LocalDemo
{
    // ローカルな材質の並び。正式catalogのIDへ対応づけるものではない。
    internal enum DemoMaterial { Gold, Iron, Lantern, Glass, Diamond, Grass }

    internal sealed class DemoPalette : System.IDisposable
    {
        private readonly List<Object> owned = new List<Object>();
        private readonly Material[] materials = new Material[6];
        internal Material GrassTop { get; }
        internal Material Guide { get; }

        internal DemoPalette()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) throw new System.InvalidOperationException("Standard shaderが必要です。Built-in Render Pipelineで開いてください。");
            var colors = new[] {
                new Color(1f, .72f, .17f), new Color(.78f, .83f, .88f),
                new Color(.66f, 1f, .88f), new Color(.48f, .85f, 1f, .42f),
                new Color(.1f, .79f, .87f), new Color(.42f, .27f, .13f)
            };
            var border = BorderTexture();
            for (int i = 0; i < materials.Length; i++)
            {
                var m = Own(new Material(shader) { name = "Demo " + (DemoMaterial)i, color = colors[i], mainTexture = border });
                m.SetFloat("_Metallic", i < 2 ? .55f : .05f);
                m.SetFloat("_Glossiness", i < 2 ? .45f : .22f);
                if (i == (int)DemoMaterial.Lantern)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetTexture("_EmissionMap", border);
                    m.SetColor("_EmissionColor", colors[i] * .18f);
                }
                if (i == (int)DemoMaterial.Glass)
                {
                    m.SetFloat("_Mode", 3);
                    m.SetInt("_SrcBlend", (int)BlendMode.One);
                    m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                materials[i] = m;
            }
            GrassTop = Own(new Material(shader) { name = "Demo grass top", color = new Color(.29f, .53f, .18f), mainTexture = border });
            Guide = Own(new Material(Shader.Find("Sprites/Default")) { name = "Demo guides" });
        }

        internal Material this[DemoMaterial kind] => materials[(int)kind];

        private Texture2D BorderTexture()
        {
            var texture = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Generated cell border", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = x < 1 || y < 1 || x > 30 || y > 30 ? new Color(.24f, .27f, .3f) : Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        public void Dispose()
        {
            foreach (var value in owned) Release(value);
            owned.Clear();
        }

        internal static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
