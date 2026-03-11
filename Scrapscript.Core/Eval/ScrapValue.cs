using System.Collections.Immutable;
using System.Text;
using Scrapscript.Core.Parser;

namespace Scrapscript.Core.Eval;

public abstract record ScrapValue
{
    public abstract string Display();
}

public record ScrapInt(long Value) : ScrapValue
{
    public override string Display() => Value.ToString();
}

public record ScrapFloat(double Value) : ScrapValue
{
    public override string Display() =>
        Value % 1 == 0 ? $"{Value:F1}" : Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public record ScrapText(string Value) : ScrapValue
{
    public override string Display() => $"\"{Escape(Value)}\"";
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
}

public record ScrapBytes(byte[] Value) : ScrapValue
{
    public virtual bool Equals(ScrapBytes? other) =>
        other is not null && Value.SequenceEqual(other.Value);
    public override int GetHashCode() =>
        Value.Aggregate(0, (h, b) => HashCode.Combine(h, b));
    public override string Display()
    {
        if (Value.Length == 1)
            return $"~{Value[0]:X2}";
        return $"~~{Convert.ToBase64String(Value)}";
    }
}

public record ScrapHole : ScrapValue
{
    public override string Display() => "()";
}

public record ScrapList(ImmutableList<ScrapValue> Items) : ScrapValue
{
    public virtual bool Equals(ScrapList? other) =>
        other is not null && Items.SequenceEqual(other.Items);
    public override int GetHashCode() =>
        Items.Aggregate(0, (h, v) => HashCode.Combine(h, v));
    public override string Display()
    {
        var items = string.Join(", ", Items.Select(v => v.Display()));
        return $"[{items}]";
    }
}

public record ScrapRecord(ImmutableDictionary<string, ScrapValue> Fields) : ScrapValue
{
    public override string Display()
    {
        var fields = string.Join(", ", Fields.Select(kv => $"{kv.Key} = {kv.Value.Display()}"));
        return $"{{ {fields} }}";
    }
}

public record ScrapVariant(string Tag, ScrapValue? Payload) : ScrapValue
{
    public override string Display()
    {
        if (Payload is null)
            return $"#{Tag}";
        // Multi-arg payload stored as list — display items space-separated
        if (Payload is ScrapList l)
            return $"#{Tag} {string.Join(" ", l.Items.Select(v => v.Display()))}";
        return $"#{Tag} {Payload.Display()}";
    }
}

public record ScrapFunction(Pattern[] Params, Expr Body, ScrapEnv Closure) : ScrapValue
{
    public override string Display() => "<function>";
    // For currying: single param lambda
    public static ScrapFunction Lambda(Pattern param, Expr body, ScrapEnv env) =>
        new ScrapFunction(new[] { param }, body, env);
}

public record ScrapCaseFunction(List<(Pattern Pattern, Expr Body)> Arms, ScrapEnv Closure) : ScrapValue
{
    public override string Display() => "<case function>";
}

public record ScrapBuiltin(string Name, Func<ScrapValue, ScrapValue> Apply) : ScrapValue
{
    public override string Display() => $"<builtin:{Name}>";
}

public record ScrapBuiltin2(string Name, Func<ScrapValue, ScrapValue, ScrapValue> Apply) : ScrapValue
{
    public override string Display() => $"<builtin:{Name}>";
}

// Partial application of a 2-arg builtin
public record ScrapBuiltinPartial(string Name, ScrapValue First, Func<ScrapValue, ScrapValue, ScrapValue> Apply) : ScrapValue
{
    public override string Display() => $"<builtin-partial:{Name}>";
}
