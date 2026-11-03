using Godot;

namespace Pluton.Game;

/// <summary>Relieve de cada planeta. La zona jugable es casi plana para que la cuadrícula coincida con la colisión.</summary>
public static class Terrain
{
    /// <summary>Límite caminable (paredes de GameView) y radio de la zona edificable (90 celdas de 2 m).</summary>
    public const float Walkable = 188f;
    public const float Buildable = 180f;

    private static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 3266489917u;
            return h ^ (h >> 16);
        }
    }

    private static float R(int x, int y, int z) => (Hash(x, y, z) & 0xFFFFF) / 1048575f;

    public static float Noise3(Vector3 p)
    {
        var xi = Mathf.FloorToInt(p.X);
        var yi = Mathf.FloorToInt(p.Y);
        var zi = Mathf.FloorToInt(p.Z);
        var fx = p.X - xi;
        var fy = p.Y - yi;
        var fz = p.Z - zi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        fz = fz * fz * (3 - 2 * fz);
        float L(float a, float b, float t) => a + (b - a) * t;
        return L(
            L(L(R(xi, yi, zi), R(xi + 1, yi, zi), fx), L(R(xi, yi + 1, zi), R(xi + 1, yi + 1, zi), fx), fy),
            L(L(R(xi, yi, zi + 1), R(xi + 1, yi, zi + 1), fx), L(R(xi, yi + 1, zi + 1), R(xi + 1, yi + 1, zi + 1), fx), fy),
            fz);
    }

    public static float Fbm(float x, float z, int seed, int octaves)
    {
        float sum = 0, amp = 0.5f, total = 0;
        var p = new Vector3(x, seed * 7.31f, z);
        for (var i = 0; i < octaves; i++)
        {
            sum += Noise3(p) * amp;
            total += amp;
            amp *= 0.5f;
            p = new Vector3(p.X * 2.03f + 13.7f, p.Y + 3.1f, p.Z * 2.03f + 7.9f);
        }
        return sum / total;
    }

    private static float Smooth(float a, float b, float x)
    {
        var t = Mathf.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Ondulación suave dentro de la zona jugable. Solo hacia abajo: nada construido a cota 0 queda enterrado.</summary>
    private static float Inner(float x, float z, int seed)
    {
        var broad = Fbm(x * 0.028f, z * 0.028f, seed, 3);
        var small = Fbm(x * 0.13f, z * 0.13f, seed + 3, 2);
        return -0.14f * Smooth(0.35f, 0.7f, broad) - 0.05f * small;
    }

    public static float Height(int planet, float x, float z)
    {
        var seed = planet == 1 ? 41 : 9;
        var d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        var inner = Inner(x, z, seed);
        if (d <= Walkable) return inner;
        var t = Smooth(Walkable, Walkable + 300f, d);
        var near = Smooth(Walkable, Walkable + 26f, d);
        var warpX = x + (Fbm(x * 0.004f, z * 0.004f, seed + 5, 3) - 0.5f) * 180f;
        var warpZ = z + (Fbm(x * 0.004f, z * 0.004f, seed + 6, 3) - 0.5f) * 180f;
        var rolling = Fbm(warpX * 0.012f, warpZ * 0.012f, seed + 1, 4);
        var h = near * (rolling - 0.3f) * 14f;
        if (planet == 1)
        {
            // Marte: mesetas con terrazas y cañones anchos.
            var m = Fbm(warpX * 0.0042f, warpZ * 0.0042f, seed + 2, 4);
            var level = m * 7f;
            var fl = Mathf.Floor(level);
            var terrace = (fl + Smooth(0.68f, 0.92f, level - fl)) / 7f;
            h += t * t * terrace * 150f;
        }
        else
        {
            // Plutón: cordillera de hielo de aristas vivas.
            var r = 1 - Mathf.Abs(Fbm(warpX * 0.0048f, warpZ * 0.0048f, seed + 2, 5) * 2 - 1);
            h += t * t * Mathf.Pow(r, 2.2f) * 175f;
        }
        return inner * (1 - near) + Mathf.Max(h, -2f);
    }

    /// <summary>Máscara de hielo: láminas que evitan la base inicial y siguen un borde irregular.</summary>
    public static float IceMask(int planet, float x, float z)
    {
        if (planet == 1) return 0;
        var large = Fbm(x * 0.0085f + 3f, z * 0.0085f, 21, 4);
        var near = Mathf.Sqrt(x * x + (z - 8) * (z - 8));
        var clear = Smooth(20f, 58f, near);
        return Smooth(0.47f, 0.6f, large + (clear - 1f) * 0.25f);
    }

    /// <summary>Hitos de la zona inicial de Plutón, fuera de los muros: norte, este, oeste y sur de la base.</summary>
    public static readonly (string id, float x, float z, float yaw)[] Landmarks =
    {
        ("tower", -26f, -236f, 0f),
        ("crane", 238f, 40f, 100f), // la pluma corre paralela al muro: de perfil desde la base y sin entrar en la zona jugable
        ("dome", -240f, 28f, 25f),
        ("wreck", 18f, 232f, 8f),
    };

    private static readonly Vector2[][] Trails = BuildTrails();

    /// <summary>Pistas viejas de la base a cada hito: ligeras curvas, para seguirlas a pie.</summary>
    private static Vector2[][] BuildTrails()
    {
        var list = new Vector2[Landmarks.Length][];
        for (var i = 0; i < Landmarks.Length; i++)
        {
            var end = new Vector2(Landmarks[i].x, Landmarks[i].z);
            var dir = end.Normalized();
            var side = new Vector2(-dir.Y, dir.X);
            var points = new Vector2[10];
            for (var k = 0; k < points.Length; k++)
            {
                var t = k / (float)(points.Length - 1);
                var bend = Mathf.Sin(t * Mathf.Pi * (1.4f + i * 0.3f) + i) * 13f * Mathf.Sin(t * Mathf.Pi);
                points[k] = dir * 34f + (end - dir * 34f) * t + side * bend;
            }
            list[i] = points;
        }
        return list;
    }

    /// <summary>Máscara y distancia con signo a la pista más cercana. Solo aspecto: la colisión sigue siendo el plano.</summary>
    private static (float mask, float across) TrailAt(float x, float z)
    {
        if (Mathf.Abs(x) > 212f || Mathf.Abs(z) > 212f) return (0, 0);
        var p = new Vector2(x, z);
        var best = 6f;
        var across = 0f;
        foreach (var points in Trails)
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var ab = points[i + 1] - a;
            var t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
            var d = (a + ab * t).DistanceTo(p);
            if (d >= best) continue;
            best = d;
            across = ab.Cross(p - a) >= 0 ? d : -d;
        }
        return (1 - Smooth(2.6f, 5.2f, best), Mathf.Clamp(across, -4f, 4f));
    }

    /// <summary>Coordenadas: 2 m en la zona jugable, y paso creciente hasta el horizonte. Sin juntas en T.</summary>
    private static float[] Coordinates()
    {
        var list = new List<float>();
        for (var c = -208f; c <= 208f; c += 2f) list.Add(c);
        var step = 4f;
        var at = 208f;
        while (at < 1600f)
        {
            at += step;
            step = Mathf.Min(step * 1.22f, 90f);
            list.Add(at);
            list.Insert(0, -at);
        }
        return list.ToArray();
    }

    private static readonly Dictionary<int, ArrayMesh> Cache = new();

    public static ArrayMesh Mesh(int planet)
    {
        if (Cache.TryGetValue(planet, out var cached)) return cached;
        var xs = Coordinates();
        var n = xs.Length;
        var heights = new float[n * n];
        System.Threading.Tasks.Parallel.For(0, n, j =>
        {
            for (var i = 0; i < n; i++) heights[j * n + i] = Height(planet, xs[i], xs[j]);
        });
        var v = new Vector3[n * n];
        var nor = new Vector3[n * n];
        var tan = new float[n * n * 4];
        var col = new Color[n * n];
        var index = new int[(n - 1) * (n - 1) * 6];
        System.Threading.Tasks.Parallel.For(0, n, j =>
        {
            for (var i = 0; i < n; i++)
            {
                var k = j * n + i;
                v[k] = new Vector3(xs[i], heights[k], xs[j]);
                var i0 = Math.Max(i - 1, 0);
                var i1 = Math.Min(i + 1, n - 1);
                var j0 = Math.Max(j - 1, 0);
                var j1 = Math.Min(j + 1, n - 1);
                // Dentro de la zona jugable la luz exagera la ondulación: se ve relieve sin mover la colisión.
                var inside = Mathf.Max(Mathf.Abs(xs[i]), Mathf.Abs(xs[j])) < Walkable;
                var relief = inside ? 5f : 1f;
                var dx = (heights[j * n + i1] - heights[j * n + i0]) / (xs[i1] - xs[i0]) * relief;
                var dz = (heights[j1 * n + i] - heights[j0 * n + i]) / (xs[j1] - xs[j0]) * relief;
                nor[k] = new Vector3(-dx, 1, -dz).Normalized();
                tan[k * 4] = 1;
                tan[k * 4 + 3] = 1;
                // G: máscara de la pista; B: distancia con signo a su eje (±4 m), que el sombreador convierte en rodadas.
                var (trail, across) = planet == 0 ? TrailAt(xs[i], xs[j]) : (0f, 0f);
                col[k] = new Color(IceMask(planet, xs[i], xs[j]), trail, 0.5f + across / 8f);
            }
        });
        System.Threading.Tasks.Parallel.For(0, n - 1, j =>
        {
            for (var i = 0; i < n - 1; i++)
            {
                var o = (j * (n - 1) + i) * 6;
                var a = j * n + i;
                var b = a + 1;
                var c = a + n;
                var d = c + 1;
                index[o] = a; index[o + 1] = b; index[o + 2] = c;
                index[o + 3] = b; index[o + 4] = d; index[o + 5] = c;
            }
        });
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = v;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = nor;
        arrays[(int)Godot.Mesh.ArrayType.Tangent] = tan;
        arrays[(int)Godot.Mesh.ArrayType.Color] = col;
        arrays[(int)Godot.Mesh.ArrayType.Index] = index;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        Cache[planet] = mesh;
        return mesh;
    }

    public const string GroundShader = """
        shader_type spatial;
        render_mode cull_back, diffuse_burley;
        uniform sampler2DArray albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2DArray detail_tex : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D macro_tex : filter_linear_mipmap, repeat_enable;
        uniform float grid_strength = 0.0;
        uniform vec3 soil_a = vec3(0.03,0.02,0.018);
        uniform vec3 soil_b = vec3(0.08,0.052,0.042);
        uniform vec3 ice_tint = vec3(0.62,0.72,0.86);
        uniform vec3 rock_tint = vec3(0.17,0.16,0.18);
        varying vec3 wp;
        varying vec3 wn;
        void vertex(){
          wp = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
          wn = NORMAL;
        }
        void fragment(){
          vec4 m1 = texture(macro_tex,wp.xz/240.0);
          vec4 m2 = texture(macro_tex,wp.xz/41.0+vec2(0.31,0.17));
          float dist = length(VERTEX);
          // Regolito: dos escalas para evitar repetición y dar grano de cerca.
          vec3 uvr = vec3(wp.xz/3.3,5.0);
          vec4 ra = texture(albedo_tex,uvr);
          vec4 rd = texture(detail_tex,uvr);
          vec4 rn = texture(albedo_tex,vec3(wp.zx/0.85+vec2(0.13,0.41),5.0));
          float near = 1.0-smoothstep(6.0,28.0,dist);
          vec3 soil = mix(soil_a,soil_b,smoothstep(0.32,0.68,m1.r*0.55+m2.r*0.45));
          soil *= ra.rgb*1.5;
          soil *= mix(1.0,rn.r*1.6,near*0.6);
          soil *= 0.72+m2.g*0.56;
          // Pista antigua: suelo apisonado y más claro, con dos rodadas y el borde roto por el grano.
          float trail = COLOR.g*smoothstep(0.2,0.55,COLOR.g+(ra.a-0.5)*0.5+(m2.r-0.5)*0.4);
          float sd = (COLOR.b-0.5)*8.0;
          soil = mix(soil,soil*1.42+vec3(0.009,0.0068,0.0058),trail*0.85);
          soil *= 1.0-trail*0.5*(1.0-smoothstep(0.1,0.42,abs(abs(sd)-1.05)))*(0.55+ra.a*0.5);
          // Hielo: láminas con polígonos de convección y borde irregular según el grano del suelo.
          vec3 uvi = vec3(wp.xz/11.0,4.0);
          vec4 ia = texture(albedo_tex,uvi);
          vec4 id = texture(detail_tex,uvi);
          float edge = COLOR.r+(m2.r-0.5)*0.55+(m2.g-0.5)*0.3+(ra.a-0.5)*0.22;
          edge -= trail*0.5;
          float ice = smoothstep(0.42,0.5,edge);
          float frost = smoothstep(0.22,0.46,edge)*(1.0-ice);
          float polygon = 1.0-smoothstep(0.0,0.75,m1.a);
          vec3 icec = ice_tint*ia.rgb*(0.7+m2.b*0.3);
          icec = mix(icec,icec*vec3(0.42,0.44,0.52),polygon*0.75);
          icec = mix(icec,soil*1.4,smoothstep(0.62,0.42,edge)*0.5);
          vec3 c = mix(soil,icec,ice);
          c = mix(c,mix(soil,ice_tint*0.45,0.42+ra.a*0.3),frost*0.75);
          float rough = mix(rd.b,clamp(id.b*(0.9+polygon),0.42,1.0),ice);
          vec2 nmap = mix(rd.rg,id.rg,ice);
          float ao = mix(rd.a,id.a,ice)*mix(1.0,0.7,polygon*ice);
          // Laderas: roca estratificada, con la dirección de los estratos en horizontal.
          float slope = 1.0-wn.y;
          float cliff = smoothstep(0.1,0.3,slope+(m2.r-0.5)*0.16);
          if (cliff>0.001){
            // La textura solo aporta de cerca; a distancia mandan los bancos de estratos de decenas de metros.
            float farAway = smoothstep(60.0,190.0,dist);
            vec2 ruv = vec2((wp.x+wp.z*0.6)/11.0,-wp.y/11.0);
            vec4 ka = mix(texture(albedo_tex,vec3(ruv,3.0)),vec4(0.5,0.49,0.48,0.5),farAway);
            vec4 kd = mix(texture(detail_tex,vec3(ruv,3.0)),vec4(0.5,0.5,0.9,1.0),farAway);
            float beds = texture(macro_tex,vec2((wp.x+wp.z)*0.0007,wp.y/64.0)).r;
            vec3 rc = rock_tint*ka.rgb*(0.7+m1.g*0.9)*(0.3+smoothstep(0.34,0.66,beds)*1.5);
            float snow = smoothstep(0.66,0.92,wn.y+(m1.r-0.5)*0.7+(m2.g-0.5)*0.25);
            rc = mix(rc,ice_tint*0.42,snow*0.75*step(0.001,ice_tint.b-ice_tint.r));
            c = mix(c,rc,cliff);
            rough = mix(rough,kd.b,cliff);
            nmap = mix(nmap,kd.rg,cliff);
            ao = mix(ao,kd.a,cliff);
          }
          ALBEDO = c;
          ROUGHNESS = rough;
          METALLIC = 0.0;
          SPECULAR = mix(0.35,0.42,ice); // más brillo quema el hielo en blanco, y a contraluz parece un segundo sol
          NORMAL_MAP = vec3(nmap,1.0);
          NORMAL_MAP_DEPTH = 1.0;
          AO = ao;
          AO_LIGHT_AFFECT = 0.35;
          vec2 g = abs(fract(wp.xz/2.0+0.5)-0.5)*2.0;
          float line = 1.0-smoothstep(0.0,0.05,min(g.x,g.y));
          float area = 1.0-smoothstep(170.0,182.0,max(abs(wp.x),abs(wp.z)));
          EMISSION = vec3(0.25,0.55,0.8)*line*grid_strength*area*0.5;
        }
        """;

    public const string SkyShader = """
        shader_type sky;
        uniform float moon = 1.0;
        uniform vec3 zenith = vec3(0.0009,0.0012,0.003);
        uniform vec3 horizon = vec3(0.006,0.009,0.02);
        uniform float haze = 0.0;
        uniform vec3 haze_color = vec3(0.6,0.32,0.18);
        uniform float star_amount = 1.0;
        uniform vec3 fill_sky = vec3(0.11,0.14,0.24);
        uniform vec3 fill_horizon = vec3(0.26,0.3,0.42);
        uniform vec3 fill_ground = vec3(0.05,0.04,0.04);
        vec3 hash3(vec3 p){
          p = fract(p*vec3(0.1031,0.1030,0.0973));
          p += dot(p,p.yxz+33.33);
          return fract((p.xxy+p.yxx)*p.zyx);
        }
        float vnoise(vec3 p){
          vec3 i = floor(p); vec3 f = fract(p); f = f*f*(3.0-2.0*f);
          float a = hash3(i).x; float b = hash3(i+vec3(1,0,0)).x; float c = hash3(i+vec3(0,1,0)).x; float d = hash3(i+vec3(1,1,0)).x;
          float e = hash3(i+vec3(0,0,1)).x; float g = hash3(i+vec3(1,0,1)).x; float h = hash3(i+vec3(0,1,1)).x; float j = hash3(i+vec3(1,1,1)).x;
          return mix(mix(mix(a,b,f.x),mix(c,d,f.x),f.y),mix(mix(e,g,f.x),mix(h,j,f.x),f.y),f.z);
        }
        float fbm(vec3 p){ return vnoise(p)*0.55+vnoise(p*2.1+3.1)*0.28+vnoise(p*4.3+7.7)*0.17; }
        // Una estrella por celda de una rejilla 3D: punto nítido, brillo y temperatura propios.
        vec3 stars(vec3 d, float scale, float keep, float size){
          vec3 g = d*scale; vec3 cell = floor(g);
          vec3 h = hash3(cell);
          if (h.x>keep) return vec3(0.0);
          vec3 at = cell+0.25+0.5*hash3(cell+17.0);
          float dist = length(g-at);
          float s = smoothstep(size,size*0.25,dist);
          float mag = pow(h.y,5.0)*3.5+0.14;
          vec3 temp = mix(vec3(1.0,0.82,0.66),vec3(0.75,0.86,1.0),h.z);
          return temp*s*mag;
        }
        void sky(){
          vec3 d = normalize(EYEDIR);
          if (AT_CUBEMAP_PASS){
            // Solo para luz ambiente y reflejos: la maquinaria se lee aunque el cielo visible sea negro.
            vec3 fill = d.y>0.0 ? mix(fill_horizon,fill_sky,smoothstep(0.0,0.45,d.y)) : mix(fill_horizon*0.45,fill_ground,smoothstep(0.0,-0.3,d.y));
            if (LIGHT0_ENABLED) fill += LIGHT0_COLOR*LIGHT0_ENERGY*pow(max(dot(d,LIGHT0_DIRECTION),0.0),24.0)*0.35;
            COLOR = fill;
          } else {
            float up = clamp(d.y,0.0,1.0);
            vec3 c = mix(horizon,zenith,smoothstep(0.0,0.42,up));
            c = mix(c,haze_color,haze*exp(-max(d.y,0.0)*5.0));
            vec3 band = normalize(vec3(0.3,0.55,-0.75));
            float across = dot(d,band);
            float galaxy = exp(-across*across*14.0);
            float cloud = fbm(d*5.0+1.3);
            c += vec3(0.022,0.02,0.034)*galaxy*smoothstep(0.32,0.72,cloud)*star_amount;
            vec3 s = stars(d,120.0,0.02,0.15)+stars(d,260.0,0.012,0.3)*0.5+stars(d,520.0,0.002+galaxy*0.02,0.55)*0.35;
            float floorFade = smoothstep(-0.02,0.035,d.y);
            c += s*star_amount*floorFade;
            if (LIGHT0_ENABLED){
              float sn = max(dot(d,LIGHT0_DIRECTION),0.0);
              c += LIGHT0_COLOR*(smoothstep(0.999965,0.999992,sn)*30.0+pow(sn,1800.0)*0.5+pow(sn,120.0)*0.018*(1.0+haze*6.0));
            }
            // Luna: esfera con terminador coherente con el sol y borde suavizado.
            vec3 mc = normalize(vec3(-0.34,0.3,-0.88));
            float mr = 0.058;
            float cosd = dot(d,mc);
            if (moon>0.5 && cosd>0.99){
              vec3 u = normalize(cross(vec3(0,1,0),mc)); vec3 v = cross(mc,u);
              vec2 q = vec2(dot(d,u),dot(d,v))/mr;
              float r2 = dot(q,q);
              if (r2<1.03){
                float rr = min(r2,1.0);
                vec3 n = normalize(q.x*u+q.y*v-sqrt(1.0-rr)*mc);
                float lit = LIGHT0_ENABLED ? max(dot(n,LIGHT0_DIRECTION),0.0) : 0.5;
                vec3 sp = n*3.5+4.0;
                float t = fbm(sp)*0.62+fbm(sp*3.3)*0.38;
                vec3 alb = mix(vec3(0.15,0.145,0.15),vec3(0.46,0.45,0.45),smoothstep(0.28,0.72,t));
                alb = mix(alb,vec3(0.34,0.17,0.11),smoothstep(0.72,0.95,n.y+t*0.3));
                vec3 disc = alb*(pow(lit,0.8)*1.1+0.006);
                c = mix(c,disc,1.0-smoothstep(0.95,1.03,r2));
              }
            }
            COLOR = c;
          }
        }
        """;
}

