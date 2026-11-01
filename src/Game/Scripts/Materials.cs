using Godot;

namespace Pluton.Game;

/// <summary>
/// Catálogo central de superficies. Todas las piezas opacas comparten un único material: la capa de textura,
/// el tinte, el metal, la rugosidad y la emisión viajan por vértice, y cada malla queda en una sola llamada de dibujo.
/// </summary>
public static class Materials
{
    /// <summary>Capas de los atlas generados por tools/TexGen. El orden coincide con Surfaces.Layers.</summary>
    public enum Layer { Painted, Steel, Rubber, Rock, Ice, Regolith, Hazard, Tread, Rust }

    public enum Pass { Opaque, Glass, Spectral }

    public readonly record struct Surface(Layer Layer, Color Tint, float Metal, float Rough, float Emission = 0, float Wear = 0, Pass Pass = Pass.Opaque);

    private const string AlbedoPath = "res://Assets/Textures/superficies_albedo.png";
    private const string DetailPath = "res://Assets/Textures/superficies_detalle.png";
    private const string MacroPath = "res://Assets/Textures/macro.png";

    /// <summary>Metros que cubre una repetición de textura, por capa: la misma densidad en toda máquina.</summary>
    public static float TileOf(Layer layer) => layer switch
    {
        Layer.Painted => 2f,
        Layer.Steel => 1.5f,
        Layer.Rubber => 0.8f,
        Layer.Rock => 4f,
        Layer.Ice => 4f,
        Layer.Regolith => 3f,
        Layer.Hazard => 1.6f,
        Layer.Tread => 1.2f,
        Layer.Rust => 2.5f,
        _ => 2f,
    };

    private static Surface P(float r, float g, float b, float rough = 0.5f, float wear = 0.4f) => new(Layer.Painted, new Color(r, g, b), 0.15f, rough, 0, wear);
    private static Surface M(float r, float g, float b, float rough = 0.5f) => new(Layer.Steel, new Color(r, g, b), 1f, rough);
    private static Surface E(float r, float g, float b, float energy) => new(Layer.Steel, new Color(r, g, b), 0f, 0.5f, energy);

    private static readonly Dictionary<string, Surface> Specs = new()
    {
        // Pintura de máquina: gris cálido que destaca sobre el suelo frío, naranja como acento de señalización.
        ["hull"] = P(0.2f, 0.2f, 0.195f),
        ["panel"] = P(0.31f, 0.31f, 0.295f),
        ["signal"] = P(0.62f, 0.17f, 0.025f, 0.48f, 0.55f),
        ["cream"] = P(0.38f, 0.36f, 0.31f),
        ["crane"] = P(0.66f, 0.44f, 0.035f, 0.5f, 0.5f),
        ["navy"] = P(0.13f, 0.18f, 0.25f),
        ["dark"] = P(0.075f, 0.08f, 0.09f, 0.55f, 0.2f),
        ["stripe"] = new(Layer.Hazard, new Color(1, 1, 1), 0f, 0.5f, 0, 0.3f),
        // Metales desnudos.
        ["steel"] = M(0.7f, 0.71f, 0.73f, 0.45f),
        ["steel_dark"] = M(0.3f, 0.31f, 0.33f, 0.55f),
        ["chrome"] = M(0.92f, 0.92f, 0.93f, 0.2f),
        ["copper"] = M(0.85f, 0.5f, 0.28f, 0.42f),
        ["brass"] = M(0.82f, 0.66f, 0.3f, 0.42f),
        ["tread"] = new(Layer.Tread, new Color(0.62f, 0.63f, 0.65f), 1f, 0.5f),
        // Goma y plásticos.
        ["rubber"] = new(Layer.Rubber, new Color(0.11f, 0.11f, 0.115f), 0f, 0.55f),
        ["hose"] = new(Layer.Rubber, new Color(0.17f, 0.15f, 0.14f), 0f, 0.5f),
        // Vidrio.
        ["glass"] = new(Layer.Steel, new Color(0.55f, 0.72f, 0.8f, 0.24f), 0f, 0.08f, 0, 0, Pass.Glass),
        // Óxido y chatarra.
        ["rust"] = new(Layer.Rust, new Color(0.115f, 0.054f, 0.027f), 0.5f, 0.6f),
        ["rustlight"] = new(Layer.Rust, new Color(0.18f, 0.094f, 0.045f), 0.55f, 0.52f),
        // Mineral.
        ["rock"] = new(Layer.Rock, new Color(0.26f, 0.22f, 0.2f), 0f, 0.5f),
        ["rock_cold"] = new(Layer.Rock, new Color(0.15f, 0.145f, 0.16f), 0f, 0.5f),
        ["redrock"] = new(Layer.Rock, new Color(0.62f, 0.28f, 0.14f), 0f, 0.5f),
        ["regolith"] = new(Layer.Regolith, new Color(0.09f, 0.062f, 0.05f), 0f, 0.5f),
        // Mismo tono que el suelo de Plutón: lo que apoya en él se funde, sin un disco de otro color.
        ["soil"] = new(Layer.Regolith, new Color(0.042f, 0.029f, 0.024f), 0f, 0.55f),
        ["ice"] = new(Layer.Ice, new Color(0.78f, 0.9f, 1f), 0f, 0.35f, 0.5f),
        ["crust"] = new(Layer.Ice, new Color(0.86f, 0.92f, 0.98f), 0f, 0.55f),
        ["ore"] = new(Layer.Rock, new Color(0.72f, 0.36f, 0.62f), 0.65f, 0.3f, 0.5f),
        ["silicon"] = new(Layer.Rock, new Color(0.3f, 0.45f, 0.72f), 0.8f, 0.2f, 0.75f),
        // Señales luminosas: siempre con forma propia; el color nunca va solo.
        ["amber"] = E(1f, 0.56f, 0.14f, 2.25f),
        ["cyan"] = E(0.3f, 0.85f, 1f, 2f),
        ["heat"] = E(1f, 0.34f, 0.07f, 2.5f),
        ["green"] = E(0.35f, 1f, 0.5f, 1.5f),
        ["red"] = E(1f, 0.2f, 0.12f, 2f),
        ["screen"] = E(0.34f, 0.72f, 0.66f, 1f),
        // Traje y evento.
        ["suit"] = P(0.86f, 0.87f, 0.84f, 0.55f, 0.1f),
        ["visor"] = M(0.9f, 0.55f, 0.15f, 0.2f),
        ["pumpkin"] = new(Layer.Rubber, new Color(0.95f, 0.42f, 0.08f), 0f, 0.6f, 0.25f),
        ["stem"] = new(Layer.Rubber, new Color(0.2f, 0.32f, 0.12f), 0f, 0.7f),
        ["spectral"] = new(Layer.Steel, new Color(0.45f, 1f, 0.75f, 0.45f), 0f, 0.5f, 1.5f, 0, Pass.Spectral),
    };

