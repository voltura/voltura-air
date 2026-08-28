using System.Text;
using System.Text.Json;

namespace VolturaAir.Host.Features.AiAssistant;

internal static class AiAssistantReadTools
{
    private const int MaximumResultCharacters = 24 * 1024;
    private const int MaximumSearchResults = 40;
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(4);
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".codex", ".git", ".ssh", "AppData", "node_modules", "$Recycle.Bin", "System Volume Information"
    ];
    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".pdf", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".md", ".rtf", ".odt", ".ods", ".odp"
    };
    private static readonly IReadOnlyDictionary<string, string> DocumentationFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["README.md"] = "README.md",
        ["docs/README.md"] = Path.Combine("docs", "README.md"),
        ["docs/features.md"] = Path.Combine("docs", "features.md"),
        ["docs/network-and-host-selection.md"] = Path.Combine("docs", "network-and-host-selection.md"),
        ["docs/pairing-feedback.md"] = Path.Combine("docs", "pairing-feedback.md"),
        ["docs/troubleshooting.md"] = Path.Combine("docs", "troubleshooting.md"),
        ["docs/architecture.md"] = Path.Combine("docs", "architecture.md"),
        ["docs/protocol.md"] = Path.Combine("docs", "protocol.md"),
        ["PRIVACY.md"] = "PRIVACY.md",
        ["SECURITY.md"] = "SECURITY.md"
    };

    internal static readonly object[] Specifications =
    [
        Tool("search_voltura_docs", "Search the bundled maintained Voltura Air documentation. Use this before reading documents.", new
        {
            type = "object",
            properties = new { query = new { type = "string", description = "Words or phrase to find in Voltura Air documentation." } },
            required = new[] { "query" },
            additionalProperties = false
        }),
        Tool("read_voltura_doc", "Read one bundled maintained Voltura Air document returned by search_voltura_docs.", new
        {
            type = "object",
            properties = new { document = new { type = "string", @enum = DocumentationFiles.Keys.ToArray() } },
            required = new[] { "document" },
            additionalProperties = false
        }),
        Tool("find_user_files", "Find likely user documents by filename under the signed-in user's profile. Returns names, local paths, sizes, and modified times only.", new
        {
            type = "object",
            properties = new { query = new { type = "string", description = "Filename words to match. Do not use wildcards." } },
            required = new[] { "query" },
            additionalProperties = false
        })
    ];

    internal static async Task<object?> InvokeAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        try
        {
            return tool switch
            {
                "search_voltura_docs" => Success(await SearchDocumentationAsync(RequiredString(arguments, "query"), cancellationToken).ConfigureAwait(false)),
                "read_voltura_doc" => Success(await ReadDocumentationAsync(RequiredString(arguments, "document"), cancellationToken).ConfigureAwait(false)),
                "find_user_files" => Success(await FindUserFilesAsync(RequiredString(arguments, "query"), cancellationToken).ConfigureAwait(false)),
                _ => Failure("That read-only operation is not available.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure("The read-only operation could not be completed with the supplied input.");
        }
    }

    internal static object Failure(string text) => Result(false, text);
    private static object Success(string text) => Result(true, Bound(text));
    private static object Result(bool success, string text) => new
    {
        success,
        contentItems = new[] { new { type = "inputText", text } }
    };
    private static object Tool(string name, string description, object inputSchema) => new
    {
        type = "function",
        name,
        description,
        inputSchema
    };

    private static async Task<string> SearchDocumentationAsync(string query, CancellationToken cancellationToken)
    {
        string[] terms = SearchTerms(query);
        var matches = new List<string>();
        foreach ((string document, string relativePath) in DocumentationFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(AiAssistantProfile.KnowledgeRoot, relativePath);
            if (!File.Exists(path)) continue;
            string[] lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            for (int index = 0; index < lines.Length && matches.Count < 80; index++)
            {
                if (!terms.Any(term => lines[index].Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                matches.Add($"{document}:{index + 1}: {lines[index].Trim()}");
            }
        }
        return matches.Count == 0
            ? "No matching maintained documentation was found."
            : string.Join(Environment.NewLine, matches);
    }

    private static async Task<string> ReadDocumentationAsync(string document, CancellationToken cancellationToken)
    {
        if (!DocumentationFiles.TryGetValue(document, out string? relativePath))
            throw new ArgumentException("Unknown document.", nameof(document));
        string root = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithin(path, root) || !File.Exists(path)) throw new IOException("Document is unavailable.");
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static Task<string> FindUserFilesAsync(string query, CancellationToken cancellationToken) => Task.Run(() =>
    {
        string[] terms = SearchTerms(query);
        string root = NormalizeUserProfileRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (!Directory.Exists(root) || !IsFixedLocalPath(root))
            throw new IOException("The user profile is unavailable.");

        var results = new List<FileInfo>();
        var pending = new Stack<string>();
        pending.Push(root);
        long deadline = Environment.TickCount64 + (long)SearchBudget.TotalMilliseconds;
        while (pending.Count > 0 && results.Count < MaximumSearchResults && Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Environment.TickCount64 >= deadline) break;
                    var info = new FileInfo(file);
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0 ||
                        !SearchableExtensions.Contains(info.Extension) ||
                        !terms.All(term => info.Name.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                    results.Add(info);
                    if (results.Count >= MaximumSearchResults) break;
                }
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Environment.TickCount64 >= deadline) break;
                    var info = new DirectoryInfo(directory);
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0 ||
                        ExcludedDirectoryNames.Contains(info.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    pending.Push(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        if (results.Count == 0) return "No matching user documents were found.";
        return string.Join(Environment.NewLine, results
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => $"{file.FullName} | {file.Length} bytes | modified {file.LastWriteTimeUtc:O}"));
    }, cancellationToken);

    internal static string NormalizeUserProfileRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new IOException("The user profile is unavailable.");
        return Path.GetFullPath(value);
    }

    private static string RequiredString(JsonElement arguments, string property)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { } text || string.IsNullOrWhiteSpace(text) || text.Length > 200)
            throw new ArgumentException("A required string was missing or invalid.", property);
        return text.Trim();
    }

    private static string[] SearchTerms(string query)
    {
        string[] terms = [.. query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)];
        return terms.Length > 0 ? terms : throw new ArgumentException("A useful search term is required.", nameof(query));
    }

    private static bool IsFixedLocalPath(string path)
    {
        string? root = Path.GetPathRoot(path);
        return root is not null && new DriveInfo(root).DriveType == DriveType.Fixed && !path.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static bool IsWithin(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static string Bound(string value) =>
        AiAssistantProtocol.BoundWithEllipsis(value, MaximumResultCharacters);
}
