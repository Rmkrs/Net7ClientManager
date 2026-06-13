// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;

public sealed partial class MainForm
{
    private void RefreshRunningClients()
    {
        var clients = this.clientManager.Clients.OrderBy(client => client.ProcessId).ToList();
        var signature = this.BuildRunningClientsSignature(clients);

        if (string.Equals(this.runningClientsFlowPanel.Tag as string, signature, StringComparison.Ordinal))
        {
            return;
        }

        this.runningClientsFlowPanel.Tag = signature;
        this.runningClientsFlowPanel.SuspendLayout();
        this.runningClientsFlowPanel.Controls.Clear();

        if (clients.Count == 0)
        {
            this.runningClientsFlowPanel.Controls.Add(this.CreateEmptyRunningClientsCard());
            this.runningClientsFlowPanel.ResumeLayout();

            return;
        }

        foreach (var client in clients)
        {
            var assignedSlot = this.clientManager.GetAssignedSlot(client);
            this.runningClientsFlowPanel.Controls.Add(this.CreateRunningClientCard(client, assignedSlot));
        }

        this.runningClientsFlowPanel.ResumeLayout();
    }

    private string BuildRunningClientsSignature(IEnumerable<ClientInstance> clients)
    {
        var builder = new StringBuilder();

        foreach (var client in clients)
        {
            var assignedSlot = this.clientManager.GetAssignedSlot(client);

            _ = builder
                .Append(client.ProcessId)
                .Append('|')
                .Append(client.State)
                .Append('|')
                .Append(assignedSlot?.Id)
                .Append('|')
                .Append(assignedSlot?.Name)
                .Append('|')
                .Append(assignedSlot?.AccountName)
                .Append(';');
        }

        return builder.ToString();
    }

    private Control CreateEmptyRunningClientsCard()
    {
        var label = new Label
        {
            Text = "No running clients detected.",
            AutoSize = false,
            Width = 220,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(red: 90, green: 96, blue: 106),
            Padding = new Padding(left: 8, top: 0, right: 8, bottom: 0),
            Margin = new Padding(left: 0, top: 0, right: 8, bottom: 8),
        };

        return label;
    }

