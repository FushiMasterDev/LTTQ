using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Shell;

public partial class FrmMain
{
    private void InitializeNavigation()
    {
        foreach (var button in navigation.Controls.OfType<Button>())
            button.Click += NavigationClick;
        navigation.SizeChanged += (_, _) => ResizeNavigation();
        DpiChanged += (_, _) => ResizeNavigation();
        workspaceTabs.SelectedIndexChanged += (_, _) => UpdateActiveNavigation();
    }

    private void NavigationClick(object? sender, EventArgs e)
    {
        if (changingRole || sender is not Button { Tag: ScreenEntry screen }) return;
        try { OpenScreen(screen.Id, screen.Create); }
        catch (Exception exception) { ErrorHandler.Show(exception, this); }
    }

    private void SetMenu(ScreenEntry[] entries)
    {
        changingRole = true;
        navigation.SuspendLayout();
        try
        {
            var allowed = entries.ToDictionary(entry => entry.Id);
            foreach (var button in navigation.Controls.OfType<Button>())
            {
                var id = button.Name[4..];
                var visible = allowed.TryGetValue(id, out var entry);
                button.Tag = entry;
                button.Visible = button.Enabled = visible;
                if (entry is not null)
                {
                    button.Text = button.AccessibleName = entry.Title;
                    navigationTips.SetToolTip(button, entry.Title);
                }
            }
            navigation.AutoScrollPosition = Point.Empty;
        }
        finally
        {
            navigation.ResumeLayout(true);
            changingRole = false;
        }
        ResizeNavigation();
        UpdateActiveNavigation();
    }

    private void ResizeNavigation()
    {
        var scale = DeviceDpi / 96f;
        var width = Math.Max(1, navigation.ClientSize.Width - navigation.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
        foreach (var button in navigation.Controls.OfType<Button>())
        {
            button.Width = width;
            button.Height = (int)Math.Round(44 * scale);
        }
    }

    private void UpdateActiveNavigation()
    {
        var id = (workspaceTabs.SelectedTab?.Tag as string)?.Split(':')[0];
        id = id switch { "F09" or "F10" or "F11" => "F08", "F16" => "F15", "F31" => "F30", _ => id };
        foreach (var button in navigation.Controls.OfType<Button>())
        {
            var selected = button.Tag is ScreenEntry entry && entry.Id == id;
            button.BackColor = selected ? UiTheme.Accent : Color.White;
            button.ForeColor = selected ? Color.White : UiTheme.Ink;
            button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : Color.FromArgb(190, 211, 206);
            button.FlatAppearance.MouseOverBackColor = selected ? Color.FromArgb(0, 85, 76) : Color.FromArgb(220, 238, 233);
            button.FlatAppearance.MouseDownBackColor = selected ? Color.FromArgb(0, 70, 62) : Color.FromArgb(198, 225, 217);
        }
    }
}
