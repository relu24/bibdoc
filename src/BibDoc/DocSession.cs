namespace BibDoc;

/// <summary>
/// A document a reader has open: its blocks as one program, its data, and the
/// values the reader has given its inputs. Each frame runs the program again;
/// a click runs it once more, to learn what that click does.
/// </summary>
public sealed class DocSession(IReadOnlyList<string> blocks, object? data, bool contained = false)
{
    private readonly Interpreter _program = new(blocks, contained);
    private readonly Dictionary<string, object?> _state = [];

    public IReadOnlyList<string> Sources { get; } = blocks;
    public object? Data { get; } = data;
    public IReadOnlyDictionary<string, object?> State => _state;

    /// <summary>What every block draws now.</summary>
    public FrameResult Frame() => _program.Run(Data, _state);

    /// <summary>An input's new value (or a list's ticked ids), by its key.</summary>
    public void Set(string key, object? value) => _state[key] = value;

    /// <summary>
    /// What a click on the button with this key does: the effects it asks for, and the states
    /// it set(), which the session keeps. A click that fails changes nothing.
    /// </summary>
    public FrameResult Click(string buttonKey)
    {
        var result = _program.Run(Data, _state, buttonKey);
        if (result.Errors.All(e => e is null))
            foreach (var (k, v) in result.Sets) _state[k] = v;
        return result;
    }

    /// <summary>Keeps the values a reader gave, when the document is edited and opened again.</summary>
    public void CarryFrom(DocSession previous)
    {
        foreach (var (k, v) in previous._state) _state[k] = v;
    }

    /// <summary>The <c>data:</c> of a front matter block (with or without its --- lines), or null.</summary>
    public static object? DataFrom(string? frontMatter, out string? error) => DataFrom(frontMatter, [], out error);

    /// <summary>
    /// A document's data: the front matter's <c>data:</c>, with the keys of each <c>bib data</c>
    /// block (YAML, a mapping) added to it in document order. A key given twice is an error
    /// that names it, and so is a data block beside a <c>data:</c> that is not a mapping.
    /// </summary>
    public static object? DataFrom(string? frontMatter, IReadOnlyList<string> dataBlocks, out string? error)
    {
        error = null;
        object? data = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(frontMatter) && Yaml.Parse(frontMatter) is Dictionary<string, object?> meta)
                meta.TryGetValue("data", out data);
            if (dataBlocks.Count == 0) return data;
            if (data is not null and not Dictionary<string, object?>)
                throw new DocError("the front matter's data: is not a mapping, so data blocks cannot add to it");
            var merged = data as Dictionary<string, object?> ?? [];
            for (int i = 0; i < dataBlocks.Count; i++)
            {
                var block = Yaml.Parse(dataBlocks[i]);
                if (block is null) continue;
                if (block is not Dictionary<string, object?> keys)
                    throw new DocError($"data block {i + 1} is not a mapping of names to values");
                foreach (var (key, value) in keys)
                {
                    if (!merged.TryAdd(key, value))
                        throw new DocError($"data block {i + 1}: '{key}' is already in the data");
                }
            }
            return merged;
        }
        catch (DocError e)
        {
            error = e.Message;
            return data;
        }
    }
}
