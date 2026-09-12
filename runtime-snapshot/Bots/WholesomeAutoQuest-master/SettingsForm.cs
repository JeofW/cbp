using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Styx;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;

namespace WholesomeAQ
{
    public sealed class RecoveryStatusRow
    {
        public QuestRecoveryKey Key { get; init; } = null!;
        public uint QuestId { get; init; }
        public QuestRecoveryStage Stage { get; init; }
        public QuestRecoveryState State { get; init; }
        public QuestFailureReason Reason { get; init; }
        public int Episode { get; init; }
        public long AttemptGeneration { get; init; }
        public string RetryOrReset { get; init; } = "";
        public string DisplayText { get; init; } = "";
    }

    public static class RecoveryStatusFormatter
    {
        public static IReadOnlyList<RecoveryStatusRow> CreateRows(IEnumerable<QuestRecoveryRecord> records)
        {
            if (records == null)
                throw new ArgumentNullException(nameof(records));

            return records.Select(record =>
            {
                string retryOrReset = RetryOrReset(record);
                return new RecoveryStatusRow
                {
                    Key = record.Key,
                    QuestId = record.Key.QuestId,
                    Stage = record.Key.Stage,
                    State = record.State,
                    Reason = record.Reason,
                    Episode = record.EpisodeCount,
                    AttemptGeneration = record.AttemptGeneration,
                    RetryOrReset = retryOrReset,
                    DisplayText = $"quest={record.Key.QuestId}; stage={record.Key.Stage}; state={record.State}; " +
                        $"reason={record.Reason}; episode={record.EpisodeCount}; {retryOrReset}"
                };
            }).ToArray();
        }

        private static string RetryOrReset(QuestRecoveryRecord record)
        {
            if (record.State == QuestRecoveryState.CoolingDown && record.CooldownUntilUtc.HasValue)
                return $"cooldown until {record.CooldownUntilUtc.Value:O}";
            if (record.State == QuestRecoveryState.Completed)
                return "no reset (completed terminal)";
            if (record.State == QuestRecoveryState.Eligible)
                return "no reset (eligible)";
            if (record.State == QuestRecoveryState.Attempting)
                return "reset on attempt outcome";
            if (record.State == QuestRecoveryState.HalfOpen)
                return "reset on single probe outcome";
            if (record.State == QuestRecoveryState.ManualBlacklist)
                return "reset only by Clear exclusion";

            string reset = ResetTrigger(record.Reason);
            return record.NextHalfOpenUtc.HasValue
                ? $"half-open after {record.NextHalfOpenUtc.Value:O}; reset on {reset}"
                : $"reset on {reset}";
        }

        private static string ResetTrigger(QuestFailureReason reason)
        {
            switch (reason)
            {
                case QuestFailureReason.LegacyUnknown:
                    return "live chain, level, dataset, or NPC evidence, or Retry now";
                case QuestFailureReason.UnsupportedObjective:
                case QuestFailureReason.InvalidQuestData:
                case QuestFailureReason.NpcMissingFromDatabase:
                    return "dataset or core change, or Retry now";
                case QuestFailureReason.PathGenerationFailed:
                case QuestFailureReason.EndpointUnreachable:
                case QuestFailureReason.NoNavigableHotspot:
                    return "dataset, core, or navigation change, or Retry now";
                case QuestFailureReason.RepeatedDeaths:
                    return "level, equipment, repair, or combat-context change, or Retry now";
                case QuestFailureReason.None:
                    return "relevant context change or Retry now";
                default:
                    return "relevant quest context change or Retry now";
            }
        }
    }

    public sealed class RecoveryActionAvailability
    {
        public bool CanRetryNow { get; init; }
        public bool CanMarkPermanent { get; init; }
        public bool CanClearExclusion { get; init; }

