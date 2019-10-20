﻿#region License
/*
 **************************************************************
 *  Author: Rick Strahl
 *          © West Wind Technologies, 2016
 *          http://www.west-wind.com/
 *
 * Created: 04/28/2016
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 **************************************************************
*/
#endregion

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MahApps.Metro;
using MahApps.Metro.Controls;
using MarkdownMonster.AddIns;
using Westwind.Utilities;


namespace MarkdownMonster
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static Mutex Mutex { get; set; }

        public static bool IsPortableMode { get; set; }


        public static string InitialStartDirectory { get; }

        public static bool StartInPresentationMode { get; set; }

        public static bool ForceNewWindow { get; set; }

        public static bool NoSplash { get; set; }

        /// <summary>
        /// Startup Command Arguments without the initial full
        /// command line. arg[0] is the first parameter on the
        /// command line.
        /// </summary>
        public static string[] CommandArgs { get; set; }


        // Flag to indicate that app shouldn't start
        // Need this so OnStartup doesn't fire
        internal static bool _noStart = false;


        static App()
        {
            //try
            //{   // Multi-Monitor DPI awareness for screen captures
            //    // requires [assembly: DisableDpiAwareness] set in assemblyinfo
            //    bool res = WindowUtilities.SetPerMonitorDpiAwareness(ProcessDpiAwareness.Process_Per_Monitor_DPI_Aware);
            //}
            //catch {  /* fails not supported on Windows 7 and older */ }

            InitialStartDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public App()
        {
            // Get just the command arguments
            CommandArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (CommandArgs.Length > 0)
            {
                var processor = new CommandLineProcessor(this);
                processor.HandleCommandLineArguments();
            }


            SplashScreen splashScreen = null;
            if (!mmApp.Configuration.DisableSplashScreen && !NoSplash)
            {
                splashScreen = new SplashScreen("assets/markdownmonstersplash.png");
                splashScreen.Show(true);
            }


            // Singleton launch marshalls subsequent launches to the singleton instance
            // via named pipes communication
            if (!ForceNewWindow && mmApp.Configuration.UseSingleWindow)
                CheckCommandLineForSingletonLaunch(splashScreen);

            // We have to manage assembly loading for Addins
            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

#if !DEBUG
            //AppDomain currentDomain = AppDomain.CurrentDomain;
            //currentDomain.UnhandledException += new UnhandledExceptionEventHandler(GlobalErrorHandler);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
#endif
            // This has to be here for AppInsights not in OnStartup
            mmApp.ApplicationStart();

        }


        protected override void OnStartup(StartupEventArgs e)
        {
            if (_noStart)
                return;

#if false
            var dotnetVersion = MarkdownMonster.Utilities.mmWindowsUtils.GetDotnetVersion();
            if (string.Compare(dotnetVersion, "4.6.2", StringComparison.Ordinal) < 0)
            {
                Task.Run(() => MessageBox.Show("Markdown Monster requires .NET 4.6.2 or later to run.\r\n\r\n" +
                                               "Please download and install the latest version of .NET version from:\r\n" +
                                               "https://www.microsoft.com/net/download/framework\r\n\r\n" +
                                               "Exiting application and navigating to .NET Runtime Downloads page.",
                    "Markdown Monster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                ));

                Thread.Sleep(10000);
                ShellUtils.GoUrl("https://www.microsoft.com/net/download/framework");
                Environment.Exit(0);
            }
#endif

            if (mmApp.Configuration.DisableHardwareAcceleration)
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            // always set directory to current location
            var dir = Assembly.GetExecutingAssembly().Location;
            Directory.SetCurrentDirectory(Path.GetDirectoryName(dir));

            if (!mmApp.Configuration.DisableAddins)
                ThreadPool.QueueUserWorkItem(p => LoadAddins());

            ThemeCustomizations();

            ThreadPool.QueueUserWorkItem(p =>
            {
                mmFileUtils.EnsureBrowserEmulationEnabled("MarkdownMonster.exe");
                mmFileUtils.EnsureSystemPath();
                mmFileUtils.EnsureAssociations();

                if (!Directory.Exists(mmApp.Configuration.InternalCommonFolder))
                {
                    Directory.CreateDirectory(mmApp.Configuration.InternalCommonFolder);


                }
            });

        }




        /// <summary>
        /// Checks to see if app is already running and if it is pushes
        /// parameters via NamedPipes to existing running application
        /// and exits this instance.
        ///
        /// Otherwise app just continues
        /// </summary>
        /// <param name="splashScreen"></param>
        private void CheckCommandLineForSingletonLaunch(SplashScreen splashScreen)
        {
            if (App.ForceNewWindow || !mmApp.Configuration.UseSingleWindow)
                return;

            // fix up the startup path
            string filesToOpen = " ";
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < CommandArgs.Length; i++)
            {
                string file = CommandArgs[i];
                if (string.IsNullOrEmpty(file))
                    continue;

                if (!file.StartsWith("-"))
                {
                    file = file.TrimEnd('\\');
                    file = Path.GetFullPath(file);
                }

                sb.AppendLine(file);

                // write fixed up path arguments
                CommandArgs[i] = file;
            }

            filesToOpen = sb.ToString();

            Mutex = new Mutex(true, @"MarkdownMonster", out bool isOnlyInstance);
            if (isOnlyInstance)
                return;

            _noStart = true;

            var manager = new NamedPipeManager("MarkdownMonster");
            manager.Write(filesToOpen);

            splashScreen?.Close(TimeSpan.MinValue);

            // Shut down application
            Environment.Exit(0);
        }



        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // missing resources are... missing
            if (args.Name.Contains(".resources"))
                return null;

            // check for assemblies already loaded
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
            if (assembly != null)
                return assembly;

            // Try to load by filename - split out the filename of the full assembly name
            // and append the base path of the original assembly (ie. look in the same dir)
            // NOTE: this doesn't account for special search paths but then that never
            //           worked before either.
            string filename = args.Name.Split(',')[0] + ".dll".ToLower();


            // try load the assembly from install path (this should always be automatic)
            try
            {
                // this allows addins to load before form has loaded
                return Assembly.LoadFrom(filename);
            }
            catch
            {
            }

            // try load from install addins folder
            string asmFile = FindFileInPath(filename, ".\\Addins");
            if (!string.IsNullOrEmpty(asmFile))
            {
                try
                {
                    return Assembly.LoadFrom(asmFile);
                }
                catch
                {
                }
            }

            return null;
        }

        /// <summary>
        /// Looks for the first match in a file structure
        /// </summary>
        /// <param name="filename">The filename only to look for</param>
        /// <param name="path">Path to start with</param>
        /// <returns>Fully qualified path of the file found or NULL</returns>
        private string FindFileInPath(string filename, string path)
        {
            filename = filename.ToLower();

            foreach (var fullFile in Directory.GetFiles(path))
            {
                var file = Path.GetFileName(fullFile).ToLower();
                if (file == filename)
                    return fullFile;

            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                var file = FindFileInPath(filename, dir);
                if (!string.IsNullOrEmpty(file))
                    return file;
            }

            return null;
        }

        private void App_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            if (!mmApp.HandleApplicationException(e.Exception as Exception, ApplicationErrorModes.AppDispatcher))
                Environment.Exit(1);

            e.Handled = true;
        }


        public static string UserDataPath { get; internal set; }
        public static string VersionCheckUrl { get; internal set; }



        private void ThemeCustomizations()
        {
            mmApp.ChangeAppThemeAtRuntime();
        }



        /// <summary>
        /// Loads all addins asynchronously without loading the
        /// addin UI  -handled in Window Load to ensure Window is up)
        /// </summary>
        private void LoadAddins()
        {
            try
            {
                AddinManager.Current.LoadAddins(Path.Combine(App.InitialStartDirectory, "AddIns"));
                AddinManager.Current.LoadAddins(mmApp.Configuration.AddinsFolder);
                AddinManager.Current.AddinsLoadingComplete = true;

                AddinManager.Current.AddinsLoaded?.Invoke();

                try
                {
                    AddinManager.Current.RaiseOnApplicationStart();
                }
                catch (Exception ex)
                {
                    mmApp.Log("Addin loading failed", ex);
                }
            }
            catch (Exception ex)
            {
                mmApp.Log("Addin loading failed", ex);
            }
        }

    }

}
