
namespace Net7ClientManager.Services;

internal readonly record struct ClientGameUiPoint(
    string Name,
    int BaseWidth,
    int BaseHeight,
    double BaseX,
    double BaseY)
{
    public bool TryScaleToClient(
        Size clientSize,
        out Point clientPoint)
    {
        clientPoint = Point.Empty;

        if (this.BaseWidth <= 0 ||
            this.BaseHeight <= 0 ||
            clientSize.Width <= 0 ||
            clientSize.Height <= 0)
        {
            return false;
        }

        var x = (int)Math.Round(
            this.BaseX *
            clientSize.Width /
            this.BaseWidth);

        var y = (int)Math.Round(
            this.BaseY *
            clientSize.Height /
            this.BaseHeight);

        clientPoint = new Point(
            Math.Clamp(x, 0, clientSize.Width - 1),
            Math.Clamp(y, 0, clientSize.Height - 1));

        return true;
    }
}

