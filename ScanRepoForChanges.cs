using Octokit;
using System.Linq;

public sealed class ScanRepoForChanges
{
    public async Task RunAsync(string org, string repo, string token)
    {
        const string branch = "main";
        var client = new GitHubClient(new ProductHeaderValue("ScanRepoForChanges"))
        {
            Credentials = new Credentials(token)
        };

        // Set pagination options
        var prRequest = new PullRequestRequest
        {
            State = ItemStateFilter.Closed,
            Base = branch
        };

        const int pageSize = 100;
        var lookFor = DateTime.UtcNow.AddMonths(-3);
        int newFiles = 0, updatedFiles = 0, deletedFiles = 0, currentPage = 1;

        while (true)
        {
            var pullRequests = await client.Repository.PullRequest.GetAllForRepository(
                org, repo, prRequest, 
                new ApiOptions {
                    PageCount = 1,
                    PageSize = pageSize,
                    StartPage = currentPage++
                });

            if (!pullRequests.Any() || pullRequests.All(pr => pr.MergedAt < lookFor)) 
                break;

            foreach (var pr in pullRequests.Where(pr => pr.Merged 
                            && pr.MergedAt >= lookFor))
            {
                var files = await client.PullRequest.Files(org, repo, pr.Number);

                foreach (var file in files.Where(f => f.FileName.EndsWith(".md")))
                {
                    switch (file.Status)
                    {
                        case "added":
                            newFiles++;
                            break;
                        case "modified":
                            updatedFiles++;
                            break;
                        case "removed":
                            deletedFiles++;
                            break;
                    }
                }
            }
        }

        // Output the statistics
        Console.WriteLine($"New Files: {newFiles}");
        Console.WriteLine($"Updated Files: {updatedFiles}");
        Console.WriteLine($"Deleted Files: {deletedFiles}");
    }
}