        public static RecoveryActionAvailability For(
            RecoveryStatusRow? row,
            IEnumerable<QuestRecoveryRecord>? records = null)
        {
            if (row == null)
                return new RecoveryActionAvailability();

            QuestRecoveryRecord[] sameQuest = (records ?? Array.Empty<QuestRecoveryRecord>())
                .Where(record => record.Key.QuestId == row.QuestId)
                .ToArray();
            QuestRecoveryRecord? current = sameQuest.FirstOrDefault(record => record.Key.Equals(row.Key));
            if (current == null || current.State != row.State ||
                current.AttemptGeneration != row.AttemptGeneration ||
                current.State == QuestRecoveryState.Completed ||
                sameQuest.Any(record => record.State == QuestRecoveryState.Completed))
                return new RecoveryActionAvailability();

            bool manuallyExcluded = current.State == QuestRecoveryState.ManualBlacklist ||
                sameQuest.Any(record => record.State == QuestRecoveryState.ManualBlacklist);
            bool automaticExclusion = current.State == QuestRecoveryState.CoolingDown ||
                current.State == QuestRecoveryState.HalfOpen ||
                current.State == QuestRecoveryState.Quarantined;

            return new RecoveryActionAvailability
            {
                CanRetryNow = !manuallyExcluded &&
                    (current.State == QuestRecoveryState.CoolingDown
                     || current.State == QuestRecoveryState.Quarantined),
                CanMarkPermanent = !manuallyExcluded && automaticExclusion,
                CanClearExclusion = manuallyExcluded
                    || automaticExclusion
            };
        }
    }

    public sealed class RecoverySettingsController
    {
        private readonly QuestRecoveryManager _manager;
        private readonly Action<string> _log;
        private readonly Func<bool> _flush;

        public RecoverySettingsController(
            QuestRecoveryManager manager,
            Action<string>? log,
            Func<bool>? flush = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _log = log ?? (_ => { });
            _flush = flush ?? manager.TryFlush;
        }

        public IReadOnlyList<RecoveryStatusRow> Refresh() =>
            RecoveryStatusFormatter.CreateRows(_manager.GetEntries());

        public string ManualQuestIdsText() => string.Join(",",
            _manager.GetEntries()
                .Where(record => record.State == QuestRecoveryState.ManualBlacklist)
                .Select(record => record.Key.QuestId)
                .Distinct()
                .OrderBy(id => id));

        public RecoveryActionAvailability ActionsFor(RecoveryStatusRow? row) =>
            RecoveryActionAvailability.For(row, _manager.GetEntries());

        public void ApplyManualQuestIds(string text)
        {
            var desired = ParseQuestIds(text);
            var existing = new HashSet<uint>(_manager.GetEntries()
                .Where(record => record.State == QuestRecoveryState.ManualBlacklist)
                .Select(record => record.Key.QuestId));
            foreach (uint questId in existing.Except(desired).OrderBy(id => id))
            {
                var row = Refresh().First(record => record.QuestId == questId
                    && record.State == QuestRecoveryState.ManualBlacklist);
                if (_manager.TrySetManualBlacklist(questId, false))
                    LogAction("Clear exclusion", row);
            }
            foreach (uint questId in desired.Except(existing).OrderBy(id => id))
            {
                var row = new RecoveryStatusRow
                {
                    Key = QuestRecoveryKey.ForQuestStage(questId, QuestRecoveryStage.Pickup),
                    QuestId = questId,
                    Stage = QuestRecoveryStage.Pickup,
                    State = QuestRecoveryState.ManualBlacklist,
                    Reason = QuestFailureReason.UserExcluded
                };
                if (_manager.TrySetManualBlacklist(questId, true))
                    LogAction("Mark permanent", row);
            }
            PersistChanges();
        }

        public bool RetryNow(RecoveryStatusRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (!ActionsFor(row).CanRetryNow)
            {
                _log($"Retry now unavailable: quest={row.QuestId}, stage={row.Stage}, reason={row.Reason}");
                return false;
            }
            if (!_manager.TryRetryNow(row.Key))
                return LogNoChange("Retry now", row);
            LogAction("Retry now", row);
            PersistChanges();
            return true;
        }

        public bool MarkPermanent(RecoveryStatusRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (!ActionsFor(row).CanMarkPermanent)
            {
                _log($"Mark permanent unavailable: quest={row.QuestId}, stage={row.Stage}, reason={row.Reason}");
                return false;
            }
            if (!_manager.TrySetManualBlacklistIfCurrentAutomatic(
                    row.Key,
                    row.State,
                    row.AttemptGeneration))
                return LogNoChange("Mark permanent", row);
            LogAction("Mark permanent", row);
            PersistChanges();
            return true;
        }

