using System.Windows;
using System.Windows.Controls;
using LinkView.Windows.ViewModels;

namespace LinkView.Windows.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void TurnCredential_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box)
        {
            vm.Draft.TurnCredential = box.Password;
            vm.Draft.ClearTurnCredential = false;
        }
    }
}
