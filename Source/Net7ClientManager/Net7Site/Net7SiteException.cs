namespace Net7ClientManager.Net7Site;

internal sealed class Net7SiteException : InvalidOperationException
{
    public Net7SiteException()
    {
    }

    public Net7SiteException(string message)
        : base(message)
    {
    }

    public Net7SiteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Net7SiteException(string message, bool isSignedOut)
        : base(message)
    {
        this.IsSignedOut = isSignedOut;
    }

    public bool IsSignedOut { get; }
}
