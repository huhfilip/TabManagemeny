using System.Windows;

namespace TabMan.App.Dialogs;

public partial class TextPromptWindow : Window
{
    private readonly Func<string, string?>? _validate;

    public TextPromptWindow(string title, string prompt, string initial, Func<string, string?>? validate)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        _validate = validate;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Value => InputBox.Text;

    public static string? Ask(Window? owner, string title, string prompt, string initial = "", Func<string, string?>? validate = null)
    {
        var dialog = new TextPromptWindow(title, prompt, initial, validate);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.Value.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var error = _validate?.Invoke(InputBox.Text.Trim());
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
