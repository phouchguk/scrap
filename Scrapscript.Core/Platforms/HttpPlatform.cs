using System.Collections.Immutable;
using System.Net;
using Scrapscript.Core.Eval;
using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core.Platforms;

public class HttpPlatform(int port = 8080, TextWriter? log = null) : IPlatform
{
    private readonly TextWriter _log = log ?? Console.Out;

    public ScrapType InputType  => TText.Instance;
    public ScrapType OutputType => new TName("http-result");

    public void RegisterTypes(TypeEnv env)
    {
        env.AddTypeDef(new TypeDef("http-result",
            ImmutableList<string>.Empty,
            ImmutableList.Create(
                new VariantDef("ok",       ImmutableList.Create<ScrapType>(TText.Instance)),
                new VariantDef("notfound", ImmutableList.Create<ScrapType>(TText.Instance)),
                new VariantDef("error",    ImmutableList.Create<ScrapType>(TText.Instance)))));
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

    // Internal — separated for testability
    public static (int status, string body) Dispatch(
        ScrapInterpreter interpreter, ScrapValue fn, string path)
    {
        var result = interpreter.Apply(fn, new ScrapText(path));
        return result switch
        {
            ScrapVariant { Tag: "ok",       Payload: ScrapText t } => (200, t.Value),
            ScrapVariant { Tag: "notfound", Payload: ScrapText t } => (404, t.Value),
            ScrapVariant { Tag: "error",    Payload: ScrapText t } => (500, t.Value),
            _ => throw new ScrapTypeError(
                     $"Http platform: unexpected response: {result.Display()}")
        };
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
