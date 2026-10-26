using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Code2Create.UnitySandbox.LocalDemo.Editor")]

namespace Code2Create.UnitySandbox.LocalDemo
{
    // 描画試作用の計算。Protocolの座標型や世界定数ではない。
    internal static class DemoCoordinates
    {
        internal static Vector3Int Cell(Vector3 point) => Vector3Int.FloorToInt(point);
        internal static Vector3 Center(Vector3Int cell) => (Vector3)cell + Vector3.one * 0.5f;
        internal static Vector3Int HitCell(RaycastHit hit) => Cell(hit.point - hit.normal * 0.001f);
        internal static Vector3Int Relative(Vector3Int cell, Vector3Int origin) => cell - origin;

        internal static Vector3Int PlotOrigin(int index)
        {
            if (index < 0 || index > 8) throw new System.ArgumentOutOfRangeException(nameof(index));
            return new Vector3Int((index % 3 - 1) * 100, 0, (index / 3 - 1) * 100);
        }
    }
}
