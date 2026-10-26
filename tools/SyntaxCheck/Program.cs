using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var roots = args.Length > 0 ? args : new[] { "unity/Assets" };
var files = new List<string>();

foreach (var root in roots)
{
    if (!Directory.Exists(root))
    {
        Console.WriteLine($"跳过不存在的目录：{root}");
        continue;
    }

    files.AddRange(Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories));
}

var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
var errorCount = 0;

foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
{
    var text = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(text, options, file);

    foreach (var diagnostic in tree.GetDiagnostics())
    {
        // 只报语法错误；语义错误（例如类型不存在）需要真正的引用程序集，这里不检查。
        if (diagnostic.Severity != DiagnosticSeverity.Error)
        {
            continue;
        }

        errorCount++;
        var span = diagnostic.Location.GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        Console.WriteLine($"[{file}:{line}] {diagnostic.Id}: {diagnostic.GetMessage()}");
    }
}

Console.WriteLine($"检查了 {files.Count} 个 C# 文件，发现 {errorCount} 个语法错误。");
return errorCount == 0 ? 0 : 1;