/// <summary>Entorno común del juego y del menú: cielo, sol, terreno, instancias y ruinas de cada planeta.</summary>
public sealed class Scenery
{
    private readonly Node3D _root;
    public readonly WorldEnvironment Env;
    public readonly DirectionalLight3D Sun;
    public readonly ShaderMaterial GroundMat;
    public readonly ShaderMaterial SkyMat;
    private readonly MeshInstance3D _terrain;
    private readonly Dictionary<int, Node3D> _dressing = new();
    private int _planet = -1;
    private int _quality = 1;

    /// <summary>Entorno activo: los ajustes se aplican al momento, en partida y en el menú.</summary>
    public static Scenery? Current { get; private set; }

    /// <summary>«Reducir efectos» vigente: lo consultan todos los efectos, sin rehacer nada al cambiarlo.</summary>
    public static bool Calm { get; private set; }

    private readonly List<DustDrift> _drifts = new();

    /// <summary>Dirección hacia el sol: rasante y a tres cuartos del encuadre inicial, para que las máquinas modelen volumen.</summary>
    private static readonly Vector3 SunPluto = new Vector3(0.62f, 0.38f, 0.68f).Normalized();
    private static readonly Vector3 SunMars = new Vector3(-0.42f, 0.58f, 0.7f).Normalized();

