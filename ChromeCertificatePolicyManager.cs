using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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
            get { return "Политики сертификатов Chrome — " + Version; }
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
                    MessageBox.Show("Неизвестный параметр: " + args[i], "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
            }

            if (selfTest)
            {
                return SelfTest.Run(registryPath);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(new PolicyStore(Registry.CurrentUser, registryPath), registryPath));
            return 0;
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

        public void Reset()
        {
            using (RegistryKey key = root.OpenSubKey(path, true))
            {
                if (key == null)
                {
                    return;
                }

                key.DeleteValue(PlatformIntegrationName, false);
                key.DeleteValue(CertificatesWithConstraintsName, false);
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
                throw new InvalidDataException("CACertificatesWithConstraints должна содержать JSON-массив.");
            }

            List<CertificatePolicyEntry> result = new List<CertificatePolicyEntry>();
            foreach (object rawItem in items)
            {
                IDictionary<string, object> item = rawItem as IDictionary<string, object>;
                if (item == null || !item.ContainsKey("certificate"))
                {
                    throw new InvalidDataException("Элемент политики не содержит certificate.");
                }

                CertificatePolicyEntry entry = new CertificatePolicyEntry();
                entry.CertificateBase64 = Convert.ToString(item["certificate"], CultureInfo.InvariantCulture);

                object rawConstraints;
                if (item.TryGetValue("constraints", out rawConstraints))
                {
                    IDictionary<string, object> constraints = rawConstraints as IDictionary<string, object>;
                    if (constraints == null)
                    {
                        throw new InvalidDataException("Поле constraints имеет неверный формат.");
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
                throw new InvalidDataException("Поле " + name + " должно содержать массив строк.");
            }

            return values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)).ToList();
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
                throw new InvalidDataException("Неподдерживаемый формат резервной копии.");
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
        private readonly PolicyStore store;
        private readonly string registryPath;
        private readonly CheckBox platformIntegration = new CheckBox();
        private readonly Label policyStatus = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly BindingSource binding = new BindingSource();
        private readonly List<CertificatePolicyEntry> entries = new List<CertificatePolicyEntry>();

        public MainForm(PolicyStore store, string registryPath)
        {
            this.store = store;
            this.registryPath = registryPath;
            InitializeUi();
            ReloadPolicies();
        }

        private void InitializeUi()
        {
            Text = Program.WindowTitle;
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(850, 500);
            Size = new Size(980, 620);
            Font = new Font("Segoe UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12);
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            Label description = new Label();
            description.AutoSize = true;
            description.MaximumSize = new Size(920, 0);
            description.Text = "Настройки применяются к Chrome текущего пользователя. Публичный Chrome Root Store остаётся включённым.";
            layout.Controls.Add(description, 0, 0);

            platformIntegration.AutoSize = true;
            platformIntegration.Margin = new Padding(0, 12, 0, 12);
            platformIntegration.Text = "Доверять сертификатам, вручную добавленным в хранилища Windows, при проверке HTTPS";
            layout.Controls.Add(platformIntegration, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoGenerateColumns = false;
            grid.MultiSelect = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.CellDoubleClick += GridCellDoubleClick;

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Сертификат", DataPropertyName = "Name", Width = 220 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SHA-1", DataPropertyName = "Thumbprint", Width = 270 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Разрешённые DNS-имена", DataPropertyName = "DnsNames", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Разрешённые CIDR-сети", DataPropertyName = "Cidrs", Width = 170, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Действует до", DataPropertyName = "NotAfter", Width = 95 });
            layout.Controls.Add(grid, 0, 2);

            FlowLayoutPanel entryButtons = new FlowLayoutPanel();
            entryButtons.AutoSize = true;
            entryButtons.Margin = new Padding(0, 10, 0, 6);
            entryButtons.WrapContents = false;
            entryButtons.Controls.Add(CreateButton("Добавить сертификат…", AddCertificate));
            entryButtons.Controls.Add(CreateButton("Изменить ограничения…", EditConstraints));
            entryButtons.Controls.Add(CreateButton("Удалить", RemoveCertificate));
            entryButtons.Controls.Add(CreateButton("Экспорт…", ExportPolicies));
            entryButtons.Controls.Add(CreateButton("Импорт…", ImportPolicies));
            layout.Controls.Add(entryButtons, 0, 3);

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
            actionButtons.FlowDirection = FlowDirection.LeftToRight;
            actionButtons.WrapContents = false;
            actionButtons.Controls.Add(CreateButton("Перечитать", delegate { ReloadPolicies(); }));
            actionButtons.Controls.Add(CreateButton("Сбросить", ResetPolicies));
            Button saveButton = CreateButton("Сохранить", SavePolicies);
            saveButton.Font = new Font(saveButton.Font, FontStyle.Bold);
            actionButtons.Controls.Add(saveButton);
            footer.Controls.Add(actionButtons, 1, 0);
            layout.Controls.Add(footer, 0, 4);
        }

        private static Button CreateButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.AutoSize = true;
            button.Text = text;
            button.Click += handler;
            return button;
        }

        private void ReloadPolicies()
        {
            try
            {
                PolicyState state = store.Load();
                platformIntegration.Checked = !state.PlatformIntegrationEnabled.HasValue || state.PlatformIntegrationEnabled.Value;
                entries.Clear();
                entries.AddRange(state.Entries);
                RefreshGrid();

                string configured = state.PlatformIntegrationEnabled.HasValue ? "настроено" : "по умолчанию: доверять";
                policyStatus.Text = "HKCU\\" + registryPath + " · " + configured;
                WarnAboutMachinePolicies();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось прочитать политики:\n\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WarnAboutMachinePolicies()
        {
            if (!String.Equals(registryPath, Program.DefaultRegistryPath, StringComparison.OrdinalIgnoreCase))
            {
                Text = Program.WindowTitle + " — тестовый раздел";
                return;
            }

            using (RegistryKey machine = Registry.LocalMachine.OpenSubKey(Program.DefaultRegistryPath, false))
            {
                if (machine != null &&
                    (machine.GetValue(PolicyStore.PlatformIntegrationName, null) != null ||
                     machine.GetValue(PolicyStore.CertificatesWithConstraintsName, null) != null))
                {
                    policyStatus.Text += " · Внимание: обнаружены политики уровня компьютера";
                    policyStatus.ForeColor = Color.DarkOrange;
                }
                else
                {
                    policyStatus.ForeColor = SystemColors.GrayText;
                }
            }
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
                        Cidrs = String.Join(", ", entry.Cidrs),
                        NotAfter = certificate.NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Entry = entry
                    });
                }
            }

            binding.DataSource = rows;
            grid.DataSource = binding;
        }

        private void AddCertificate(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Сертификаты (*.cer;*.crt;*.der)|*.cer;*.crt;*.der|Все файлы (*.*)|*.*";
                dialog.Title = "Выберите корневой сертификат";
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
                            MessageBox.Show("Этот сертификат уже добавлен.", "Сертификат", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    MessageBox.Show("Не удалось добавить сертификат:\n\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            EditConstraints(sender, EventArgs.Empty);
        }

        private void EditConstraints(object sender, EventArgs e)
        {
            GridRow row = SelectedRow();
            if (row == null)
            {
                return;
            }

            List<string> domains;
            List<string> cidrs;
            if (ConstraintsDialog.TryEdit(this, row.Name, row.Entry.DnsNames, row.Entry.Cidrs, out domains, out cidrs))
            {
                row.Entry.DnsNames = domains;
                row.Entry.Cidrs = cidrs;
                RefreshGrid();
            }
        }

        private void RemoveCertificate(object sender, EventArgs e)
        {
            GridRow row = SelectedRow();
            if (row == null)
            {
                return;
            }

            if (MessageBox.Show("Удалить сертификат «" + row.Name + "» из политики Chrome?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                entries.Remove(row.Entry);
                RefreshGrid();
            }
        }

        private GridRow SelectedRow()
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите сертификат в списке.", "Сертификат", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            return grid.SelectedRows[0].DataBoundItem as GridRow;
        }

        private void SavePolicies(object sender, EventArgs e)
        {
            try
            {
                foreach (CertificatePolicyEntry entry in entries)
                {
                    if (entry.DnsNames.Count == 0 && entry.Cidrs.Count == 0)
                    {
                        throw new InvalidDataException("Для каждого сертификата требуется хотя бы одно DNS-имя или CIDR-ограничение.");
                    }
                }

                PolicyState state = new PolicyState();
                state.PlatformIntegrationEnabled = platformIntegration.Checked;
                state.Entries.AddRange(entries);

                PolicyState current = store.Load();
                if (!PolicyStatesEqual(current, state) && !OfferBackup(current, "сохранением новых настроек"))
                {
                    return;
                }

                store.Save(state);
                ReloadPolicies();
                PoliciesSavedDialog.ShowSaved(this);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Нет прав на запись политики текущего пользователя.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сохранить политики:\n\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetPolicies(object sender, EventArgs e)
        {
            if (MessageBox.Show("Удалить обе политики текущего пользователя и вернуть поведение Chrome по умолчанию?", "Сброс политик", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                PolicyState current = store.Load();
                if (!OfferBackup(current, "сбросом политик"))
                {
                    return;
                }

                store.Reset();
                ReloadPolicies();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сбросить политики:\n\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportPolicies(object sender, EventArgs e)
        {
            try
            {
                ExportPolicyState(store.Load());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось экспортировать политики:\n\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportPolicies(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Резервные копии политик (*.json)|*.json|Все файлы (*.*)|*.*";
                dialog.Title = "Выберите резервную копию";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    PolicyState state = PolicyBackup.Load(dialog.FileName);
                    ValidatePolicyState(state);
                    platformIntegration.Checked = !state.PlatformIntegrationEnabled.HasValue || state.PlatformIntegrationEnabled.Value;
                    entries.Clear();
                    entries.AddRange(state.Entries);
                    RefreshGrid();
                    policyStatus.Text = "Резервная копия загружена. Проверьте настройки и нажмите «Сохранить».";
                    policyStatus.ForeColor = Color.DarkGreen;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось импортировать политики:\n\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private bool OfferBackup(PolicyState current, string action)
        {
            if (!HasConfiguredPolicies(current))
            {
                return true;
            }

            DialogResult answer = MessageBox.Show(
                "Создать резервную копию текущих политик перед " + action + "?",
                "Резервная копия",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel)
            {
                return false;
            }

            return answer != DialogResult.Yes || ExportPolicyState(current);
        }

        private bool ExportPolicyState(PolicyState state)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.AddExtension = true;
                dialog.DefaultExt = "json";
                dialog.Filter = "Резервные копии политик (*.json)|*.json";
                dialog.FileName = "ChromeCertificatePolicy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
                dialog.Title = "Сохранить резервную копию";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                PolicyBackup.Save(dialog.FileName, state);
                MessageBox.Show("Резервная копия сохранена:\n\n" + dialog.FileName, "Резервная копия", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
        }

        private bool PolicyStatesEqual(PolicyState left, PolicyState right)
        {
            return left.PlatformIntegrationEnabled == right.PlatformIntegrationEnabled &&
                String.Equals(store.SerializeEntries(left.Entries), store.SerializeEntries(right.Entries), StringComparison.Ordinal);
        }

        private static bool HasConfiguredPolicies(PolicyState state)
        {
            return state.PlatformIntegrationEnabled.HasValue || state.Entries.Count > 0;
        }

        private static void ValidatePolicyState(PolicyState state)
        {
            foreach (CertificatePolicyEntry entry in state.Entries)
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.CertificateBase64))
                {
                    throw new InvalidDataException("Резервная копия содержит пустой сертификат.");
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
            X509BasicConstraintsExtension constraints = certificate.Extensions
                .OfType<X509BasicConstraintsExtension>()
                .FirstOrDefault();
            if (constraints == null || !constraints.CertificateAuthority)
            {
                throw new InvalidDataException("Выбранный сертификат не является сертификатом центра сертификации (CA).");
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

        private sealed class GridRow
        {
            public string Name { get; set; }
            public string Thumbprint { get; set; }
            public string DnsNames { get; set; }
            public string Cidrs { get; set; }
            public string NotAfter { get; set; }
            public CertificatePolicyEntry Entry { get; set; }
        }
    }

    internal sealed class PoliciesSavedDialog : Form
    {
        private const string PolicyUrl = "chrome://policy/";
        private readonly Label refreshStatus = new Label();

        private PoliciesSavedDialog()
        {
            Text = "Политики сохранены";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            Font = new Font("Segoe UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.Dock = DockStyle.Fill;
            Controls.Add(layout);

            Label saved = new Label();
            saved.AutoSize = true;
            saved.Text = "Политики сохранены. Chrome должен быть запущен.";
            saved.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(saved, 0, 0);

            FlowLayoutPanel refreshPanel = new FlowLayoutPanel();
            refreshPanel.AutoSize = true;
            refreshPanel.WrapContents = false;
            refreshPanel.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(refreshPanel, 0, 1);

            Button refresh = new Button();
            refresh.AutoSize = true;
            refresh.Text = "Повторно загрузить правила Chrome";
            refresh.Click += RefreshPolicies;
            refreshPanel.Controls.Add(refresh);

            refreshStatus.AutoSize = true;
            refreshStatus.Anchor = AnchorStyles.Left;
            refreshStatus.ForeColor = Color.ForestGreen;
            refreshStatus.Font = new Font(Font, FontStyle.Bold);
            refreshStatus.Text = "✓ Команда обновления передана Chrome";
            refreshStatus.Visible = false;
            refreshPanel.Controls.Add(refreshStatus);

            Label verification = new Label();
            verification.AutoSize = true;
            verification.Text = "Для ручной проверки скопируйте адрес и вставьте его в Chrome:";
            verification.Margin = new Padding(0, 0, 0, 6);
            layout.Controls.Add(verification, 0, 2);

            LinkLabel policyLink = new LinkLabel();
            policyLink.AutoSize = true;
            policyLink.Text = "Скопировать chrome://policy/";
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
            close.Text = "Закрыть";
            buttons.Controls.Add(close);
            AcceptButton = close;
        }

        public static void ShowSaved(IWin32Window owner)
        {
            using (PoliciesSavedDialog dialog = new PoliciesSavedDialog())
            {
                dialog.ShowDialog(owner);
            }
        }

        private void RefreshPolicies(object sender, EventArgs e)
        {
            object shell = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    throw new InvalidOperationException("Компонент Windows Shell недоступен.");
                }

                shell = Activator.CreateInstance(shellType);
                shellType.InvokeMember(
                    "ShellExecute",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { "chrome.exe", "--refresh-platform-policy", String.Empty, "open", 1 });

                Button button = sender as Button;
                if (button != null)
                {
                    button.Visible = false;
                    refreshStatus.Visible = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось повторно загрузить правила Chrome:\n\n" + ex.Message, "Chrome", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            Clipboard.SetText(PolicyUrl);
            LinkLabel link = sender as LinkLabel;
            if (link != null)
            {
                link.Text = "Скопировано: chrome://policy/";
            }
        }
    }

    internal sealed class ConstraintsDialog : Form
    {
        private readonly TextBox domains = new TextBox();
        private readonly TextBox cidrs = new TextBox();

        private ConstraintsDialog(string certificateName, IEnumerable<string> currentDomains, IEnumerable<string> currentCidrs)
        {
            Text = "Ограничения сертификата";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(560, 460);
            Size = new Size(680, 560);
            Font = new Font("Segoe UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            Label certificate = new Label();
            certificate.AutoSize = true;
            certificate.Text = certificateName;
            certificate.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(certificate, 0, 0);

            Label dnsHint = new Label();
            dnsHint.AutoSize = true;
            dnsHint.Text = "DNS-имена — по одному на строку, без схемы, порта и пути.\nДля домена и поддоменов: gosuslugi.ru и .gosuslugi.ru";
            dnsHint.Margin = new Padding(0, 0, 0, 6);
            layout.Controls.Add(dnsHint, 0, 1);

            ConfigureMultilineTextBox(domains);
            domains.Text = String.Join(Environment.NewLine, currentDomains);
            domains.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(domains, 0, 2);

            Label cidrHint = new Label();
            cidrHint.AutoSize = true;
            cidrHint.Text = "CIDR-сети — по одной на строку, например 10.1.1.0/24 или 2001:db8::/64.";
            cidrHint.Margin = new Padding(0, 0, 0, 6);
            layout.Controls.Add(cidrHint, 0, 3);

            ConfigureMultilineTextBox(cidrs);
            cidrs.Text = String.Join(Environment.NewLine, currentCidrs);
            cidrs.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(cidrs, 0, 4);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);
            layout.Controls.Add(buttons, 0, 5);

            Button cancel = new Button();
            cancel.AutoSize = true;
            cancel.Text = "Отмена";
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            Button ok = new Button();
            ok.AutoSize = true;
            ok.Text = "ОК";
            ok.Click += ValidateAndClose;
            buttons.Controls.Add(ok);

            CancelButton = cancel;
        }

        public List<string> ResultDomains { get; private set; }
        public List<string> ResultCidrs { get; private set; }

        public static bool TryEdit(
            IWin32Window owner,
            string certificateName,
            IEnumerable<string> currentDomains,
            IEnumerable<string> currentCidrs,
            out List<string> resultDomains,
            out List<string> resultCidrs)
        {
            using (ConstraintsDialog dialog = new ConstraintsDialog(certificateName, currentDomains, currentCidrs))
            {
                bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
                resultDomains = accepted ? dialog.ResultDomains : null;
                resultCidrs = accepted ? dialog.ResultCidrs : null;
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
                ResultCidrs = ParseCidrs(cidrs.Text);
                if (ResultDomains.Count == 0 && ResultCidrs.Count == 0)
                {
                    throw new InvalidDataException("Укажите хотя бы одно DNS-имя или CIDR-сеть.");
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Неверное ограничение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
                    throw new InvalidDataException("Недопустимое DNS-имя: " + raw);
                }

                bool leadingDot = raw.StartsWith(".", StringComparison.Ordinal);
                string value = leadingDot ? raw.Substring(1) : raw;
                value = value.TrimEnd('.');
                string ascii = idn.GetAscii(value).ToLowerInvariant();

                if (ascii.Length == 0 || ascii.Length > 253 || ascii.Split('.').Any(label => label.Length == 0 || label.Length > 63 || label.StartsWith("-", StringComparison.Ordinal) || label.EndsWith("-", StringComparison.Ordinal) || !Regex.IsMatch(label, @"^[a-z0-9-]+$")))
                {
                    throw new InvalidDataException("Недопустимое DNS-имя: " + raw);
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
                    throw new InvalidDataException("Недопустимая CIDR-сеть: " + raw);
                }

                byte[] bytes = address.GetAddressBytes();
                int maximumPrefixLength = bytes.Length * 8;
                if (prefixLength < 0 || prefixLength > maximumPrefixLength)
                {
                    throw new InvalidDataException("Недопустимая длина префикса CIDR: " + raw);
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

                List<string> parsed = ConstraintsDialog.ParseDomains("GOSUSLUGI.RU\n.пример.рф");
                if (parsed.Count != 2 || parsed[0] != "gosuslugi.ru" || !parsed[1].StartsWith(".xn--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Self-test failed: DNS normalization error.");
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
