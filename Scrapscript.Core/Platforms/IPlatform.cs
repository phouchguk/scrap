using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core.Platforms;

public interface IPlatform
{
    ScrapType InputType { get; }
    ScrapType OutputType { get; }
    void RegisterTypes(TypeEnv env);
    void Run(ScrapInterpreter interpreter, string source);
}