    private Control CreateRunningClientCard(ClientInstance client, ClientSlot? assignedSlot)
    {
        var title = assignedSlot?.Name ?? "Unassigned";
        var account = string.IsNullOrWhiteSpace(assignedSlot?.AccountName)
            ? "No account"
            : assignedSlot.AccountName;

        var stateText = FormatClientState(client.State);
        var footer = string.Create(CultureInfo.InvariantCulture, $"{stateText} · PID {client.ProcessId}");

        var card = new Panel
        {
            Width = 160,
            Height = 56,
            Margin = new Padding(left: 0, top: 0, right: 8, bottom: 8),
            Padding = new Padding(left: 8, top: 6, right: 8, bottom: 6),
            BackColor = assignedSlot == null
                ? Color.FromArgb(red: 255, green: 252, blue: 240)
                : Color.FromArgb(red: 232, green: 245, blue: 255),
        };

        var statusDot = new Panel
        {
            Location = new Point(x: 8, y: 10),
            Size = new Size(width: 8, height: 8),
            BackColor = assignedSlot == null
                ? Color.FromArgb(red: 210, green: 153, blue: 36)
                : Color.FromArgb(red: 43, green: 147, blue: 72),
        };

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Font = new Font(this.Font.FontFamily, emSize: 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(red: 28, green: 35, blue: 45),
            Location = new Point(x: 22, y: 6),
            Size = new Size(width: card.Width - 30, height: 16),
        };

        var accountLabel = new Label
        {
            Text = account,
            AutoSize = false,
            Font = new Font(this.Font.FontFamily, emSize: 8.0f, FontStyle.Regular),
            ForeColor = Color.FromArgb(red: 86, green: 99, blue: 116),
            Location = new Point(x: 22, y: 23),
            Size = new Size(width: card.Width - 30, height: 15),
        };

        var footerLabel = new Label
        {
            Text = footer,
            AutoSize = false,
            Font = new Font(this.Font.FontFamily, emSize: 7.8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(red: 90, green: 96, blue: 106),
            Location = new Point(x: 22, y: 39),
            Size = new Size(width: card.Width - 30, height: 14),
        };

        card.Controls.Add(statusDot);
        card.Controls.Add(titleLabel);
        card.Controls.Add(accountLabel);
        card.Controls.Add(footerLabel);

        return card;
    }

    private void SaveSlotButton_OnClick(object? sender, EventArgs e)
    {
        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null || this.isUpdatingEditor)
        {
            return;
        }

        slot.Name = string.IsNullOrWhiteSpace(this.slotNameTextBox.Text)
            ? "Unnamed Slot"
            : this.slotNameTextBox.Text.Trim();

        slot.AccountName = string.IsNullOrWhiteSpace(this.accountNameTextBox.Text)
            ? null
            : this.accountNameTextBox.Text.Trim();

        slot.AutoLogin = this.autoLoginCheckBox.Checked;
        slot.Bounds.Left = decimal.ToInt32(this.leftNumeric.Value);
        slot.Bounds.Top = decimal.ToInt32(this.topNumeric.Value);

        if (this.resolutionComboBox.SelectedItem is ResolutionPresetComboBoxItem selectedPreset)
        {
            slot.ResolutionPresetName = selectedPreset.Name;
            slot.Bounds.Width = selectedPreset.Width;
            slot.Bounds.Height = selectedPreset.Height;
        }

        this.clientManager.SaveSettings();

        var assignedClient = this.clientManager.Clients.FirstOrDefault(client => client.AssignedSlotId == slot.Id);
        assignedClient?.HostForm?.ApplySlot(slot);

        this.LoadSelectedSlotIntoEditor(slot);
        this.layoutDesignerControl.Invalidate();
        this.RefreshAll();
    }

    private void AddSlotButton_OnClick(object? sender, EventArgs e)
    {
        var slotNumber = this.clientManager.CurrentProfile.Slots.Count + 1;
        var preset = this.clientManager.DefaultSlotResolutionPreset;
        var bounds = this.layoutDesignerControl.CreateDefaultSlotBounds();

        bounds.Width = preset.Width;
        bounds.Height = preset.Height;

        var slot = new ClientSlot
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"Client {slotNumber}"),
            Bounds = bounds,
            ResolutionPresetName = preset.Name,
        };

        this.clientManager.CurrentProfile.Slots.Add(slot);
        this.clientManager.SaveSettings();
        this.clientManager.ReconcileClientsToCurrentProfile();

        this.layoutDesignerControl.SelectSlot(slot);
        this.RefreshAll();
    }

    private void RemoveSlotButton_OnClick(object? sender, EventArgs e)
    {
        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null)
        {
            return;
        }

        this.clientManager.CurrentProfile.Slots.Remove(slot);

        foreach (var client in this.clientManager.Clients.Where(client => client.AssignedSlotId == slot.Id))
        {
            client.AssignedSlotId = null;
            client.HostForm?.SetUnassignedTitle();
        }

        this.clientManager.SaveSettings();
        this.layoutDesignerControl.SelectSlot(slot: null);
        this.RefreshAll();
    }

    private static string FormatClientState(ClientState state)
    {
        return state switch
        {
            ClientState.WaitingForGameWindow => "Waiting for window",
            ClientState.Docked => "Docked",
            ClientState.WaitingForTos => "Waiting for TOS",
            ClientState.AcceptingTos => "Accepting TOS",
            ClientState.WaitingForSizzle => "Waiting for sizzle",
            ClientState.WaitingForLogin => "Waiting for login",
            ClientState.LoginNameFilled => "Login filled",
            ClientState.Closing => "Closing",
            ClientState.Stopped => "Stopped",
            _ => state.ToString(),
        };
    }
}
