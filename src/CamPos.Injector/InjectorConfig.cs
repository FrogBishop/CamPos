using System.Text.Json;

namespace CamPos.Injector;

internal sealed class InjectorConfig
{
    public string? GameDirectory { get; set; }
    public string? GameExecutable { get; set; }
    public string? Arguments { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static InjectorConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "找不到配置文件。请复制 CamPos.Injector.example.json 为 CamPos.Injector.json，并填写 GameDirectory。",
                path);
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<InjectorConfig>(json, Options)
                     ?? throw new InvalidDataException("配置文件内容为空。");
        if (string.IsNullOrWhiteSpace(config.GameDirectory))
            throw new InvalidDataException("配置项 GameDirectory 不能为空。");
        return config;
    }

    public string ResolveGameExecutable()
    {
        var directory = Path.GetFullPath(GameDirectory!.Trim().Trim('"'));
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("GameDirectory 不存在: " + directory);

        if (!string.IsNullOrWhiteSpace(GameExecutable))
        {
            var name = Path.GetFileName(GameExecutable.Trim());
            if (!name.Equals(GameExecutable.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GameExecutable 只能写文件名，不能写路径。");
            var explicitPath = Path.Combine(directory, name);
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException("GameExecutable 不存在: " + explicitPath, explicitPath);
            return explicitPath;
        }

        var candidates = new[] { "GenshinImpact.exe", "YuanShen.exe" }
            .Select(name => Path.Combine(directory, name))
            .Where(File.Exists)
            .ToArray();
        return candidates.Length switch
        {
            0 => throw new FileNotFoundException(
                "GameDirectory 中没有 GenshinImpact.exe 或 YuanShen.exe: " + directory),
            > 1 => throw new InvalidDataException(
                "GameDirectory 中同时存在国际服和国服 exe，请增加 GameExecutable 指定其中一个。"),
            _ => candidates[0]
        };
    }
}
