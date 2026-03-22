using System.Collections.Immutable;
using System.Net;
using Scrapscript.Core.Eval;
using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core.Platforms;

public class HttpPlatform(int port = 8080, TextWriter? log = null) : IPlatform
{
    private readonly TextWriter _log = log ?? Console.Out;

    public ScrapType InputType  => TText.Instance;
    public ScrapType OutputType => new TName("http-response");

    public void RegisterTypes(TypeEnv env)
    {
        // Programs return #send { status = int, body = text }.
        env.AddTypeDef(new TypeDef("http-response",
            ImmutableList<string>.Empty,
            ImmutableList.Create(
                new VariantDef("send", ImmutableList.Create<ScrapType>(
                    new TRecord(ImmutableDictionary<string, ScrapType>.Empty
                        .Add("status", TInt.Instance)
                        .Add("body",   TText.Instance)))))));
    }

    public void Run(ScrapInterpreter interpreter, string source)
    {
        interpreter.CheckAgainstPlatform(source, this);
        var fn = interpreter.Eval(source, typeCheck: false);

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _log.WriteLine($"Listening on http://localhost:{port}/");

        while (true)
        {
            var ctx = listener.GetContext();
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            try
            {
                var (status, body) = Dispatch(interpreter, fn, path);
                Write(ctx.Response, status, body);
            }
            catch (Exception ex)
            {
                Write(ctx.Response, 500, ex.Message);
            }
        }
    }

    // Internal — separated for testability.
    // Applies the program function to the request path, then runs the effect loop
    // until a terminal #send { status, body } is reached.
    public static (int status, string body) Dispatch(
        ScrapInterpreter interpreter, ScrapValue fn, string path)
    {
        var effect = interpreter.Apply(fn, new ScrapText(path));
        while (true)
        {
            switch (effect)
            {
                case ScrapVariant { Tag: "send", Payload: ScrapRecord r }:
                    var status = r.Fields.TryGetValue("status", out var s) && s is ScrapInt si
                        ? (int)si.Value : 200;
                    var body = r.Fields.TryGetValue("body", out var b) && b is ScrapText bt
                        ? bt.Value : "";
                    return (status, body);

                // Future effects go here — execute the effect, call its continuation,
                // and continue the loop. Example:
                // case ScrapVariant { Tag: "query", Payload: ScrapRecord q }:
                //     var rows = RunQuery(q.Fields["sql"]);
                //     effect = interpreter.Apply(q.Fields["then"], rows);
                //     continue;

                default:
                    throw new ScrapTypeError(
                        $"Http platform: unexpected effect: {effect.Display()}");
            }
        }
    }

    private static void Write(HttpListenerResponse resp, int status, string body)
    {
        resp.StatusCode = status;
        resp.ContentType = "text/plain; charset=utf-8";
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        resp.ContentLength64 = bytes.Length;
        resp.OutputStream.Write(bytes);
        resp.OutputStream.Close();
    }
}