    public static Surface Spec(string name) => Specs.TryGetValue(name, out var s) ? s : new Surface(Layer.Painted, Colors.Magenta, 0, 0.5f);

    private static TextureLayered? _albedo, _detail;
    private static Texture2D? _macro;
    public static TextureLayered AlbedoArray => _albedo ??= GD.Load<TextureLayered>(AlbedoPath);
    public static TextureLayered DetailArray => _detail ??= GD.Load<TextureLayered>(DetailPath);
    public static Texture2D Macro => _macro ??= GD.Load<Texture2D>(MacroPath);

    private const string IndustrialCode = """
        shader_type spatial;
        render_mode cull_back, diffuse_burley, specular_schlick_ggx;
        uniform sampler2DArray albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2DArray detail_tex : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform vec3 dust_color = vec3(0.055,0.04,0.036);
        uniform float dust_amount = 0.7;
        varying flat float v_layer;
        varying flat float v_metal;
        varying flat float v_rough;
        varying flat float v_emit;
        varying float v_y;
        varying float v_up;
        void vertex(){
          v_layer = floor(UV2.x);
          v_metal = clamp((fract(UV2.x)-0.02)/0.96,0.0,1.0);
          v_emit = floor(UV2.y)*0.25;
          v_rough = clamp((fract(UV2.y)-0.02)/0.96,0.0,1.0);
          v_y = VERTEX.y;
          v_up = normalize(MODEL_NORMAL_MATRIX*NORMAL).y;
        }
        void fragment(){
          vec3 uvw = vec3(UV,v_layer);
          vec4 a = texture(albedo_tex,uvw);
          vec4 d = texture(detail_tex,uvw);
          vec3 col = a.rgb*COLOR.rgb;
          float metal = v_metal;
          float rough = clamp(d.b*v_rough*2.0,0.05,1.0);
          float wear = COLOR.a;
          // De cerca la textura se queda corta: segunda escala fina, solo a menos de 8 m para no gastar lejos.
          float close = 1.0-smoothstep(2.5,8.0,length(VERTEX));
          vec2 nmap = d.rg;
          float fine = 0.0;
          if (close>0.001){
            vec3 uvn = vec3(UV*5.3+vec2(0.37,0.11),v_layer);
            vec4 d2 = texture(detail_tex,uvn);
            fine = (texture(albedo_tex,uvn).a-0.5)*close;
            nmap += (d2.rg-0.5)*close*0.9;
            rough = clamp(rough*(1.0+(d2.b-0.5)*0.7*close),0.05,1.0);
            col *= 1.0+fine*0.55;
          }
          // Pintura: aristas y rozaduras dejan ver el acero. Resto: la arista solo recoge más luz.
          float painted = float(v_layer<0.5)+float(abs(v_layer-6.0)<0.5);
          float chip = painted*smoothstep(0.62,0.66,wear*0.42+a.a*0.55+fine*0.16);
          col = mix(col,vec3(0.42,0.42,0.44),chip);
          metal = mix(metal,1.0,chip);
          rough = mix(rough,0.42,chip);
          col *= 1.0+(1.0-painted)*wear*0.25;
          // Polvo del planeta: se acumula abajo y sobre las caras que miran arriba.
          float low = smoothstep(0.85,0.0,v_y+(a.a-0.5)*0.7);
          float top = smoothstep(0.6,0.95,v_up)*(0.2+a.a*0.8);
          float dust = clamp(max(low*0.75,top*0.7),0.0,1.0)*dust_amount*(1.0-step(0.01,v_emit));
          col = mix(col,dust_color,dust);
          metal *= 1.0-dust;
          rough = mix(rough,0.92,dust);
          ALBEDO = col;
          METALLIC = metal;
          ROUGHNESS = rough;
          SPECULAR = 0.45;
          NORMAL_MAP = vec3(nmap,1.0);
          AO = d.a;
          AO_LIGHT_AFFECT = 0.25;
          EMISSION = COLOR.rgb*v_emit;
        }
        """;

