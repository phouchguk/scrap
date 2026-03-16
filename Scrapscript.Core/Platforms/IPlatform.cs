namespace Scrapscript.Core.Platforms;

public interface IPlatform
{
    void Run(ScrapInterpreter interpreter, string source);
}
