// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.ActivityJournal;
using Net7ClientManager.Navigation;

internal sealed class FactionDetailsOverlayForm : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly Color backgroundColor =
        Color.FromArgb(9, 16, 30);
    private static readonly Color panelColor =
        Color.FromArgb(15, 27, 48);
    private static readonly Color borderColor =
        Color.FromArgb(43, 176, 211);
    private static readonly Color separatorColor =
        Color.FromArgb(42, 78, 105);
    private static readonly Color headingColor =
        Color.FromArgb(139, 226, 244);
    private static readonly Color labelColor =
        Color.White;
    private static readonly Color mutedColor =
        Color.FromArgb(169, 186, 203);
    private static readonly Color helpfulColor =
        Color.FromArgb(112, 224, 148);
    private static readonly Color harmfulColor =
        Color.FromArgb(255, 140, 128);
    private static readonly Color standingMarkerColor =
        Color.FromArgb(70, 225, 255);

    // These are the same danger, neutral, and safe colors used by Atlas.
    private static readonly Color dangerColor =
        Color.FromArgb(236, 94, 94);
    private static readonly Color neutralColor =
        Color.FromArgb(242, 188, 73);
    private static readonly Color safeColor =
        Color.FromArgb(76, 210, 139);

    private readonly StringFormat noWrapFormat = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private readonly StringFormat rightNoWrapFormat = new()
    {
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.None,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private readonly StringFormat exactNoWrapFormat = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.None,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private readonly StringFormat wrapFormat = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        Trimming = StringTrimming.EllipsisWord,
    };

    private FactionDetailsPresentation presentation =
        FactionDetailsPresentation.Hidden;
    private string presentationFingerprint = "";
    private Font? sectionFont;
    private Font? compactFont;
    private Font? compactBoldFont;

    public FactionDetailsOverlayForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.BackColor = backgroundColor;
        this.DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void SetPresentation(FactionDetailsPresentation nextPresentation)
    {
        ArgumentNullException.ThrowIfNull(nextPresentation);

        if (string.Equals(
                this.presentationFingerprint,
                nextPresentation.Fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.presentation = nextPresentation;
        this.presentationFingerprint = nextPresentation.Fingerprint;
        this.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var scale = this.GetScale();

        this.ReplaceFont(
            ref this.sectionFont,
            "Segoe UI Semibold",
            15.0f * scale,
            FontStyle.Bold);
        this.ReplaceFont(
            ref this.compactFont,
            "Segoe UI",
            11.5f * scale,
            FontStyle.Regular);
        this.ReplaceFont(
            ref this.compactBoldFont,
            "Segoe UI Semibold",
            11.5f * scale,
            FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (this.sectionFont == null ||
            this.compactFont == null ||
            this.compactBoldFont == null)
        {
            return;
        }

        e.Graphics.Clear(backgroundColor);
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var borderPen = new Pen(borderColor, 1.0f);
        using var separatorPen = new Pen(separatorColor, 1.0f);
        using var panelBrush = new SolidBrush(panelColor);
        using var headingBrush = new SolidBrush(headingColor);
        using var labelBrush = new SolidBrush(labelColor);
        using var mutedBrush = new SolidBrush(mutedColor);
        using var helpfulBrush = new SolidBrush(helpfulColor);
        using var harmfulBrush = new SolidBrush(harmfulColor);
        using var dangerBrush = new SolidBrush(dangerColor);
        using var neutralBrush = new SolidBrush(neutralColor);
        using var safeBrush = new SolidBrush(safeColor);

        var outer = new Rectangle(
            0,
            0,
            Math.Max(1, this.ClientSize.Width - 1),
            Math.Max(1, this.ClientSize.Height - 1));
        e.Graphics.FillRectangle(panelBrush, outer);
        e.Graphics.DrawRectangle(borderPen, outer);

        var padding = Scale(12);
        var content = Rectangle.Inflate(outer, -padding, -Scale(9));
        var y = content.Top;

        y = this.DrawHeaderAndStanding(
            e.Graphics,
            content,
            y,
            labelBrush,
            mutedBrush,
            dangerBrush,
            neutralBrush,
            safeBrush);

        var descriptionHeight = Scale(64);
        var descriptionBounds = new Rectangle(
            content.Left,
            y,
            content.Width,
            descriptionHeight);
        e.Graphics.DrawString(
            string.IsNullOrWhiteSpace(this.presentation.Description)
                ? "No faction description is available."
                : this.presentation.Description,
            this.compactFont,
            string.IsNullOrWhiteSpace(this.presentation.Description)
                ? mutedBrush
                : labelBrush,
            descriptionBounds,
            this.wrapFormat);
        y = descriptionBounds.Bottom + Scale(7);

        e.Graphics.DrawLine(
            separatorPen,
            content.Left,
            y,
            content.Right,
            y);
        y += Scale(8);

        y = this.DrawReactions(
            e.Graphics,
            content,
            y,
            helpfulBrush,
            harmfulBrush,
            mutedBrush);

        y += Scale(6);
        e.Graphics.DrawLine(
            separatorPen,
            content.Left,
            y,
            content.Right,
            y);
        y += Scale(7);

        var recentHeadingBounds = new Rectangle(
            content.Left,
            y,
            content.Width,
            Scale(20));
        e.Graphics.DrawString(
            "Recent reputation changes",
            this.compactBoldFont,
            headingBrush,
            recentHeadingBounds,
            this.noWrapFormat);
        y = recentHeadingBounds.Bottom + Scale(2);

        this.DrawHistory(
            e.Graphics,
            new Rectangle(
                content.Left,
                y,
                content.Width,
                Math.Max(1, content.Bottom - y)),
            labelBrush,
            mutedBrush,
            helpfulBrush,
            harmfulBrush);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = new IntPtr(MaNoActivate);
            return;
        }

        if (message.Msg == WmNcHitTest)
        {
            message.Result = new IntPtr(HtTransparent);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.sectionFont?.Dispose();
            this.compactFont?.Dispose();
            this.compactBoldFont?.Dispose();
            this.noWrapFormat.Dispose();
            this.rightNoWrapFormat.Dispose();
            this.exactNoWrapFormat.Dispose();
            this.wrapFormat.Dispose();
        }

        base.Dispose(disposing);
    }

    private int DrawHeaderAndStanding(
        Graphics graphics,
        Rectangle content,
        int y,
        Brush labelBrush,
        Brush mutedBrush,
        Brush dangerBrush,
        Brush neutralBrush,
        Brush safeBrush)
    {
        var lineHeight = Scale(24);
        var reputation = this.presentation.Reputation;
        var standingBrush = reputation.HasValue
            ? this.GetStandingBrush(
                reputation.Value,
                dangerBrush,
                neutralBrush,
                safeBrush)
            : mutedBrush;
        var valueText = reputation.HasValue
            ? MathF.Truncate(reputation.Value)
                .ToString("N0", CultureInfo.CurrentCulture)
            : "—";
        var reputationLabel = "Reputation:";
        var affiliationText = this.presentation.IsAffiliated
            ? "[Your Faction]"
            : "";

        var valueWidth = MeasureWidth(
            graphics,
            valueText,
            this.sectionFont) + Scale(10);
        var reputationLabelWidth = MeasureWidth(
            graphics,
            reputationLabel,
            this.sectionFont) + Scale(18);
        var reputationGroupWidth =
            reputationLabelWidth + valueWidth;
        var reputationLeft = Math.Max(
            content.Left,
            content.Right - reputationGroupWidth);

        var leftArea = new Rectangle(
            content.Left,
            y,
            Math.Max(1, reputationLeft - content.Left - Scale(12)),
            lineHeight);
        var affiliationWidth = string.IsNullOrEmpty(affiliationText)
            ? 0
            : MeasureWidth(
                graphics,
                affiliationText,
                this.compactBoldFont) + Scale(8);
        var nameBounds = new Rectangle(
            leftArea.Left,
            leftArea.Top,
            Math.Max(1, leftArea.Width - affiliationWidth),
            leftArea.Height);
        graphics.DrawString(
            string.IsNullOrWhiteSpace(this.presentation.DisplayName)
                ? this.presentation.FactionKey
                : this.presentation.DisplayName,
            this.sectionFont,
            labelBrush,
            nameBounds,
            this.noWrapFormat);

        if (affiliationWidth > 0)
        {
            var nameWidth = Math.Min(
                nameBounds.Width,
                MeasureWidth(
                    graphics,
                    string.IsNullOrWhiteSpace(this.presentation.DisplayName)
                        ? this.presentation.FactionKey
                        : this.presentation.DisplayName,
                    this.sectionFont) + Scale(5));
            var affiliationBounds = new Rectangle(
                nameBounds.Left + nameWidth,
                y,
                Math.Max(1, leftArea.Right - (nameBounds.Left + nameWidth)),
                lineHeight);
            graphics.DrawString(
                affiliationText,
                this.compactBoldFont,
                safeBrush,
                affiliationBounds,
                this.noWrapFormat);
        }

        var reputationLabelBounds = new Rectangle(
            reputationLeft,
            y,
            reputationLabelWidth,
            lineHeight);
        var reputationValueBounds = new Rectangle(
            reputationLabelBounds.Right,
            y,
            valueWidth,
            lineHeight);
        graphics.DrawString(
            reputationLabel,
            this.sectionFont,
            labelBrush,
            reputationLabelBounds,
            this.exactNoWrapFormat);
        graphics.DrawString(
            valueText,
            this.sectionFont,
            standingBrush,
            reputationValueBounds,
            this.rightNoWrapFormat);

        var trackTop = y + lineHeight + Scale(5);
        var trackHeight = Scale(10);
        var trackBounds = new Rectangle(
            content.Left,
            trackTop,
            content.Width,
            trackHeight);
        this.DrawStandingTrack(
            graphics,
            trackBounds,
            reputation,
            dangerBrush,
            neutralBrush,
            safeBrush);

        var rangeBounds = new Rectangle(
            content.Left,
            trackBounds.Bottom + Scale(1),
            content.Width,
            Scale(15));
        graphics.DrawString(
            "Hostile",
            this.compactFont,
            mutedBrush,
            new Rectangle(
                rangeBounds.Left,
                rangeBounds.Top,
                rangeBounds.Width / 2,
                rangeBounds.Height),
            this.noWrapFormat);
        graphics.DrawString(
            "Friendly",
            this.compactFont,
            mutedBrush,
            new Rectangle(
                rangeBounds.Left + (rangeBounds.Width / 2),
                rangeBounds.Top,
                rangeBounds.Width / 2,
                rangeBounds.Height),
            this.rightNoWrapFormat);

        return rangeBounds.Bottom + Scale(10);
    }

    private void DrawStandingTrack(
        Graphics graphics,
        Rectangle bounds,
        float? reputation,
        Brush dangerBrush,
        Brush neutralBrush,
        Brush safeBrush)
    {
        var dangerEnd = RatioForReaction(
            FactionStandingBandResolver.DangerUpperExclusive);
        var safeStart = RatioForReaction(
            FactionStandingBandResolver.SafeLowerInclusive);
        var dangerWidth = (int)Math.Round(bounds.Width * dangerEnd);
        var neutralEnd = (int)Math.Round(bounds.Width * safeStart);

        graphics.FillRectangle(
            dangerBrush,
            new Rectangle(
                bounds.Left,
                bounds.Top,
                Math.Max(1, dangerWidth),
                bounds.Height));
        graphics.FillRectangle(
            neutralBrush,
            new Rectangle(
                bounds.Left + dangerWidth,
                bounds.Top,
                Math.Max(1, neutralEnd - dangerWidth),
                bounds.Height));
        graphics.FillRectangle(
            safeBrush,
            new Rectangle(
                bounds.Left + neutralEnd,
                bounds.Top,
                Math.Max(1, bounds.Width - neutralEnd),
                bounds.Height));

        if (!reputation.HasValue)
        {
            return;
        }

        var markerX = bounds.Left + (int)Math.Round(
            FactionStandingBandResolver.Normalize(reputation.Value) *
            Math.Max(0, bounds.Width - 1));
        markerX = Math.Clamp(markerX, bounds.Left, bounds.Right - 1);

        using var shadowPen = new Pen(Color.FromArgb(210, 0, 0, 0), Scale(5));
        using var markerPen = new Pen(standingMarkerColor, Scale(3));
        graphics.DrawLine(
            shadowPen,
            markerX,
            bounds.Top - Scale(2),
            markerX,
            bounds.Bottom + Scale(2));
        graphics.DrawLine(
            markerPen,
            markerX,
            bounds.Top - Scale(2),
            markerX,
            bounds.Bottom + Scale(2));
    }

    private int DrawReactions(
        Graphics graphics,
        Rectangle content,
        int y,
        Brush helpfulBrush,
        Brush harmfulBrush,
        Brush mutedBrush)
    {
        var headingHeight = Scale(20);
        var columnGap = Scale(24);
        var columnWidth = Math.Max(
            1,
            (content.Width - columnGap) / 2);
        var leftColumn = new Rectangle(
            content.Left,
            y,
            columnWidth,
            headingHeight);
        var rightColumn = new Rectangle(
            leftColumn.Right + columnGap,
            y,
            columnWidth,
            headingHeight);

        graphics.DrawString(
            "Defeating helps this faction",
            this.compactBoldFont,
            helpfulBrush,
            leftColumn,
            this.noWrapFormat);
        graphics.DrawString(
            "Defeating harms this faction",
            this.compactBoldFont,
            harmfulBrush,
            rightColumn,
            this.noWrapFormat);
        y += headingHeight;

        var reactionRows = Math.Max(
            this.presentation.HelpfulKills.Count,
            this.presentation.HarmfulKills.Count);
        var rowHeight = Scale(17);

        if (reactionRows == 0)
        {
            graphics.DrawString(
                "No known kill reactions for this faction.",
                this.compactFont,
                mutedBrush,
                new Rectangle(
                    content.Left,
                    y,
                    content.Width,
                    rowHeight),
                this.noWrapFormat);
            return y + rowHeight;
        }

        for (var index = 0; index < reactionRows; index++)
        {
            var rowTop = y + (index * rowHeight);

            if (index < this.presentation.HelpfulKills.Count)
            {
                this.DrawReactionRow(
                    graphics,
                    this.presentation.HelpfulKills[index],
                    new Rectangle(
                        leftColumn.Left,
                        rowTop,
                        leftColumn.Width,
                        rowHeight),
                    helpfulBrush);
            }

            if (index < this.presentation.HarmfulKills.Count)
            {
                this.DrawReactionRow(
                    graphics,
                    this.presentation.HarmfulKills[index],
                    new Rectangle(
                        rightColumn.Left,
                        rowTop,
                        rightColumn.Width,
                        rowHeight),
                    harmfulBrush);
            }
        }

        return y + (reactionRows * rowHeight);
    }

    private void DrawReactionRow(
        Graphics graphics,
        FactionReactionPresentation reaction,
        Rectangle bounds,
        Brush valueBrush)
    {
        var suffix = string.Create(
            CultureInfo.InvariantCulture,
            $"{(reaction.IsEstimated ? "~" : "")}{reaction.Multiplier:+0%;-0%;0%}");
        var valueWidth = Math.Max(
            Scale(58),
            MeasureWidth(
                graphics,
                suffix,
                this.compactBoldFont) + Scale(8));
        var nameBounds = new Rectangle(
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Width - valueWidth - Scale(5)),
            bounds.Height);
        var valueBounds = new Rectangle(
            nameBounds.Right + Scale(5),
            bounds.Top,
            valueWidth,
            bounds.Height);
        graphics.DrawString(
            reaction.DefeatedFactionName,
            this.compactFont,
            Brushes.White,
            nameBounds,
            this.noWrapFormat);
        graphics.DrawString(
            suffix,
            this.compactBoldFont,
            valueBrush,
            valueBounds,
            this.rightNoWrapFormat);
    }

    private void DrawHistory(
        Graphics graphics,
        Rectangle bounds,
        Brush labelBrush,
        Brush mutedBrush,
        Brush helpfulBrush,
        Brush harmfulBrush)
    {
        var rowHeight = Scale(21);

        if (this.presentation.RecentChanges.Count == 0)
        {
            graphics.DrawString(
                "No recorded reputation changes yet.",
                this.compactFont,
                mutedBrush,
                new Rectangle(
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    rowHeight),
                this.noWrapFormat);
            return;
        }

        for (var index = 0;
             index < this.presentation.RecentChanges.Count &&
             bounds.Top + (index * rowHeight) < bounds.Bottom;
             index++)
        {
            var entry = this.presentation.RecentChanges[index];
            this.DrawHistoryRow(
                graphics,
                entry,
                new Rectangle(
                    bounds.Left,
                    bounds.Top + (index * rowHeight),
                    bounds.Width,
                    Math.Min(
                        rowHeight,
                        bounds.Bottom - (bounds.Top + index * rowHeight))),
                labelBrush,
                mutedBrush,
                entry.Delta >= 0 ? helpfulBrush : harmfulBrush);
        }
    }

    private void DrawHistoryRow(
        Graphics graphics,
        ReputationJournalEntry entry,
        Rectangle bounds,
        Brush labelBrush,
        Brush mutedBrush,
        Brush deltaBrush)
    {
        var timestampText = entry.OccurredAt
            .ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        var deltaText = entry.Delta.ToString(
            "+0;-0;0",
            CultureInfo.CurrentCulture);
        var currentText = entry.CurrentReaction.ToString(
            "N0",
            CultureInfo.CurrentCulture);

        var timestampBounds = new Rectangle(
            bounds.Left,
            bounds.Top,
            Scale(118),
            bounds.Height);
        var deltaBounds = new Rectangle(
            timestampBounds.Right + Scale(5),
            bounds.Top,
            Scale(42),
            bounds.Height);
        var currentBounds = new Rectangle(
            deltaBounds.Right + Scale(4),
            bounds.Top,
            Scale(68),
            bounds.Height);
        var reasonBounds = new Rectangle(
            currentBounds.Right + Scale(8),
            bounds.Top,
            Math.Max(1, bounds.Right - currentBounds.Right - Scale(8)),
            bounds.Height);

        graphics.DrawString(
            timestampText,
            this.compactFont,
            mutedBrush,
            timestampBounds,
            this.noWrapFormat);
        graphics.DrawString(
            deltaText,
            this.compactBoldFont,
            deltaBrush,
            deltaBounds,
            this.noWrapFormat);
        graphics.DrawString(
            currentText,
            this.compactFont,
            labelBrush,
            currentBounds,
            this.noWrapFormat);

        if (!string.IsNullOrWhiteSpace(entry.Reason))
        {
            graphics.DrawString(
                entry.Reason.Trim(),
                this.compactFont,
                labelBrush,
                reasonBounds,
                this.noWrapFormat);
        }
    }

    private Brush GetStandingBrush(
        float reputation,
        Brush dangerBrush,
        Brush neutralBrush,
        Brush safeBrush)
    {
        return FactionStandingBandResolver.ResolveStandard(reputation) switch
        {
            GalaxyAtlasSafetyBand.Danger => dangerBrush,
            GalaxyAtlasSafetyBand.Safe => safeBrush,
            _ => neutralBrush,
        };
    }

    private static float RatioForReaction(float reaction) =>
        FactionStandingBandResolver.Normalize(reaction);

    private static int MeasureWidth(
        Graphics graphics,
        string text,
        Font font) =>
        (int)Math.Ceiling(
            graphics.MeasureString(
                text,
                font,
                int.MaxValue,
                StringFormat.GenericTypographic).Width);

    private float GetScale() =>
        Math.Clamp(
            this.ClientSize.Height / 450f,
            0.76f,
            1.35f);

    private int Scale(int value) =>
        Math.Max(1, (int)Math.Round(value * this.GetScale()));

    private void ReplaceFont(
        ref Font? target,
        string family,
        float size,
        FontStyle style)
    {
        if (target != null &&
            Math.Abs(target.Size - size) <= 0.05f &&
            target.Style == style)
        {
            return;
        }

        target?.Dispose();
        target = new Font(
            family,
            size,
            style,
            GraphicsUnit.Pixel);
    }
}