    private static ShaderMaterial? _industrial;

    public static ShaderMaterial Industrial
    {
        get
        {
            if (_industrial != null) return _industrial;
            _industrial = new ShaderMaterial { Shader = new Shader { Code = IndustrialCode } };
            _industrial.SetShaderParameter("albedo_tex", AlbedoArray);
            _industrial.SetShaderParameter("detail_tex", DetailArray);
            return _industrial;
        }
    }

    private static readonly Dictionary<Pass, Material> PassMaterials = new();

    /// <summary>Material de la pasada: opaco compartido, vidrio o efecto espectral.</summary>
    public static Material ForPass(Pass pass)
    {
        if (pass == Pass.Opaque) return Industrial;
        if (PassMaterials.TryGetValue(pass, out var m)) return m;
        m = pass == Pass.Glass
            ? new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = Colors.White,
                Metallic = 0.1f,
                Roughness = 0.06f,
                MetallicSpecular = 0.9f,
                CullMode = BaseMaterial3D.CullModeEnum.Back,
            }
            : Standard("spectral");
        PassMaterials[pass] = m;
        return m;
    }

    /// <summary>Tinte y polvo por planeta. Afecta a toda la maquinaria sin rehacer mallas.</summary>
    public static void SetDust(Color color, float amount)
    {
        Industrial.SetShaderParameter("dust_color", new Vector3(color.R, color.G, color.B));
        Industrial.SetShaderParameter("dust_amount", amount);
        DustColor = new Vector3(color.R, color.G, color.B);
        DustChanged?.Invoke();
    }

    /// <summary>Color de polvo vigente, para materiales que no comparten el industrial (bastidor de las cintas).</summary>
    public static Vector3 DustColor { get; private set; } = new(0.055f, 0.04f, 0.036f);
    public static event Action? DustChanged;

    private static readonly Dictionary<string, StandardMaterial3D> StandardCache = new();

    /// <summary>Equivalente sin atributos de vértice, para primitivas sueltas del motor y efectos.</summary>
    public static StandardMaterial3D Standard(string name)
    {
        if (StandardCache.TryGetValue(name, out var m)) return m;
        var spec = Spec(name);
        m = new StandardMaterial3D
        {
            AlbedoColor = spec.Tint.LinearToSrgb() with { A = spec.Tint.A },
            Metallic = spec.Metal,
            Roughness = Mathf.Clamp(spec.Rough * 1.4f, 0.05f, 1f),
        };
        if (spec.Emission > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = spec.Tint.LinearToSrgb();
            m.EmissionEnergyMultiplier = spec.Emission;
        }
        if (spec.Pass != Pass.Opaque) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        StandardCache[name] = m;
        return m;
    }

    /// <summary>Materiales que señalan el estado de una máquina. Compartidos: cambiar de estado no crea recursos.</summary>
    public static StandardMaterial3D Lamp(string name)
    {
        var key = "lamp_" + name;
        if (StandardCache.TryGetValue(key, out var m)) return m;
        var spec = Spec(name);
        m = new StandardMaterial3D
        {
            AlbedoColor = spec.Tint.LinearToSrgb() * 0.5f,
            EmissionEnabled = true,
            Emission = spec.Tint.LinearToSrgb(),
            EmissionEnergyMultiplier = 3f,
            Roughness = 0.4f,
        };
        StandardCache[key] = m;
        return m;
    }
}
