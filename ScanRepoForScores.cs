using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Octokit;

namespace ScanPullRequestsForScores;

public record Score
{
    public int TotalScore { get; init; } 
    public int WordsPhases { get; init; }
    public int Correctness { get; init; }
    public int Clarity { get; init; }
}

public record User
{
    public string? Login { get; init; }
}

public record Comment
{
    //public long Id { get; init; }
    public string? Body { get; init; }
    public User? User { get; init; }
}

public sealed class ScanPRs
{
    private const string LookForUserId = "acrolinxatmsft1";
    private const int MinScore = 80;

    private readonly string owner;
    private readonly string repo;
    private readonly string header = new('-', 80);
    private readonly HttpClient httpClient = new();
    private readonly GitHubClient client;
    private readonly Regex lookForScores;
    private readonly Octokit.ProductHeaderValue userAgentInfo = new("ScanPRsInMicrosoftDocs", "1.0.0");
    private int totalPRs, totalPRsScanned, scoreWentUp, scoreWentDown, totalLessThanMinScore, scoreStayedSame;

    public ScanPRs(string owner, string repo, string gitHubToken)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.repo = repo ?? throw new ArgumentNullException(nameof(repo));

        client = new GitHubClient(userAgentInfo) { Credentials = new Credentials(gitHubToken) };

        // Article | Total score<br>(Required: 80) | Words + phrases<br>(Brand, terms) | Correctness<br>(Spelling, grammar) | Clarity<br>(Readability)
        lookForScores = new Regex(
            @$"\[([^]]+)\]\(https:\/\/github.com\/{owner}\/{repo}\/blob\/[a-zA-Z0-9]+\/([^]]+)\) \| \[(\d+)\]\(https:\/\/microsoft-ce-csi.acrolinx.cloud\/api\/v1\/checking\/scorecards\/[a-zA-Z0-9-]+\) \| (\d+) \| (\d+) \| (\d+) \|", 
            RegexOptions.Compiled);
    }

    public async Task RunAsync()
    {
        var options = new ApiOptions { PageCount = 1, PageSize = 100, StartPage = 1 };
        var requestOptions = new PullRequestRequest { State = ItemStateFilter.Open };
        var pullRequests = await client.PullRequest.GetAllForRepository(owner, repo, requestOptions, options);

        for (; pullRequests.Count > 0; options.StartPage++)
        {
            foreach (var pr in pullRequests)
            {
                totalPRs++;
                if (IsPrEligible(pr))
                {
                    await ProcessPullRequestAsync(pr);
                }
            }

            pullRequests = await client.PullRequest.GetAllForRepository(owner, repo, options);
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"\r\nScanned {totalPRsScanned} of {totalPRs} PRs\r\n" +
                          $"\t{totalLessThanMinScore} files have less than {MinScore}\r\n" +
                          $"\t{scoreWentDown} files have scores that went down\r\n" +
                          $"\t{scoreWentUp} files have scores that went up\r\n" +
                          $"\t{scoreStayedSame} files have scores that didn't change\r\n" +
                          $"\tTotal scores changed = {scoreWentDown + scoreWentUp}");

        Console.ResetColor();
    }

    private static bool IsPrEligible(PullRequest pr)
    {
        var invalidStates = new[] { "[stale]", "do not merge", "do not publish" };
        return !invalidStates.Any(state => pr.Title.Contains(state, StringComparison.CurrentCultureIgnoreCase)) &&
               pr.Labels.Any(l => string.Equals(l.Name, "needs-human-review", StringComparison.CurrentCultureIgnoreCase));
    }

    private async Task ProcessPullRequestAsync(PullRequest pr)
    {
        var comments = await GetCommentsAsync(pr.Number);
        if (comments == null || comments.Count == 0) return;

        var scores = ParseCommentsAsync(comments.Where(c => c.User?.Login == LookForUserId));
        bool hasHeader = false;

        // Skip this PR if we don't have enough scores to compare.
        if (scores.Count == 0 || !scores.Any(s => s.Value.Count > 1)) return;

        totalPRsScanned++;

        foreach (var (filePath, scoreList) in scores)
        {
            var firstScore = scoreList.First();
            var lastScore = scoreList.Last();
            
            bool badScore = lastScore.TotalScore < MinScore;
            if (badScore) totalLessThanMinScore++;

            bool changedScore = false;
            if (firstScore.TotalScore > lastScore.TotalScore)
            {
                scoreWentDown++;
                changedScore = true;
            }
            else if (firstScore.TotalScore < lastScore.TotalScore)
            {
                scoreWentUp++;
                changedScore = true;
            }
            else
            {
                scoreStayedSame++;
            }

            if (badScore || changedScore)
            {
                if (!hasHeader)
                {
                    hasHeader = true;
                    Console.WriteLine($"\r\n{pr.Number}: \"{pr.Title.Trim()}\" by {pr.User.Login}");
                    Console.WriteLine(header);
                }

                PrintScores(filePath, firstScore, lastScore);
            }
        }
    }

    private async Task<List<Comment>?> GetCommentsAsync(int issueNumber)
    {
        var url = client.Connection.BaseAddress + $"repos/{owner}/{repo}/issues/{issueNumber}/comments";
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(userAgentInfo.Name, userAgentInfo.Version));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.Credentials.GetToken());
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<Comment>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private Dictionary<string, List<Score>> ParseCommentsAsync(IEnumerable<Comment> comments)
    {
        var scores = new Dictionary<string, List<Score>>();

        foreach (var comment in comments)
        {
            var input = comment.Body;
            if (string.IsNullOrEmpty(input)) continue;

            var matches = lookForScores.Matches(input);
            foreach (Match match in matches.Where(m => m.Success))
            {
                var filePath = match.Groups[1].Value;
                if (Path.GetExtension(filePath).ToLower() != ".md") continue;

                if (!scores.ContainsKey(filePath))
                    scores[filePath] = [];

                scores[filePath].Add(new Score
                {
                    TotalScore = int.Parse(match.Groups[3].Value),
                    WordsPhases = int.Parse(match.Groups[4].Value),
                    Correctness = int.Parse(match.Groups[5].Value),
                    Clarity = int.Parse(match.Groups[6].Value)
                });
            }
        }

        return scores;
    }

    private static void PrintScores(string filePath, Score firstScore, Score lastScore)
    {
        Console.Write(Path.GetFileName(filePath) + ": ");
        PrintScore("Total", firstScore.TotalScore, lastScore.TotalScore);
        PrintScore("Words", firstScore.WordsPhases, lastScore.WordsPhases);
        PrintScore("Correctness", firstScore.Correctness, lastScore.Correctness);
        PrintScore("Clarity", firstScore.Clarity, lastScore.Clarity);
        Console.WriteLine();
    }

    private static void PrintScore(string label, int first, int last)
    {
        Console.Write($"{label}: ");

        if (last > first)
        {
            Console.ForegroundColor = last >= MinScore ? ConsoleColor.Green : ConsoleColor.Yellow;
        }
        else if (first > last)
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }
        else
        {
            Console.ForegroundColor = last >= MinScore ? ConsoleColor.White : ConsoleColor.Yellow;
        }

        Console.Write($"{first}->{last}");
        Console.ResetColor();
        Console.Write(' ');
    }
}