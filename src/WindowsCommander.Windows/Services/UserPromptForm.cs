using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WindowsCommander.Windows.Services;

// The ask_user dialog: a message, an optional text field and one button per
// option. It closes itself when the timeout elapses so an unanswered prompt
// never lingers on the desktop after the tool call has returned.
internal sealed class UserPromptForm : Forms.Form
{
    private readonly Forms.TextBox? textBox;

    public UserPromptForm(string title, string message, IReadOnlyList<string> options, bool allowTextInput, string? defaultText, int timeoutMs)
    {
        Text = title;
        AutoScaleMode = Forms.AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = Forms.AutoSizeMode.GrowAndShrink;
        FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = Forms.FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        Padding = new Forms.Padding(12);
        MinimumSize = new Drawing.Size(360, 0);

        var layout = new Forms.TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = Forms.AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = Forms.DockStyle.Fill
        };

        layout.Controls.Add(new Forms.Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Drawing.Size(520, 0),
            Margin = new Forms.Padding(0, 0, 0, 12)
        });

        if (allowTextInput)
        {
            textBox = new Forms.TextBox
            {
                Text = defaultText ?? string.Empty,
                Width = 480,
                Margin = new Forms.Padding(0, 0, 0, 12)
            };
            layout.Controls.Add(textBox);
        }

        var buttons = new Forms.FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = Forms.AutoSizeMode.GrowAndShrink,
            FlowDirection = Forms.FlowDirection.LeftToRight,
            Anchor = Forms.AnchorStyles.Right,
            WrapContents = true
        };

        foreach (var option in options)
        {
            var button = new Forms.Button
            {
                Text = option,
                AutoSize = true,
                MinimumSize = new Drawing.Size(88, 0)
            };
            button.Click += (_, _) => Finish("answered", option);
            buttons.Controls.Add(button);
        }

        layout.Controls.Add(buttons);
        Controls.Add(layout);

        // Enter (and Space, while it has focus) picks the first option, Esc /
        // the close box cancels.
        var firstButton = (Forms.Button)buttons.Controls[0];
        AcceptButton = firstButton;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Forms.Keys.Escape)
            {
                Finish("cancelled", null);
            }
        };

        var timer = new Forms.Timer { Interval = timeoutMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Finish("timeout", null);
        };

        Shown += (_, _) =>
        {
            timer.Start();

            // The server is a background process, so a plain Activate() only
            // flashes the taskbar and the keyboard keeps going to the previous
            // window. Force real activation so Enter / Esc reach the dialog.
            WindowService.ForceForeground(Handle);
            Activate();
            if (textBox is not null)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
            else
            {
                firstButton.Focus();
            }
        };

        FormClosed += (_, _) => timer.Dispose();
    }

    public string Decision { get; private set; } = "cancelled";

    public string? Choice { get; private set; }

    public string? EnteredText { get; private set; }

    private void Finish(string decision, string? choice)
    {
        Decision = decision;
        Choice = choice;
        EnteredText = decision == "answered" ? textBox?.Text : null;
        Close();
    }
}