    public Scenery(Node3D root, Settings prefs)
    {
        _root = root;
        Current = this;
        Calm = prefs.ReduceEffects;
        SkyMat = new ShaderMaterial { Shader = new Shader { Code = Terrain.SkyShader } };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = SkyMat, RadianceSize = Sky.RadianceSizeEnum.Size128, ProcessMode = Sky.ProcessModeEnum.Automatic },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            TonemapWhite = 6f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.6f,
            SsaoLightAffect = 0.15f,
            GlowEnabled = prefs.Glow,
            GlowIntensity = 0.45f,
            GlowBloom = 0f,
            // Con 1,1 las caras al sol ya entraban en el brillo y se quemaban en blanco con halo (H-10).
            GlowHdrThreshold = 1.5f,
            FogEnabled = true,
            FogLightColor = new Color(0.22f, 0.28f, 0.42f),
            FogDensity = 0.0022f,
            FogSkyAffect = 0f,
        };
        Env = new WorldEnvironment { Environment = env };
        _root.AddChild(Env);

        Sun = new DirectionalLight3D
        {
            LightColor = new Color(0.94f, 0.95f, 1f),
            LightEnergy = 1.9f,
            LightAngularDistance = 0.4f,
            ShadowEnabled = prefs.Shadows,
            ShadowBlur = 0.6f,
            DirectionalShadowMaxDistance = 130,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        _root.AddChild(Sun);

        GroundMat = new ShaderMaterial { Shader = new Shader { Code = Terrain.GroundShader } };
        GroundMat.SetShaderParameter("albedo_tex", Materials.AlbedoArray);
        GroundMat.SetShaderParameter("detail_tex", Materials.DetailArray);
        GroundMat.SetShaderParameter("macro_tex", Materials.Macro);
        GroundMat.SetShaderParameter("grid_strength", 0f);
        _terrain = new MeshInstance3D { MaterialOverride = GroundMat };
        _root.AddChild(_terrain);
        ApplyQuality(prefs.Quality);
    }

    public void SetPlanet(int planet)
    {
        if (planet == _planet) return;
        _planet = planet;
        var mars = planet == 1;
        _terrain.Mesh = Terrain.Mesh(planet);
        var sun = mars ? SunMars : SunPluto;
        Sun.Transform = new Transform3D(Basis.LookingAt(-sun, Vector3.Up), Vector3.Zero);
        Sun.LightColor = mars ? new Color(1f, 0.88f, 0.74f) : new Color(0.94f, 0.95f, 1f);
        Sun.LightEnergy = mars ? 2.3f : 1.9f;
        Env.Environment.FogLightColor = mars ? new Color(0.5f, 0.28f, 0.17f) : new Color(0.2f, 0.25f, 0.38f);

        GroundMat.SetShaderParameter("soil_a", mars ? new Vector3(0.17f, 0.056f, 0.025f) : new Vector3(0.03f, 0.02f, 0.018f));
        GroundMat.SetShaderParameter("soil_b", mars ? new Vector3(0.42f, 0.16f, 0.07f) : new Vector3(0.08f, 0.052f, 0.042f));
        GroundMat.SetShaderParameter("ice_tint", mars ? new Vector3(0.7f, 0.5f, 0.42f) : new Vector3(0.62f, 0.72f, 0.86f));
        GroundMat.SetShaderParameter("rock_tint", mars ? new Vector3(0.3f, 0.11f, 0.055f) : new Vector3(0.11f, 0.1f, 0.11f));

        SkyMat.SetShaderParameter("moon", mars ? 0f : 1f);
        SkyMat.SetShaderParameter("zenith", mars ? new Vector3(0.05f, 0.03f, 0.03f) : new Vector3(0.0009f, 0.0012f, 0.003f));
        SkyMat.SetShaderParameter("horizon", mars ? new Vector3(0.24f, 0.12f, 0.07f) : new Vector3(0.006f, 0.009f, 0.02f));
        SkyMat.SetShaderParameter("haze", mars ? 0.55f : 0f);
        SkyMat.SetShaderParameter("star_amount", mars ? 0.12f : 1f);
        SkyMat.SetShaderParameter("fill_sky", mars ? new Vector3(0.3f, 0.2f, 0.17f) : new Vector3(0.11f, 0.14f, 0.24f));
        SkyMat.SetShaderParameter("fill_horizon", mars ? new Vector3(0.6f, 0.36f, 0.24f) : new Vector3(0.26f, 0.3f, 0.42f));
        SkyMat.SetShaderParameter("fill_ground", mars ? new Vector3(0.16f, 0.08f, 0.05f) : new Vector3(0.05f, 0.04f, 0.04f));

        Materials.SetDust(mars ? new Color(0.27f, 0.11f, 0.05f) : new Color(0.055f, 0.04f, 0.036f), mars ? 0.85f : 0.65f);
        foreach (var (id, node) in _dressing) node.Visible = id == planet;
        if (!_dressing.ContainsKey(planet))
        {
            var dressing = BuildDressing(planet);
            _dressing[planet] = dressing;
            _root.AddChild(dressing);
        }
    }

    /// <summary>Preset real: cambia sombras, oclusión, antialiasing y anisotropía. Bajo prioriza los 60 FPS.</summary>
    public void ApplyQuality(int quality)
    {
        _quality = Mathf.Clamp(quality, 0, 2);
        var env = Env.Environment;
        env.SsaoEnabled = _quality >= 1;
        env.SsaoRadius = _quality == 2 ? 1.6f : 1.2f;
        Sun.DirectionalShadowMode = _quality == 0 ? DirectionalLight3D.ShadowMode.Parallel2Splits : DirectionalLight3D.ShadowMode.Parallel4Splits;
        Sun.DirectionalShadowMaxDistance = _quality switch { 0 => 70, 1 => 130, _ => 190 };
        RenderingServer.DirectionalShadowAtlasSetSize(_quality switch { 0 => 2048, 1 => 4096, _ => 8192 }, true);
        var viewport = ((SceneTree)Engine.GetMainLoop()).Root;
        viewport.Msaa3D = _quality switch { 0 => Viewport.Msaa.Disabled, 1 => Viewport.Msaa.Msaa2X, _ => Viewport.Msaa.Msaa4X };
        viewport.AnisotropicFilteringLevel = _quality switch { 0 => Viewport.AnisotropicFiltering.Anisotropy2X, 1 => Viewport.AnisotropicFiltering.Anisotropy4X, _ => Viewport.AnisotropicFiltering.Anisotropy16X };
    }

    public void ApplyPrefs(Settings prefs)
    {
        Sun.ShadowEnabled = prefs.Shadows;
        Env.Environment.GlowEnabled = prefs.Glow;
        ApplyQuality(prefs.Quality);
        if (Calm == prefs.ReduceEffects) return;
        Calm = prefs.ReduceEffects;
        foreach (var dust in _drifts) dust.Amount = Calm ? DustDrift.CalmAmount : DustDrift.FullAmount;
    }

    private Node3D BuildDressing(int planet)
    {
        var node = new Node3D();
        ScatterGround(node, planet);
        ScatterBoulders(node, planet);
        BuildRuins(node, planet);
        if (planet == 0) BuildLandmarks(node);
        var dust = new DustDrift(planet == 1) { Amount = Calm ? DustDrift.CalmAmount : DustDrift.FullAmount };
        node.AddChild(dust);
        _drifts.Add(dust);
        return node;
    }

    /// <summary>Guijarros y trozos de hielo en bloques de 48 m, visibles a 90 m: presupuesto en docs/GUIA_VISUAL.md.</summary>
    private void ScatterGround(Node3D parent, int planet)
    {
        var rockMat = planet == 1 ? "redrock" : "rock";
        var variants = new[]
        {
            new Models.Builder().Rock(rockMat, 0, 0, 0, 1f, 0.55f, 0.85f, 3, 1).Commit(),
            new Models.Builder().Rock(rockMat, 0, 0, 0, 1f, 0.42f, 1.1f, 7, 1).Commit(),
        };
        var shard = planet == 1 ? null : new Models.Builder().Rock("crust", 0, 0, 0, 1f, 0.3f, 0.8f, 11, 0).Commit();
        const float block = 48f;
        var rng = new Models.Rng(1907 + planet);
        var half = (int)(Terrain.Walkable / block) + 1;
        for (var bx = -half; bx < half; bx++)
        for (var bz = -half; bz < half; bz++)
        {
            for (var v = 0; v < variants.Length; v++)
            {
                var list = new List<Transform3D>();
                for (var i = 0; i < 130; i++)
                {
                    var x = (bx + rng.Next()) * block;
                    var z = (bz + rng.Next()) * block;
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > Terrain.Walkable + 6) continue;
                    // Densidad por manchas: zonas pedregosas y zonas limpias.
                    var patch = Terrain.Fbm(x * 0.05f + 9, z * 0.05f, 33, 2);
                    if (rng.Next() > (patch - 0.26f) * 3.6f) continue;
                    if (Terrain.IceMask(planet, x, z) > 0.5f && rng.Next() < 0.8f) continue;
                    // Altura máxima menor que la zapata de las máquinas: nada asoma bajo lo construido.
                    var s = Mathf.Pow(rng.Next(), 2.6f) * 0.34f + 0.05f;
                    var basis = Basis.FromEuler(new Vector3(rng.Range(-0.3f, 0.3f), rng.Range(0, 6.28f), rng.Range(-0.3f, 0.3f))).Scaled(new Vector3(s, s * rng.Range(0.65f, 1f), s));
                    list.Add(new Transform3D(basis, new Vector3(x, Terrain.Height(planet, x, z) + s * 0.05f, z)));
                }
                AddInstances(parent, variants[v], list, 95f);
            }
            if (shard != null)
            {
                var list = new List<Transform3D>();
                for (var i = 0; i < 40; i++)
                {
                    var x = (bx + rng.Next()) * block;
                    var z = (bz + rng.Next()) * block;
                    var ice = Terrain.IceMask(planet, x, z);
                    if (ice < 0.3f || ice > 0.85f || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > Terrain.Walkable + 6) continue;
                    var s = rng.Range(0.1f, 0.42f);
                    var basis = Basis.FromEuler(new Vector3(rng.Range(-0.5f, 0.5f), rng.Range(0, 6.28f), rng.Range(-0.5f, 0.5f))).Scaled(new Vector3(s, s, s));
                    list.Add(new Transform3D(basis, new Vector3(x, 0.02f + Terrain.Height(planet, x, z), z)));
                }
                AddInstances(parent, shard, list, 95f);
            }
        }
    }

    private static void AddInstances(Node3D parent, Mesh mesh, List<Transform3D> list, float range, bool shadow = false)
    {
        if (list.Count == 0) return;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
        for (var i = 0; i < list.Count; i++) multi.SetInstanceTransform(i, list[i]);
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            VisibilityRangeEnd = range,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>Rocas grandes con estratos en el anillo exterior: fuera de la zona edificable, sin estorbar construcción.</summary>
    private static void ScatterBoulders(Node3D parent, int planet)
    {
        var mat = planet == 1 ? "redrock" : "rock_cold";
        var meshes = new[]
        {
            new Models.Builder().Rock(mat, 0, 0, 0, 6f, 5.4f, 5.1f, 21, 2, 3.5f).Commit(),
            new Models.Builder().Rock(mat, 0, 0, 0, 7.2f, 3.6f, 5.4f, 37, 2, 5f).Commit(),
            new Models.Builder().Rock(mat, 0, 0, 0, 4.2f, 7.2f, 4.8f, 58, 2, 4f).Commit(),
        };
        var rng = new Models.Rng(4711 + planet);
        var lists = new[] { new List<Transform3D>(), new List<Transform3D>(), new List<Transform3D>() };
        for (var i = 0; i < 260; i++)
        {
            var angle = rng.Range(0, Mathf.Tau);
            var side = rng.Range(Terrain.Walkable + 5f, 330f);
            var x = Mathf.Cos(angle);
            var z = Mathf.Sin(angle);
            var m = side / Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            x *= m;
            z *= m;
            var s = (Mathf.Pow(rng.Next(), 2.6f) * 18f + 1.4f) / 6f;
            // Los hitos quedan despejados: ninguna roca los tapa ni los atraviesa.
            if (planet == 0 && NearLandmark(x, z)) continue;
            var basis = Basis.FromEuler(new Vector3(rng.Range(-0.08f, 0.08f), rng.Range(0, 6.28f), rng.Range(-0.08f, 0.08f))).Scaled(new Vector3(s, s, s));
            lists[i % 3].Add(new Transform3D(basis, new Vector3(x, Terrain.Height(planet, x, z) + s * 0.6f, z)));
        }
        for (var i = 0; i < 3; i++) AddInstances(parent, meshes[i], lists[i], 0, true);
    }

    private static bool NearLandmark(float x, float z)
    {
        var at = new Vector2(x, z);
        return Terrain.Landmarks.Any(l => new Vector2(l.x, l.z).DistanceTo(at) < (l.id is "crane" or "wreck" ? 96f : 58f));
    }

    /// <summary>Hitos para orientarse: mallas sueltas, sin cuerpo físico y fuera de la zona caminable.</summary>
    private static void BuildLandmarks(Node3D parent)
    {
        foreach (var (id, x, z, yaw) in Terrain.Landmarks)
        {
            var mesh = id switch
            {
                "tower" => Models.LandmarkTower(),
                "crane" => Models.LandmarkCrane(),
                "dome" => Models.LandmarkDome(),
                _ => Models.LandmarkWreck(),
            };
            var root = new MeshInstance3D { Name = "hito_" + id, Mesh = mesh, Position = new Vector3(x, Terrain.Height(0, x, z) - 0.6f, z), RotationDegrees = new Vector3(0, yaw, 0) };
            parent.AddChild(root);
            if (id == "tower") root.AddChild(new LandmarkBeacon { Position = new Vector3(0, Models.BeaconHeight, 0) });
        }
    }

    /// <summary>Puntos de referencia en el horizonte. Plutón: colonia minera abandonada. Marte: cúpulas y antenas.</summary>
    private static void BuildRuins(Node3D parent, int planet)
    {
        var b = new Models.Builder();
        if (planet == 1)
        {
            b.Lathe("cream", 0, 0, 0, new[] { (10f, 0f), (10f, 1.5f), (9.5f, 4f), (7f, 8f), (3f, 10.4f), (0f, 11f) }, 28);
            b.Box("steel_dark", 0, 4.5f, 0, 21f, 0.4f, 0.6f, bevel: 0);
            b.Box("hull", 15, 2.5f, 0, 10, 5, 7);
            b.Seams(15, 2.5f, 0, 10, 5, 7);
            for (var i = 0; i < 3; i++) b.Beam("steel_dark", new Vector3(26 + i, 0, -3 + i * 3), new Vector3(28, 16, 0), 0.5f, 0.5f);
            b.Box("signal", 28, 16.2f, 0, 1.4f, 0.6f, 1.4f);
            b.Lathe("steel", 28, 17, 0, new[] { (0.2f, 0f), (3.4f, 2.2f), (3.1f, 2.3f), (0.1f, 0.2f) }, 18, roll: -25);
        }
        else
        {
            // Silo destripado, nave derrumbada y torre de celosía inclinada.
            b.Lathe("rust", 0, 0, 0, new[] { (7f, 0f), (7f, 22f), (6f, 25f), (0f, 26f) }, 20);
            b.Box("rust", 0, 27f, 0, 16f, 1.4f, 16f, roll: 6);
            b.Box("hull", 14, 6.5f, 0, 16, 13, 11);
            b.Seams(14, 6.5f, 0, 16, 13, 11);
            b.Box("rust", 14, 14.6f, 0, 18, 1f, 13, pitch: -5);
            for (var i = 0; i < 6; i++)
            {
                var y0 = i * 6f;
                var lean = i * 0.5f;
                foreach (var (sx, sz) in new[] { (-1.6f, -1.6f), (1.6f, -1.6f), (1.6f, 1.6f), (-1.6f, 1.6f) })
                    b.Beam("steel_dark", new Vector3(26 + lean + sx, y0, 3 + sz), new Vector3(26.5f + lean + sx, y0 + 6, 3 + sz), 0.32f, 0.32f);
                b.Beam("rust", new Vector3(24.4f + lean, y0, 1.4f), new Vector3(28f + lean, y0 + 6, 1.4f), 0.2f, 0.2f);
                b.Beam("rust", new Vector3(24.4f + lean, y0, 4.6f), new Vector3(28f + lean, y0 + 6, 4.6f), 0.2f, 0.2f);
            }
            b.Tube("steel_dark", 1f, 10, new Vector3(5, 4, 5), new Vector3(9, 8, 5), new Vector3(9, 8, 14));
        }
        var mesh = b.Commit();
        // En Plutón la colonia se queda en cuatro restos: seis copias iguales competían con los hitos.
        foreach (var (x, z, yaw) in planet == 0
            ? new[] { (-235f, -180f, 20f), (240f, -120f, 130f), (250f, 170f, 250f), (-110f, 245f, 80f) }
            : new[] { (-235f, -180f, 20f), (240f, -120f, 130f), (250f, 170f, 250f), (-110f, 245f, 80f), (90f, -255f, 310f), (-260f, 90f, 175f) })
            parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(x, Terrain.Height(planet, x, z) - 1, z), RotationDegrees = new Vector3(0, yaw, 0) });
    }
}

