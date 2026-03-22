using Scrapscript.Core.Eval;
using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core.Platforms;

public static class PlatformTypes
{
    public static void RuntimeCheck(ScrapValue value, ScrapType expected, TypeEnv env)
    {
        switch (expected)
        {
            case TText:
                if (value is not ScrapText)
                    throw new ScrapTypeError($"Platform expected text, got: {value.Display()}");
                break;
            case TInt:
                if (value is not ScrapInt)
                    throw new ScrapTypeError($"Platform expected int, got: {value.Display()}");
                break;
            case TFloat:
                if (value is not ScrapFloat)
                    throw new ScrapTypeError($"Platform expected float, got: {value.Display()}");
                break;
            case THole:
                if (value is not ScrapHole)
                    throw new ScrapTypeError($"Platform expected (), got: {value.Display()}");
                break;
            case TName named:
                var typeDef = env.LookupTypeDef(named.Name)
                    ?? throw new ScrapTypeError($"Platform type '{named.Name}' not found in environment");
                if (value is not ScrapVariant variant)
                    throw new ScrapTypeError($"Platform expected a variant of type '{named.Name}', got: {value.Display()}");
                var variantDef = typeDef.Variants.FirstOrDefault(v => v.Tag == variant.Tag)
                    ?? throw new ScrapTypeError($"Platform type '{named.Name}' has no variant '#{variant.Tag}'");
                if (variantDef.PayloadTypes.Count > 0)
                {
                    var payload = variant.Payload
                        ?? throw new ScrapTypeError($"Platform expected payload for #{variant.Tag}, got none");
                    if (variantDef.PayloadTypes.Count == 1)
                        RuntimeCheck(payload, variantDef.PayloadTypes[0], env);
                    else if (payload is ScrapList list)
                        for (int i = 0; i < variantDef.PayloadTypes.Count; i++)
                            RuntimeCheck(list.Items[i], variantDef.PayloadTypes[i], env);
                }
                break;
            case TRecord rec:
                if (value is not ScrapRecord record)
                    throw new ScrapTypeError($"Platform expected a record, got: {value.Display()}");
                foreach (var (field, fieldType) in rec.Fields)
                {
                    if (!record.Fields.TryGetValue(field, out var fieldVal))
                        throw new ScrapTypeError($"Platform record missing field '{field}'");
                    RuntimeCheck(fieldVal, fieldType, env);
                }
                break;
            case TVar:
                break; // unconstrained — trust compile-time
            default:
                throw new ScrapTypeError($"Platform cannot runtime-check type '{expected}'");
        }
    }
}
