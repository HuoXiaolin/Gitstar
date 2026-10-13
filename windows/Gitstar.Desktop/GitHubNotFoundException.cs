namespace Gitstar.Desktop;

public sealed class GitHubNotFoundException(string message):IOException(message);
