namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private sealed record ProfileComboBoxItem(
        Guid? ProfileId,
        string DisplayText)
    {
        public override string ToString()
        {
            return this.DisplayText;
        }
    }
}
