using System.Text;

// Dedicated CodeGraph MCP stdio host. This executable intentionally has no IPC or
// self-test mode; stdout belongs exclusively to the MCP JSON-RPC transport.
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CodeGraphNativeLibraryResolver.Install();

        try
        {
            await CodeGraphMcpServer.RunAsync(ResolveProjectRoot(args));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string ResolveProjectRoot(string[] args)
    {
        string? projectRoot = null;
        var projectIndex = Array.IndexOf(args, "--project");
        if (projectIndex >= 0)
        {
            if (projectIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[projectIndex + 1]))
            {
                throw new ArgumentException("--project requires a directory path.");
            }

            projectRoot = args[projectIndex + 1];
        }

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            projectRoot = Environment.GetEnvironmentVariable("CODEGRAPH_PROJECT_PATH");
        }

        projectRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : projectRoot.Trim());
        if (!Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException($"CodeGraph project directory does not exist: {projectRoot}");
        }

        return projectRoot;
    }
}
