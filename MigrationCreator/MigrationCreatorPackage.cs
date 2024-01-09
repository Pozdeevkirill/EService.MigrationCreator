global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using EnvDTE;
using EnvDTE80;
using Microsoft;
using Microsoft.Build.Framework;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using MigrationCreator.Options;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Project = EnvDTE.Project;

namespace MigrationCreator
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.MigrationCreatorString)]
    [ProvideOptionPage(typeof(Options.OptionsProvider.GeneralOptions), "MigrationCreator", "General", 0, 0, true, SupportsProfiles = true)]
    public sealed class MigrationCreatorPackage : ToolkitPackage
    {
        private static string _folder; //Папка, где хранится шаблон файла
        private const string fileNameTemplate = "V{0}{1}.cs"; //Шаблон имени файла
        private const string _defaultExt = ".txt"; //Расширение шаблона
        private static string _template = " "; //путь к шаблону
        private const string _templateDir = "Templates";

        public static DTE2 _dte;
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            Assumes.Present(_dte);

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
            {
                CommandID menuCommandID = new CommandID(PackageGuids.MigrationCreator, PackageIds.MyCommand);
                OleMenuCommand menuItem = new OleMenuCommand(ExecuteAsync, menuCommandID);
                mcs.AddCommand(menuItem);
            }
        }
        private void ExecuteAsync(object sender, EventArgs e)
        {
            //Получаю папку, по которой кликнули правой кнопкой мыши(Куда будет создаваться файл)
            NewItemTarget target = NewItemTarget.Create(_dte);

            //Вывожу диалоговое окно для создания файла и получаю имя миграции
            string input = PromptForFileName(target.Directory).TrimStart('/', '\\').Replace("/", "\\");

            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            //Формирую полное имя файла миграции
            string date = DateTime.Now.ToString("yyyy MM dd HH mm ss").Replace(" ", "");
            string fileName = string.Format(fileNameTemplate, date, input);

            try
            {
                AddItemAsync(fileName, target).Forget();
            }
            catch(Exception ex) 
            {
                VS.MessageBox.ShowError("MigrationTemplateCreator",
                        $"Error creating file '{fileName}':{Environment.NewLine}{ex.Message}");
            }

            
        }

        /// <summary>
        /// Выводит диалоговое окно, куда вводиться название файла
        /// </summary>
        /// <returns>Название файла</returns>
        private string PromptForFileName(string folder)
        {
            DirectoryInfo dir = new DirectoryInfo(folder);
            FileNameDialog dialog = new FileNameDialog(dir.Name)
            {
                Owner = Application.Current.MainWindow
            };

            bool? result = dialog.ShowDialog();
            return (result.HasValue && result.Value) ? dialog.Input : string.Empty;
        }

        private async Task AddItemAsync(string name,  NewItemTarget target)
        {
            await AddFileAsync(name, target);
        }

        private async Task AddFileAsync(string name, NewItemTarget target)
        {
            FileInfo file;
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            file = new FileInfo(Path.Combine(target.Directory, name));

            Directory.CreateDirectory(file.DirectoryName);

            if (!file.Exists)
            {
                Project project;
                project = target.Project;

                await WriteFileAsync(project, file.FullName);

                if (target.ProjectItem != null && target.ProjectItem.IsKind(EnvDTE.Constants.vsProjectItemKindVirtualFolder))
                {
                    target.ProjectItem.ProjectItems.AddFromFile(file.FullName);
                }
                else
                {
                    project.AddFileToProject(file);
                }

                VsShellUtilities.OpenDocument(this, file.FullName);

                ExecuteCommandIfAvailable("SolutionExplorer.SyncWithActiveDocument");
                _dte.ActiveDocument.Activate();
            }
            else
            {
                VS.MessageBox.ShowWarningAsync("MigrationCreator", $"The file '{file}' already exists.");
            }
        }

        private void ExecuteCommandIfAvailable(string commandName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Command command;

            try
            {
                command = _dte.Commands.Item(commandName);
            }
            catch (ArgumentException)
            {
                // The command does not exist, so we can't execute it.
                return;
            }

            if (command.IsAvailable)
            {
                _dte.ExecuteCommand(commandName);
            }
        }

        private static void AddTemplatesFromCurrentFolder(string template, string dir)
        {
            var assembly = Assembly.GetExecutingAssembly().Location;
            _folder = Path.Combine(Path.GetDirectoryName(assembly), "Templates");
            _template = Directory.GetFiles(_folder, "*" + _defaultExt, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static async Task<int> WriteFileAsync(Project project, string file)
        {
            string template = await GetTemplateFilePathAsync(project, file);

            if (!string.IsNullOrEmpty(template))
            {
                int index = template.IndexOf('$');

                if (index > -1)
                {
                    template = template.Remove(index, 1);
                }

                await WriteToDiskAsync(file, template);
                return index;
            }

            await WriteToDiskAsync(file, string.Empty);

            return 0;
        }

        //Возвращает контент для файла, полученного из шаблона
        public static async Task<string> GetTemplateFilePathAsync(Project project, string file)
        {
            var name = Path.GetFileName(file);
            var safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
            var relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file) ?? "");



            AddTemplatesFromCurrentFolder(_template, Path.GetDirectoryName(file));

            var template = await ReplaceTokensAsync(project, safeName, relative, _template);
            return NormalizeLineEndings(template);
        }

        //Заполняет шаблон данными
        private static async Task<string> ReplaceTokensAsync(Project project, string name, string relative, string templateFile)
        {
            if (string.IsNullOrEmpty(templateFile))
            {
                return templateFile;
            }
            var options = await General.GetLiveInstanceAsync();

            var rootNs = project.GetRootNamespace();
            var ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;

            var date = DateTime.Now.ToString("yyyy, MM, dd, HH, mm, ss");

            if (!string.IsNullOrEmpty(relative))
            {
                ns += "." + ProjectHelpers.CleanNameSpace(relative);
            }

            using (var reader = new StreamReader(templateFile))
            {
                var content = await reader.ReadToEndAsync();

                return content.Replace("{namespace}", ns)
                              .Replace("{itemname}", name)
                              .Replace("{date}", date)
                              .Replace("{username}", options.UserName);
            }
        }

        private static string NormalizeLineEndings(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            return Regex.Replace(content, @"\r\n|\n\r|\n|\r", "\r\n");
        }

        private static async Task WriteToDiskAsync(string file, string content)
        {
            using (StreamWriter writer = new StreamWriter(file, false, GetFileEncoding(file)))
            {
                await writer.WriteAsync(content);
            }
        }

        private static Encoding GetFileEncoding(string file)
        {
            string[] noBom = { ".cmd", ".bat", ".json" };
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (noBom.Contains(ext))
            {
                return new UTF8Encoding(false);
            }

            return new UTF8Encoding(true);
        }

    }


}