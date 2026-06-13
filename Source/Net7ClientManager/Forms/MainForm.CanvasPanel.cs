// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private Control CreateCanvasPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(red: 244, green: 247, blue: 251),
        };

        this.addSlotButton = new Button
        {
            Text = "+ Add Slot",
            Width = 96,
            Height = 30,
            Location = new Point(x: 16, y: 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };

        this.addSlotButton.Click += this.AddSlotButton_OnClick;

        panel.Controls.Add(this.layoutDesignerControl);
        panel.Controls.Add(this.addSlotButton);

        this.addSlotButton.BringToFront();

        return panel;
    }
}
