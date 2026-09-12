using System;
using System.Drawing;
using System.Windows.Forms;

namespace MobPackAvoidance
{
    public class MobPackAvoidanceSettingsForm : Form
    {
        private readonly MobPackAvoidance _plugin;

        private CheckBox _chkEnabled;
        private NumericUpDown _numScanRadius;
        private NumericUpDown _numClusterRadius;
        private NumericUpDown _numMaxPackSize;
        private NumericUpDown _numBlackspotDuration;
        private NumericUpDown _numBlackspotRadius;
        private NumericUpDown _numLevelAdvantage;
        private CheckBox _chkAvoidElites;
        private NumericUpDown _numEliteDiff;
        private Button _btnSave;
        private Button _btnCancel;

        // Color Palette
        private readonly Color _colorLightBg = ColorTranslator.FromHtml("#d3fad6");      // Light pastel green
        private readonly Color _colorPanelBg = ColorTranslator.FromHtml("#d1efb5");      // Soft light green
        private readonly Color _colorAccent = ColorTranslator.FromHtml("#edeba0");        // Light yellow/beige
        private readonly Color _colorMutedOlive = ColorTranslator.FromHtml("#c3c48d");    // Muted olive
        private readonly Color _colorDarkTaupe = ColorTranslator.FromHtml("#928c6f");     // Darker taupe/brownish-gray

        public MobPackAvoidanceSettingsForm(MobPackAvoidance plugin)
        {
            _plugin = plugin;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // Form setup
            this.Text = "MobPackAvoidance Configuration";
            this.Size = new Size(420, 540);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = _colorLightBg;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.Black;

            // Main container layout
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 3;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Content
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F)); // Action Buttons

            // 1. Header Panel
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.BackColor = _colorMutedOlive;
            
            Label lblHeader = new Label();
            lblHeader.Text = "MOB PACK AVOIDANCE";
            lblHeader.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            lblHeader.ForeColor = Color.White;
            lblHeader.Location = new Point(15, 12);
            lblHeader.AutoSize = true;
            headerPanel.Controls.Add(lblHeader);

            mainLayout.Controls.Add(headerPanel, 0, 0);

            // 2. Settings Group Box / Content Panel
            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Padding = new Padding(15, 10, 15, 10);

            GroupBox grpSettings = new GroupBox();
            grpSettings.Text = " Settings ";
            grpSettings.Dock = DockStyle.Fill;
            grpSettings.BackColor = _colorPanelBg;
            grpSettings.ForeColor = _colorDarkTaupe;
            grpSettings.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            grpSettings.Padding = new Padding(10, 20, 10, 10);

            TableLayoutPanel settingsLayout = new TableLayoutPanel();
            settingsLayout.Dock = DockStyle.Fill;
            settingsLayout.ColumnCount = 2;
            settingsLayout.RowCount = 9;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            
            // Set row heights
            for (int i = 0; i < 9; i++)
            {
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            }

            // Normal Font for inside the GroupBox
            Font labelFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Color labelColor = Color.FromArgb(40, 40, 40);