/// <summary>Escena de fondo del menú: base inicial con modelos reales, luz de juego y cámara que se desplaza despacio.</summary>
public partial class MenuScene : Node3D
{
    private readonly Settings _prefs;
    private readonly List<(Node3D node, Vector3 axis, float speed)> _spin = new();
    private readonly List<(Node3D node, Vector3 rest, float phase)> _pump = new();
    private Camera3D _camera = null!;
    private readonly List<(MultiMesh multi, float offset, float side)> _goods = new();
    private (Vector3 eye, Vector3 target)? _fixedView;
    private double _time;
    private const float BeltLength = 18f;

    public MenuScene(Settings prefs) { _prefs = prefs; }

    public override void _Ready()
    {
        var scenery = new Scenery(this, _prefs);
        scenery.SetPlanet(0);
        void Place(string def, float x, float z, int rot = 0)
        {
            var model = Models.Machine(def);
            var root = new Node3D { Position = new Vector3(x, 0, z), RotationDegrees = new Vector3(0, rot * 90, 0) };
            root.AddChild(new MeshInstance3D { Mesh = model.Body });
            root.AddChild(new MeshInstance3D { Mesh = Models.Lamp(), Position = model.LampPos, MaterialOverride = Materials.Lamp("green") });
            if (model.Rotor != null)
            {
                var rotor = new MeshInstance3D { Mesh = model.Rotor, Position = model.RotorPos };
                root.AddChild(rotor);
                _spin.Add((rotor, model.RotorAxis, model.RotorSpeed));
            }
            if (model.Piston != null)
            {
                var piston = new MeshInstance3D { Mesh = model.Piston, Position = model.PistonPos };
                root.AddChild(piston);
                _pump.Add((piston, model.PistonPos, x));
            }
            if (model.GlowPos is { } glow)
                root.AddChild(new OmniLight3D { Position = glow, LightColor = model.GlowColor, LightEnergy = 0.7f, OmniRange = 7f });
            AddChild(root);
        }
        Place("ore_extractor", 12, -6);
        Place("smelter", 2, -4);
        Place("depot", -9, -8);
        Place("aux_core", -16, 0);
        Place("rtg_generator", -3, 6);
        Place("export_terminal", -13, 7);
        Place("recycler", 16, 4);
        AddChild(new MeshInstance3D { Mesh = Models.Debris("scrap_pile", 0), Position = new Vector3(7, 0, 8), RotationDegrees = new Vector3(0, 40, 0) });
        AddChild(new MeshInstance3D { Mesh = Models.Debris("scrap_pile", 2), Position = new Vector3(21, 0, 9) });
        AddChild(new MeshInstance3D { Mesh = Models.Debris("ice_block", 1), Position = new Vector3(26, 0, -1) });
        AddChild(new MeshInstance3D { Mesh = Models.NodeMesh("ore"), Position = new Vector3(24, 0, -11) });

        // Tramo de cinta decorativo con dos productos distinguibles por forma.
        var belt = new Models.Builder();
        belt.Box("rubber", 0, 0.5f, 0, BeltLength, 0.06f, 1.1f, bevel: 0.01f);
        belt.Box("steel_dark", 0, 0.42f, 0.58f, BeltLength, 0.14f, 0.08f);
        belt.Box("steel_dark", 0, 0.42f, -0.58f, BeltLength, 0.14f, 0.08f);
        for (var i = -4; i <= 4; i++)
        {
            belt.Beam("steel_dark", new Vector3(i * 2f, 0, 0.5f), new Vector3(i * 2f, 0.4f, 0.5f), 0.08f, 0.08f);
            belt.Beam("steel_dark", new Vector3(i * 2f, 0, -0.5f), new Vector3(i * 2f, 0.4f, -0.5f), 0.08f, 0.08f);
        }
        var beltRoot = new Node3D { Position = new Vector3(0, 0, 3f) };
        beltRoot.AddChild(new MeshInstance3D { Mesh = belt.Commit() });
        // Toda la gama de objetos pasa por la cinta: es también el muestrario de sus mallas.
        var items = new[] { "scrap", "recovered", "ore", "ingot", "parts", "ice", "coolant", "cryo_module", "silicon_ore", "wafer", "nav_core" };
        for (var i = 0; i < items.Length; i++)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = ItemVisuals.MeshOf(items[i]), InstanceCount = 2 };
            beltRoot.AddChild(new MultiMeshInstance3D { Multimesh = multi });
            _goods.Add((multi, i * BeltLength / 22f, i % 2 == 0 ? 0.22f : -0.22f));
        }
        // Encuadres fijos para documentar modelos con capturas del juego (--menu-view).
        var hasView = CmdArgs.Parse().TryGetValue("menu-view", out var view);
        if (hasView)
            _fixedView = view switch
            {
                "extractor" => (new Vector3(17, 2.2f, 2), new Vector3(12, 2.6f, -6)),
                "smelter" => (new Vector3(7, 2f, 5), new Vector3(2, 2f, -4)),
                "depot" => (new Vector3(-13, 2f, 1), new Vector3(-9, 1.8f, -8)),
                "rtg" => (new Vector3(-7, 1.9f, 11), new Vector3(-3, 1.3f, 6)),
                "terminal" => (new Vector3(-9, 2.2f, 13), new Vector3(-13, 1.6f, 7)),
                "scrap" => (new Vector3(9.4f, 1.3f, 10.2f), new Vector3(7, 0.3f, 8)),
                "items" => (new Vector3(0.2f, 1.25f, 4.5f), new Vector3(0.2f, 0.55f, 3f)),
                "paint" => (new Vector3(5.6f, 1.75f, 0.2f), new Vector3(3.6f, 1.3f, -1.9f)),
                "train" => (new Vector3(44f, 4.4f, 10.5f), new Vector3(35f, 1.2f, 18f)),
                "station" => (new Vector3(36f, 3f, -19f), new Vector3(30f, 1.6f, -27f)),
                "astronaut" => (new Vector3(-23.2f, 1.45f, 7f), new Vector3(-22f, 1.1f, 9f)),
                _ => null,
            };
        AddChild(beltRoot);
        // Piezas que solo aparecen en sus encuadres de documentación.
        if (view == "train")
        {
            AddChild(new MeshInstance3D { Mesh = Models.Locomotive(), Position = new Vector3(30, 0, 18), RotationDegrees = new Vector3(0, 90, 0) });
            var wagon = new MeshInstance3D { Mesh = Models.Wagon(), Position = new Vector3(37.5f, 0, 18), RotationDegrees = new Vector3(0, 90, 0) };
            wagon.AddChild(new MeshInstance3D { Mesh = Models.WagonLoad(), Position = new Vector3(0, 0.95f, 0), Scale = new Vector3(1, 1.1f, 1) });
            AddChild(wagon);
        }
        if (view == "station") Place("station", 30, -30);
        if (view == "astronaut")
        {
            var person = new Node3D { Position = new Vector3(-22, 0, 9), RotationDegrees = new Vector3(0, 195, 0) };
            person.AddChild(new MeshInstance3D { Mesh = Models.Astronaut() });
            person.AddChild(new RemoteToolView { Position = new Vector3(-0.3f, 1.02f, 0.42f), RotationDegrees = new Vector3(0, 180, 0), StartEquipped = true });
            AddChild(person);
        }

        _camera = new Camera3D { Fov = 50, Far = 3000, Near = 0.1f, Current = true };
        AddChild(_camera);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var t = (float)_time;
        foreach (var (node, axis, speed) in _spin) node.RotateObjectLocal(axis, (float)delta * speed);
        foreach (var (node, rest, phase) in _pump) node.Position = rest + new Vector3(0, Mathf.Sin(t * 1.6f + phase) * 0.3f, 0);
        var flow = _fixedView == null ? t * 0.8f : 0f;
        foreach (var (multi, offset, side) in _goods)
            for (var i = 0; i < 2; i++)
            {
                var at = Mathf.PosMod(flow + offset + i * BeltLength / 2f, BeltLength) - BeltLength / 2;
                multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(at, 0.53f, side)));
            }
        if (_fixedView is { } fixedView)
        {
            _camera.LookAtFromPosition(fixedView.eye, fixedView.target, Vector3.Up);
            return;
        }
        // Desplazamiento muy lento: la escena respira sin distraer de los botones.
        var sway = Mathf.Sin(t * 0.05f);
        var eye = new Vector3(-24f + sway * 5f, 4.4f + sway * 0.5f, 25f);
        _camera.LookAtFromPosition(eye, new Vector3(-13f + sway * 3f, 3.6f, -2f), Vector3.Up);
    }
}

