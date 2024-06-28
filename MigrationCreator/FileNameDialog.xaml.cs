using System.Windows;
using System.Windows.Input;

namespace MigrationCreator
{
    /// <summary>
    /// Interaction logic for FileNameDialog.xaml
    /// </summary>
    public partial class FileNameDialog : Window
    {
        private const string DEFAULT_TEXT = "Enter a migration file name";

        public FileNameDialog(string folder)
        {
            InitializeComponent();

            lblFolder.Content = string.Format("{0}/", folder);
            Loaded += (s, e) =>
            {
                //Icon = BitmapFrame.Create(new Uri("pack://application:,,,/MigrationTemplateCreator;component/Resources/Icon.png", UriKind.RelativeOrAbsolute));
                Title = Vsix.Name;

                txtName.Focus();
                txtName.CaretIndex = 0;
                txtName.Text = DEFAULT_TEXT;
                txtName.Select(0, txtName.Text.Length);

                txtName.PreviewKeyDown += (a, b) =>
                {
                    if (b.Key == Key.Escape)
                    {
                        if (string.IsNullOrWhiteSpace(txtName.Text) || txtName.Text == DEFAULT_TEXT)
                        {
                            Close();
                        }
                        else
                        {
                            txtName.Text = string.Empty;
                        }
                    }
                    else if (txtName.Text == DEFAULT_TEXT)
                    {
                        txtName.Text = string.Empty;
                        btnCreate.IsEnabled = true;
                    }
                };

            };
        }

        public string Input => txtName.Text.Trim();

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
