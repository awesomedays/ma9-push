using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

using Ma9_Season_Push.Logging;

namespace Ma9_Season_Push.Core;

/// <summary>
/// 트레이(ContextMenuStrip) 스타일 유틸
/// - 항상 다크
/// - 둥근 모서리/그림자 미적용(WinForms 기본 제약)
/// - 색/패딩/호버/구분선/마진을 레퍼런스 느낌으로 정리
/// - 폭(Width) 강제 적용으로 텍스트 클리핑/hover 영역 축소 문제 해결
/// </summary>
internal static class TrayMenuStyle
{
    // ====== 팔레트(필요 시 여기만 조정) ======
    private static readonly Color Bg = Color.FromArgb(54, 55, 70);  // 기존보다 밝게
    private static readonly Color ItemHover = Color.FromArgb(80, 80, 80);
    private static readonly Color ItemPressed = Color.FromArgb(100, 100, 100);

    private static readonly Color Text = Color.FromArgb(235, 235, 245);
    private static readonly Color TextDisabled = Color.FromArgb(155, 155, 165);

    private static readonly Color Separator = Color.FromArgb(110, 110, 110);
    private static readonly Color Border = Color.FromArgb(62, 62, 66);

    // ====== 레이아웃(필요 시 여기만 조정) ======
    private const int ItemHeight = 35;                 // 항목 높이
    private const int PadLeftRight = 15;               // 좌우 패딩
    private const int SeparatorPadLeftRight = 5;      // 구분선 좌우 여백

    // 메뉴 폭(필요 시 여기만 조정)
    // - 텍스트가 짧아도 폭이 과하게 줄어 hover 영역/표시가 깨지는 것을 방지
    private const int FixedMenuWidthPx = 110;

    // Segoe UI는 Win11 기본 계열이어서 메뉴에서는 가장 무난합니다.
    private static readonly Font MenuFont = new("Segoe UI", 9.0f, FontStyle.Regular);

