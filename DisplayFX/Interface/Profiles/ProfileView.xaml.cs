using System.Windows;
using System.Windows.Controls;

namespace DisplayFX.Interface.Profiles;

public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
    }

    private void ProfileCard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileViewModel vm)
        {
            vm.IsSelected = true;
        }
    }
}