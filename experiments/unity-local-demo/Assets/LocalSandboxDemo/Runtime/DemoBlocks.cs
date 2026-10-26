using System.Collections.Generic;
using UnityEngine;

namespace Code2Create.UnitySandbox.LocalDemo
{
    internal sealed class DemoBlocks
    {
        private readonly Transform parent;
        private readonly DemoPalette palette;
        private readonly Dictionary<Vector3Int, GameObject> cells = new Dictionary<Vector3Int, GameObject>();
        internal int Count => cells.Count;

        internal DemoBlocks(Transform parent, DemoPalette palette) { this.parent = parent; this.palette = palette; }

        internal GameObject Put(Vector3Int cell, DemoMaterial kind)
        {
            Remove(cell);
            var block = Cube(kind.ToString(), parent, DemoCoordinates.Center(cell), Vector3.one, palette[kind]);
            if (kind == DemoMaterial.Grass)
            {
                // 上面を緑、側面を土色にする。照準と衝突は親の1セルのcolliderで扱う。
                var top = Cube("Grass top", block.transform, (Vector3)cell + new Vector3(.5f, 1f, .5f), new Vector3(.98f, .02f, .98f), palette.GrassTop);
                top.GetComponent<Collider>().enabled = false;
            }
            cells.Add(cell, block);
            return block;
        }

        internal bool Remove(Vector3Int cell)
        {
            if (!cells.TryGetValue(cell, out var block)) return false;
            // Destroyはframe末尾なので、置換時には先に衝突と表示を無効にする。
            block.SetActive(false);
            DemoPalette.Release(block);
            cells.Remove(cell);
            return true;
        }

        internal void Clear()
        {
            foreach (var block in cells.Values) { block.SetActive(false); DemoPalette.Release(block); }
            cells.Clear();
        }

        internal static GameObject Cube(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = center;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }
    }
}
