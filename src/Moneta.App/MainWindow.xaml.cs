using System.Windows;

namespace Moneta.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    private void TransactionSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => DetailsScroll?.ScrollToTop();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Version de développement — données fictives\n\n"
            + "Prototype Windows WPF Moneta en lecture seule, avec données de démonstration en mémoire.\n"
            + "Choisissez un compte, parcourez ses opérations et consultez le détail de la ligne sélectionnée. "
            + "Les devises restent séparées et aucune conversion n'est calculée.\n\n"
            + "Les commandes grisées et leurs raccourcis ne sont pas implémentés. "
            + "L'ordre des menus, les séparateurs et les accès Alt sont provisoires et propres à Moneta ; "
            + "ils ne prétendent pas reproduire exactement BankPerfect. "
            + "Les commandes dont l'emplacement reste inconnu ou contradictoire, dont Remplacer, sont omises.",
            "À propos de Moneta",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
