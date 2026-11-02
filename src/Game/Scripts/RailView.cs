using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>
/// Dibuja las vías agrupadas por zonas: una malla por zona en vez de una por tramo (H-3).
/// Un cambio solo rehace las zonas afectadas. El resaltado de un tramo usa una copia aparte.
/// </summary>
public sealed class RailView
{
    private const int ZoneCells = 32;

    private readonly Node _parent;
    private readonly Func<RailNode, RailNode, byte, double, Vector3> _point;
    private readonly Dictionary<int, (int a, int b, byte shape, (int x, int z) zone)> _known = new();
    private readonly Dictionary<(int x, int z), HashSet<int>> _zones = new();
    private readonly Dictionary<(int x, int z), MeshInstance3D> _meshes = new();
    private readonly HashSet<(int x, int z)> _dirty = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _gone = new();
    private int _planet = -1;
    private int _stamp = int.MinValue;
    private MeshInstance3D? _highlight;
    private int _highlightId = -1;

    public RailView(Node parent, Func<RailNode, RailNode, byte, double, Vector3> point)
    {
        _parent = parent;
        _point = point;
    }

    public int SegmentCount => _known.Count;
    public int ZoneCount => _meshes.Count;

    /// <summary>Solo trabaja si cambió la estructura o el planeta.</summary>
    public void Sync(World w, int planet, int structuralStamp)
    {
        if (planet == _planet && structuralStamp == _stamp) return;
        if (planet != _planet) Clear();
        _planet = planet;
        _stamp = structuralStamp;

        _seen.Clear();
        foreach (var s in w.RailSegments.Values)
        {
            if (s.Planet != planet || !w.RailNodes.TryGetValue(s.A, out var a) || !w.RailNodes.TryGetValue(s.B, out var b)) continue;
            _seen.Add(s.Id);
            if (_known.TryGetValue(s.Id, out var old))
            {
                if (old.a == s.A && old.b == s.B && old.shape == s.Shape) continue;
                Forget(s.Id, old.zone);
            }
            var zone = ZoneOf(a, b);
            _known[s.Id] = (s.A, s.B, s.Shape, zone);
            if (!_zones.TryGetValue(zone, out var members)) _zones[zone] = members = new HashSet<int>();
            members.Add(s.Id);
            _dirty.Add(zone);
        }
        if (_seen.Count != _known.Count)
        {
            _gone.Clear();
            foreach (var id in _known.Keys)
                if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Forget(id, _known[id].zone);
                _known.Remove(id);
            }
        }
        foreach (var zone in _dirty) Rebuild(w, zone);
        _dirty.Clear();
        if (_highlight != null && !_known.ContainsKey(_highlightId)) DropHighlight();
    }

    /// <summary>Copia de un solo tramo, para resaltarlo sin teñir la zona entera. Dibuja encima de la zona con la misma geometría.</summary>
    public GeometryInstance3D? HighlightOf(World w, int id)
    {
        if (id == _highlightId && _highlight != null) return _highlight;
        DropHighlight();
        if (!w.RailSegments.TryGetValue(id, out var s) || !w.RailNodes.TryGetValue(s.A, out var a) || !w.RailNodes.TryGetValue(s.B, out var b)) return null;
        _highlight = new MeshInstance3D
        {
            Mesh = Models.RailMesh(t => _point(a, b, s.Shape, t), s.LengthMm / 1000f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _highlightId = id;
        _parent.AddChild(_highlight);
        return _highlight;
    }

    public void Clear()
    {
        foreach (var mesh in _meshes.Values) mesh.QueueFree();
        _meshes.Clear();
        _zones.Clear();
        _known.Clear();
        _dirty.Clear();
        DropHighlight();
        _planet = -1;
        _stamp = int.MinValue;
    }

    private void DropHighlight()
    {
        _highlight?.QueueFree();
        _highlight = null;
        _highlightId = -1;
    }

    private void Forget(int id, (int x, int z) zone)
    {
        if (_zones.TryGetValue(zone, out var members)) members.Remove(id);
        _dirty.Add(zone);
    }

    // Un tramo pertenece a la zona de su punto medio; su malla puede salirse un poco y el AABB lo cubre.
    private static (int x, int z) ZoneOf(RailNode a, RailNode b) =>
        (Mathf.FloorToInt((a.X + b.X) / 2f / ZoneCells), Mathf.FloorToInt((a.Z + b.Z) / 2f / ZoneCells));

    private void Rebuild(World w, (int x, int z) zone)
    {
        if (!_zones.TryGetValue(zone, out var members) || members.Count == 0)
        {
            _zones.Remove(zone);
            if (_meshes.Remove(zone, out var empty)) empty.QueueFree();
            return;
        }
        var builder = new Models.Builder();
        foreach (var id in members)
        {
            var s = w.RailSegments[id];
            var a = w.RailNodes[s.A];
            var b = w.RailNodes[s.B];
            Models.AppendRail(builder, t => _point(a, b, s.Shape, t), s.LengthMm / 1000f);
        }
        if (!_meshes.TryGetValue(zone, out var mesh))
        {
            mesh = new MeshInstance3D();
            _parent.AddChild(mesh);
            _meshes[zone] = mesh;
        }
        mesh.Mesh = builder.Commit();
    }
}
