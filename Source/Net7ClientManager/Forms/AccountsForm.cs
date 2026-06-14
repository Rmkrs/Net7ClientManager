// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Models;

public sealed class AccountsForm : Form
{
    private readonly List<GameAccount> accounts;

    private readonly FlowLayoutPanel accountsPanel = new();
    private readonly FlowLayoutPanel charactersPanel = new();

    private readonly Button addAccountButton = new();
    private readonly Button editAccountButton = new();
    private readonly Button deleteAccountButton = new();

    private readonly Button editCharacterButton = new();
    private readonly Button deleteCharacterButton = new();

    private GameAccount? selectedAccount;
    private int selectedCharacterSlotNumber = 1;
    private readonly Action<IReadOnlyList<GameAccount>> saveAccounts;

    public AccountsForm(IReadOnlyList<GameAccount> accounts, Action<IReadOnlyList<GameAccount>> saveAccounts)
    {
        this.accounts = CloneAccounts(accounts);
        this.saveAccounts = saveAccounts;

        this.Text = "Accounts";
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimumSize = new Size(980, 620);
        this.Size = new Size(1120, 720);

        this.BuildUi();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
        this.NormalizeAccountSortOrder();

        this.Resize += this.AccountsForm_Resize;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.Resize -= this.AccountsForm_Resize;

        this.addAccountButton.Click -= this.AddAccountButton_OnClick;
        this.editAccountButton.Click -= this.EditAccountButton_OnClick;
        this.deleteAccountButton.Click -= this.DeleteAccountButton_OnClick;

        this.editCharacterButton.Click -= this.EditCharacterButton_OnClick;
        this.deleteCharacterButton.Click -= this.DeleteCharacterButton_OnClick;

        base.OnFormClosed(e);
    }

