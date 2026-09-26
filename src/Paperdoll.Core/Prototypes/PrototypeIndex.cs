using System.Collections.Concurrent;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Prototypes;

/// <summary>A prototype file's path and contents.</summary>
public sealed record PrototypeSource(string Path, byte[] Contents);

/// <summary>Something wrong with a prototype file; the rest still loads.</summary>
public sealed record LoadProblem(string Path, string Message);

/// <summary>One prototype as written in its file, before its parents are applied.</summary>
public sealed record Prototype(string Kind, string Id, string Path, YamlMappingNode Node)
{
    public IReadOnlyList<string> Parents { get; } = ReadParents(Node);

    public bool Abstract => Node.Children.TryGetValue(new YamlScalarNode("abstract"), out var value)
        && value is YamlScalarNode { Value: "true" or "True" };

    private static IReadOnlyList<string> ReadParents(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode("parent"), out var parent))
            return [];
        return parent switch
        {
            YamlScalarNode scalar when !string.IsNullOrEmpty(scalar.Value) => [scalar.Value],
            YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>().Select(s => s.Value!).ToList(),
            _ => [],
        };
    }
}

/// <summary>
/// Every prototype in a fork's <c>Resources/Prototypes</c>, by kind and id, with parents applied
/// the way the game applies them (<see cref="Inheritance"/>).
/// </summary>
public sealed class PrototypeIndex
{
    private readonly Dictionary<(string Kind, string Id), Prototype> _prototypes;
    private readonly ConcurrentDictionary<(string Kind, string Id), YamlMappingNode> _resolved = new();

    private PrototypeIndex(Dictionary<(string, string), Prototype> prototypes, List<LoadProblem> problems)
    {
        _prototypes = prototypes;
        Problems = problems;
    }

    public IReadOnlyList<LoadProblem> Problems { get; }

    public int Count => _prototypes.Count;

    public static PrototypeIndex Load(IEnumerable<PrototypeSource> sources)
    {
        var parsed = sources
            .Where(s => s.Path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .AsParallel().AsOrdered()
            .Select(Parse)
            .ToList();

        var prototypes = new Dictionary<(string, string), Prototype>();
        var problems = new List<LoadProblem>();
        foreach (var (found, fileProblems) in parsed)
        {
            problems.AddRange(fileProblems);
            foreach (var proto in found)
            {
                // The game refuses to start with two prototypes of one kind and id, so a fork that
                // runs has none; keep the first and note the second.
                if (!prototypes.TryAdd((proto.Kind, proto.Id), proto))
                    problems.Add(new LoadProblem(proto.Path, $"Duplicate {proto.Kind} {proto.Id}, also in {prototypes[(proto.Kind, proto.Id)].Path}."));
            }
        }
        return new PrototypeIndex(prototypes, problems);
    }

    private static (List<Prototype>, List<LoadProblem>) Parse(PrototypeSource source)
    {
        var found = new List<Prototype>();
        var problems = new List<LoadProblem>();
        try
        {
            // StreamReader drops a byte order mark, which some prototype files have.
            using var reader = new StreamReader(new MemoryStream(source.Contents), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var yaml = new YamlStream();
            yaml.Load(reader);

            foreach (var document in yaml.Documents)
            {
                if (document.RootNode is not YamlSequenceNode root)
                    continue;
                foreach (var item in root.Children.OfType<YamlMappingNode>())
                {
                    if (Scalar(item, "type") is { } kind && Scalar(item, "id") is { } id)
                        found.Add(new Prototype(kind, id, source.Path, item));
                }
            }
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            problems.Add(new LoadProblem(source.Path, e.Message));
        }
        return (found, problems);
    }

    internal static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    public bool TryGet(string kind, string id, out Prototype prototype) =>
        _prototypes.TryGetValue((kind, id), out prototype!);

    public IEnumerable<Prototype> OfKind(string kind) =>
        _prototypes.Values.Where(p => p.Kind == kind);

    /// <summary>
    /// The prototype with its parents applied, or null if there is no such prototype.
    /// Missing parents are skipped, as a broken reference in one fork file should not hide the rest.
    /// </summary>
    public YamlMappingNode? Resolve(string kind, string id) => Resolve(kind, id, []);

    private YamlMappingNode? Resolve(string kind, string id, HashSet<string> visiting)
    {
        if (_resolved.TryGetValue((kind, id), out var done))
            return done;
        if (!_prototypes.TryGetValue((kind, id), out var proto))
            return null;
        if (!visiting.Add(id))
            return proto.Node; // a parent loop; the game rejects these

        var node = proto.Node;
        foreach (var parentId in proto.Parents)
        {
            if (Resolve(kind, parentId, visiting) is { } parent)
                node = Inheritance.Apply(kind, parent, node);
        }

        visiting.Remove(id);
        return _resolved[(kind, id)] = node;
    }
}
