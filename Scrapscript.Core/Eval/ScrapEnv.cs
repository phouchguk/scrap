namespace Scrapscript.Core.Eval;

public class ScrapEnv
{
    private readonly Dictionary<string, ScrapValue> _bindings = new();
    private readonly ScrapEnv? _parent;

    public ScrapEnv(ScrapEnv? parent = null)
    {
        _parent = parent;
    }

    public ScrapValue Lookup(string name)
    {
        if (_bindings.TryGetValue(name, out var value))
            return value;
        if (_parent != null)
            return _parent.Lookup(name);
        throw new ScrapNameError($"Unbound variable: {name}");
    }

    public bool TryLookup(string name, out ScrapValue? value)
    {
        if (_bindings.TryGetValue(name, out value))
            return true;
        if (_parent != null)
            return _parent.TryLookup(name, out value);
        value = null;
        return false;
    }

    public ScrapEnv Extend(string name, ScrapValue value)
    {
        var env = new ScrapEnv(this);
        env._bindings[name] = value;
        return env;
    }

    public ScrapEnv ExtendMany(IEnumerable<(string, ScrapValue)> pairs)
    {
        var env = new ScrapEnv(this);
        foreach (var (name, value) in pairs)
            env._bindings[name] = value;
        return env;
    }

    public void Set(string name, ScrapValue value)
    {
        _bindings[name] = value;
    }
}
