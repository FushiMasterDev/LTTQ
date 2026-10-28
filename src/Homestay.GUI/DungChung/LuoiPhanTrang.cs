using Homestay.DTO.Common;
using System.ComponentModel;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Common;

public sealed class PageQueryEventArgs(PageRequest request) : EventArgs
{
    public PageRequest Request { get; } = request;
}
[DefaultEvent(nameof(QueryRequested))]
public sealed class PagedGrid : UserControl
{
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        RowHeadersVisible = false, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
    };
    private readonly ComboBox size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly Label count = new() { AutoSize = true, Padding = new Padding(8, 9, 0, 0), Text = "Chưa nối dữ liệu" };
    private readonly Button previous = new() { Text = "‹ Trước", AutoSize = true, Enabled = false };
    private readonly Button next = new() { Text = "Sau ›", AutoSize = true, Enabled = false };
    private readonly Button export = new() { Text = "Xuất CSV", AutoSize = true };
    private int page = 1, total;
    private string? sort;
    private bool descending;
    private bool changingPageSize;
    private string? preferencesPath;
    public event EventHandler<PageQueryEventArgs>? QueryRequested;
    public event EventHandler? DetailRequested;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<CancellationToken, Task<DataTable>>? ExportFilteredAsync { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DataGridView Grid => grid;
    public PagedGrid()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var columns = new Button { Text = "Hiện cột", AutoSize = true };
        top.Controls.AddRange([columns, export]);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        size.Items.AddRange([10, 20, 50, 100]); size.SelectedItem = 50;
        bottom.Controls.AddRange([previous, next, new Label {Text = "Dòng/trang", AutoSize = true, Padding = new Padding(4,9,0,0)}, size, count]);
        layout.Controls.Add(top, 0, 0); layout.Controls.Add(grid, 0, 1); layout.Controls.Add(bottom, 0, 2);
        Controls.Add(layout);
        columns.Click += (_, _) => ShowColumns(columns);
        previous.Click += (_, _) => Request(page - 1);
        next.Click += (_, _) => Request(page + 1);
        size.SelectedIndexChanged += (_, _) => { if (!changingPageSize) Request(1); };
        grid.CellDoubleClick += (_, e) => { if(e.RowIndex >= 0) DetailRequested?.Invoke(this, EventArgs.Empty); };
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.ColumnIndex < 0) return;
            var column = grid.Columns[e.ColumnIndex];
            if (column.Tag is false) return;
            var key = column.Tag as string ?? column.DataPropertyName;
            descending = sort == key && !descending; sort = key; Request(1);
        };
        export.Click += async (_, _) => await ErrorHandler.RunAsync(export, ExportAsync);
    }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PageRequest Query => new() { PageNumber = page, PageSize = (int)size.SelectedItem!, SortColumn = sort, SortDescending = descending };
    public void SetSortKeys(params string?[] keys)
    {
        if (keys.Length != grid.Columns.Count) throw new ArgumentException("Số khóa sắp xếp không khớp.");
        for (var i=0;i<keys.Length;i++) grid.Columns[i].Tag = keys[i] is string key ? (object)key : false;
    }
    public void SetColumns(params string[] headers)
    {
        grid.Columns.Clear();
        for (var i = 0; i < headers.Length; i++)
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Column" + i, DataPropertyName = "Column" + i, HeaderText = headers[i],
                MinimumWidth = 100, FillWeight = 100, SortMode = DataGridViewColumnSortMode.Programmatic
            });
        RestoreColumns();
    }
    public void SetPage<T>(PagedResult<T> result, Func<T, object?[]> map)
    {
        if (!size.Items.Contains(result.PageSize)) throw new ArgumentOutOfRangeException(nameof(result.PageSize));
        var table = new DataTable();
        foreach (DataGridViewColumn column in grid.Columns) table.Columns.Add(column.DataPropertyName, typeof(object));
        foreach (var item in result.Items)
        {
            var values = map(item);
            if (values.Length != table.Columns.Count) throw new InvalidDataException("Số cột hiển thị không khớp dữ liệu.");
            table.Rows.Add(values);
        }
        changingPageSize = true;
        try { size.SelectedItem = result.PageSize; }
        finally { changingPageSize = false; }
        SetPage(table, result.TotalCount, result.PageNumber);
    }
    public void SetPage(DataTable data, int totalRows, int pageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalRows);
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        grid.DataSource = data;
        total = totalRows; page = pageNumber;
        var pages = Math.Max(1, (int)Math.Ceiling((double)total / (int)size.SelectedItem!));
        count.Text = $"Trang {page}/{pages} · {total} dòng";
        previous.Enabled = page > 1; next.Enabled = page < pages;
        grid.ClearSelection();
    }
    public void SetPreferenceIdentity(string userKey, string gridKey)
    {
        var file = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userKey + "\n" + gridKey)));
        preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HomestayDesktop", "columns", file + ".json");
        RestoreColumns();
    }
    public void OpenSelectedDetail()
    {
        if(grid.SelectedRows.Count!=1)throw new InvalidDataException("Vui lòng chọn một dòng.");
        DetailRequested?.Invoke(this,EventArgs.Empty);
    }
    private void Request(int target)
    {
        if(QueryRequested is null) { ErrorHandler.Pending(FindForm()); return; }
        QueryRequested.Invoke(this, new PageQueryEventArgs(new PageRequest { PageNumber = target, PageSize = (int)size.SelectedItem!, SortColumn = sort, SortDescending = descending }));
    }
    private void ShowColumns(Control owner)
    {
        var menu = new ContextMenuStrip();
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var item = new ToolStripMenuItem(column.HeaderText) { Checked = column.Visible, CheckOnClick = true };
            item.CheckedChanged += (_, _) =>
            {
                if (!item.Checked && grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible) == 1)
                { item.Checked = true; return; }
                column.Visible = item.Checked;
                if (preferencesPath is null) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
                    File.WriteAllText(preferencesPath, JsonSerializer.Serialize(
                        grid.Columns.Cast<DataGridViewColumn>().Where(c => !c.Visible).Select(c => c.Name)));
                }
                catch (Exception e) { ErrorHandler.Show(e, FindForm()); }
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(owner, new Point(0, owner.Height));
    }
    private void RestoreColumns()
    {
        if (preferencesPath is null || !File.Exists(preferencesPath)) return;
        try
        {
            var hidden = JsonSerializer.Deserialize<string[]>(File.ReadAllText(preferencesPath)) ?? [];
            foreach (DataGridViewColumn c in grid.Columns) c.Visible = !hidden.Contains(c.Name);
            if (grid.Columns.Count > 0 && grid.Columns.Cast<DataGridViewColumn>().All(c => !c.Visible))
                grid.Columns[0].Visible = true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { ErrorHandler.Logger?.Error(e); }
    }
    private async Task ExportAsync()
    {
        var columns = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToArray();
        if (columns.Length == 0) return;
        object?[][] rows;
        if (grid.SelectedRows.Count > 0)
            rows = grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index)
                .Select(r => columns.Select(c => r.Cells[c.Index].Value).ToArray()).ToArray();
        else
        {
            if (ExportFilteredAsync is null) throw new NotImplementedException();
            var table = await ExportFilteredAsync(CancellationToken.None);
            rows = table.Rows.Cast<DataRow>().Select(r => columns.Select(c => (object?)r[c.DataPropertyName]).ToArray()).ToArray();
        }
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "DanhSach.csv", AddExtension = true };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            await CsvExporter.WriteAsync(dialog.FileName, columns.Select(c => c.HeaderText).ToArray(), rows);
    }
}
