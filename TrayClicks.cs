using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class TrayClicks : IDisposable {
    readonly NotifyIcon icon;
    readonly Action open;
    readonly Func<string> status;
    readonly Timer single = new Timer { Interval = SystemInformation.DoubleClickTime + 50 };
    readonly Timer dismiss = new Timer { Interval = 6000 };
    readonly StatusWindow popup = new StatusWindow();
    Point clickLocation;
    uint lastClickTime;
    bool pendingClick;
    bool suppressMouseUp;
    readonly Label label = new Label {
        AutoSize = true, MaximumSize = new Size(340,0), Padding = new Padding(12,8,12,8),
        BackColor = SystemColors.Info, ForeColor = SystemColors.InfoText
    };

    internal TrayClicks(NotifyIcon icon, Func<string> status, Action open) {
        this.icon = icon;
        this.status = status;
        this.open = open;
        popup.Controls.Add(label);
        icon.MouseUp += MouseUp;
        icon.MouseDoubleClick += NativeDoubleClick;
        single.Tick += delegate { single.Stop(); pendingClick = false; ShowStatus(); };
        dismiss.Tick += delegate { dismiss.Stop(); popup.Hide(); };
    }

    // Count completed clicks rather than depending on Explorer's double-click callback.
    void MouseUp(object sender, MouseEventArgs e) {
        AppLauncher.Log("Tray mouse-up: " + e.Button);
        if (e.Button == MouseButtons.Left && suppressMouseUp) {
            suppressMouseUp = false;
            return;
        }
        if (e.Button != MouseButtons.Left) {
            single.Stop();
            pendingClick = false;
            return;
        }
        Point position = Cursor.Position;
        uint now = unchecked((uint)Environment.TickCount);
        Size tolerance = SystemInformation.DoubleClickSize;
        bool doubleClick = pendingClick && unchecked(now - lastClickTime) <= SystemInformation.DoubleClickTime &&
            Math.Abs(position.X - clickLocation.X) <= tolerance.Width / 2 &&
            Math.Abs(position.Y - clickLocation.Y) <= tolerance.Height / 2;
        single.Stop();
        pendingClick = false;
        if (doubleClick) {
            OpenWindow();
            return;
        }
        clickLocation = position;
        lastClickTime = now;
        pendingClick = true;
        single.Start();
    }

    void NativeDoubleClick(object sender, MouseEventArgs e) {
        if (e.Button != MouseButtons.Left) return;
        AppLauncher.Log("Tray native double-click.");
        suppressMouseUp = true;
        OpenWindow();
    }

    void OpenWindow() {
        single.Stop();
        pendingClick = false;
        dismiss.Stop();
        popup.Hide();
        open();
    }

    void ShowStatus() {
        label.Text = status();
        Point cursor = clickLocation;
        Rectangle area = Screen.FromPoint(cursor).WorkingArea;
        Size size = label.GetPreferredSize(Size.Empty);
        popup.ClientSize = size;
        int x = Math.Max(area.Left, Math.Min(cursor.X - size.Width / 2, area.Right - size.Width));
        int y = Math.Max(area.Top, Math.Min(cursor.Y - size.Height - 32, area.Bottom - size.Height - 24));
        popup.Location = new Point(x,y);
        if (!popup.Visible) popup.Show();
        dismiss.Stop();
        dismiss.Start();
    }

    sealed class StatusWindow : Form {
        public StatusWindow() {
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = SystemColors.Info;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return parameters;
            }
        }
    }

    public void Dispose() {
        icon.MouseUp -= MouseUp;
        icon.MouseDoubleClick -= NativeDoubleClick;
        single.Dispose();
        dismiss.Dispose();
        popup.Dispose();
    }
}
