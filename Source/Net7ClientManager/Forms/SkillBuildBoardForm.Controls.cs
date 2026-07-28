// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Drawing.Drawing2D;
using System.Globalization;
using Net7ClientManager.SkillPlanning;

internal sealed partial class SkillBuildBoardForm
{
    private static Panel CreateMessagePage()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 10, 8, 8),
            BackColor = Color.Transparent,
        };
    }

    private static Label CreateMessageHeading(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(13.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
    }

    private static Label CreateMessageBody(
        string text,
        Color color)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 72,
            Text = text,
            TextAlign = ContentAlignment.TopLeft,
            Font = MainWindowTheme.CreateBodyFont(9.5f),
            ForeColor = color,
            BackColor = Color.Transparent,
        };
    }

    private static Button CreateActionButton(
        string text,
        bool primary = false,
        bool danger = false,
        int width = 132)
    {
        var button = new Button
        {
            Height = 32,
            Width = width,
            Margin = Padding.Empty,
            Padding = new Padding(6, 0, 6, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            TabStop = false,
        };

        MainWindowTheme.StyleButton(button, primary, danger);
        return button;
    }

    private static Button CreateCompactButton(
        string text,
        bool danger = false)
    {
        var button = CreateActionButton(
            text,
            danger: danger,
            width: 30);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2);
        button.Font = MainWindowTheme.CreateHeadingFont(11.0f);
        return button;
    }

    private static Label CreateEquipmentGroupHeading(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 8, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(9.5f),
            ForeColor = MainWindowTheme.Accent,
            BackColor = MainWindowTheme.Panel,
            AutoEllipsis = true,
        };
    }

    private abstract class SkillPipsControlBase : Control
    {
        protected const int PreferredPipDiameter = 17;
        protected const int PreferredPipGap = 8;
        private const int MinimumPipDiameter = 13;
        private const int MinimumPipGap = 2;

        protected SkillPipsControlBase(int maximumRank)
        {
            this.MaximumRank = Math.Max(0, maximumRank);
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                value: true);
            this.TabStop = false;
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
        }

        protected int MaximumRank { get; }

        protected (int Diameter, int Gap) CalculatePipLayout()
        {
            var diameter = PreferredPipDiameter;
            var gap = PreferredPipGap;
            var gapCount = Math.Max(0, this.MaximumRank - 1);
            var availableWidth = Math.Max(0, this.ClientSize.Width - 1);

            if ((this.MaximumRank * diameter) +
                (gapCount * gap) <= availableWidth)
            {
                return (diameter, gap);
            }

            if (gapCount > 0)
            {
                gap = Math.Max(
                    MinimumPipGap,
                    (availableWidth -
                     (this.MaximumRank * diameter)) /
                    gapCount);
            }

            if ((this.MaximumRank * diameter) +
                (gapCount * gap) <= availableWidth)
            {
                return (diameter, gap);
            }

            diameter = Math.Max(
                MinimumPipDiameter,
                (availableWidth -
                 (gapCount * MinimumPipGap)) /
                Math.Max(1, this.MaximumRank));
            gap = gapCount > 0
                ? Math.Max(
                    0,
                    (availableWidth -
                     (this.MaximumRank * diameter)) /
                    gapCount)
                : 0;

            return (diameter, gap);
        }

        protected void PaintPip(
            Graphics graphics,
            Rectangle bounds,
            Color fillColor,
            Color borderColor,
            bool highlight)
        {
            using var shadowBrush = new SolidBrush(
                Color.FromArgb(150, 0, 0, 0));
            graphics.FillEllipse(
                shadowBrush,
                bounds.X + 1,
                bounds.Y + 2,
                bounds.Width,
                bounds.Height);

            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor);
            graphics.FillEllipse(fillBrush, bounds);
            graphics.DrawEllipse(borderPen, bounds);

            if (!highlight)
            {
                return;
            }

            using var highlightBrush = new SolidBrush(
                Color.FromArgb(210, 255, 235, 151));
            graphics.FillEllipse(
                highlightBrush,
                bounds.X + 3,
                bounds.Y + 2,
                Math.Max(4, bounds.Width / 3),
                Math.Max(3, bounds.Height / 4));
        }
    }

    private sealed class RankComparisonPipsControl : SkillPipsControlBase
    {
        private int currentRank;
        private int targetRank;

        public RankComparisonPipsControl(
            int currentRank,
            int targetRank,
            int maximumRank)
            : base(maximumRank)
        {
            this.currentRank = Math.Max(0, currentRank);
            this.targetRank = Math.Max(0, targetRank);
            this.Cursor = Cursors.Default;
            this.AccessibleName = string.Create(
                CultureInfo.InvariantCulture,
                $"Current rank {this.currentRank}, target rank {this.targetRank} of {maximumRank}");
        }

        public void UpdateState(int currentRank, int targetRank)
        {
            this.currentRank = Math.Max(0, currentRank);
            this.targetRank = Math.Max(0, targetRank);
            this.AccessibleName = string.Create(
                CultureInfo.InvariantCulture,
                $"Current rank {this.currentRank}, target rank {this.targetRank} of {this.MaximumRank}");
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var previousSmoothingMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var (diameter, gap) = this.CalculatePipLayout();
            var y = Math.Max(0, (this.ClientSize.Height - diameter) / 2);

            for (var index = 0; index < this.MaximumRank; index++)
            {
                var current = index < this.currentRank;
                var planned = !current && index < this.targetRank;
                var bounds = new Rectangle(
                    index * (diameter + gap),
                    y,
                    diameter,
                    diameter);
                this.PaintPip(
                    e.Graphics,
                    bounds,
                    current
                        ? Color.FromArgb(194, 151, 45)
                        : planned
                            ? Color.FromArgb(28, 126, 157)
                            : Color.FromArgb(5, 9, 14),
                    current
                        ? Color.FromArgb(238, 206, 99)
                        : planned
                            ? MainWindowTheme.Accent
                            : Color.FromArgb(82, 101, 117),
                    current);
            }

            e.Graphics.SmoothingMode = previousSmoothingMode;
        }
    }

    private sealed class EditableSkillRankPipsControl : SkillPipsControlBase
    {
        private int currentRank;
        private int minimumTargetRank;
        private int targetRank;

        public EditableSkillRankPipsControl(
            int currentRank,
            int targetRank,
            int minimumTargetRank,
            int maximumRank)
            : base(maximumRank)
        {
            this.currentRank = Math.Max(0, currentRank);
            this.minimumTargetRank = Math.Clamp(
                minimumTargetRank,
                0,
                maximumRank);
            this.targetRank = Math.Clamp(
                targetRank,
                this.minimumTargetRank,
                maximumRank);
            this.UpdateAccessibleName();
        }

        public void UpdateState(
            int currentRank,
            int targetRank,
            int minimumTargetRank)
        {
            this.currentRank = Math.Max(0, currentRank);
            this.minimumTargetRank = Math.Clamp(
                minimumTargetRank,
                0,
                this.MaximumRank);
            this.targetRank = Math.Clamp(
                targetRank,
                this.minimumTargetRank,
                this.MaximumRank);
            this.UpdateAccessibleName();
            this.Invalidate();
        }

        public event EventHandler<TargetRankChangedEventArgs>?
            TargetRankChanged;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left ||
                this.MaximumRank <= 0)
            {
                return;
            }

            var (diameter, gap) = this.CalculatePipLayout();
            var cellWidth = Math.Max(1, diameter + gap);
            var rank = Math.Clamp(
                (e.X / cellWidth) + 1,
                this.minimumTargetRank,
                this.MaximumRank);

            if (rank == this.targetRank)
            {
                rank = Math.Max(
                    this.minimumTargetRank,
                    rank - 1);
            }

            if (rank == this.targetRank)
            {
                return;
            }

            this.targetRank = rank;
            this.UpdateAccessibleName();
            this.Invalidate();
            this.TargetRankChanged?.Invoke(
                this,
                new TargetRankChangedEventArgs(rank));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var previousSmoothingMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var (diameter, gap) = this.CalculatePipLayout();
            var y = Math.Max(0, (this.ClientSize.Height - diameter) / 2);

            for (var index = 0; index < this.MaximumRank; index++)
            {
                var current = index < this.currentRank;
                var planned = !current && index < this.targetRank;
                var bounds = new Rectangle(
                    index * (diameter + gap),
                    y,
                    diameter,
                    diameter);
                this.PaintPip(
                    e.Graphics,
                    bounds,
                    current
                        ? Color.FromArgb(194, 151, 45)
                        : planned
                            ? Color.FromArgb(28, 126, 157)
                            : Color.FromArgb(5, 9, 14),
                    current
                        ? Color.FromArgb(238, 206, 99)
                        : planned
                            ? MainWindowTheme.Accent
                            : Color.FromArgb(82, 101, 117),
                    current);

                if (index != this.targetRank - 1)
                {
                    continue;
                }

                using var targetPen = new Pen(MainWindowTheme.Accent, 2.0f);
                e.Graphics.DrawEllipse(
                    targetPen,
                    Rectangle.Inflate(bounds, 2, 2));
            }

            e.Graphics.SmoothingMode = previousSmoothingMode;
        }

        private void UpdateAccessibleName()
        {
            this.AccessibleName = string.Create(
                CultureInfo.InvariantCulture,
                $"Current rank {this.currentRank}, target rank {this.targetRank} of {this.MaximumRank}. Click a rank to change the target.");
        }
    }

    private sealed class TargetRankChangedEventArgs(int targetRank) : EventArgs
    {
        public int TargetRank { get; } = targetRank;
    }

}
