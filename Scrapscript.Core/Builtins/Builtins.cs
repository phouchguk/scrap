using System.Collections.Immutable;
using System.Text;
using Scrapscript.Core.Eval;

namespace Scrapscript.Core.Builtins;

public static class BuiltinEnv
{
    public static ScrapEnv Create()
    {
        var env = new ScrapEnv();

        // Type conversion
        env.Set("to-float", new ScrapBuiltin("to-float", v => v switch
        {
            ScrapInt i => new ScrapFloat((double)i.Value),
            ScrapFloat f => f,
            _ => throw new ScrapTypeError($"to-float: expected int, got {v.Display()}")
        }));

        env.Set("round", new ScrapBuiltin("round", v => v switch
        {
            ScrapFloat f => new ScrapInt((long)Math.Round(f.Value, MidpointRounding.AwayFromZero)),
            _ => throw new ScrapTypeError($"round: expected float, got {v.Display()}")
        }));

        env.Set("ceil", new ScrapBuiltin("ceil", v => v switch
        {
            ScrapFloat f => new ScrapInt((long)Math.Ceiling(f.Value)),
            _ => throw new ScrapTypeError($"ceil: expected float, got {v.Display()}")
        }));

        env.Set("floor", new ScrapBuiltin("floor", v => v switch
        {
            ScrapFloat f => new ScrapInt((long)Math.Floor(f.Value)),
            _ => throw new ScrapTypeError($"floor: expected float, got {v.Display()}")
        }));

        // bytes module
        env.Set("bytes/to-utf8-text", new ScrapBuiltin("bytes/to-utf8-text", v => v switch
        {
            ScrapBytes b => new ScrapText(Encoding.UTF8.GetString(b.Value)),
            _ => throw new ScrapTypeError($"bytes/to-utf8-text: expected bytes, got {v.Display()}")
        }));

        // list module
        env.Set("list/first", new ScrapBuiltin("list/first", v => v switch
        {
            ScrapList l when l.Items.Count > 0 =>
                new ScrapVariant("just", l.Items[0]),
            ScrapList => new ScrapVariant("nothing", null),
            _ => throw new ScrapTypeError($"list/first: expected list, got {v.Display()}")
        }));

        env.Set("list/length", new ScrapBuiltin("list/length", v => v switch
        {
            ScrapList l => new ScrapInt(l.Items.Count),
            _ => throw new ScrapTypeError($"list/length: expected list, got {v.Display()}")
        }));

        env.Set("list/repeat", new ScrapBuiltin2("list/repeat", (a, b) => a switch
        {
            ScrapInt n => new ScrapList(Enumerable.Repeat(b, (int)n.Value).ToImmutableList()),
            _ => throw new ScrapTypeError($"list/repeat: expected int as first argument")
        }));

        env.Set("list/map", new ScrapBuiltin("list/map", f =>
            new ScrapBuiltin("list/map(f)", lst => lst switch
            {
                ScrapList l => new ScrapList(l.Items.Select(item => Evaluator.ApplyFunction(f, item)).ToImmutableList()),
                _ => throw new ScrapTypeError("list/map: expected list")
            })));

        env.Set("list/filter", new ScrapBuiltin("list/filter", f =>
            new ScrapBuiltin("list/filter(f)", lst => lst switch
            {
                ScrapList l => new ScrapList(l.Items
                    .Where(item => Evaluator.ApplyFunction(f, item) is ScrapVariant { Tag: "true" })
                    .ToImmutableList()),
                _ => throw new ScrapTypeError("list/filter: expected list")
            })));

        env.Set("list/fold", new ScrapBuiltin("list/fold", f =>
            new ScrapBuiltin("list/fold(f)", init =>
                new ScrapBuiltin("list/fold(f,init)", lst => lst switch
                {
                    ScrapList l => l.Items.Aggregate(init, (acc, item) =>
                        Evaluator.ApplyFunction(Evaluator.ApplyFunction(f, acc), item)),
                    _ => throw new ScrapTypeError("list/fold: expected list")
                }))));

        // text module
        env.Set("text/length", new ScrapBuiltin("text/length", v => v switch
        {
            ScrapText t => new ScrapInt(t.Value.Length),
            _ => throw new ScrapTypeError($"text/length: expected text")
        }));

        env.Set("text/repeat", new ScrapBuiltin2("text/repeat", (a, b) => (a, b) switch
        {
            (ScrapInt n, ScrapText t) => new ScrapText(string.Concat(Enumerable.Repeat(t.Value, (int)n.Value))),
            _ => throw new ScrapTypeError($"text/repeat: expected int and text")
        }));

        // maybe module
        env.Set("maybe/default", new ScrapBuiltin2("maybe/default", (def, m) => m switch
        {
            ScrapVariant { Tag: "just" } v => v.Payload ?? new ScrapHole(),
            ScrapVariant { Tag: "nothing" } => def,
            _ => throw new ScrapTypeError($"maybe/default: expected maybe value")
        }));

        // string/join
        env.Set("string/join", new ScrapBuiltin2("string/join", (sep, lst) => (sep, lst) switch
        {
            (ScrapText s, ScrapList l) =>
                new ScrapText(string.Join(s.Value, l.Items.Select(i =>
                    i is ScrapText t ? t.Value : throw new ScrapTypeError("string/join: list must contain text")))),
            _ => throw new ScrapTypeError("string/join: expected text and list")
        }));

        // dict/get
        env.Set("dict/get", new ScrapBuiltin2("dict/get", (key, dict) => (key, dict) switch
        {
            (ScrapText k, ScrapRecord r) =>
                r.Fields.TryGetValue(k.Value, out var v)
                    ? new ScrapVariant("just", v)
                    : new ScrapVariant("nothing", null),
            _ => throw new ScrapTypeError("dict/get: expected text key and record")
        }));

        // int/float math
        env.Set("abs", new ScrapBuiltin("abs", v => v switch
        {
            ScrapInt i   => new ScrapInt(Math.Abs(i.Value)),
            ScrapFloat f => new ScrapFloat(Math.Abs(f.Value)),
            _ => throw new ScrapTypeError($"abs: expected int or float, got {v.Display()}")
        }));

        env.Set("min", new ScrapBuiltin2("min", (a, b) => (a, b) switch
        {
            (ScrapInt x,   ScrapInt y)   => x.Value <= y.Value ? x : y,
            (ScrapFloat x, ScrapFloat y) => x.Value <= y.Value ? x : y,
            _ => throw new ScrapTypeError($"min: expected two ints or two floats")
        }));

        env.Set("max", new ScrapBuiltin2("max", (a, b) => (a, b) switch
        {
            (ScrapInt x,   ScrapInt y)   => x.Value >= y.Value ? x : y,
            (ScrapFloat x, ScrapFloat y) => x.Value >= y.Value ? x : y,
            _ => throw new ScrapTypeError($"max: expected two ints or two floats")
        }));

        // list module (continued)
        env.Set("list/reverse", new ScrapBuiltin("list/reverse", v => v switch
        {
            ScrapList l => new ScrapList(l.Items.Reverse().ToImmutableList()),
            _ => throw new ScrapTypeError($"list/reverse: expected list")
        }));

        env.Set("list/sort", new ScrapBuiltin("list/sort", v => v switch
        {
            ScrapList l => new ScrapList(l.Items
                .OrderBy(x => x, ScrapValueComparer.Instance)
                .ToImmutableList()),
            _ => throw new ScrapTypeError($"list/sort: expected list")
        }));

        env.Set("list/zip", new ScrapBuiltin2("list/zip", (a, b) => (a, b) switch
        {
            (ScrapList la, ScrapList lb) => new ScrapList(
                la.Items.Zip(lb.Items, (x, y) =>
                    (ScrapValue)new ScrapList(ImmutableList.Create(x, y)))
                .ToImmutableList()),
            _ => throw new ScrapTypeError($"list/zip: expected two lists")
        }));

        // text module (continued)
        env.Set("text/trim", new ScrapBuiltin("text/trim", v => v switch
        {
            ScrapText t => new ScrapText(t.Value.Trim()),
            _ => throw new ScrapTypeError($"text/trim: expected text")
        }));

        env.Set("text/split", new ScrapBuiltin2("text/split", (sep, str) => (sep, str) switch
        {
            (ScrapText s, ScrapText t) => new ScrapList(
                t.Value.Split(s.Value)
                 .Select(part => (ScrapValue)new ScrapText(part))
                 .ToImmutableList()),
            _ => throw new ScrapTypeError($"text/split: expected two text values")
        }));

        env.Set("text/to-upper", new ScrapBuiltin("text/to-upper", v => v switch
        {
            ScrapText t => new ScrapText(t.Value.ToUpper()),
            _ => throw new ScrapTypeError($"text/to-upper: expected text")
        }));

        env.Set("text/to-lower", new ScrapBuiltin("text/to-lower", v => v switch
        {
            ScrapText t => new ScrapText(t.Value.ToLower()),
            _ => throw new ScrapTypeError($"text/to-lower: expected text")
        }));

        // Boolean conveniences (#true and #false as values)
        env.Set("true", new ScrapVariant("true", null));
        env.Set("false", new ScrapVariant("false", null));

        return env;
    }
}
