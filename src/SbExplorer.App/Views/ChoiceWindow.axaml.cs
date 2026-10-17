using Avalonia.Controls;

namespace SbExplorer.App.Views;

public partial class ChoiceWindow : Window
{
    public ChoiceWindow() => InitializeComponent();

    public ChoiceWindow(string message, IReadOnlyList<string> options)
        : this()
    {
        MessageText.Text = message;
        if (options.Count > 1)
        {
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            cancel.Click += (_, _) => Close(null);
            Buttons.Children.Add(cancel);
        }

        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var button = new Button { Content = options[i], IsDefault = i == options.Count - 1 };
            if (i == options.Count - 1)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => Close(index);
            Buttons.Children.Add(button);
        }
    }
}
