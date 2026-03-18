using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Platforms;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.Serialization;
using Scrapscript.Core.TypeChecker;

var cliArgs = args; // top-level 'args' is the implicit program args

if (cliArgs.Length >= 1)
{
    var yard = new LocalYard();

    switch (cliArgs[0])
    {
        case "yard" when cliArgs.Length >= 2 && cliArgs[1] == "init":
            yard.Init();
            Console.WriteLine($"Initialized scrapyard at {yard.Root}");
            return;

        case "flat" when cliArgs.Length >= 2:
        {
            var src = ResolveSource(cliArgs.Skip(1).ToArray());
            var interpreter = new ScrapInterpreter(yard);
            var value = interpreter.Eval(src, typeCheck: false);
            var flat = FlatEncoder.Encode(value);
            Console.WriteLine(Convert.ToHexString(flat));
            return;
        }

        case "push" when cliArgs.Length >= 2:
        {
            var src = ResolveSource(cliArgs.Skip(1).ToArray());
            var interpreter = new ScrapInterpreter(yard);
            var value = interpreter.Eval(src, typeCheck: false);
            yard.Init();
            var hashRef = yard.Push(FlatEncoder.Encode(value));
            Console.WriteLine($"${hashRef}");
            return;
        }

        case "pull" when cliArgs.Length >= 2:
        {
            var hashRef = cliArgs[1];
            var flat = yard.Pull(hashRef);
            if (flat is null) { Console.Error.WriteLine($"Not found: {hashRef}"); return; }
            var value = FlatDecoder.Decode(flat);
            Console.WriteLine(value.Display());
            return;
        }

        case "js" when cliArgs.Length == 2 && cliArgs[1] == "--runtime":
            Console.WriteLine(Scrapscript.Core.Compiler.JsCompiler.Runtime);
            return;

        case "js" when cliArgs.Length >= 2:
        {
            var jsArgs = cliArgs.Skip(1).ToArray();
            var includeRuntime = true;
            if (jsArgs[0] == "--no-runtime") { includeRuntime = false; jsArgs = jsArgs.Skip(1).ToArray(); }
            var src = ResolveSource(jsArgs);
            var interpreter = new ScrapInterpreter(yard);
            Console.WriteLine(interpreter.CompileToJs(src, includeRuntime));
            return;
        }

        case "eval" when cliArgs.Length >= 2:
        {
            var evalArgs = cliArgs.Skip(1).ToArray();
            DateTimeOffset? asOf = null;
            if (evalArgs.Length > 0 && evalArgs[0].StartsWith("--t="))
            {
                asOf = DateTimeOffset.Parse(evalArgs[0]["--t=".Length..]);
                evalArgs = evalArgs.Skip(1).ToArray();
            }
            var src = ResolveSource(evalArgs);
            var map = new LocalMap();
            var interpreter = new ScrapInterpreter(yard, map);
            var value = interpreter.Eval(src, typeCheck: false, asOf: asOf);
            Console.WriteLine(value.Display());
            return;
        }

        case "map" when cliArgs.Length >= 2 && cliArgs[1] == "init":
        {
            var map = new LocalMap();
            map.Init();
            Console.WriteLine($"Initialized map at {map.Root}");
            return;
        }

        case "map" when cliArgs.Length >= 4 && cliArgs[1] == "commit":
        {
            var name = cliArgs[2];
            var src = ResolveSource(cliArgs.Skip(3).ToArray());
            var map = new LocalMap();
            var interpreter = new ScrapInterpreter(yard, map);
            var value = interpreter.Eval(src, typeCheck: false);
            yard.Init();
            var hashRef = yard.Push(FlatEncoder.Encode(value));
            map.Init();
            var label = map.Commit(name, hashRef);
            Console.WriteLine(label);
            return;
        }

        case "map" when cliArgs.Length >= 3 && cliArgs[1] == "history":
        {
            var name = cliArgs[2];
            var map = new LocalMap();
            var history = map.History(name);
            foreach (var entry in history)
                Console.WriteLine($"{name}@{entry.Version}  {entry.Timestamp:yyyy-MM-ddTHH:mm:ssZ}  ${entry.HashRef}");
            return;
        }

        case "run" when cliArgs.Length >= 2:
        {
            var runArgs = cliArgs.Skip(1).ToArray();
            var platformName = "console";
            if (runArgs[0].StartsWith("--platform="))
            {
                platformName = runArgs[0]["--platform=".Length..];
                runArgs = runArgs.Skip(1).ToArray();
            }
            var source = File.ReadAllText(runArgs[0]);
            IPlatform platform = platformName switch
            {
                "console" => new ConsolePlatform(),
                "http"    => new HttpPlatform(),
                _ => throw new Exception($"Unknown platform: {platformName}")
            };
            platform.Run(new ScrapInterpreter(yard), source);
            return;
        }
    }
}

