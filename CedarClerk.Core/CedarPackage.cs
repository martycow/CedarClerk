using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

public class CedarPackageException(string message, Exception? inner = null) : Exception(message, inner);

public record CedarPackageMeta(string Title, DateTime CreatedAt, string[]? Tags = null, string? TargetChannel = null);

public record CedarAsset(string Name, byte[] Bytes);

public record CedarPackageContents(string DocumentJson, string Title, DateTime CreatedAt, IReadOnlyDictionary<string, byte[]> Assets);

// .cedar is a zip container: document.json ({formatVersion, meta, doc: <TipTap JSON>}) + assets/<original-filename>.
public static class CedarPackage
{
    public const int CurrentFormatVersion = 1;
    private const string MediaPrefix = "/media/";

    public static void Write(Stream output, string tiptapJson, CedarPackageMeta meta, IReadOnlyList<CedarAsset> assets)
    {
        JsonNode docNode;
        try
        {
            docNode = JsonNode.Parse(tiptapJson) ?? throw new CedarPackageException("Document JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new CedarPackageException("Document JSON is not valid.", ex);
        }

        var wrapper = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["meta"] = new JsonObject
            {
                ["title"] = meta.Title,
                ["createdAt"] = JsonValue.Create(meta.CreatedAt),
                ["tags"] = new JsonArray((meta.Tags ?? []).Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
                ["targetChannel"] = meta.TargetChannel,
            },
            ["doc"] = docNode,
        };

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var docEntry = archive.CreateEntry("document.json", CompressionLevel.Optimal);
        using (var entryStream = docEntry.Open())
        using (var writer = new StreamWriter(entryStream))
            writer.Write(wrapper.ToJsonString());

        foreach (var asset in assets)
        {
            var assetEntry = archive.CreateEntry($"assets/{asset.Name}", CompressionLevel.Optimal);
            using var assetStream = assetEntry.Open();
            assetStream.Write(asset.Bytes, 0, asset.Bytes.Length);
        }
    }

    public static CedarPackageContents Read(Stream input)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new CedarPackageException("The .cedar file is not a valid zip archive.", ex);
        }

        using (archive)
        {
            var docEntry = archive.GetEntry("document.json")
                ?? throw new CedarPackageException("document.json is missing from the package.");

            string text;
            using (var docStream = docEntry.Open())
            using (var reader = new StreamReader(docStream))
                text = reader.ReadToEnd();

            JsonNode root;
            try
            {
                root = JsonNode.Parse(text) ?? throw new CedarPackageException("document.json is empty.");
            }
            catch (JsonException ex)
            {
                throw new CedarPackageException("document.json is not valid JSON.", ex);
            }

            if (root["formatVersion"] is not { } fvNode || fvNode.GetValue<int>() != CurrentFormatVersion)
                throw new CedarPackageException($"Unsupported or missing formatVersion (expected {CurrentFormatVersion}).");

            var docNode = root["doc"] ?? throw new CedarPackageException("document.json is missing the 'doc' field.");
            var metaNode = root["meta"];
            var title = metaNode?["title"]?.GetValue<string>() ?? "Untitled";
            var createdAt = metaNode?["createdAt"]?.GetValue<DateTime>() ?? DateTime.UtcNow;

            var assets = new Dictionary<string, byte[]>();
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal)) continue;
                var name = entry.FullName["assets/".Length..];
                if (name.Length == 0) continue;
                if (name.Contains("..") || name.Contains('/') || name.Contains('\\') || Path.IsPathRooted(name))
                    throw new CedarPackageException($"Invalid asset path in package: {entry.FullName}");

                using var assetStream = entry.Open();
                using var ms = new MemoryStream();
                assetStream.CopyTo(ms);
                assets[name] = ms.ToArray();
            }

            return new CedarPackageContents(docNode.ToJsonString(), title, createdAt, assets);
        }
    }

    // Finds distinct /media/<name> filenames referenced anywhere in the TipTap JSON tree
    // (image/video/audio src, carousel/collage image arrays, etc).
    public static IReadOnlyList<string> FindReferencedMediaPaths(string tiptapJson)
    {
        var node = JsonNode.Parse(tiptapJson);
        var found = new List<string>();
        CollectMediaPaths(node, found);
        return found.Distinct().ToList();
    }

    // Rewrites every /media/<oldName> reference in the TipTap JSON tree to /media/<newName>
    // using oldToNewNames; references with no matching entry are left untouched.
    public static string RewriteMediaPaths(string tiptapJson, IReadOnlyDictionary<string, string> oldToNewNames)
    {
        var node = JsonNode.Parse(tiptapJson) ?? throw new CedarPackageException("Document JSON is empty.");
        RewriteMediaPathsRecursive(node, oldToNewNames);
        return node.ToJsonString();
    }

    private static void CollectMediaPaths(JsonNode? node, List<string> found)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                    CollectMediaPaths(kvp.Value, found);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    CollectMediaPaths(item, found);
                break;
            case JsonValue val when val.TryGetValue<string>(out var s) && s.StartsWith(MediaPrefix, StringComparison.Ordinal):
                found.Add(s[MediaPrefix.Length..]);
                break;
        }
    }

    private static void RewriteMediaPathsRecursive(JsonNode? node, IReadOnlyDictionary<string, string> map)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                {
                    var value = obj[key];
                    if (value is JsonValue val && val.TryGetValue<string>(out var s) && s.StartsWith(MediaPrefix, StringComparison.Ordinal))
                    {
                        var name = s[MediaPrefix.Length..];
                        if (map.TryGetValue(name, out var newName))
                            obj[key] = $"{MediaPrefix}{newName}";
                    }
                    else
                    {
                        RewriteMediaPathsRecursive(value, map);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    RewriteMediaPathsRecursive(item, map);
                break;
        }
    }
}
