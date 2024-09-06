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

        var tokenFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "github-token.txt");
        if (!File.Exists(tokenFile))
        {
            await Console.Error.WriteLineAsync($"GitHub token does not exist - place PAT into {tokenFile}.");
            return;
        }

        var token = (await File.ReadAllTextAsync(tokenFile)).Trim();
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
}
