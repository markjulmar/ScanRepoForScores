# ScanRepoForScores

This is a .NET tool that scans all the open pull requests in a GitHub repository and looks for the Acrolinx scoring that is typically in a Microsoft Learn publishing repo. It then identifies Markdown files where the score changed over the life of the pull request and gathers some statistics.

It skips pull requests where only one commit is present, or where the title indicates the PR is stale or marked as DO NOT COMMIT. It also skips all pull requests not marked as "Requres Review" (e.g., all auto-merged PRs).