    public static ContextMenuStrip CreateDarkMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            BackColor = Bg,
            ForeColor = Text,
            Font = MenuFont,
            Padding = Padding.Empty,
            Renderer = new DarkMenuRenderer(
                bg: Bg,
                hover: ItemHover,
                pressed: ItemPressed,
                separator: Separator,
                border: Border,
                text: Text,
                disabledText: TextDisabled,
                separatorPadLeftRight: SeparatorPadLeftRight
            )
        };

        // 기본 이미지 스케일링이 남아있으면 여백이 생기는 경우가 있어 명시적으로 비활성화
        menu.ImageScalingSize = Size.Empty;

        // ✅ 메뉴가 열리기 직전에 폭을 확정해야 레이아웃/hover 영역이 안정적임
        menu.Opening += (_, __) =>
        {
            ApplyFixedWidth(menu, FixedMenuWidthPx);
        };

        return menu;
    }

    public static ToolStripMenuItem CreateItem(string text, Action onClick)
    {
        if (onClick == null) throw new ArgumentNullException(nameof(onClick));

        var item = BaseItem(text);
        item.Click += (_, __) =>
        {
            try { onClick(); }
            catch (Exception ex) { Logger.Error($"Tray menu click failed: {text} ex={ex}"); }
        };
        return item;
    }

    public static ToolStripMenuItem CreateItemAsync(string text, Func<Task> onClickAsync)
    {
        if (onClickAsync == null) throw new ArgumentNullException(nameof(onClickAsync));

        var item = BaseItem(text);
        item.Click += async (_, __) =>
        {
            try { await onClickAsync().ConfigureAwait(true); } // UI 컨텍스트 유지
            catch (Exception ex) { Logger.Error($"Tray menu async click failed: {text} ex={ex}"); }
        };
        return item;
    }
    public static ToolStripSeparator CreateSeparator()
    {
        return new ToolStripSeparator
        {
            AutoSize = false,
            Height = 12,
            Margin = new Padding(0, 2, 0, 2),
            Padding = new Padding(0, 2, 0, 2) // ✅ ContentRectangle 확보용 (중요)
        };
    }

    /// <summary>
    /// 메뉴 폭을 고정하고, 모든 항목 폭도 동일하게 고정한다.
    /// - 텍스트 클리핑(앞의 2글자만 보임)
    /// - hover 영역이 좁게 잡힘
    /// 위 2개 문제를 동시에 해결한다.
    /// </summary>
    public static void ApplyFixedWidth(ContextMenuStrip menu, int width)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        if (width < 80) width = 80;

        // ✅ 핵심: 메뉴는 AutoSize 유지(높이는 OS/ToolStrip이 계산하도록 둠)
        menu.AutoSize = true;

        // ✅ 폭만 고정: Min/Max로 잠그면 높이 계산은 유지되고 폭만 강제됨
        menu.MinimumSize = new Size(width, 0);
        menu.MaximumSize = new Size(width, 0);

        // 아이템도 폭 고정(hover 영역 포함) - 높이는 이미 item.Height로 고정되어 있으니 건드리지 않음
        foreach (ToolStripItem it in menu.Items)
        {
            it.AutoSize = false;
            it.Width = width;
        }
    }
    private static ToolStripMenuItem BaseItem(string text)
    {
        return new ToolStripMenuItem(text)
        {
            AutoSize = false,
            Height = ItemHeight, // 예: 30
            Padding = new Padding(
                PadLeftRight,
                6,                 // 🔥 위
                PadLeftRight,
                6                  // 🔥 아래
            ),
            Margin = Padding.Empty,
            BackColor = Bg,
            ForeColor = Text
        };
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color _bg;
        private readonly Color _hover;
        private readonly Color _pressed;
        private readonly Color _separator;
        private readonly Color _border;
        private readonly Color _text;
        private readonly Color _disabledText;
        private readonly int _sepPadLR;

        public DarkMenuRenderer(
            Color bg,
            Color hover,
            Color pressed,
            Color separator,
            Color border,
            Color text,
            Color disabledText,
            int separatorPadLeftRight)
            : base(new DarkColorTable(bg, hover, pressed, border))
        {
            RoundedEdges = false; // 둥근 모서리 미사용
            _bg = bg;
            _hover = hover;
            _pressed = pressed;
            _separator = separator;
            _border = border;
            _text = text;
            _disabledText = disabledText;
            _sepPadLR = separatorPadLeftRight;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(_bg);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var p = new Pen(_border);
            e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);

            Color fill;
            if (!e.Item.Enabled) fill = _bg;
            else if (e.Item.Pressed) fill = _pressed;
            else if (e.Item.Selected) fill = _hover;
            else fill = _bg;

            using var b = new SolidBrush(fill);
            e.Graphics.FillRectangle(b, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _text : _disabledText;
            e.TextFormat = e.TextFormat
                | TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix;

            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            // ContentRectangle이 0이면 그릴 공간이 없는 상태
            var rect = e.Item.ContentRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            // 좌우 여백 적용
            int x1 = rect.Left + _sepPadLR;
            int x2 = rect.Right - _sepPadLR;
            if (x2 <= x1)
                return;

            // ✅ 항상 rect 내부에 들어오는 y를 사용 (클리핑 방지)
            int y = rect.Top + (rect.Height / 3);
            if (y < rect.Top) y = rect.Top;
            if (y > rect.Bottom - 1) y = rect.Bottom - 1;

            using var p = new Pen(_separator);
            e.Graphics.DrawLine(p, x1, y, x2, y);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private readonly Color _bg;
        private readonly Color _hover;
        private readonly Color _pressed;
        private readonly Color _border;

        public DarkColorTable(Color bg, Color hover, Color pressed, Color border)
        {
            _bg = bg;
            _hover = hover;
            _pressed = pressed;
            _border = border;
            UseSystemColors = false; // 시스템 색 혼입 방지
        }

        public override Color ToolStripDropDownBackground => _bg;

        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _border;

        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;

        public override Color MenuItemPressedGradientBegin => _pressed;
        public override Color MenuItemPressedGradientMiddle => _pressed;
        public override Color MenuItemPressedGradientEnd => _pressed;

        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
    }
}
