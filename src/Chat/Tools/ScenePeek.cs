using System;
using System.Linq;
using System.Text;
using Godot;

namespace Sts2AgentMcp.Tools;

/// <summary>
/// Development: dumps a packed scene's node tree with the stored (non-default) properties, or a resource, so game UI
/// assets (styles, textures, fonts) can be reused. Scenes are instantiated off-tree (no _Ready) and freed.
/// </summary>
internal static class ScenePeek
{
    public static string Dump(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "probe needs text = res:// path (scene, resource, or directory ending in /)";
        if (path.EndsWith('/')) return ListDir(path);
        var res = ResourceLoader.Load(path);
        if (res == null) return $"cannot load {path}";
        if (res is not PackedScene scene) return Describe(res, 0);

        var root = scene.Instantiate(PackedScene.GenEditState.Disabled);
        var sb = new StringBuilder();
        try { Walk(root, 0, sb); }
        finally { root.Free(); }
        return sb.ToString();
    }

    private static string ListDir(string path)
    {
        var files = ResourceLoader.ListDirectory(path);
        return files == null || files.Length == 0 ? $"(empty or missing) {path}" : string.Join("\n", files.Select(f => path + f));
    }

    private static void Walk(Node node, int depth, StringBuilder sb)
    {
        string pad = new(' ', depth * 2);
        string script = node.GetScript().Obj is Script s ? $" script={s.ResourcePath}" : "";
        sb.AppendLine($"{pad}{node.Name} ({node.GetClass()}){(node.IsUniqueNameInOwner() ? " %" : "")}{script}");
        foreach (var prop in node.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)(long)prop["usage"];
            if ((usage & PropertyUsageFlags.Storage) == 0) continue;
            string name = (string)prop["name"];
            if (name is "script" or "owner" || name.StartsWith("metadata/")) continue;
            var value = node.Get(name);
            var def = ClassDB.ClassGetPropertyDefaultValue(node.GetClass(), name);
            if (def.VariantType != Variant.Type.Nil && Equals(value.Obj, def.Obj)) continue;
            if (value.VariantType == Variant.Type.Nil) continue;
            string text = value.Obj is Resource r ? Describe(r, depth + 2).TrimEnd() : value.ToString();
            if (text.Length > 300) text = text[..300] + "…";
            sb.AppendLine($"{pad}  · {name} = {text}");
        }
        foreach (var child in node.GetChildren()) Walk(child, depth + 1, sb);
    }

    private static string Describe(Resource r, int depth)
    {
        string pad = new(' ', depth * 2);
        var sb = new StringBuilder($"{r.GetClass()} {r.ResourcePath}");
        switch (r)
        {
            case StyleBoxTexture st:
                sb.Append($" texture={st.Texture?.ResourcePath} size={st.Texture?.GetSize()} margins(l{st.TextureMarginLeft} t{st.TextureMarginTop} r{st.TextureMarginRight} b{st.TextureMarginBottom})" +
                          $" content(l{st.ContentMarginLeft} t{st.ContentMarginTop} r{st.ContentMarginRight} b{st.ContentMarginBottom}) modulate={st.ModulateColor} region={st.RegionRect}" +
                          $" axis={st.AxisStretchHorizontal}/{st.AxisStretchVertical}");
                break;
            case StyleBoxFlat sf:
                sb.Append($" bg={sf.BgColor} border={sf.BorderColor} w(l{sf.BorderWidthLeft} t{sf.BorderWidthTop}) corner={sf.CornerRadiusTopLeft} content(l{sf.ContentMarginLeft} t{sf.ContentMarginTop})");
                break;
            case Texture2D tex:
                sb.Append($" size={tex.GetSize()}");
                break;
            case Font font:
                sb.Append($" name={font.GetFontName()}");
                break;
            case ShaderMaterial sm:
                sb.Append($" shader={sm.Shader?.ResourcePath}");
                break;
        }
        return pad + sb;
    }
}