/// <summary>Polvo a ras de suelo que acompaña a la cámara. Pocas partículas: se ve el viento, no tapa nada.</summary>
public partial class DustDrift : GpuParticles3D
{
    public const int FullAmount = 90;
    public const int CalmAmount = 30;

    public DustDrift(bool mars)
    {
        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(22, 0.8f, 22),
            Direction = new Vector3(1, 0.05f, 0.35f),
            Spread = 12,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 2.2f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.6f,
            ScaleMax = 1.6f,
        };
        Lifetime = 7;
        Preprocess = 7;
        LocalCoords = false;
        ProcessMaterial = material;
        CastShadow = ShadowCastingSetting.Off;
        VisibilityAabb = new Aabb(new Vector3(-40, -6, -40), new Vector3(80, 14, 80));
        DrawPass1 = new QuadMesh
        {
            Size = new Vector2(0.05f, 0.05f),
            Material = new StandardMaterial3D
            {
                AlbedoTexture = new GradientTexture2D
                {
                    Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) } },
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new Vector2(0.5f, 0.5f),
                    FillTo = new Vector2(1f, 0.5f),
                    Width = 16,
                    Height = 16,
                },
                // Se desvanece cerca de la cámara: una mota nunca tapa la vista.
                DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
                DistanceFadeMinDistance = 1f,
                DistanceFadeMaxDistance = 5f,
                AlbedoColor = mars ? new Color(0.72f, 0.45f, 0.3f, 0.3f) : new Color(0.72f, 0.74f, 0.8f, 0.24f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                DisableFog = true,
            },
        };
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport()?.GetCamera3D();
        if (camera != null) GlobalPosition = camera.GlobalPosition with { Y = 0.6f };
    }
}
