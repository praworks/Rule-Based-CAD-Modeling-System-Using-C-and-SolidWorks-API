using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AICAD.Services;

namespace AICAD.UI
{
    internal class HistoryBrowser : Form
    {
    private readonly IStepStore _store;
        private readonly ListBox _runs;
        private readonly ListView _steps;
        private readonly TextBox _plan;

    public HistoryBrowser(IStepStore store)
        {
            _store = store;
            Text = "Run History";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 600);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 300
            };

            _runs = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            _runs.SelectedIndexChanged += (s, e) => LoadStepsForSelected();
            split.Panel1.Controls.Add(_runs);

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
            _steps = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            _steps.Columns.Add("#", 40);
            _steps.Columns.Add("Op", 140);
            _steps.Columns.Add("Params", 480);
            _steps.Columns.Add("OK", 40);
            _steps.Columns.Add("Error", 300);
            right.Panel1.Controls.Add(_steps);

            _plan = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
            right.Panel2.Controls.Add(_plan);

            split.Panel2.Controls.Add(right);
            Controls.Add(split);

            LoadRuns();
        }

        private void LoadRuns()
        {
            _runs.Items.Clear();
            var rows = _store?.GetRecentRuns(100) ?? new System.Collections.Generic.List<RunRow>();
            foreach (var r in rows)
            {
                var ok = r.Success ? "OK" : "ERR";
                var label = $"{r.Timestamp} [{ok}] {Truncate(r.Prompt, 80)}";
                _runs.Items.Add(new RunItem { Label = label, Row = r });
            }
        }

        private void LoadStepsForSelected()
        {
            _steps.Items.Clear();
            _plan.Clear();
            if (!(_runs.SelectedItem is RunItem item)) return;
            _plan.Text = item.Row.Plan ?? string.Empty;
            var steps = _store?.GetStepsForRun(item.Row.RunKey) ?? new System.Collections.Generic.List<StepRow>();
            foreach (var s in steps.OrderBy(s => s.StepIndex))
            {
                var li = new ListViewItem(s.StepIndex.ToString());
                li.SubItems.Add(s.Op ?? "");
                li.SubItems.Add(Truncate(s.ParamsJson, 180));
                li.SubItems.Add(s.Success ? "Y" : "N");
                li.SubItems.Add(Truncate(s.Error, 160));
                if (!s.Success) li.ForeColor = Color.Firebrick;
                _steps.Items.Add(li);
            }
            foreach (ColumnHeader ch in _steps.Columns) ch.Width = -2; // autosize
        }

        private static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length <= n) return s;
            return s.Substring(0, n - 1) + "…";
        }

        private class RunItem
        {
            public string Label { get; set; }
            public RunRow Row { get; set; }
            public override string ToString() => Label ?? Row?.RunKey ?? base.ToString();
        }
    }
}