var interpreter2 = new ScrapInterpreter();
var sessionBindings = new List<string>(); // accumulated "name = expr" fragments

Console.WriteLine("Scrapscript REPL  (Ctrl+C to exit)");

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;
    if (string.IsNullOrWhiteSpace(line)) continue;

    var input = line;

    // Collect continuation lines if input is incomplete
    while (true)
    {
        bool isBinding = DetectBinding(input, out var bindingName);
        var source = isBinding
            ? BuildSource("()", new[] { input }.Concat(sessionBindings))
            : BuildSource(input, sessionBindings);

        try
        {
            if (isBinding)
            {
                // Validate the binding parses and type-checks, then persist it
                interpreter2.Eval(source);
                sessionBindings.Add(input);
                Console.WriteLine($"defined: {bindingName}");
            }
            else
            {
                var result = interpreter2.Eval(source);
                Console.WriteLine(result.Display());
            }
            break;
        }
        catch (ParseError ex) when (IsLikelyIncomplete(ex))
        {
            Console.Write("  ");
            var more = Console.ReadLine();
            if (more is null) { Console.WriteLine(); break; }
            input += "\n" + more;
        }
        catch (ParseError ex)        { Console.WriteLine($"Parse error: {ex.Message}"); break; }
        catch (LexError ex)          { Console.WriteLine($"Lex error: {ex.Message}"); break; }
        catch (TypeCheckError ex)    { Console.WriteLine($"Type error: {ex.Message}"); break; }
        catch (ScrapTypeError ex)    { Console.WriteLine($"Type error: {ex.Message}"); break; }
        catch (ScrapNameError ex)    { Console.WriteLine($"Name error: {ex.Message}"); break; }
        catch (ScrapMatchError ex)   { Console.WriteLine($"Match error: {ex.Message}"); break; }
        catch (Exception ex)         { Console.WriteLine($"Error: {ex.Message}"); break; }
    }
}

// "body ; b1 ; b2 ; ..."
static string BuildSource(string body, IEnumerable<string> bindings)
{
    var parts = bindings.ToList();
    return parts.Count == 0 ? body : body + " ; " + string.Join(" ; ", parts);
}

// Returns true if the input looks like a top-level binding (name = ... or name : type = ...)
static bool DetectBinding(string input, out string name)
{
    try
    {
        var tokens = new Lexer(input).Tokenize();
        if (tokens.Count >= 2 &&
            tokens[0].Type == TokenType.Identifier &&
            (tokens[1].Type == TokenType.Equals || tokens[1].Type == TokenType.Colon))
        {
            name = tokens[0].Text;
            return true;
        }
    }
    catch { }
    name = "";
    return false;
}

static bool IsLikelyIncomplete(ParseError ex) =>
    ex.Message.Contains("Eof") || ex.Message.Contains("EOF");

// If a single argument is an existing file path, read it; otherwise join args as source.
static string ResolveSource(string[] args) =>
    args.Length == 1 && File.Exists(args[0])
        ? File.ReadAllText(args[0])
        : string.Join(" ", args);
