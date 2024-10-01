using System.Diagnostics;
using ScanPullRequestsForScores;

public static class Program
{
    public static async Task Main(string[] args)
    {
        string org, repo;

        if (args.Length > 0)
        {
            var parts = args[0].Split('/');
            switch (parts.Length)
            {
                case 0:
                case 1:
                    org = "MicrosoftDocs";
                    repo = args[0];
                    break;
                case 2:
                    org = parts[0];
                    repo = parts[1];
                    break;
                default:
                    PrintHelp();
                    return;
            }
        }
        else
        {
            PrintHelp();
            return;
        }

        var token = await ReadGitHubToken();
        if (string.IsNullOrEmpty(token))
        {
            await Console.Error.WriteLineAsync("Missing GitHub token. Exiting.");
            return;
        }

        Console.WriteLine($"Running on {org}/{repo}");

        try
        {
            //await new ScanPRs(org, repo, token).RunAsync();
            await new ScanRepoForChanges().RunAsync(org, repo, token);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null) Console.WriteLine(ex.InnerException.Message);
        }
    }

    private static void PrintHelp() => Console.Error.WriteLine("Missing arguments. Usage: <org/repo>");

    private static async Task<string?> ReadGitHubToken()
    {
        var psi = new ProcessStartInfo("op")
        {
            Arguments = "read op://personal/github.com/token",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return null;
        
        var token = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return token?.TrimEnd();
    }
}
