using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;

var interpreter = new ScrapInterpreter();
Console.WriteLine("Scrapscript REPL  (Ctrl+C to exit)");

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;
    if (string.IsNullOrWhiteSpace(line)) continue;

    var input = line;
    while (true)
    {
        try
        {
            var result = interpreter.Eval(input);
            Console.WriteLine(result.Display());
            break;
        }
        catch (ParseError ex)
        {
            if (IsLikelyIncomplete(ex))
            {
                Console.Write("  ");
                var more = Console.ReadLine();
                if (more is null) { Console.WriteLine(); break; }
                input += "\n" + more;
            }
            else
            {
                Console.WriteLine($"Parse error: {ex.Message}");
                break;
            }
        }
        catch (LexError ex)
        {
            Console.WriteLine($"Lex error: {ex.Message}");
            break;
        }
        catch (ScrapTypeError ex)
        {
            Console.WriteLine($"Type error: {ex.Message}");
            break;
        }
        catch (ScrapNameError ex)
        {
            Console.WriteLine($"Name error: {ex.Message}");
            break;
        }
        catch (ScrapMatchError ex)
        {
            Console.WriteLine($"Match error: {ex.Message}");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            break;
        }
    }
}

static bool IsLikelyIncomplete(ParseError ex) =>
    ex.Message.Contains("Eof") || ex.Message.Contains("EOF");