            // Row 0: Enabled
            Label lblEnabled = new Label { Text = "Plugin Enabled:", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _chkEnabled = new CheckBox { Checked = _plugin.Enabled, Anchor = AnchorStyles.Left, BackColor = Color.Transparent };
            settingsLayout.Controls.Add(lblEnabled, 0, 0);
            settingsLayout.Controls.Add(_chkEnabled, 1, 0);

            // Row 1: Scan Radius
            Label lblScanRadius = new Label { Text = "Scan Radius (yds):", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numScanRadius = CreateStyledNumeric(10, 500, _plugin.ScanRadius);
            settingsLayout.Controls.Add(lblScanRadius, 0, 1);
            settingsLayout.Controls.Add(_numScanRadius, 1, 1);

            // Row 2: Cluster Radius
            Label lblClusterRadius = new Label { Text = "Cluster Radius (yds):", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numClusterRadius = CreateStyledNumeric(1, 100, _plugin.ClusterRadius);
            settingsLayout.Controls.Add(lblClusterRadius, 0, 2);
            settingsLayout.Controls.Add(_numClusterRadius, 1, 2);

            // Row 3: Max Pack Size
            Label lblMaxPackSize = new Label { Text = "Max Pack Size:", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numMaxPackSize = CreateStyledNumeric(1, 20, _plugin.MaxPackSize);
            settingsLayout.Controls.Add(lblMaxPackSize, 0, 3);
            settingsLayout.Controls.Add(_numMaxPackSize, 1, 3);

            // Row 4: Blackspot Duration (ms)
            Label lblBlackspotDuration = new Label { Text = "Blackspot Duration (ms):", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numBlackspotDuration = CreateStyledNumeric(1000, 3600000, _plugin.BlackspotDuration, 5000);
            settingsLayout.Controls.Add(lblBlackspotDuration, 0, 4);
            settingsLayout.Controls.Add(_numBlackspotDuration, 1, 4);

            // Row 5: Blackspot Radius
            Label lblBlackspotRadius = new Label { Text = "Blackspot Radius (yds):", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numBlackspotRadius = CreateStyledNumeric(1, 200, (decimal)_plugin.BlackspotRadius);
            settingsLayout.Controls.Add(lblBlackspotRadius, 0, 5);
            settingsLayout.Controls.Add(_numBlackspotRadius, 1, 5);

            // Row 6: Level Advantage Required
            Label lblLevelAdvantage = new Label { Text = "1v2 Level Advantage:", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numLevelAdvantage = CreateStyledNumeric(-10, 80, _plugin.LevelAdvantageRequired);
            settingsLayout.Controls.Add(lblLevelAdvantage, 0, 6);
            settingsLayout.Controls.Add(_numLevelAdvantage, 1, 6);
 
            // Row 7: Avoid High Level Elites
            Label lblAvoidElites = new Label { Text = "Avoid High Level Elites:", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _chkAvoidElites = new CheckBox { Checked = _plugin.AvoidHighLevelElites, Anchor = AnchorStyles.Left, BackColor = Color.Transparent };
            settingsLayout.Controls.Add(lblAvoidElites, 0, 7);
            settingsLayout.Controls.Add(_chkAvoidElites, 1, 7);

            // Row 8: Elite Level Difference
            Label lblEliteDiff = new Label { Text = "Elite Level Diff (+X):", Font = labelFont, ForeColor = labelColor, Anchor = AnchorStyles.Left, AutoSize = true };
            _numEliteDiff = CreateStyledNumeric(-10, 80, _plugin.EliteLevelDifference);
            settingsLayout.Controls.Add(lblEliteDiff, 0, 8);
            settingsLayout.Controls.Add(_numEliteDiff, 1, 8);

            grpSettings.Controls.Add(settingsLayout);
            contentPanel.Controls.Add(grpSettings);
            mainLayout.Controls.Add(contentPanel, 0, 1);

            // 3. Action Buttons Panel (Bottom)
            FlowLayoutPanel actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.FlowDirection = FlowDirection.RightToLeft;
            actionPanel.Padding = new Padding(15, 10, 15, 10);
            actionPanel.BackColor = _colorLightBg;

            _btnCancel = CreateStyledButton("Cancel", DialogResult.Cancel);
            _btnSave = CreateStyledButton("Save", DialogResult.OK);
            _btnSave.Click += BtnSave_Click;

            actionPanel.Controls.Add(_btnCancel);
            actionPanel.Controls.Add(_btnSave);

            mainLayout.Controls.Add(actionPanel, 0, 2);

            this.Controls.Add(mainLayout);
            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private NumericUpDown CreateStyledNumeric(decimal min, decimal max, decimal val, decimal increment = 1)
        {
            var num = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                Increment = increment,
                Anchor = AnchorStyles.Right,
                Width = 110,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right
            };
            return num;
        }

        private Button CreateStyledButton(string text, DialogResult result)
        {
            Button btn = new Button
            {
                Text = text,
                DialogResult = result,
                Width = 85,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorMutedOlive,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = _colorDarkTaupe;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = _colorDarkTaupe;
            btn.FlatAppearance.MouseDownBackColor = _colorPanelBg;

            return btn;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            _plugin.UpdateSettings(
                _chkEnabled.Checked,
                (int)_numScanRadius.Value,
                (int)_numClusterRadius.Value,
                (int)_numMaxPackSize.Value,
                (int)_numBlackspotDuration.Value,
                (float)_numBlackspotRadius.Value,
                (int)_numLevelAdvantage.Value,
                _chkAvoidElites.Checked,
                (int)_numEliteDiff.Value
            );
            this.Close();
        }
    }
}
