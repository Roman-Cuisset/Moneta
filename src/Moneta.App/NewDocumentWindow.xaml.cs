using System.Windows;

namespace Moneta.App;

public partial class NewDocumentWindow : Window
{
    public string FolderName => DocumentName.Text.Trim();
    public NewDocumentWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => DocumentName.Focus();
    }
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderName))
        {
            ErrorText.Text = L.Get("NewDocumentWindow.EnterAFileName");
            DocumentName.Focus();
            return;
        }
        DialogResult = true;
    }
}
