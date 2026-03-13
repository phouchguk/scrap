using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;
using Scrapscript.Core.TypeChecker;

var interpreter = new ScrapInterpreter();
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
                interpreter.Eval(source);
                sessionBindings.Add(input);
                Console.WriteLine($"defined: {bindingName}");
            }
            else
            {
                var result = interpreter.Eval(source);
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