        public bool ClearExclusion(RecoveryStatusRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (!ActionsFor(row).CanClearExclusion)
            {
                _log($"Clear exclusion unavailable: quest={row.QuestId}, stage={row.Stage}, reason={row.Reason}");
                return false;
            }
            if (!_manager.TryClearExclusion(row.Key))
                return LogNoChange("Clear exclusion", row);
            LogAction("Clear exclusion", row);
            PersistChanges();
            return true;
        }

        private bool LogNoChange(string action, RecoveryStatusRow row)
        {
            _log($"{action} made no change; status refreshed: quest={row.QuestId}, stage={row.Stage}, reason={row.Reason}");
            return false;
        }

        private void PersistChanges()
        {
            if (!_flush())
            {
                throw new InvalidOperationException(
                    "Quest recovery changes were not persisted; retry Save or the action.");
            }
        }

        private void LogAction(string action, RecoveryStatusRow row) =>
            _log($"{action}: quest={row.QuestId}, stage={row.Stage}, reason={row.Reason}");

        private static HashSet<uint> ParseQuestIds(string text)
        {
            var result = new HashSet<uint>();
            foreach (string part in (text ?? "").Split(
                new[] { ',', ';', '\r', '\n', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (uint.TryParse(part.Trim(), out uint questId) && questId > 0)
                    result.Add(questId);
            }
            return result;
        }
    }

    public class SettingsForm : Form
    {
        private readonly WholesomeAQSettings _settings;
        private readonly Action<string> _log;
        private readonly Action? _forceStop;
        private readonly Action? _resume;
        private readonly Action? _recoveryChanged;
        private readonly RecoverySettingsController _recovery;
        private readonly Timer _recoveryRefreshTimer;
        private readonly bool _recoveryAvailable;
        private readonly string _recoveryUnavailableReason;
        private NumericUpDown _numStartDist = null!;
        private NumericUpDown _numStep = null!;
        private NumericUpDown _numMaxDist = null!;
        private NumericUpDown _numMaxQuests = null!;
        private NumericUpDown _numMinLevelOffset = null!;
        private NumericUpDown _numRestHP = null!;
        private NumericUpDown _numRestMP = null!;
        private NumericUpDown _numRestResumeHP = null!;
        private NumericUpDown _numRestResumeMP = null!;
        private CheckBox _chkAutoVendor = null!;
        private CheckBox _chkAutoTrain = null!;
        private CheckBox _chkSellWhite = null!;
        private CheckBox _chkSellGreen = null!;
        private CheckBox _chkSellBlue = null!;
        private TextBox _txtManualQuestIds = null!;
        private DataGridView _recoveryGrid = null!;
        private Button _retryRecoveryButton = null!;
        private Button _markPermanentButton = null!;
        private Button _clearExclusionButton = null!;
        private Label _recoveryAvailabilityLabel = null!;

        public SettingsForm(
            WholesomeAQSettings settings,
            Action<string> log,
            Action? forceStop = null,
            Action? resume = null,
            Action? recoveryChanged = null,
            QuestRecoveryManager? recoveryManager = null,
            bool recoveryAvailable = true,
            string? recoveryUnavailableReason = null)
        {
            _settings = settings;
            _log = log;
            _forceStop = forceStop;
            _resume = resume;
            _recoveryChanged = recoveryChanged;
            _recovery = new RecoverySettingsController(recoveryManager ?? QuestRecoveryManager.Instance, log);
            _recoveryAvailable = recoveryAvailable;
            _recoveryUnavailableReason = recoveryUnavailableReason ?? "Quest recovery is unavailable.";
            Text = "Wholesome Auto Quest Settings";
            Size = new Size(900, 760);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            BuildForm();
            LoadSettings();
            RefreshRecoveryEntries();
            _recoveryRefreshTimer = new Timer { Interval = 2000 };
            _recoveryRefreshTimer.Tick += (_, _) => RefreshRecoveryEntries();
            if (_recoveryAvailable)
                _recoveryRefreshTimer.Start();
            FormClosed += (_, _) =>
            {
                _recoveryRefreshTimer.Stop();
                _recoveryRefreshTimer.Dispose();
            };
        }

        private void BuildForm()
        {
            int y = 12;
            const int labelW = 140;
            const int ctrlW = 80;

            void AddRow(string label, Control ctrl)
            {
                var lbl = new Label { Text = label, Location = new Point(12, y + 3), Size = new Size(labelW, 20) };
                ctrl.Location = new Point(160, y);
                ctrl.Size = new Size(ctrlW, 24);
                Controls.Add(lbl);
                Controls.Add(ctrl);
                y += 30;
            }

            _numStartDist = new NumericUpDown { Minimum = 50, Maximum = 2000 };
            AddRow("Scan start distance:", _numStartDist);
            _numStep = new NumericUpDown { Minimum = 50, Maximum = 2000 };
            AddRow("Scan step:", _numStep);
            _numMaxDist = new NumericUpDown { Minimum = 500, Maximum = 10000, Increment = 250 };
            AddRow("Scan max distance:", _numMaxDist);
            _numMaxQuests = new NumericUpDown { Minimum = 1, Maximum = 50 };
            AddRow("Max quests per profile:", _numMaxQuests);
            _numMinLevelOffset = new NumericUpDown { Minimum = 0, Maximum = 20 };
            AddRow("Min quest level offset:", _numMinLevelOffset);
            _numRestHP = new NumericUpDown { Minimum = 10, Maximum = 100 };
            AddRow("Rest start HP%:", _numRestHP);
            _numRestMP = new NumericUpDown { Minimum = 10, Maximum = 100 };
            AddRow("Rest start MP%:", _numRestMP);
            _numRestResumeHP = new NumericUpDown { Minimum = 10, Maximum = 100 };
            AddRow("Rest resume HP%:", _numRestResumeHP);
            _numRestResumeMP = new NumericUpDown { Minimum = 10, Maximum = 100 };
            AddRow("Rest resume MP%:", _numRestResumeMP);

            _chkAutoVendor = new CheckBox { Text = "Auto-vendor (repair + sell)", Checked = true, Location = new Point(280, 12), Size = new Size(250, 24) };
            _chkAutoTrain = new CheckBox { Text = "Auto-train (class trainer)", Checked = true, Location = new Point(280, 42), Size = new Size(250, 24) };
            _chkSellWhite = new CheckBox { Text = "Sell whites", Location = new Point(280, 78), Size = new Size(120, 24) };
            _chkSellGreen = new CheckBox { Text = "Sell greens", Location = new Point(410, 78), Size = new Size(120, 24) };
            _chkSellBlue = new CheckBox { Text = "Sell blues", Location = new Point(540, 78), Size = new Size(120, 24) };
            Controls.AddRange(new Control[] { _chkAutoVendor, _chkAutoTrain, _chkSellWhite, _chkSellGreen, _chkSellBlue });

            var manualLabel = new Label { Text = "Manual permanent quest IDs (comma separated):", Location = new Point(280, 116), Size = new Size(360, 20) };
            _txtManualQuestIds = new TextBox { Name = "manualQuestIdsTextBox", Location = new Point(280, 140), Size = new Size(580, 24) };
            _txtManualQuestIds.ReadOnly = !_recoveryAvailable;
            Controls.Add(manualLabel);
            Controls.Add(_txtManualQuestIds);

            y += 8;
            Controls.Add(new Label { Text = "Quest recovery status (read-only):", Location = new Point(12, y), Size = new Size(260, 20) });
            _recoveryAvailabilityLabel = new Label
            {
                Name = "recoveryAvailabilityLabel",
                Text = _recoveryAvailable ? "" : _recoveryUnavailableReason,
                ForeColor = Color.DarkRed,
                Location = new Point(275, y),
                Size = new Size(585, 20)
            };
            Controls.Add(_recoveryAvailabilityLabel);
            y += 24;
            _recoveryGrid = new DataGridView
            {
                Name = "recoveryGrid",
                Location = new Point(12, y),
                Size = new Size(848, 260),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _recoveryGrid.Columns.Add("Quest", "Quest");
            _recoveryGrid.Columns.Add("Stage", "Stage");
            _recoveryGrid.Columns.Add("State", "State");
            _recoveryGrid.Columns.Add("Reason", "Reason");
            _recoveryGrid.Columns.Add("Episode", "Episode");
            _recoveryGrid.Columns.Add("RetryReset", "Cooldown / reset trigger");
            int[] widths = { 60, 80, 105, 160, 60, 360 };
            for (int index = 0; index < widths.Length; index++)
                _recoveryGrid.Columns[index].Width = widths[index];
            _recoveryGrid.SelectionChanged += (_, _) => UpdateRecoveryButtons();
            Controls.Add(_recoveryGrid);
            y += 270;

            _retryRecoveryButton = RecoveryButton("retryRecoveryButton", "Retry now", 12, y);
            _markPermanentButton = RecoveryButton("markPermanentButton", "Mark permanent", 150, y);
            _clearExclusionButton = RecoveryButton("clearExclusionButton", "Clear exclusion", 288, y);
            _retryRecoveryButton.Click += (_, _) => ApplySelected(_recovery.RetryNow);
            _markPermanentButton.Click += (_, _) => ApplySelected(_recovery.MarkPermanent);
            _clearExclusionButton.Click += (_, _) => ApplySelected(_recovery.ClearExclusion);
            Controls.AddRange(new Control[] { _retryRecoveryButton, _markPermanentButton, _clearExclusionButton });
            y += 38;

            var btnAbandon = new Button { Text = "Abandon Old Quests", Location = new Point(12, y), Size = new Size(200, 30) };
            btnAbandon.Click += BtnAbandon_Click;
            Controls.Add(btnAbandon);
            var btnForceStop = new Button { Text = "Force Stop", Location = new Point(230, y), Size = new Size(120, 30), BackColor = Color.IndianRed };
            btnForceStop.Click += (_, _) => _forceStop?.Invoke();
            Controls.Add(btnForceStop);
            var btnResume = new Button { Text = "Resume", Location = new Point(360, y), Size = new Size(120, 30), BackColor = Color.LightGreen };
            btnResume.Click += (_, _) => _resume?.Invoke();
            Controls.Add(btnResume);
            var btnSave = new Button { Text = "Save", Location = new Point(650, y), Size = new Size(100, 30) };
            btnSave.Click += (_, _) =>
            {
                if (SaveSettings())
                    Close();
            };
            Controls.Add(btnSave);
            var btnCancel = new Button { Text = "Cancel", Location = new Point(760, y), Size = new Size(100, 30) };
            btnCancel.Click += (_, _) => Close();
            Controls.Add(btnCancel);
        }

        private static Button RecoveryButton(string name, string text, int x, int y) => new()
        {
            Name = name,
            Text = text,
            Location = new Point(x, y),
            Size = new Size(130, 28),
            Enabled = false
        };

        private void LoadSettings()
        {
            _numStartDist.Value = _settings.ScanStartDistance;
            _numStep.Value = _settings.ScanStep;
            _numMaxDist.Value = _settings.ScanMaxDistance;
            _numMaxQuests.Value = _settings.MaxQuestsPerProfile;
            _numMinLevelOffset.Value = _settings.MinQuestLevelOffset;
            _numRestHP.Value = _settings.RestHealthPercent;
            _numRestMP.Value = _settings.RestManaPercent;
            _numRestResumeHP.Value = _settings.RestResumeHealthPercent;
            _numRestResumeMP.Value = _settings.RestResumeManaPercent;
            _chkAutoVendor.Checked = _settings.EnableAutoVendor;
            _chkAutoTrain.Checked = _settings.EnableAutoTrain;
            _chkSellWhite.Checked = _settings.SellWhite;
            _chkSellGreen.Checked = _settings.SellGreen;
            _chkSellBlue.Checked = _settings.SellBlue;
            _txtManualQuestIds.Text = _recoveryAvailable ? _recovery.ManualQuestIdsText() : "";
        }

        private bool SaveSettings()
        {
            _settings.ScanStartDistance = (int)_numStartDist.Value;
            _settings.ScanStep = (int)_numStep.Value;
            _settings.ScanMaxDistance = (int)_numMaxDist.Value;
            _settings.MaxQuestsPerProfile = (int)_numMaxQuests.Value;
            _settings.MinQuestLevelOffset = (int)_numMinLevelOffset.Value;
            _settings.RestHealthPercent = (int)_numRestHP.Value;
            _settings.RestManaPercent = (int)_numRestMP.Value;
            _settings.RestResumeHealthPercent = (int)_numRestResumeHP.Value;
            _settings.RestResumeManaPercent = (int)_numRestResumeMP.Value;
            _settings.EnableAutoVendor = _chkAutoVendor.Checked;
            _settings.EnableAutoTrain = _chkAutoTrain.Checked;
            _settings.SellWhite = _chkSellWhite.Checked;
            _settings.SellGreen = _chkSellGreen.Checked;
            _settings.SellBlue = _chkSellBlue.Checked;
            if (_recoveryAvailable)
            {
                try
                {
                    _recovery.ApplyManualQuestIds(_txtManualQuestIds.Text);
                    _recoveryChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    ShowRecoveryError($"Quest recovery settings save failed: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        public void RefreshRecoveryEntries()
        {
            IReadOnlyList<RecoveryStatusRow> rows = _recoveryAvailable
                ? _recovery.Refresh()
                : Array.Empty<RecoveryStatusRow>();
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                if (!IsHandleCreated)
                    return;
                try
                {
                    BeginInvoke(new Action(() => ApplyRecoveryRows(rows)));
                }
                catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated)
                {
                }
                return;
            }
            ApplyRecoveryRows(rows);
        }

        private void ApplyRecoveryRows(IReadOnlyList<RecoveryStatusRow> rows)
        {
            QuestRecoveryKey? selectedKey = SelectedRow()?.Key;
            _recoveryGrid.Rows.Clear();
            DataGridViewRow? selectedGridRow = null;
            foreach (RecoveryStatusRow row in rows)
            {
                int index = _recoveryGrid.Rows.Add(row.QuestId, row.Stage, row.State, row.Reason, row.Episode, row.RetryOrReset);
                DataGridViewRow gridRow = _recoveryGrid.Rows[index];
                gridRow.Tag = row;
                if (selectedKey != null && selectedKey.Equals(row.Key))
                    selectedGridRow = gridRow;
            }
            _recoveryGrid.ClearSelection();
            if (selectedGridRow != null)
                selectedGridRow.Selected = true;
            UpdateRecoveryButtons();
        }

        private RecoveryStatusRow? SelectedRow() =>
            _recoveryGrid.SelectedRows.Count == 1
                ? _recoveryGrid.SelectedRows[0].Tag as RecoveryStatusRow
                : null;

        private void UpdateRecoveryButtons()
        {
            RecoveryActionAvailability availability = _recoveryAvailable
                ? _recovery.ActionsFor(SelectedRow())
                : new RecoveryActionAvailability();
            _retryRecoveryButton.Enabled = availability.CanRetryNow;
            _markPermanentButton.Enabled = availability.CanMarkPermanent;
            _clearExclusionButton.Enabled = availability.CanClearExclusion;
        }

        private void ApplySelected(Func<RecoveryStatusRow, bool> action)
        {
            RecoveryStatusRow? row = SelectedRow();
            if (row == null)
                return;
            try
            {
                if (action(row))
                    _recoveryChanged?.Invoke();
                RefreshRecoveryEntries();
            }
            catch (Exception ex)
            {
                ShowRecoveryError($"Quest recovery action failed: {ex.Message}");
            }
        }

        private void ShowRecoveryError(string message)
        {
            _log?.Invoke(message);
            _recoveryAvailabilityLabel.Text = message;
            _recoveryAvailabilityLabel.ForeColor = Color.DarkRed;
        }

        private void BtnAbandon_Click(object? sender, EventArgs e)
        {
            if (!StyxWoW.IsInGame || StyxWoW.Me == null)
            {
                MessageBox.Show("You must be in game.", "Abandon Quests", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LocalPlayer me = StyxWoW.Me;
            QuestLog questLog = me.QuestLog;
            int maxLevel = me.Level + 3;
            int abandoned = 0;
            foreach (PlayerQuest pq in questLog.GetAllQuests())
            {
                if (!pq.IsCompleted && pq.Level > maxLevel)
                {
                    questLog.AbandonQuestById(pq.Id);
                    abandoned++;
                }
            }

            _log?.Invoke($"Abandoned {abandoned} over-level quests.");
            MessageBox.Show($"Abandoned {abandoned} quest(s).", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