    public IReadOnlyList<GameAccount> Accounts => this.accounts;

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12),
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.Controls.Add(root);

        root.Controls.Add(this.BuildAccountsArea(), 0, 0);
        root.Controls.Add(this.BuildCharactersArea(), 1, 0);
    }

    private Control BuildAccountsArea()
    {
        var group = new GroupBox
        {
            Text = "Accounts",
            Dock = DockStyle.Fill,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        group.Controls.Add(layout);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };

        this.addAccountButton.Text = "Add";
        this.addAccountButton.Width = 90;
        this.addAccountButton.Click += this.AddAccountButton_OnClick;

        this.editAccountButton.Text = "Edit";
        this.editAccountButton.Width = 90;
        this.editAccountButton.Click += this.EditAccountButton_OnClick;

        this.deleteAccountButton.Text = "Delete";
        this.deleteAccountButton.Width = 90;
        this.deleteAccountButton.Click += this.DeleteAccountButton_OnClick;

        buttonPanel.Controls.Add(this.addAccountButton);
        buttonPanel.Controls.Add(this.editAccountButton);
        buttonPanel.Controls.Add(this.deleteAccountButton);

        this.accountsPanel.Dock = DockStyle.Fill;
        this.accountsPanel.AutoScroll = true;
        this.accountsPanel.FlowDirection = FlowDirection.TopDown;
        this.accountsPanel.WrapContents = false;

        layout.Controls.Add(buttonPanel, 0, 0);
        layout.Controls.Add(this.accountsPanel, 0, 1);

        return group;
    }

    private Control BuildCharactersArea()
    {
        var group = new GroupBox
        {
            Text = "Characters",
            Dock = DockStyle.Fill,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        group.Controls.Add(layout);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };

        this.editCharacterButton.Text = "Edit";
        this.editCharacterButton.Width = 90;
        this.editCharacterButton.Click += this.EditCharacterButton_OnClick;

        this.deleteCharacterButton.Text = "Delete";
        this.deleteCharacterButton.Width = 90;
        this.deleteCharacterButton.Click += this.DeleteCharacterButton_OnClick;

        buttonPanel.Controls.Add(this.editCharacterButton);
        buttonPanel.Controls.Add(this.deleteCharacterButton);

        this.charactersPanel.Dock = DockStyle.Fill;
        this.charactersPanel.AutoScroll = true;
        this.charactersPanel.FlowDirection = FlowDirection.TopDown;
        this.charactersPanel.WrapContents = false;

        layout.Controls.Add(buttonPanel, 0, 0);
        layout.Controls.Add(this.charactersPanel, 0, 1);

        return group;
    }

    private void ReloadAccounts()
    {
        var selectedAccountId = this.selectedAccount?.Id;

        this.accountsPanel.SuspendLayout();

        try
        {
            this.accountsPanel.Controls.Clear();

            var orderedAccounts = this.accounts
                .OrderBy(account => account.SortOrder)
                .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var account in orderedAccounts)
            {
                this.accountsPanel.Controls.Add(this.CreateAccountCard(account));
            }
        }
        finally
        {
            this.accountsPanel.ResumeLayout();
        }

        this.selectedAccount = this.accounts.FirstOrDefault(account => account.Id == selectedAccountId)
                               ?? this.accounts.FirstOrDefault();

        this.HighlightSelectedCards();
    }

    private void ReloadCharacters()
    {
        this.charactersPanel.SuspendLayout();

        try
        {
            this.charactersPanel.Controls.Clear();

            for (var slotNumber = 1; slotNumber <= 5; slotNumber++)
            {
                this.charactersPanel.Controls.Add(this.CreateCharacterSlotCard(slotNumber));
            }
        }
        finally
        {
            this.charactersPanel.ResumeLayout();
        }

        this.HighlightSelectedCards();
        this.UpdateButtonState();
    }

    private Control CreateAccountCard(GameAccount account)
    {
        var cardWidth = GetCardWidth(this.accountsPanel, 300);

        var orderedAccounts = this.accounts
            .OrderBy(gameAccount => gameAccount.SortOrder)
            .ThenBy(gameAccount => gameAccount.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(gameAccount => gameAccount.LoginName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accountIndex = orderedAccounts.FindIndex(existing => existing.Id == account.Id);
        var canMoveUp = accountIndex > 0;
        var canMoveDown = accountIndex >= 0 && accountIndex < orderedAccounts.Count - 1;

        var card = new Panel
        {
            Width = cardWidth,
            Height = 108,
            Margin = new Padding(4, 4, 8, 6),
            BorderStyle = BorderStyle.FixedSingle,
            Tag = account,
            Cursor = Cursors.Hand,
        };

        var login = new Label
        {
            Text = EmptyToFallback(account.LoginName, "Missing login name"),
            Font = new Font(this.Font, FontStyle.Bold),
            Location = new Point(12, 10),
            Size = new Size(cardWidth - 78, 22),
        };

        var title = new Label
        {
            Text = string.IsNullOrWhiteSpace(account.DisplayName)
                ? "No display name"
                : $"Display name: {account.DisplayName}",
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 34),
            Size = new Size(cardWidth - 36, 20),
        };

        var password = new Label
        {
            Text = string.IsNullOrEmpty(account.ProtectedPassword)
                ? "Password: not set"
                : "Password: set",
            Location = new Point(12, 56),
            Size = new Size(180, 20),
        };

        var characters = new Label
        {
            Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Characters: {account.Characters.Count} / 5"),
            Location = new Point(210, 56),
            Size = new Size(cardWidth - 260, 20),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var hint = new Label
        {
            Text = "Double-click to edit",
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 78),
            Size = new Size(cardWidth - 78, 20),
        };

        var arrowButtonWidth = 30;
        var arrowButtonHeight = 26;

        var arrowButtonRightMargin = 10;

        var moveUpButton = new Button
        {
            Text = "↑",
            Font = new Font(this.Font.FontFamily, 9, FontStyle.Bold),
            Width = arrowButtonWidth,
            Height = arrowButtonHeight,
            Location = new Point(
                cardWidth - arrowButtonWidth - arrowButtonRightMargin,
                8),
            Visible = canMoveUp,
            Tag = account,
        };

        var moveDownButton = new Button
        {
            Text = "↓",
            Font = new Font(this.Font.FontFamily, 9, FontStyle.Bold),
            Width = arrowButtonWidth,
            Height = arrowButtonHeight,
            Location = new Point(
                cardWidth - arrowButtonWidth - arrowButtonRightMargin,
                card.Height - arrowButtonHeight - 8),
            Visible = canMoveDown,
            Tag = account,
        };

        moveUpButton.Click += this.MoveAccountUpButton_OnClick;
        moveDownButton.Click += this.MoveAccountDownButton_OnClick;

        card.Controls.Add(title);
        card.Controls.Add(login);
        card.Controls.Add(password);
        card.Controls.Add(characters);
        card.Controls.Add(hint);
        card.Controls.Add(moveUpButton);
        card.Controls.Add(moveDownButton);

        card.Click += (_, _) => this.SelectAccount(account);
        foreach (Control child in card.Controls)
        {
            child.Click += (_, _) => this.SelectAccount(account);
        }

        card.DoubleClick += (_, _) =>
        {
            this.SelectAccount(account);
            this.EditAccountButton_OnClick(this, EventArgs.Empty);
        };

        foreach (Control child in card.Controls)
        {
            child.DoubleClick += (_, _) =>
            {
                this.SelectAccount(account);
                this.EditAccountButton_OnClick(this, EventArgs.Empty);
            };
        }

        return card;
    }

    private void MoveAccountUpButton_OnClick(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: GameAccount account })
        {
            return;
        }

        this.MoveAccount(account, -1);
    }

    private void MoveAccountDownButton_OnClick(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: GameAccount account })
        {
            return;
        }

        this.MoveAccount(account, 1);
    }

    private void MoveAccount(GameAccount account, int direction)
    {
        var orderedAccounts = this.accounts
            .OrderBy(gameAccount => gameAccount.SortOrder)
            .ThenBy(gameAccount => gameAccount.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(gameAccount => gameAccount.LoginName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = orderedAccounts.FindIndex(existing => existing.Id == account.Id);

        if (index < 0)
        {
            return;
        }

        var newIndex = index + direction;

        if (newIndex < 0 || newIndex >= orderedAccounts.Count)
        {
            return;
        }

        (orderedAccounts[index].SortOrder, orderedAccounts[newIndex].SortOrder) =
            (orderedAccounts[newIndex].SortOrder, orderedAccounts[index].SortOrder);

        this.selectedAccount = account;

        this.NormalizeAccountSortOrder();
        this.SaveAccounts();

        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
    }

    private Control CreateCharacterSlotCard(int slotNumber)
    {
        var character = this.selectedAccount?.Characters
            .FirstOrDefault(character => character.CharacterSlotNumber == slotNumber);

        var cardWidth = GetCardWidth(this.charactersPanel, 420);
        var professionX = Math.Max(300, cardWidth - 280);

        var card = new Panel
        {
            Width = cardWidth,
            Height = 92,
            Margin = new Padding(4, 4, 8, 6),
            BorderStyle = BorderStyle.FixedSingle,
            Tag = slotNumber,
            Cursor = Cursors.Hand,
        };

        var title = new Label
        {
            Text = character?.Name ?? "Empty character slot",
            Font = new Font(this.Font, FontStyle.Bold),
            ForeColor = character == null
                ? SystemColors.GrayText
                : SystemColors.ControlText,
            Location = new Point(12, 10),
            Size = new Size(cardWidth - 36, 22),
        };

        var slotLabel = new Label
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"Character Slot {slotNumber}"),
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 36),
            Size = new Size(180, 20),
        };

        var profession = new Label
        {
            Text = character == null
                ? "No character configured"
                : $"{character.Race} - {character.Profession}",
            Location = new Point(professionX, 36),
            Size = new Size(cardWidth - professionX - 24, 20),
        };

        var hint = new Label
        {
            Text = character == null
                ? "Double-click to create this character"
                : "Double-click to edit this character",
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 60),
            Size = new Size(cardWidth - 36, 20),
        };

        card.Controls.Add(title);
        card.Controls.Add(slotLabel);
        card.Controls.Add(profession);
        card.Controls.Add(hint);

        card.Click += (_, _) => this.SelectCharacterSlot(slotNumber);
        foreach (Control child in card.Controls)
        {
            child.Click += (_, _) => this.SelectCharacterSlot(slotNumber);
        }

        card.DoubleClick += (_, _) =>
        {
            this.SelectCharacterSlot(slotNumber);
            this.EditCharacterButton_OnClick(this, EventArgs.Empty);
        };

        foreach (Control child in card.Controls)
        {
            child.DoubleClick += (_, _) =>
            {
                this.SelectCharacterSlot(slotNumber);
                this.EditCharacterButton_OnClick(this, EventArgs.Empty);
            };
        }

        return card;
    }

    private static int GetCardWidth(ScrollableControl parent, int minimumWidth)
    {
        var scrollbarAllowance = SystemInformation.VerticalScrollBarWidth + 32;
        var width = parent.ClientSize.Width - scrollbarAllowance;

        return Math.Max(minimumWidth, width);
    }

    private void SelectAccount(GameAccount account)
    {
        this.selectedAccount = account;
        this.selectedCharacterSlotNumber = 1;

        this.ReloadCharacters();
        this.HighlightSelectedCards();
        this.UpdateButtonState();
    }

    private void SelectCharacterSlot(int slotNumber)
    {
        this.selectedCharacterSlotNumber = slotNumber;

        this.HighlightSelectedCards();
        this.UpdateButtonState();
    }

    private void HighlightSelectedCards()
    {
        var selectedBackColor = Color.FromArgb(218, 235, 255);
        var normalBackColor = Color.White;

        foreach (Control control in this.accountsPanel.Controls)
        {
            if (control is Panel { Tag: GameAccount account } panel)
            {
                panel.BackColor = this.selectedAccount?.Id == account.Id
                    ? selectedBackColor
                    : normalBackColor;
            }
        }

        foreach (Control control in this.charactersPanel.Controls)
        {
            if (control is Panel { Tag: int slotNumber } panel)
            {
                panel.BackColor = this.selectedCharacterSlotNumber == slotNumber
                    ? selectedBackColor
                    : normalBackColor;
            }
        }
    }

    private void UpdateButtonState()
    {
        this.editAccountButton.Enabled = this.selectedAccount != null;
        this.deleteAccountButton.Enabled = this.selectedAccount != null;

        this.editCharacterButton.Enabled = this.selectedAccount != null;
        this.deleteCharacterButton.Enabled =
            this.selectedAccount?.Characters.Exists(character => character.CharacterSlotNumber == this.selectedCharacterSlotNumber) == true;
    }

    private void AddAccountButton_OnClick(object? sender, EventArgs e)
    {
        var account = new GameAccount
        {
            DisplayName = "New Account",
        };

        using var editor = new AccountEditorForm(account);

        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.accounts.Add(account);
        this.selectedAccount = account;

        this.SaveAccounts();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
    }

    private void EditAccountButton_OnClick(object? sender, EventArgs e)
    {
        if (this.selectedAccount == null)
        {
            return;
        }

        using var editor = new AccountEditorForm(this.selectedAccount);

        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.SaveAccounts();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
    }

    private void DeleteAccountButton_OnClick(object? sender, EventArgs e)
    {
        if (this.selectedAccount == null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete account '{this.selectedAccount.DisplayName}'?",
            "Delete Account",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        this.accounts.Remove(this.selectedAccount);
        this.selectedAccount = this.accounts.FirstOrDefault();
        this.selectedCharacterSlotNumber = 1;

        this.SaveAccounts();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
    }

    private void EditCharacterButton_OnClick(object? sender, EventArgs e)
    {
        if (this.selectedAccount == null)
        {
            return;
        }

        var character = this.selectedAccount.Characters
            .FirstOrDefault(character => character.CharacterSlotNumber == this.selectedCharacterSlotNumber) ??
                        new GameCharacter
                        {
                            CharacterSlotNumber = this.selectedCharacterSlotNumber,
                        };

        using var editor = new CharacterEditorForm(character);

        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.selectedAccount.Characters.RemoveAll(existing =>
            existing.CharacterSlotNumber == character.CharacterSlotNumber);

        this.selectedAccount.Characters.Add(character);

        this.selectedAccount.Characters = [.. this.selectedAccount.Characters
            .OrderBy(gameCharacter => gameCharacter.CharacterSlotNumber)];

        this.SaveAccounts();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
    }

    private void DeleteCharacterButton_OnClick(object? sender, EventArgs e)
    {
        if (this.selectedAccount == null)
        {
            return;
        }

        var character = this.selectedAccount.Characters
            .FirstOrDefault(character => character.CharacterSlotNumber == this.selectedCharacterSlotNumber);

        if (character == null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            string.Create(CultureInfo.InvariantCulture, $"Delete character '{character.Name}' from slot {character.CharacterSlotNumber}?"),
            "Delete Character",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        this.selectedAccount.Characters.Remove(character);

        this.SaveAccounts();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
    }

    private void AccountsForm_Resize(object? sender, EventArgs e)
    {
        this.ReloadAccounts();
        this.ReloadCharacters();
    }

    private void NormalizeAccountSortOrder()
    {
        var orderedAccounts = this.accounts
            .OrderBy(account => account.SortOrder)
            .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < orderedAccounts.Count; index++)
        {
            orderedAccounts[index].SortOrder = index;
        }
    }

    private void SaveAccounts()
    {
        this.NormalizeAccountSortOrder();
        this.saveAccounts(this.accounts);
    }

    private static List<GameAccount> CloneAccounts(IReadOnlyList<GameAccount> source)
    {
        return [.. source.Select(account => new GameAccount
        {
            Id = account.Id,
            SortOrder = account.SortOrder,
            DisplayName = account.DisplayName,
            LoginName = account.LoginName,
            ProtectedPassword = account.ProtectedPassword,
            Characters = [.. account.Characters.Select(character => new GameCharacter
            {
                Id = character.Id,
                CharacterSlotNumber = character.CharacterSlotNumber,
                Name = character.Name,
                Race = character.Race,
                Profession = character.Profession,
                CharacterSelectClickActionName = character.CharacterSelectClickActionName,
            })],
        })];
    }

    private static string EmptyToFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;
    }
}
