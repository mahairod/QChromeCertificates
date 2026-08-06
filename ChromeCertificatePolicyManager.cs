using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ChromeCertificatePolicyManager
{
    internal static class Program
    {
        internal const string DefaultRegistryPath = @"Software\Policies\Google\Chrome";
        private const string VersionResourceName = "ChromeCertificatePolicyManager.VERSION";

        internal static readonly string Version = LoadVersion();

        internal static string WindowTitle
        {
            get { return UiText.Get("Менеджер сертификатов браузера — ", "Browser Certificate Manager — ") + Version; }
        }

        private static string LoadVersion()
        {
            using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(VersionResourceName))
            {
                if (stream == null)
                {
                    return "неизвестная версия";
                }

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd().Trim();
                }
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string registryPath = DefaultRegistryPath;
            bool selfTest = false;
            AppSettings settings = AppSettings.Load();
            UiText.Language = settings.Language;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--self-test")
                {
                    selfTest = true;
                }
                else if (args[i] == "--registry-path" && i + 1 < args.Length)
                {
                    registryPath = args[++i];
                }
                else
                {
                    MessageBox.Show(
                        UiText.Get("Неизвестный параметр: ", "Unknown argument: ") + args[i],
                        UiText.Get("Ошибка", "Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 2;
                }
            }

            if (selfTest)
            {
                return SelfTest.Run(registryPath);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!String.Equals(registryPath, DefaultRegistryPath, StringComparison.OrdinalIgnoreCase))
            {
                Application.Run(new MainForm(settings, registryPath));
            }
            else
            {
                Application.Run(new MainForm(settings, null));
            }
            return 0;
        }
    }

    internal static class UiText
    {
        public static string Language { get; set; }

        public static bool IsRussian
        {
            get { return String.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase); }
        }

        public static string Get(string russian, string english)
        {
            return IsRussian ? russian : english;
        }
    }

    internal static class UiScale
    {
        public static int ValueFor(Control control, int value)
        {
            using (Graphics graphics = control.CreateGraphics())
            {
                return (int)Math.Round(value * graphics.DpiX / 96F);
            }
        }

        public static Size SizeFor(Control control, int width, int height)
        {
            return new Size(ValueFor(control, width), ValueFor(control, height));
        }

        public static int LogicalValueFor(Control control, int value)
        {
            using (Graphics graphics = control.CreateGraphics())
            {
                return (int)Math.Round(value * 96F / graphics.DpiX);
            }
        }

        public static void EqualizeButtonSizes(params Button[] buttons)
        {
            foreach (Button button in buttons)
            {
                button.MinimumSize = Size.Empty;
            }

            int width = buttons.Max(button => button.PreferredSize.Width);
            int height = buttons.Max(button => button.PreferredSize.Height);
            foreach (Button button in buttons)
            {
                button.MinimumSize = new Size(width, height);
            }
        }
    }

    internal sealed class AppSettings
    {
        public string Language { get; set; }
        public string BrowserId { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }

        public AppSettings()
        {
            Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
            BrowserId = "chrome";
            WindowWidth = 980;
            WindowHeight = 640;
        }

        private static string SettingsPath
        {
            get
            {
                string executable = Application.ExecutablePath;
                string name = Path.GetFileNameWithoutExtension(executable) + "-settings.json";
                return Path.Combine(Path.GetDirectoryName(executable), name);
            }
        }

        public static AppSettings Load()
        {
            AppSettings fallback = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return fallback;
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppSettings loaded = serializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                if (loaded == null)
                {
                    return fallback;
                }

                if (loaded.Language != "ru" && loaded.Language != "en")
                {
                    loaded.Language = fallback.Language;
                }
            if (BrowserDefinition.Find(loaded.BrowserId) == null)
            {
                loaded.BrowserId = fallback.BrowserId;
            }
            if (loaded.WindowWidth < 850)
            {
                loaded.WindowWidth = fallback.WindowWidth;
            }
            if (loaded.WindowHeight < 520)
            {
                loaded.WindowHeight = fallback.WindowHeight;
            }
            return loaded;
            }
            catch
            {
                return fallback;
            }
        }

        public void Save()
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                File.WriteAllText(SettingsPath, serializer.Serialize(this), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    internal sealed class BrowserDefinition
    {
        public static readonly BrowserDefinition[] All =
        {
            new BrowserDefinition("chrome", "Chrome", @"Software\Policies\Google\Chrome", "chrome.exe", "chrome://policy/"),
            new BrowserDefinition("edge", "Edge", @"Software\Policies\Microsoft\Edge", "msedge.exe", "edge://policy/"),
            new BrowserDefinition("brave", "Brave", @"Software\Policies\BraveSoftware\Brave", "brave.exe", "brave://policy/"),
            new BrowserDefinition("chromium", "Chromium", @"Software\Policies\Chromium", "chrome.exe", "chrome://policy/")
        };

        private BrowserDefinition(string id, string name, string registryPath, string executableName, string policyUrl)
        {
            Id = id;
            Name = name;
            RegistryPath = registryPath;
            ExecutableName = executableName;
            PolicyUrl = policyUrl;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string RegistryPath { get; private set; }
        public string ExecutableName { get; private set; }
        public string PolicyUrl { get; private set; }

        public string ResolveExecutable()
        {
            if (Id != "chromium")
            {
                return ExecutableName;
            }

            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            foreach (string root in roots)
            {
                if (String.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string candidate = Path.Combine(root, "Chromium", "Application", "chrome.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(UiText.Get(
                "Не найден исполняемый файл Chromium в стандартных каталогах.",
                "The Chromium executable was not found in the standard locations."));
        }

        public static BrowserDefinition Find(string id)
        {
            return All.FirstOrDefault(browser => String.Equals(browser.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class CertificatePolicyEntry
    {
        public string CertificateBase64 { get; set; }
        public List<string> DnsNames { get; set; }
        public List<string> Cidrs { get; set; }

        public CertificatePolicyEntry()
        {
            DnsNames = new List<string>();
            Cidrs = new List<string>();
        }

        public X509Certificate2 GetCertificate()
        {
            return new X509Certificate2(Convert.FromBase64String(CertificateBase64));
        }
    }

    internal sealed class PolicyState
    {
        public bool? PlatformIntegrationEnabled { get; set; }
        public List<CertificatePolicyEntry> Entries { get; set; }

        public PolicyState()
        {
            Entries = new List<CertificatePolicyEntry>();
        }
    }

    internal sealed class PolicyStore
    {
        internal const string PlatformIntegrationName = "CAPlatformIntegrationEnabled";
        internal const string CertificatesWithConstraintsName = "CACertificatesWithConstraints";

        private readonly RegistryKey root;
        private readonly string path;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public PolicyStore(RegistryKey root, string path)
        {
            this.root = root;
            this.path = path;
            serializer.MaxJsonLength = Int32.MaxValue;
        }

        public PolicyState Load()
        {
            PolicyState state = new PolicyState();
            using (RegistryKey key = root.OpenSubKey(path, false))
            {
                if (key == null)
                {
                    return state;
                }

                object integration = key.GetValue(PlatformIntegrationName, null);
                if (integration != null)
                {
                    state.PlatformIntegrationEnabled = Convert.ToInt32(integration, CultureInfo.InvariantCulture) != 0;
                }

                string json = key.GetValue(CertificatesWithConstraintsName, null) as string;
                if (!String.IsNullOrWhiteSpace(json))
                {
                    state.Entries = DeserializeEntries(json);
                }
            }

            return state;
        }

        public void Save(PolicyState state)
        {
            using (RegistryKey key = root.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (state.PlatformIntegrationEnabled.HasValue)
                {
                    key.SetValue(PlatformIntegrationName, state.PlatformIntegrationEnabled.Value ? 1 : 0, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue(PlatformIntegrationName, false);
                }

                if (state.Entries.Count == 0)
                {
                    key.DeleteValue(CertificatesWithConstraintsName, false);
                }
                else
                {
                    key.SetValue(CertificatesWithConstraintsName, SerializeEntries(state.Entries), RegistryValueKind.String);
                }
            }
        }

        public string SerializeEntries(IList<CertificatePolicyEntry> entries)
        {
            ArrayList items = new ArrayList();
            foreach (CertificatePolicyEntry entry in entries)
            {
                Dictionary<string, object> constraints = new Dictionary<string, object>();
                if (entry.DnsNames.Count > 0)
                {
                    constraints["permitted_dns_names"] = entry.DnsNames.ToArray();
                }
                if (entry.Cidrs.Count > 0)
                {
                    constraints["permitted_cidrs"] = entry.Cidrs.ToArray();
                }

                Dictionary<string, object> item = new Dictionary<string, object>();
                item["certificate"] = entry.CertificateBase64;
                item["constraints"] = constraints;
                items.Add(item);
            }

            return serializer.Serialize(items);
        }

        private List<CertificatePolicyEntry> DeserializeEntries(string json)
        {
            object parsed = serializer.DeserializeObject(json);
            object[] items = parsed as object[];
            if (items == null)
            {
                throw new InvalidDataException(UiText.Get(
                    "CACertificatesWithConstraints должна содержать JSON-массив.",
                    "CACertificatesWithConstraints must contain a JSON array."));
            }

            List<CertificatePolicyEntry> result = new List<CertificatePolicyEntry>();
            foreach (object rawItem in items)
            {
                IDictionary<string, object> item = rawItem as IDictionary<string, object>;
                if (item == null || !item.ContainsKey("certificate"))
                {
                    throw new InvalidDataException(UiText.Get(
                        "Элемент политики не содержит certificate.",
                        "A policy entry does not contain certificate."));
                }

                CertificatePolicyEntry entry = new CertificatePolicyEntry();
                entry.CertificateBase64 = Convert.ToString(item["certificate"], CultureInfo.InvariantCulture);

                object rawConstraints;
                if (item.TryGetValue("constraints", out rawConstraints))
                {
                    IDictionary<string, object> constraints = rawConstraints as IDictionary<string, object>;
                    if (constraints == null)
                    {
                        throw new InvalidDataException(UiText.Get(
                            "Поле constraints имеет неверный формат.",
                            "The constraints field has an invalid format."));
                    }

                    entry.DnsNames = ReadStringList(constraints, "permitted_dns_names");
                    entry.Cidrs = ReadStringList(constraints, "permitted_cidrs");
                }

                result.Add(entry);
            }

            return result;
        }

        private static List<string> ReadStringList(IDictionary<string, object> dictionary, string name)
        {
            object value;
            if (!dictionary.TryGetValue(name, out value))
            {
                return new List<string>();
            }

            object[] values = value as object[];
            if (values == null)
            {
                throw new InvalidDataException(UiText.Get("Поле ", "The ") + name + UiText.Get(
                    " должно содержать массив строк.",
                    " field must contain an array of strings."));
            }

            return values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)).ToList();
        }
    }

    internal static class ElevatedPolicyWriter
    {
        public static bool Save(string registryPath, PolicyState state, PolicyStore store)
        {
            SecurityIdentifier userSid = WindowsIdentity.GetCurrent().User;
            if (userSid == null)
            {
                throw new InvalidOperationException(UiText.Get(
                    "Не удалось определить SID текущего пользователя.",
                    "Could not determine the current user's SID."));
            }

            string temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "ChromeCertificatePolicyManager-" + Guid.NewGuid().ToString("N") + ".reg");
            try
            {
                string content = BuildRegistryFile(userSid.Value, registryPath, state, store);
                File.WriteAllText(temporaryPath, content, Encoding.Unicode);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe"),
                    Arguments = "import \"" + temporaryPath + "\" /reg:64",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            throw new InvalidOperationException(UiText.Get(
                                "Не удалось запустить импорт реестра.",
                                "Could not start the registry import."));
                        }
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            throw new InvalidOperationException(UiText.Get(
                                "Импорт реестра завершился с кодом ",
                                "Registry import exited with code ") +
                                process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
                        }
                    }
                }
                catch (Win32Exception ex)
                {
                    const int OperationCanceled = 1223;
                    if (ex.NativeErrorCode == OperationCanceled)
                    {
                        return false;
                    }
                    throw;
                }

                return true;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }

        internal static string BuildRegistryFile(string userSid, string registryPath, PolicyState state, PolicyStore store)
        {
            if (!Regex.IsMatch(userSid ?? String.Empty, @"^S-\d+(?:-\d+)+$") ||
                String.IsNullOrWhiteSpace(registryPath) ||
                registryPath.IndexOfAny(new[] { '\r', '\n', '[', ']' }) >= 0)
            {
                throw new InvalidDataException("Invalid registry target.");
            }

            StringBuilder content = new StringBuilder();
            content.AppendLine("Windows Registry Editor Version 5.00");
            content.AppendLine();
            content.AppendLine("[HKEY_USERS\\" + userSid + "\\" + registryPath + "]");
            if (state.PlatformIntegrationEnabled.HasValue)
            {
                content.AppendLine("\"" + PolicyStore.PlatformIntegrationName + "\"=dword:" +
                    (state.PlatformIntegrationEnabled.Value ? "00000001" : "00000000"));
            }
            else
            {
                content.AppendLine("\"" + PolicyStore.PlatformIntegrationName + "\"=-");
            }

            if (state.Entries.Count == 0)
            {
                content.AppendLine("\"" + PolicyStore.CertificatesWithConstraintsName + "\"=-");
            }
            else
            {
                string json = store.SerializeEntries(state.Entries);
                content.AppendLine("\"" + PolicyStore.CertificatesWithConstraintsName + "\"=\"" +
                    EscapeRegistryString(json) + "\"");
            }
            content.AppendLine();
            return content.ToString();
        }

        private static string EscapeRegistryString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    internal static class PolicyBackup
    {
        private const int CurrentFormatVersion = 1;

        private sealed class BackupDocument
        {
            public int FormatVersion { get; set; }
            public string CreatedAtUtc { get; set; }
            public PolicyState Policy { get; set; }
        }

        public static void Save(string path, PolicyState state)
        {
            BackupDocument document = new BackupDocument
            {
                FormatVersion = CurrentFormatVersion,
                CreatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Policy = state
            };

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            File.WriteAllText(path, serializer.Serialize(document), new UTF8Encoding(false));
        }

        public static PolicyState Load(string path)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            BackupDocument document = serializer.Deserialize<BackupDocument>(File.ReadAllText(path, Encoding.UTF8));
            if (document == null || document.FormatVersion != CurrentFormatVersion || document.Policy == null)
            {
                throw new InvalidDataException(UiText.Get(
                    "Неподдерживаемый формат резервной копии.",
                    "Unsupported policy backup format."));
            }

            if (document.Policy.Entries == null)
            {
                document.Policy.Entries = new List<CertificatePolicyEntry>();
            }

            return document.Policy;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings;
        private readonly string testRegistryPath;
        private readonly ComboBox browserSelector = new ComboBox();
        private readonly ComboBox languageSelector = new ComboBox();
        private readonly Label browserLabel = new Label();
        private readonly Label languageLabel = new Label();
        private readonly Label description = new Label();
        private readonly CheckBox isolation = new CheckBox();
        private readonly Panel certificateEditor = new Panel();
        private readonly DataGridView grid = new DataGridView();
        private readonly BindingSource binding = new BindingSource();
        private readonly List<CertificatePolicyEntry> entries = new List<CertificatePolicyEntry>();
        private readonly Label policyStatus = new Label();
        private readonly Button addButton;
        private readonly Button removeButton;
        private readonly Button importButton;
        private readonly Button exportButton;
        private readonly Button applyButton;
        private readonly Button cancelButton;
        private readonly DataGridViewTextBoxColumn nameColumn = new DataGridViewTextBoxColumn();
        private readonly DataGridViewTextBoxColumn thumbprintColumn = new DataGridViewTextBoxColumn();
        private readonly DataGridViewTextBoxColumn domainsColumn = new DataGridViewTextBoxColumn();
        private readonly DataGridViewTextBoxColumn expiresColumn = new DataGridViewTextBoxColumn();
        private BrowserDefinition browser;
        private PolicyStore store;
        private bool updatingUi;
        private string baselineSignature;
        private bool machinePoliciesDetected;

        public MainForm(AppSettings settings, string testRegistryPath)
        {
            this.settings = settings;
            this.testRegistryPath = testRegistryPath;
            browser = BrowserDefinition.Find(settings.BrowserId) ?? BrowserDefinition.All[0];
            addButton = CreateButton(AddCertificate);
            removeButton = CreateButton(RemoveCertificate);
            importButton = CreateButton(ImportPolicies);
            exportButton = CreateButton(ExportPolicies);
            applyButton = CreateButton(ApplyPolicies);
            cancelButton = CreateButton(delegate { ReloadPolicies(); });
            InitializeUi();
            ReloadPolicies();
        }

        private void InitializeUi()
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = UiScale.SizeFor(this, 850, 520);
            Size initialSize = UiScale.SizeFor(this, settings.WindowWidth, settings.WindowHeight);
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(
                Math.Min(initialSize.Width, workingArea.Width),
                Math.Min(initialSize.Height, workingArea.Height));
            Font = new Font("Segoe UI", 10F);
            FormClosing += MainFormClosing;
            Shown += MainFormShown;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            TableLayoutPanel context = new TableLayoutPanel();
            context.Dock = DockStyle.Fill;
            context.AutoSize = true;
            context.ColumnCount = 4;
            context.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            context.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            context.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            context.Padding = new Padding(0, 0, 0, 10);
            layout.Controls.Add(context, 0, 0);

            browserLabel.AutoSize = true;
            browserLabel.Anchor = AnchorStyles.Left;
            browserLabel.Margin = new Padding(0, 0, 8, 0);
            context.Controls.Add(browserLabel, 0, 0);

            browserSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            browserSelector.Width = 220;
            browserSelector.Items.AddRange(BrowserDefinition.All);
            browserSelector.SelectedItem = browser;
            browserSelector.SelectedIndexChanged += BrowserChanged;
            context.Controls.Add(browserSelector, 1, 0);

            languageLabel.AutoSize = true;
            languageLabel.Anchor = AnchorStyles.Left;
            languageLabel.Margin = new Padding(18, 0, 8, 0);
            context.Controls.Add(languageLabel, 2, 0);

            languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            languageSelector.Items.AddRange(new object[] { "Русский", "English" });
            languageSelector.SelectedIndex = UiText.IsRussian ? 0 : 1;
            languageSelector.SelectedIndexChanged += LanguageChanged;
            context.Controls.Add(languageSelector, 3, 0);

            description.AutoSize = true;
            description.MaximumSize = new Size(930, 0);
            description.ForeColor = SystemColors.GrayText;
            description.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(description, 0, 1);

            isolation.AutoSize = true;
            isolation.Font = new Font(Font, FontStyle.Bold);
            isolation.Margin = new Padding(0, 0, 0, 12);
            isolation.CheckedChanged += IsolationChanged;
            layout.Controls.Add(isolation, 0, 2);

            certificateEditor.Dock = DockStyle.Fill;
            layout.Controls.Add(certificateEditor, 0, 3);
            TableLayoutPanel editorLayout = new TableLayoutPanel();
            editorLayout.Dock = DockStyle.Fill;
            editorLayout.ColumnCount = 1;
            editorLayout.RowCount = 2;
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            certificateEditor.Controls.Add(editorLayout);

            grid.Dock = DockStyle.Fill;
            grid.Font = new Font("Segoe UI", 9F);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoGenerateColumns = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.MultiSelect = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.CellDoubleClick += GridCellDoubleClick;
            grid.SelectionChanged += delegate { UpdateButtonStates(); };
            nameColumn.DataPropertyName = "Name";
            nameColumn.Width = 230;
            thumbprintColumn.DataPropertyName = "Thumbprint";
            thumbprintColumn.Width = 270;
            domainsColumn.DataPropertyName = "DnsNames";
            domainsColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            domainsColumn.DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True };
            expiresColumn.DataPropertyName = "NotAfter";
            expiresColumn.MinimumWidth = UiScale.ValueFor(this, 90);
            expiresColumn.Width = UiScale.ValueFor(this, 120);
            expiresColumn.Resizable = DataGridViewTriState.True;
            grid.Columns.AddRange(nameColumn, thumbprintColumn, domainsColumn, expiresColumn);
            editorLayout.Controls.Add(grid, 0, 0);

            FlowLayoutPanel entryButtons = new FlowLayoutPanel();
            entryButtons.AutoSize = true;
            entryButtons.Margin = new Padding(0, 10, 0, 6);
            entryButtons.WrapContents = false;
            entryButtons.Controls.Add(addButton);
            entryButtons.Controls.Add(removeButton);
            entryButtons.Controls.Add(importButton);
            entryButtons.Controls.Add(exportButton);
            editorLayout.Controls.Add(entryButtons, 0, 1);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            policyStatus.AutoSize = true;
            policyStatus.Anchor = AnchorStyles.Left;
            policyStatus.ForeColor = SystemColors.GrayText;
            footer.Controls.Add(policyStatus, 0, 0);
            FlowLayoutPanel actionButtons = new FlowLayoutPanel();
            actionButtons.AutoSize = true;
            actionButtons.WrapContents = false;
            actionButtons.Controls.Add(applyButton);
            actionButtons.Controls.Add(cancelButton);
            applyButton.Font = new Font(applyButton.Font, FontStyle.Bold);
            footer.Controls.Add(actionButtons, 1, 0);
            layout.Controls.Add(footer, 0, 4);

            UpdateUiText();
        }

        private static Button CreateButton(EventHandler handler)
        {
            Button button = new Button();
            button.AutoSize = true;
            button.Click += handler;
            return button;
        }

        private void UpdateUiText()
        {
            Text = Program.WindowTitle + (IsDirty() ? " *" : String.Empty);
            browserLabel.Text = UiText.Get("Браузер:", "Browser:");
            languageLabel.Text = UiText.Get("Язык:", "Language:");
            description.Text = UiText.Get(
                "Политики применяются к текущему пользователю. Встроенное хранилище корневых сертификатов браузера остаётся включённым.",
                "Policies apply to the current user. The browser's built-in root certificate store remains enabled.");
            isolation.Text = UiText.Get("Изолировать браузерное хранилище сертификатов", "Isolate the browser certificate store");
            nameColumn.HeaderText = UiText.Get("Сертификат", "Certificate");
            thumbprintColumn.HeaderText = "SHA-1";
            domainsColumn.HeaderText = UiText.Get("Разрешённые домены", "Allowed domains");
            expiresColumn.HeaderText = UiText.Get("Действует до", "Valid until");
            addButton.Text = UiText.Get("Добавить сертификат…", "Add cert…");
            removeButton.Text = UiText.Get("Удалить сертификат", "Remove cert");
            importButton.Text = UiText.Get("Импорт…", "Import…");
            exportButton.Text = UiText.Get("Экспорт…", "Export…");
            applyButton.Text = UiText.Get("Применить", "Apply");
            cancelButton.Text = UiText.Get("Отмена", "Cancel");
            UiScale.EqualizeButtonSizes(applyButton, cancelButton);
            UpdatePolicyStatus();
        }

        private void BrowserChanged(object sender, EventArgs e)
        {
            if (updatingUi)
            {
                return;
            }

            BrowserDefinition selected = browserSelector.SelectedItem as BrowserDefinition;
            if (selected == null || selected == browser)
            {
                return;
            }
            if (IsDirty())
            {
                MessageBox.Show(
                    UiText.Get("Сначала примените или отмените изменения.", "Apply or cancel the changes first."),
                    UiText.Get("Есть неприменённые изменения", "Unapplied changes"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                updatingUi = true;
                browserSelector.SelectedItem = browser;
                updatingUi = false;
                return;
            }

            browser = selected;
            settings.BrowserId = browser.Id;
            settings.Save();
            ReloadPolicies();
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            if (updatingUi || languageSelector.SelectedIndex < 0)
            {
                return;
            }

            settings.Language = languageSelector.SelectedIndex == 0 ? "ru" : "en";
            UiText.Language = settings.Language;
            settings.Save();
            UpdateUiText();
        }

        private void IsolationChanged(object sender, EventArgs e)
        {
            if (updatingUi)
            {
                return;
            }

            if (!isolation.Checked && entries.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    UiText.Get(
                        "Отключение изоляции удалит все добавленные сертификаты при применении изменений. Продолжить?",
                        "Turning off isolation will remove all added certificates when the changes are applied. Continue?"),
                    UiText.Get("Удаление сертификатов", "Remove certificates"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    updatingUi = true;
                    isolation.Checked = true;
                    updatingUi = false;
                    return;
                }

                entries.Clear();
                RefreshGrid();
            }

            certificateEditor.Enabled = isolation.Checked;
            UpdateButtonStates();
        }

        private void ReloadPolicies()
        {
            try
            {
                string registryPath = testRegistryPath ?? browser.RegistryPath;
                store = new PolicyStore(Registry.CurrentUser, registryPath);
                PolicyState state = store.Load();
                updatingUi = true;
                isolation.Checked = state.PlatformIntegrationEnabled == false;
                entries.Clear();
                entries.AddRange(state.Entries);
                RefreshGrid();
                certificateEditor.Enabled = isolation.Checked;
                updatingUi = false;
                baselineSignature = CaptureUiSignature();
                WarnAboutMachinePolicies();
                UpdateButtonStates();
                UpdateUiText();
            }
            catch (Exception ex)
            {
                updatingUi = false;
                ShowError(UiText.Get("Не удалось прочитать политики:\n\n", "Could not read policies:\n\n") + ex.Message);
            }
        }

        private void WarnAboutMachinePolicies()
        {
            machinePoliciesDetected = false;
            policyStatus.ForeColor = SystemColors.GrayText;
            if (testRegistryPath != null)
            {
                policyStatus.Text = "HKCU\\" + testRegistryPath + " · " + UiText.Get("тестовый раздел", "test key");
                return;
            }

            using (RegistryKey machine = Registry.LocalMachine.OpenSubKey(browser.RegistryPath, false))
            {
                if (machine != null &&
                    (machine.GetValue(PolicyStore.PlatformIntegrationName, null) != null ||
                     machine.GetValue(PolicyStore.CertificatesWithConstraintsName, null) != null))
                {
                    machinePoliciesDetected = true;
                }
            }
        }

        private void UpdatePolicyStatus()
        {
            if (store == null)
            {
                return;
            }

            string registryPath = testRegistryPath ?? browser.RegistryPath;
            string text = "HKCU\\" + registryPath;
            bool legacyCidrs = entries.Any(entry => entry.Cidrs != null && entry.Cidrs.Count > 0);
            if (legacyCidrs)
            {
                text += UiText.Get(" · Обнаружены устаревшие CIDR-ограничения", " · Legacy CIDR constraints detected");
            }
            if (machinePoliciesDetected)
            {
                text += UiText.Get(" · Обнаружены политики уровня компьютера", " · Machine-level policies detected");
            }
            policyStatus.ForeColor = legacyCidrs || machinePoliciesDetected ? Color.DarkOrange : SystemColors.GrayText;
            policyStatus.Text = text;
        }

        private void RefreshGrid()
        {
            List<GridRow> rows = new List<GridRow>();
            foreach (CertificatePolicyEntry entry in entries)
            {
                using (X509Certificate2 certificate = entry.GetCertificate())
                {
                    rows.Add(new GridRow
                    {
                        Name = GetCertificateName(certificate),
                        Thumbprint = certificate.Thumbprint,
                        DnsNames = String.Join(", ", entry.DnsNames),
                        NotAfter = certificate.NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Entry = entry
                    });
                }
            }

            binding.DataSource = rows;
            grid.DataSource = binding;
            UpdateButtonStates();
        }

        private void AddCertificate(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = UiText.Get(
                    "Сертификаты (*.cer;*.crt;*.der)|*.cer;*.crt;*.der|Все файлы (*.*)|*.*",
                    "Certificates (*.cer;*.crt;*.der)|*.cer;*.crt;*.der|All files (*.*)|*.*");
                dialog.Title = UiText.Get("Выберите корневой сертификат", "Select a root certificate");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    using (X509Certificate2 certificate = new X509Certificate2(dialog.FileName))
                    {
                        EnsureCertificateAuthority(certificate);
                        if (ContainsCertificate(certificate.Thumbprint))
                        {
                            MessageBox.Show(
                                UiText.Get("Этот сертификат уже добавлен.", "This certificate has already been added."),
                                UiText.Get("Сертификат", "Certificate"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            return;
                        }

                        entries.Add(new CertificatePolicyEntry
                        {
                            CertificateBase64 = Convert.ToBase64String(certificate.RawData),
                            DnsNames = new List<string>(),
                            Cidrs = new List<string>()
                        });
                    }

                    RefreshGrid();
                }
                catch (Exception ex)
                {
                    ShowError(UiText.Get("Не удалось добавить сертификат:\n\n", "Could not add the certificate:\n\n") + ex.Message);
                }
            }
        }

        private void GridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;
            EditDomains();
        }

        private void EditDomains()
        {
            GridRow row = SelectedRow(false);
            if (row == null)
            {
                return;
            }
            if (row.Entry.Cidrs != null && row.Entry.Cidrs.Count > 0)
            {
                MessageBox.Show(
                    UiText.Get(
                        "Эта запись содержит CIDR-ограничения из версии 1. Удалите их через JSON или версию 1 перед редактированием доменов.",
                        "This entry contains CIDR constraints from version 1. Remove them through JSON or version 1 before editing domains."),
                    UiText.Get("CIDR не поддерживается", "CIDR is not supported"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<string> domains;
            string issuerName;
            using (X509Certificate2 certificate = row.Entry.GetCertificate())
            {
                issuerName = GetIssuerOrganization(certificate);
                if (String.IsNullOrWhiteSpace(issuerName))
                {
                    issuerName = certificate.GetNameInfo(X509NameType.SimpleName, true);
                    if (String.IsNullOrWhiteSpace(issuerName))
                    {
                        issuerName = certificate.Issuer;
                    }
                }
            }
            if (ConstraintsDialog.TryEdit(this, row.Name, issuerName, row.Entry.DnsNames, out domains))
            {
                row.Entry.DnsNames = domains;
                RefreshGrid();
            }
        }

        private void RemoveCertificate(object sender, EventArgs e)
        {
            GridRow row = SelectedRow(true);
            if (row == null)
            {
                return;
            }

            if (MessageBox.Show(
                UiText.Get("Удалить сертификат «", "Remove certificate “") + row.Name + UiText.Get("»?", "”?"),
                UiText.Get("Подтверждение", "Confirmation"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                entries.Remove(row.Entry);
                RefreshGrid();
            }
        }

        private GridRow SelectedRow(bool showMessage)
        {
            if (grid.SelectedRows.Count == 0)
            {
                if (showMessage)
                {
                    MessageBox.Show(
                        UiText.Get("Выберите сертификат в списке.", "Select a certificate in the list."),
                        UiText.Get("Сертификат", "Certificate"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return null;
            }
            return grid.SelectedRows[0].DataBoundItem as GridRow;
        }

        private void ApplyPolicies(object sender, EventArgs e)
        {
            try
            {
                if (isolation.Checked)
                {
                    foreach (CertificatePolicyEntry entry in entries)
                    {
                        if (entry.Cidrs != null && entry.Cidrs.Count > 0)
                        {
                            throw new InvalidDataException(UiText.Get(
                                "CIDR-ограничения из версии 1 нельзя применить в версии 2.",
                                "CIDR constraints from version 1 cannot be applied in version 2."));
                        }
                        if (entry.DnsNames.Count == 0)
                        {
                            throw new InvalidDataException(UiText.Get(
                                "Для каждого сертификата требуется хотя бы один домен.",
                                "Each certificate requires at least one domain."));
                        }
                    }
                }

                PolicyState state = BuildStateForSave();
                try
                {
                    store.Save(state);
                }
                catch (UnauthorizedAccessException)
                {
                    string registryPath = testRegistryPath ?? browser.RegistryPath;
                    if (!ElevatedPolicyWriter.Save(registryPath, state, store))
                    {
                        return;
                    }
                }
                ReloadPolicies();
                PoliciesSavedDialog.ShowSaved(this, browser);
            }
            catch (UnauthorizedAccessException)
            {
                ShowError(UiText.Get(
                    "Нет прав на запись политики текущего пользователя.",
                    "There is no permission to write the current user's policy."));
            }
            catch (Exception ex)
            {
                ShowError(UiText.Get("Не удалось применить политики:\n\n", "Could not apply policies:\n\n") + ex.Message);
            }
        }

        private PolicyState BuildStateForSave()
        {
            PolicyState state = new PolicyState();
            if (isolation.Checked)
            {
                state.PlatformIntegrationEnabled = false;
                state.Entries.AddRange(entries);
            }
            return state;
        }

        private void ExportPolicies(object sender, EventArgs e)
        {
            try
            {
                ExportPolicyState(BuildStateForSave());
            }
            catch (Exception ex)
            {
                ShowError(UiText.Get("Не удалось экспортировать политики:\n\n", "Could not export policies:\n\n") + ex.Message);
            }
        }

        private void ImportPolicies(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = UiText.Get(
                    "Резервные копии политик (*.json)|*.json|Все файлы (*.*)|*.*",
                    "Policy backups (*.json)|*.json|All files (*.*)|*.*");
                dialog.Title = UiText.Get("Выберите резервную копию", "Select a policy backup");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    PolicyState state = PolicyBackup.Load(dialog.FileName);
                    ValidatePolicyState(state);
                    updatingUi = true;
                    isolation.Checked = state.PlatformIntegrationEnabled == false || state.Entries.Count > 0;
                    entries.Clear();
                    entries.AddRange(state.Entries);
                    RefreshGrid();
                    certificateEditor.Enabled = isolation.Checked;
                    updatingUi = false;
                    policyStatus.Text = UiText.Get(
                        "Резервная копия загружена. Проверьте настройки и нажмите «Применить».",
                        "The backup has been loaded. Review the settings and click Apply.");
                    policyStatus.ForeColor = Color.DarkGreen;
                    UpdateButtonStates();
                }
                catch (Exception ex)
                {
                    updatingUi = false;
                    ShowError(UiText.Get("Не удалось импортировать политики:\n\n", "Could not import policies:\n\n") + ex.Message);
                }
            }
        }

        private bool ExportPolicyState(PolicyState state)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.AddExtension = true;
                dialog.DefaultExt = "json";
                dialog.Filter = UiText.Get("Резервные копии политик (*.json)|*.json", "Policy backups (*.json)|*.json");
                dialog.FileName = browser.Name + "CertificatePolicy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
                dialog.Title = UiText.Get("Сохранить резервную копию", "Save policy backup");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                PolicyBackup.Save(dialog.FileName, state);
                MessageBox.Show(
                    UiText.Get("Резервная копия сохранена:\n\n", "The backup has been saved:\n\n") + dialog.FileName,
                    UiText.Get("Резервная копия", "Policy backup"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return true;
            }
        }

        private string CaptureUiSignature()
        {
            if (store == null)
            {
                return null;
            }
            return (isolation.Checked ? "1|" : "0|") + store.SerializeEntries(entries);
        }

        private bool IsDirty()
        {
            return baselineSignature != null && !String.Equals(baselineSignature, CaptureUiSignature(), StringComparison.Ordinal);
        }

        private void UpdateButtonStates()
        {
            bool dirty = IsDirty();
            applyButton.Enabled = dirty;
            cancelButton.Enabled = dirty;
            browserSelector.Enabled = !dirty && testRegistryPath == null;
            certificateEditor.Enabled = isolation.Checked;
            removeButton.Enabled = isolation.Checked && grid.SelectedRows.Count > 0;
            Text = Program.WindowTitle + (dirty ? " *" : String.Empty);
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsDirty() && MessageBox.Show(
                    UiText.Get("Закрыть программу и отбросить неприменённые изменения?", "Close the application and discard unapplied changes?"),
                    UiText.Get("Есть неприменённые изменения", "Unapplied changes"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.WindowWidth = Math.Max(850, UiScale.LogicalValueFor(this, bounds.Width));
            settings.WindowHeight = Math.Max(520, UiScale.LogicalValueFor(this, bounds.Height));
            settings.Save();
        }

        private void MainFormShown(object sender, EventArgs e)
        {
            CenterToScreen();
            int currentDomainsWidth = domainsColumn.Width;
            domainsColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            domainsColumn.Width = currentDomainsWidth;
            expiresColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        private static void ValidatePolicyState(PolicyState state)
        {
            foreach (CertificatePolicyEntry entry in state.Entries)
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.CertificateBase64))
                {
                    throw new InvalidDataException(UiText.Get(
                        "Резервная копия содержит пустой сертификат.",
                        "The backup contains an empty certificate."));
                }

                entry.DnsNames = entry.DnsNames ?? new List<string>();
                entry.Cidrs = entry.Cidrs ?? new List<string>();
                entry.DnsNames = ConstraintsDialog.ParseDomains(String.Join(Environment.NewLine, entry.DnsNames));
                entry.Cidrs = ConstraintsDialog.ParseCidrs(String.Join(Environment.NewLine, entry.Cidrs));
                using (X509Certificate2 certificate = entry.GetCertificate())
                {
                    EnsureCertificateAuthority(certificate);
                }
            }
        }

        private static void EnsureCertificateAuthority(X509Certificate2 certificate)
        {
            X509BasicConstraintsExtension constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (constraints == null || !constraints.CertificateAuthority)
            {
                throw new InvalidDataException(UiText.Get(
                    "Выбранный сертификат не является сертификатом центра сертификации (CA).",
                    "The selected certificate is not a certificate authority (CA) certificate."));
            }
        }

        private bool ContainsCertificate(string thumbprint)
        {
            foreach (CertificatePolicyEntry entry in entries)
            {
                using (X509Certificate2 certificate = entry.GetCertificate())
                {
                    if (String.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string GetCertificateName(X509Certificate2 certificate)
        {
            string name = certificate.GetNameInfo(X509NameType.SimpleName, false);
            return String.IsNullOrWhiteSpace(name) ? certificate.Subject : name;
        }

        private static string GetIssuerOrganization(X509Certificate2 certificate)
        {
            return GetOrganizationFromFormattedName(certificate.IssuerName.Format(true));
        }

        internal static string GetOrganizationFromFormattedName(string formattedName)
        {
            foreach (string line in Regex.Split(formattedName ?? String.Empty, @"\r?\n"))
            {
                Match match = Regex.Match(line, @"^\s*O\s*=\s*(.*?)\s*$", RegexOptions.IgnoreCase);
                if (match.Success && match.Groups[1].Value.Length > 0)
                {
                    return match.Groups[1].Value;
                }
            }
            return null;
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, UiText.Get("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private sealed class GridRow
        {
            public string Name { get; set; }
            public string Thumbprint { get; set; }
            public string DnsNames { get; set; }
            public string NotAfter { get; set; }
            public CertificatePolicyEntry Entry { get; set; }
        }
    }

    internal sealed class PoliciesSavedDialog : Form
    {
        private readonly BrowserDefinition browser;
        private readonly Label refreshStatus = new Label();
        private readonly LinkLabel policyLink = new LinkLabel();

        private PoliciesSavedDialog(BrowserDefinition browser)
        {
            this.browser = browser;
            Text = UiText.Get("Изменения применены", "Changes applied");
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            Font = new Font("Segoe UI", 10F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.Dock = DockStyle.Fill;
            Controls.Add(layout);

            Label saved = new Label();
            saved.AutoSize = true;
            saved.Text = UiText.Get(
                "Изменения применены. Браузеру отправлен сигнал об обновлении политик.",
                "The changes have been applied. A policy refresh signal was sent to the browser.");
            saved.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(saved, 0, 0);

            refreshStatus.AutoSize = true;
            refreshStatus.ForeColor = Color.ForestGreen;
            refreshStatus.Font = new Font(Font, FontStyle.Bold);
            refreshStatus.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(refreshStatus, 0, 1);

            Label verification = new Label();
            verification.AutoSize = true;
            verification.Text = UiText.Get(
                "Для ручной проверки скопируйте адрес и вставьте его в браузер:",
                "For manual verification, copy the address and paste it into the browser:");
            verification.Margin = new Padding(0, 0, 0, 6);
            layout.Controls.Add(verification, 0, 2);

            policyLink.AutoSize = true;
            policyLink.Text = UiText.Get("Скопировать ", "Copy ") + browser.PolicyUrl;
            policyLink.LinkClicked += CopyPolicyUrl;
            policyLink.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(policyLink, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0);
            layout.Controls.Add(buttons, 0, 4);

            Button close = new Button();
            close.AutoSize = true;
            close.DialogResult = DialogResult.OK;
            close.Text = UiText.Get("Закрыть", "Close");
            buttons.Controls.Add(close);
            AcceptButton = close;
            Shown += delegate { RefreshPolicies(); };
        }

        public static void ShowSaved(IWin32Window owner, BrowserDefinition browser)
        {
            using (PoliciesSavedDialog dialog = new PoliciesSavedDialog(browser))
            {
                dialog.ShowDialog(owner);
            }
        }

        private void RefreshPolicies()
        {
            object shell = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    throw new InvalidOperationException(UiText.Get(
                        "Компонент Windows Shell недоступен.",
                        "Windows Shell is unavailable."));
                }

                shell = Activator.CreateInstance(shellType);
                shellType.InvokeMember(
                    "ShellExecute",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { browser.ResolveExecutable(), "--refresh-platform-policy", String.Empty, "open", 1 });
                refreshStatus.Text = UiText.Get("Сигнал отправлен: ", "Signal sent: ") + browser.Name;
            }
            catch (Exception ex)
            {
                refreshStatus.ForeColor = Color.DarkRed;
                refreshStatus.Text = UiText.Get(
                    "Изменения сохранены, но сигнал обновления отправить не удалось: ",
                    "The changes were saved, but the refresh signal could not be sent: ") + ex.Message;
            }
            finally
            {
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        private void CopyPolicyUrl(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Clipboard.SetText(browser.PolicyUrl);
            policyLink.Text = UiText.Get("Скопировано: ", "Copied: ") + browser.PolicyUrl;
        }
    }

    internal sealed class ConstraintsDialog : Form
    {
        private readonly TextBox domains = new TextBox();
        private readonly CheckBox includeSubdomains = new CheckBox();
        private bool updatingSubdomainMode;

        private ConstraintsDialog(string certificateName, string issuerName, IEnumerable<string> currentDomains)
        {
            Text = UiText.Get("Разрешённые домены", "Allowed domains");
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = UiScale.SizeFor(this, 520, 360);
            Size = UiScale.SizeFor(this, 640, 440);
            Font = new Font("Segoe UI", 10F);
            Shown += delegate { CenterToParent(); };

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            TableLayoutPanel certificateInfo = new TableLayoutPanel();
            certificateInfo.AutoSize = true;
            certificateInfo.ColumnCount = 1;
            certificateInfo.RowCount = 2;
            certificateInfo.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(certificateInfo, 0, 0);

            Label certificate = new Label();
            certificate.AutoSize = true;
            certificate.Font = new Font(Font, FontStyle.Bold);
            certificate.Text = certificateName;
            certificate.Margin = new Padding(0);
            certificateInfo.Controls.Add(certificate, 0, 0);

            Label issuer = new Label();
            issuer.AutoSize = true;
            issuer.ForeColor = SystemColors.GrayText;
            issuer.Text = UiText.Get("Выдан: ", "Issued by: ") + issuerName;
            issuer.Margin = new Padding(0, 3, 0, 0);
            certificateInfo.Controls.Add(issuer, 0, 1);

            Label dnsHint = new Label();
            dnsHint.AutoSize = true;
            dnsHint.Text = UiText.Get(
                "Домены — по одному на строку, без схемы, порта и пути.",
                "Enter one domain per line, without a scheme, port, or path.");
            dnsHint.Margin = new Padding(0, 0, 0, 6);
            layout.Controls.Add(dnsHint, 0, 1);

            ConfigureMultilineTextBox(domains);
            domains.Text = String.Join(Environment.NewLine, PrepareDisplayDomains(currentDomains));
            domains.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(domains, 0, 2);

            includeSubdomains.AutoSize = true;
            includeSubdomains.Checked = true;
            includeSubdomains.Text = UiText.Get("Автоматически включать поддомены", "Automatically include subdomains");
            includeSubdomains.Margin = new Padding(0, 0, 0, 12);
            includeSubdomains.CheckedChanged += IncludeSubdomainsChanged;
            layout.Controls.Add(includeSubdomains, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);
            layout.Controls.Add(buttons, 0, 4);

            Button cancel = new Button();
            cancel.AutoSize = true;
            cancel.Text = UiText.Get("Отмена", "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            Button ok = new Button();
            ok.AutoSize = true;
            ok.Text = "OK";
            ok.Click += ValidateAndClose;
            buttons.Controls.Add(ok);

            UiScale.EqualizeButtonSizes(ok, cancel);

            CancelButton = cancel;
        }

        public List<string> ResultDomains { get; private set; }

        public static bool TryEdit(
            IWin32Window owner,
            string certificateName,
            string issuerName,
            IEnumerable<string> currentDomains,
            out List<string> resultDomains)
        {
            using (ConstraintsDialog dialog = new ConstraintsDialog(certificateName, issuerName, currentDomains))
            {
                bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
                resultDomains = accepted ? dialog.ResultDomains : null;
                return accepted;
            }
        }

        private static void ConfigureMultilineTextBox(TextBox textBox)
        {
            textBox.AcceptsReturn = true;
            textBox.Dock = DockStyle.Fill;
            textBox.Multiline = true;
            textBox.ScrollBars = ScrollBars.Vertical;
        }

        private void ValidateAndClose(object sender, EventArgs e)
        {
            try
            {
                ResultDomains = ParseDomains(domains.Text);
                if (includeSubdomains.Checked)
                {
                    ResultDomains = ExpandSubdomains(ResultDomains);
                }
                if (ResultDomains.Count == 0)
                {
                    throw new InvalidDataException(UiText.Get("Укажите хотя бы один домен.", "Enter at least one domain."));
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    UiText.Get("Неверный домен", "Invalid domain"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void IncludeSubdomainsChanged(object sender, EventArgs e)
        {
            if (updatingSubdomainMode)
            {
                return;
            }

            try
            {
                List<string> parsed = ParseDomains(domains.Text);
                List<string> displayed = includeSubdomains.Checked
                    ? CollapseSubdomainPairs(parsed)
                    : ExpandSubdomains(parsed);
                domains.Text = String.Join(Environment.NewLine, displayed);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    UiText.Get("Неверный домен", "Invalid domain"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                updatingSubdomainMode = true;
                includeSubdomains.Checked = !includeSubdomains.Checked;
                updatingSubdomainMode = false;
            }
        }

        private static List<string> PrepareDisplayDomains(IEnumerable<string> currentDomains)
        {
            return CollapseSubdomainPairs(currentDomains ?? Enumerable.Empty<string>());
        }

        internal static List<string> CollapseSubdomainPairs(IEnumerable<string> domains)
        {
            List<string> source = domains.ToList();
            List<string> result = new List<string>();
            foreach (string domain in source)
            {
                if (domain.StartsWith(".", StringComparison.Ordinal) &&
                    source.Contains(domain.Substring(1), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                result.Add(domain);
            }
            return result;
        }

        internal static List<string> ExpandSubdomains(IEnumerable<string> domains)
        {
            List<string> result = new List<string>();
            foreach (string domain in domains)
            {
                if (!result.Contains(domain, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(domain);
                }
                if (!domain.StartsWith(".", StringComparison.Ordinal))
                {
                    string subdomain = "." + domain;
                    if (!result.Contains(subdomain, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(subdomain);
                    }
                }
            }
            return result;
        }

        internal static List<string> ParseDomains(string text)
        {
            string[] parts = SplitValues(text);
            IdnMapping idn = new IdnMapping();
            List<string> result = new List<string>();
            foreach (string raw in parts)
            {
                if (raw.Contains("://") || raw.Contains("/") || raw.Contains(":") || raw.Contains("*"))
                {
                    throw new InvalidDataException(UiText.Get("Недопустимый домен: ", "Invalid domain: ") + raw);
                }

                bool leadingDot = raw.StartsWith(".", StringComparison.Ordinal);
                string value = leadingDot ? raw.Substring(1) : raw;
                value = value.TrimEnd('.');
                string ascii = idn.GetAscii(value).ToLowerInvariant();

                if (ascii.Length == 0 || ascii.Length > 253 || ascii.Split('.').Any(label => label.Length == 0 || label.Length > 63 || label.StartsWith("-", StringComparison.Ordinal) || label.EndsWith("-", StringComparison.Ordinal) || !Regex.IsMatch(label, @"^[a-z0-9-]+$")))
                {
                    throw new InvalidDataException(UiText.Get("Недопустимый домен: ", "Invalid domain: ") + raw);
                }

                string normalized = leadingDot ? "." + ascii : ascii;
                if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        internal static List<string> ParseCidrs(string text)
        {
            List<string> result = new List<string>();
            foreach (string raw in SplitValues(text))
            {
                string[] parts = raw.Split('/');
                int prefixLength;
                System.Net.IPAddress address;
                if (parts.Length != 2 || raw.Contains("%") ||
                    !System.Net.IPAddress.TryParse(parts[0], out address) ||
                    !Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength))
                {
                    throw new InvalidDataException(UiText.Get("Недопустимая CIDR-сеть: ", "Invalid CIDR network: ") + raw);
                }

                byte[] bytes = address.GetAddressBytes();
                int maximumPrefixLength = bytes.Length * 8;
                if (prefixLength < 0 || prefixLength > maximumPrefixLength)
                {
                    throw new InvalidDataException(UiText.Get("Недопустимая длина префикса CIDR: ", "Invalid CIDR prefix length: ") + raw);
                }

                int wholeBytes = prefixLength / 8;
                int remainingBits = prefixLength % 8;
                if (remainingBits > 0)
                {
                    bytes[wholeBytes] = (byte)(bytes[wholeBytes] & (0xFF << (8 - remainingBits)));
                    wholeBytes++;
                }
                for (int i = wholeBytes; i < bytes.Length; i++)
                {
                    bytes[i] = 0;
                }

                string normalized = new System.Net.IPAddress(bytes) + "/" + prefixLength.ToString(CultureInfo.InvariantCulture);
                if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        private static string[] SplitValues(string text)
        {
            return Regex.Split(text ?? String.Empty, @"[\r\n,;]+")
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
        }
    }

    internal static class SelfTest
    {
        public static int Run(string registryPath)
        {
            string testPath = registryPath + "\\SelfTest_" + Guid.NewGuid().ToString("N");
            string backupPath = Path.Combine(Path.GetTempPath(), "ChromeCertificatePolicyManager-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                PolicyStore store = new PolicyStore(Registry.CurrentUser, testPath);
                CertificatePolicyEntry entry = new CertificatePolicyEntry
                {
                    CertificateBase64 = "AQID",
                    DnsNames = new List<string> { "gosuslugi.ru", ".gosuslugi.ru" },
                    Cidrs = new List<string> { "10.1.1.0/24" }
                };

                PolicyState input = new PolicyState();
                input.PlatformIntegrationEnabled = false;
                input.Entries.Add(entry);
                store.Save(input);

                PolicyState output = store.Load();
                if (output.PlatformIntegrationEnabled != false ||
                    output.Entries.Count != 1 ||
                    output.Entries[0].DnsNames.Count != 2 ||
                    output.Entries[0].Cidrs.Count != 1 ||
                    output.Entries[0].CertificateBase64 != "AQID")
                {
                    Console.Error.WriteLine("Self-test failed: stored policy differs from loaded policy.");
                    return 1;
                }

                string registryFile = ElevatedPolicyWriter.BuildRegistryFile(
                    "S-1-5-21-1-2-3-1001", testPath, input, store);
                if (!registryFile.Contains("[HKEY_USERS\\S-1-5-21-1-2-3-1001\\" + testPath + "]") ||
                    !registryFile.Contains("\"CAPlatformIntegrationEnabled\"=dword:00000000") ||
                    !registryFile.Contains("\\\"certificate\\\""))
                {
                    Console.Error.WriteLine("Self-test failed: registry import file generation error.");
                    return 1;
                }

                List<string> parsed = ConstraintsDialog.ParseDomains("GOSUSLUGI.RU\n.пример.рф");
                if (parsed.Count != 2 || parsed[0] != "gosuslugi.ru" || !parsed[1].StartsWith(".xn--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Self-test failed: DNS normalization error.");
                    return 1;
                }

                List<string> expanded = ConstraintsDialog.ExpandSubdomains(new[] { "example.ru" });
                if (expanded.Count != 2 || expanded[0] != "example.ru" || expanded[1] != ".example.ru")
                {
                    Console.Error.WriteLine("Self-test failed: subdomain expansion error.");
                    return 1;
                }

                List<string> collapsed = ConstraintsDialog.CollapseSubdomainPairs(expanded);
                if (collapsed.Count != 1 || collapsed[0] != "example.ru")
                {
                    Console.Error.WriteLine("Self-test failed: subdomain collapse error.");
                    return 1;
                }

                string issuerOrganization = MainForm.GetOrganizationFromFormattedName(
                    "C=RU\r\nO=The Ministry of Digital Development and Communications\r\nCN=Russian Trusted Root CA");
                if (issuerOrganization != "The Ministry of Digital Development and Communications")
                {
                    Console.Error.WriteLine("Self-test failed: issuer organization parsing error.");
                    return 1;
                }

                List<string> parsedCidrs = ConstraintsDialog.ParseCidrs("10.1.1.5/24\n2001:db8::1/64");
                if (parsedCidrs.Count != 2 || parsedCidrs[0] != "10.1.1.0/24" || parsedCidrs[1] != "2001:db8::/64")
                {
                    Console.Error.WriteLine("Self-test failed: CIDR normalization error.");
                    return 1;
                }

                PolicyBackup.Save(backupPath, input);
                PolicyState restored = PolicyBackup.Load(backupPath);
                if (restored.Entries.Count != 1 || restored.Entries[0].Cidrs.Count != 1)
                {
                    Console.Error.WriteLine("Self-test failed: backup round-trip error.");
                    return 1;
                }

                Console.WriteLine("Self-test passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Self-test failed: " + ex);
                return 1;
            }
            finally
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(testPath, false);
                }
                catch
                {
                }

                try
                {
                    File.Delete(backupPath);
                }
                catch
                {
                }
            }
        }
    }
}
