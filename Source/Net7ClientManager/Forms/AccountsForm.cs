using System.ComponentModel;
// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

public sealed class AccountsForm : ThemedForm
{
    private readonly List<GameAccount> accounts;

    private readonly ThemedFlowLayoutPanel accountsPanel = new();
    private readonly ThemedFlowLayoutPanel charactersPanel = new();

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
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimumSize = new Size(width: 980, height: 620);
        this.Size = new Size(width: 1180, height: 760);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: false,
            showMaximizeButton: true);

        this.BuildUi();
        this.ReloadAccounts();
        this.ReloadCharacters();
        this.UpdateButtonState();
        this.NormalizeAccountSortOrder();

        this.ResizeEnd += this.AccountsForm_Resize;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.ResizeEnd -= this.AccountsForm_Resize;

        this.addAccountButton.Click -= this.AddAccountButton_OnClick;
        this.editAccountButton.Click -= this.EditAccountButton_OnClick;
        this.deleteAccountButton.Click -= this.DeleteAccountButton_OnClick;

        this.editCharacterButton.Click -= this.EditCharacterButton_OnClick;
        this.deleteCharacterButton.Click -= this.DeleteCharacterButton_OnClick;

        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(all: 18),
            BackColor = MainWindowTheme.Background,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.Controls.Add(root);

        var accountsArea = this.BuildAccountsArea();
        accountsArea.Margin = new Padding(left: 0, top: 0, right: 8, bottom: 0);

        var charactersArea = this.BuildCharactersArea();
        charactersArea.Margin = new Padding(left: 8, top: 0, right: 0, bottom: 0);

        root.Controls.Add(accountsArea, column: 0, row: 0);
        root.Controls.Add(charactersArea, column: 1, row: 0);
    }

    private Control BuildAccountsArea()
    {
        var group = new GroupBox
        {
            Text = "Accounts",
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            ForeColor = MainWindowTheme.Text,
            Padding = new Padding(all: 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(all: 8),
            BackColor = MainWindowTheme.Panel,
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        group.Controls.Add(layout);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 4, right: 0, bottom: 4),
            BackColor = MainWindowTheme.Panel,
        };

        this.addAccountButton.Text = "Add";
        this.addAccountButton.Width = 90;
        this.addAccountButton.Height = 32;
        this.addAccountButton.Margin = new Padding(left: 0, top: 0, right: 6, bottom: 0);
        MainWindowTheme.StyleButton(this.addAccountButton, primary: true);
        this.addAccountButton.Click += this.AddAccountButton_OnClick;

        this.editAccountButton.Text = "Edit";
        this.editAccountButton.Width = 90;
        this.editAccountButton.Height = 32;
        this.editAccountButton.Margin = new Padding(left: 0, top: 0, right: 6, bottom: 0);
        MainWindowTheme.StyleButton(this.editAccountButton);
        this.editAccountButton.Click += this.EditAccountButton_OnClick;

        this.deleteAccountButton.Text = "Delete";
        this.deleteAccountButton.Width = 90;
        this.deleteAccountButton.Height = 32;
        this.deleteAccountButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(this.deleteAccountButton, danger: true);
        this.deleteAccountButton.Click += this.DeleteAccountButton_OnClick;

        buttonPanel.Controls.Add(this.addAccountButton);
        buttonPanel.Controls.Add(this.editAccountButton);
        buttonPanel.Controls.Add(this.deleteAccountButton);

        this.accountsPanel.Dock = DockStyle.Fill;
        this.accountsPanel.AutoScroll = true;
        this.accountsPanel.FlowDirection = FlowDirection.TopDown;
        this.accountsPanel.WrapContents = false;
        this.accountsPanel.BackColor = MainWindowTheme.Panel;

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
            BackColor = MainWindowTheme.Panel,
            ForeColor = MainWindowTheme.Text,
            Padding = new Padding(all: 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(all: 8),
            BackColor = MainWindowTheme.Panel,
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        group.Controls.Add(layout);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 4, right: 0, bottom: 4),
            BackColor = MainWindowTheme.Panel,
        };

        this.editCharacterButton.Text = "Edit";
        this.editCharacterButton.Width = 90;
        this.editCharacterButton.Height = 32;
        this.editCharacterButton.Margin = new Padding(left: 0, top: 0, right: 6, bottom: 0);
        MainWindowTheme.StyleButton(this.editCharacterButton);
        this.editCharacterButton.Click += this.EditCharacterButton_OnClick;

        this.deleteCharacterButton.Text = "Delete";
        this.deleteCharacterButton.Width = 90;
        this.deleteCharacterButton.Height = 32;
        this.deleteCharacterButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(this.deleteCharacterButton, danger: true);
        this.deleteCharacterButton.Click += this.DeleteCharacterButton_OnClick;

        buttonPanel.Controls.Add(this.editCharacterButton);
        buttonPanel.Controls.Add(this.deleteCharacterButton);

        this.charactersPanel.Dock = DockStyle.Fill;
        this.charactersPanel.AutoScroll = true;
        this.charactersPanel.FlowDirection = FlowDirection.TopDown;
        this.charactersPanel.WrapContents = false;
        this.charactersPanel.BackColor = MainWindowTheme.Panel;

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

        var card = new AccountsCardPanel
        {
            Width = cardWidth,
            Height = 108,
            Margin = new Padding(left: 4, top: 4, right: 8, bottom: 6),
            Tag = account,
            Cursor = Cursors.Hand,
            BackColor = MainWindowTheme.Panel,
            AccentColor = MainWindowTheme.Border,
        };

        var login = new Label
        {
            Text = UiObfuscationMode.AccountName(
                account.LoginName,
                fallback: "Missing login name"),
            Font = new Font(this.Font, FontStyle.Bold),
            Location = new Point(12, 10),
            Size = new Size(cardWidth - 78, 22),
            ForeColor = MainWindowTheme.Text,
        };

        var title = new Label
        {
            Text = string.IsNullOrWhiteSpace(account.DisplayName)
                ? "No display name"
                : string.Concat(
                    "Display name: ",
                    UiObfuscationMode.AccountName(
                        account.DisplayName)),
            ForeColor = MainWindowTheme.MutedText,
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
            ForeColor = MainWindowTheme.Text,
        };

        var characters = new Label
        {
            Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Characters: {account.Characters.Count} / 5"),
            Location = new Point(210, 56),
            Size = new Size(cardWidth - 260, 20),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.Text,
        };

        var hint = new Label
        {
            Text = "Double-click to edit",
            ForeColor = MainWindowTheme.MutedText,
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

        MainWindowTheme.StyleButton(moveUpButton);
        MainWindowTheme.StyleButton(moveDownButton);

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

        var card = new AccountsCardPanel
        {
            Width = cardWidth,
            Height = 92,
            Margin = new Padding(left: 4, top: 4, right: 8, bottom: 6),
            Tag = slotNumber,
            Cursor = Cursors.Hand,
            BackColor = MainWindowTheme.Panel,
            AccentColor = MainWindowTheme.Border,
        };

        var title = new Label
        {
            Text = character?.Name ?? "Empty character slot",
            Font = new Font(this.Font, FontStyle.Bold),
            ForeColor = character == null
                ? MainWindowTheme.MutedText
                : MainWindowTheme.Text,
            Location = new Point(12, 10),
            Size = new Size(cardWidth - 36, 22),
        };

        var slotLabel = new Label
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"Character Slot {slotNumber}"),
            ForeColor = MainWindowTheme.MutedText,
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
            ForeColor = MainWindowTheme.Text,
        };

        var hint = new Label
        {
            Text = character == null
                ? "Double-click to create this character"
                : "Double-click to edit this character",
            ForeColor = MainWindowTheme.MutedText,
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
        var effectiveClientWidth = parent.ClientSize.Width;

        if (parent.VerticalScroll.Visible)
        {
            effectiveClientWidth += SystemInformation.VerticalScrollBarWidth;
        }

        var scrollbarAllowance = SystemInformation.VerticalScrollBarWidth + 32;
        var width = effectiveClientWidth - scrollbarAllowance;

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
        foreach (Control control in this.accountsPanel.Controls)
        {
            if (control is AccountsCardPanel { Tag: GameAccount account } panel)
            {
                var isSelected = this.selectedAccount?.Id == account.Id;
                panel.BackColor = isSelected
                    ? MainWindowTheme.ElevatedPanel
                    : MainWindowTheme.Panel;
                panel.AccentColor = isSelected
                    ? MainWindowTheme.AccentBorder
                    : MainWindowTheme.Border;
            }
        }

        foreach (Control control in this.charactersPanel.Controls)
        {
            if (control is AccountsCardPanel { Tag: int slotNumber } panel)
            {
                var isSelected = this.selectedCharacterSlotNumber == slotNumber;
                panel.BackColor = isSelected
                    ? MainWindowTheme.ElevatedPanel
                    : MainWindowTheme.Panel;
                panel.AccentColor = isSelected
                    ? MainWindowTheme.AccentBorder
                    : MainWindowTheme.Border;
            }
        }
    }

    private void UpdateButtonState()
    {
        this.editAccountButton.Enabled = this.selectedAccount != null;
        this.deleteAccountButton.Enabled = this.selectedAccount != null;

        this.editCharacterButton.Enabled = this.selectedAccount != null;
        this.deleteCharacterButton.Enabled =
            this.selectedAccount?.Characters.Exists(character =>
                character.CharacterSlotNumber ==
                this.selectedCharacterSlotNumber) == true;
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

        if (!ThemedMessageDialog.Confirm(
                this,
                "Delete account",
                string.Concat(
                    "Delete account '",
                    UiObfuscationMode.AccountName(
                        this.selectedAccount.ToString()),
                    "'?"),
                confirmButtonText: "Delete"))
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

        if (!ThemedMessageDialog.Confirm(
                this,
                "Delete character",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Delete character '{character.Name}' from slot {character.CharacterSlotNumber}?"),
                confirmButtonText: "Delete"))
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
    private sealed class ThemedFlowLayoutPanel : FlowLayoutPanel
    {
        public ThemedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }

    private sealed class AccountsCardPanel : Panel
    {
        private Color accentColor = MainWindowTheme.Border;

        public AccountsCardPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => this.accentColor;
            set
            {
                if (this.accentColor == value)
                {
                    return;
                }

                this.accentColor = value;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var pen = new Pen(this.accentColor);
            e.Graphics.DrawRectangle(
                pen,
                x: 0,
                y: 0,
                width: Math.Max(0, this.ClientSize.Width - 1),
                height: Math.Max(0, this.ClientSize.Height - 1));
        }
    }

}
