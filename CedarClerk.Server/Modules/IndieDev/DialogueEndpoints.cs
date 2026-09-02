using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// The Yarn dialogue tool: a node-graph editor over DialogueScript, a .yarn export for the engine,
// and an xlsx sheet that round-trips translations. The sheet's key is the #line: id the server
// stamps on save (YarnDialogue.EnsureLineIds) — base text always re-reads from the script body on
// export, so the sheet can never show a stale source line. Not-yours answers 404, never 403.
public static class DialogueEndpoints
{
    public record SaveDialogueRequest(string? Name = null, string? GraphJson = null);
    public record CreateDialogueRequest(string Name);
    private record GraphNode(string Id, string Title, double X, double Y, string Body);

    private const int NameMaxLength = 120;
    private const int GraphMaxKb = 2048;
    private const long XlsxMaxBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions GraphJsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapDialogueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/dialogues").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var scripts = await db.DialogueScripts.Where(s => s.ProjectId == projectId && s.OwnerId == uid)
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync();
            return Results.Ok(scripts.Select(s => new
            {
                s.Id, s.Name, s.ProjectId, s.CreatedAt, s.UpdatedAt,
                NodeCount = ParseGraph(s.GraphJson)?.Count ?? 0,
            }));
        });

        group.MapPost("/", async (Guid projectId, CreateDialogueRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var name = req.Name.Trim();
            if (name.Length == 0 || name.Length > NameMaxLength)
                return Results.BadRequest(new { error = ErrorMessages.DialogueNameLength(NameMaxLength) });

            // Seeded with a Start node so the first open lands in an editor, not an empty canvas.
            var seed = new List<GraphNode> { new(Guid.NewGuid().ToString("N")[..8], "Start", 80, 80, "") };
            var script = new DialogueScript
            {
                OwnerId = uid,
                ProjectId = projectId,
                Name = name,
                GraphJson = JsonSerializer.Serialize(seed, GraphJsonOptions),
            };
            db.DialogueScripts.Add(script);
            await db.SaveChangesAsync();
            return Results.Created($"/api/dialogues/{script.Id}", Describe(script));
        });

        var single = app.MapGroup("/api/dialogues/{id:guid}").RequireAuthorization();

        single.MapGet("/", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var script = await db.DialogueScripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (script is null) return Results.NotFound();

            var languages = await db.DialogueLineTranslations
                .Where(t => t.DialogueScriptId == id && t.OwnerId == uid)
                .Select(t => t.Language).Distinct().OrderBy(l => l).ToListAsync();
            return Results.Ok(new
            {
                script.Id, script.Name, script.ProjectId, script.GraphJson,
                script.CreatedAt, script.UpdatedAt, Languages = languages,
            });
        });

        single.MapPut("/", async (Guid id, SaveDialogueRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var script = await db.DialogueScripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (script is null) return Results.NotFound();

            if (req.Name is not null)
            {
                var name = req.Name.Trim();
                if (name.Length == 0 || name.Length > NameMaxLength)
                    return Results.BadRequest(new { error = ErrorMessages.DialogueNameLength(NameMaxLength) });
                script.Name = name;
            }

            if (req.GraphJson is not null)
            {
                if (Encoding.UTF8.GetByteCount(req.GraphJson) > GraphMaxKb * 1024)
                    return Results.BadRequest(new { error = ErrorMessages.DialogueGraphTooLarge(GraphMaxKb) });
                var nodes = ParseGraph(req.GraphJson);
                if (nodes is null)
                    return Results.BadRequest(new { error = ErrorMessages.DialogueGraphInvalid });

                // Yarn addresses nodes by title, so two nodes collapsing onto one sanitized name
                // would silently rewire every jump between them in the export.
                var duplicate = nodes.GroupBy(n => YarnDialogue.SafeTitle(n.Title))
                    .FirstOrDefault(g => g.Count() > 1);
                if (duplicate is not null)
                    return Results.BadRequest(new { error = ErrorMessages.DialogueNodeTitleDuplicate(duplicate.First().Title) });

                // Stamp #line: ids on save, not on export — the sheet must key on ids that are
                // already in the stored body, or a re-export would re-key the whole sheet.
                var usedIds = new HashSet<string>(StringComparer.Ordinal);
                var stamped = nodes
                    .Select(n => n with { Body = YarnDialogue.EnsureLineIds(n.Body, usedIds) })
                    .ToList();
                script.GraphJson = JsonSerializer.Serialize(stamped, GraphJsonOptions);
            }

            script.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(Describe(script));
        });

        single.MapDelete("/", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var script = await db.DialogueScripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (script is null) return Results.NotFound();

            await db.DialogueLineTranslations
                .Where(t => t.DialogueScriptId == id && t.OwnerId == uid)
                .ExecuteDeleteAsync();
            db.DialogueScripts.Remove(script);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        single.MapGet("/export/yarn", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var script = await db.DialogueScripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (script is null) return Results.NotFound();

            var yarn = YarnDialogue.BuildYarnFile(ToYarnNodes(script));
            return Results.File(Encoding.UTF8.GetBytes(yarn), "text/plain", SanitizeFileName(script.Name) + ".yarn");
        });

        single.MapGet("/export/xlsx", async (Guid id, string? languages, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var script = await db.DialogueScripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (script is null) return Results.NotFound();

            var lines = YarnDialogue.ExtractLines(ToYarnNodes(script));
            var translations = await db.DialogueLineTranslations
                .Where(t => t.DialogueScriptId == id && t.OwnerId == uid)
                .ToListAsync();
            var langs = NormalizeSheetLanguages(translations.Select(t => t.Language)
                .Concat((languages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            var byKey = translations.ToDictionary(t => (t.LineId, t.Language), t => t.Text);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Lines");
            string[] fixedHeaders = ["Id", "Node", "Character", "Text"];
            for (var c = 0; c < fixedHeaders.Length; c++) ws.Cell(1, c + 1).Value = fixedHeaders[c];
            for (var c = 0; c < langs.Count; c++) ws.Cell(1, fixedHeaders.Length + c + 1).Value = langs[c];
            ws.Row(1).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);

            for (var r = 0; r < lines.Count; r++)
            {
                var line = lines[r];
                ws.Cell(r + 2, 1).Value = line.LineId;
                ws.Cell(r + 2, 2).Value = line.NodeTitle;
                ws.Cell(r + 2, 3).Value = line.Character;
                ws.Cell(r + 2, 4).Value = line.Text;
                for (var c = 0; c < langs.Count; c++)
                    if (byKey.TryGetValue((line.LineId, langs[c]), out var text))
                        ws.Cell(r + 2, fixedHeaders.Length + c + 1).Value = text;
            }
            ws.Column(1).Width = 12;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 16;
            for (var c = 4; c <= fixedHeaders.Length + langs.Count; c++)
            {
                ws.Column(c).Width = 60;
                ws.Column(c).Style.Alignment.WrapText = true;
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Results.File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                SanitizeFileName(script.Name) + ".xlsx");
        });

        single.MapPost("/import/xlsx", async (Guid id, IFormFile file, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var script = await db.DialogueScripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (script is null) return Results.NotFound();

            if (file.Length == 0 || file.Length > XlsxMaxBytes)
                return Results.BadRequest(new { error = $"File is too large ({XlsxMaxBytes / (1024 * 1024)}MB maximum)" });

            // ClosedXML needs a seekable stream; IFormFile's underlying stream may not be.
            using var uploadCopy = new MemoryStream();
            await using (var uploadStream = file.OpenReadStream())
                await uploadStream.CopyToAsync(uploadCopy);
            uploadCopy.Position = 0;

            List<(string LineId, string Language, string Text)> cells;
            try { cells = ReadSheet(uploadCopy, out var noIdColumn); if (noIdColumn) return Results.BadRequest(new { error = ErrorMessages.DialogueXlsxNoIdColumn }); }
            catch (Exception) { return Results.BadRequest(new { error = ErrorMessages.DialogueXlsxUnreadable }); }

            var currentIds = YarnDialogue.ExtractLines(ToYarnNodes(script)).Select(l => l.LineId)
                .ToHashSet(StringComparer.Ordinal);
            var existing = await db.DialogueLineTranslations
                .Where(t => t.DialogueScriptId == id && t.OwnerId == uid)
                .ToDictionaryAsync(t => (t.LineId, t.Language));

            int added = 0, updated = 0;
            var unknown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (lineId, language, text) in cells)
            {
                // Unknown ids are kept, not refused: a line deleted in the editor and restored by
                // undo gets its translations back, and losing a translator's work to a race with
                // editing would be the worse failure.
                if (!currentIds.Contains(lineId)) unknown.Add(lineId);
                if (existing.TryGetValue((lineId, language), out var row))
                {
                    if (row.Text == text) continue;
                    row.Text = text;
                    row.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    var row2 = new DialogueLineTranslation
                    {
                        OwnerId = uid, DialogueScriptId = id,
                        LineId = lineId, Language = language, Text = text,
                    };
                    db.DialogueLineTranslations.Add(row2);
                    existing[(lineId, language)] = row2;
                    added++;
                }
            }
            await db.SaveChangesAsync();

            var languages = cells.Select(c => c.Language).Distinct().OrderBy(l => l).ToList();
            return Results.Ok(new { added, updated, languages, unknownLines = unknown.Count });
        }).DisableAntiforgery()
          .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = XlsxMaxBytes });
    }

    private static object Describe(DialogueScript s) =>
        new { s.Id, s.Name, s.ProjectId, s.GraphJson, s.CreatedAt, s.UpdatedAt };

    private static List<GraphNode>? ParseGraph(string graphJson)
    {
        try
        {
            var nodes = JsonSerializer.Deserialize<List<GraphNode>>(graphJson, GraphJsonOptions);
            if (nodes is null || nodes.Any(n => n.Id is null || n.Title is null || n.Body is null)) return null;
            return nodes;
        }
        catch (JsonException) { return null; }
    }

    private static List<YarnDialogueNode> ToYarnNodes(DialogueScript script) =>
        (ParseGraph(script.GraphJson) ?? [])
        .Select(n => new YarnDialogueNode(n.Id, n.Title, n.X, n.Y, n.Body))
        .ToList();

    // First worksheet only; fixed columns by header name, every later non-fixed header is a
    // language column. Empty cells are skipped, never deleted — an unfilled cell means "not yet",
    // not "remove the translation".
    private static List<(string LineId, string Language, string Text)> ReadSheet(Stream stream, out bool noIdColumn)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var header = ws.Row(1);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        int idColumn = 0;
        var languageColumns = new List<(int Column, string Language)>();
        var seenLanguages = new HashSet<string>(StringComparer.Ordinal);
        string[] fixedHeaders = ["id", "node", "character", "text"];
        for (var c = 1; c <= lastColumn; c++)
        {
            var name = header.Cell(c).GetString().Trim().ToLowerInvariant();
            if (name.Length == 0) continue;
            if (name == "id") { idColumn = c; continue; }
            if (!fixedHeaders.Contains(name) && Languages.IsContentLanguage(name) && seenLanguages.Add(name))
                languageColumns.Add((c, name));
        }
        noIdColumn = idColumn == 0;
        if (noIdColumn) return [];

        var cells = new List<(string, string, string)>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var lineId = row.Cell(idColumn).GetString().Trim();
            if (lineId.Length == 0) continue;
            foreach (var (column, language) in languageColumns)
            {
                var text = row.Cell(column).GetString().Trim();
                if (text.Length > 0) cells.Add((lineId, language, text));
            }
        }
        return cells;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return safe.Length == 0 ? "dialogue" : safe;
    }

    // Export and import share this list so a translation sheet cannot invent an unsupported locale.
    public static IReadOnlyList<string> NormalizeSheetLanguages(IEnumerable<string?> languages)
    {
        var requested = languages
            .Select(language => language?.Trim().ToLowerInvariant() ?? "")
            .Where(language => language.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return Languages.ContentLanguages.Where(requested.Contains).ToList();
    }
}
