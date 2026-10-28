using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
namespace Homestay.GUI.Staff;
/// <summary>Lựa chọn mở danh sách đơn ngoại lệ theo loại.</summary>
public sealed record BookingExceptionsSelection(BookingExceptionKind? Kind = null);
/// <summary>Phần dành cho quản lý của màn hình đơn: danh sách đơn ngoại lệ và giao nhiều đơn.</summary>
public partial class FrmBookings
{
    private readonly TabPage exceptionsTab = new("Ngoại lệ") { Padding = new Padding(8) };
    private readonly PagedGrid exceptionsGrid = new() { Dock = DockStyle.Fill };
    private readonly ComboBox exceptionKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly Button assignManyButton = new() { Text = "Giao các đơn đã chọn", AutoSize = true, Enabled = false };
    private readonly Button createTaskButton = new() { Text = "Giao việc cho đơn", AutoSize = true, Enabled = false };
    private LiveGrid<BookingExceptionItem>? exceptionsLive;
    private bool IsExceptions => viewTabs.SelectedTab == exceptionsTab;
    private void InitializeManagerBookings(BookingExceptionsSelection? selection)
    {
        if (!services!.Can(Permission.OperationsReportView)) return;
        exceptionsTab.Controls.Add(exceptionsGrid); viewTabs.TabPages.Add(exceptionsTab);
        toolbar.Controls.AddRange([assignManyButton, createTaskButton, exceptionKind]);
        exceptionKind.DisplayMember = nameof(Choice.Label); exceptionKind.Items.Add(new Choice(null, "Mọi ngoại lệ"));
        foreach (var kind in Enum.GetValues<BookingExceptionKind>()) exceptionKind.Items.Add(new Choice(kind, ExceptionName(kind)));
        exceptionKind.SelectedIndex = selection?.Kind is {} k ? (int)k + 1 : 0;
        exceptionsGrid.SetColumns("Mã đơn", "Vấn đề", "Thời gian cảnh báo", "Đã quá hạn", "Hạn xử lý");
        exceptionsGrid.SetSortKeys("BookingCode", null, "SinceUtc", null, null);
        exceptionsGrid.SetPreferenceIdentity(services.Session!.UserId.ToString(), "F08.exceptions");
        exceptionsLive = new(exceptionsGrid, () => {
            var kind = (exceptionKind.SelectedItem as Choice)?.Value as BookingExceptionKind?;
            var area = filterPanelField1.SelectedItem?.Id; var home = filterPanelField2.SelectedItem?.Id; var text = filterPanelField0.Text;
            return (q, ct) => services.Bookings.ListExceptionsAsync(new() { Page = q with { SearchText = text }, Kind = kind, AreaId = area, HomestayId = home }, ct);
        }, x => [x.BookingCode, x.Problem, Duration(x.WorkingElapsedSeconds ?? x.ElapsedSeconds), Duration(x.OverdueSeconds), EmployeeUi.Time(x.DeadlineAtUtc)]);
        exceptionsLive.Changed += RefreshExceptionActions;
        exceptionsGrid.DetailRequested += (_, _) => detailButton.PerformClick();
        exceptionKind.SelectedIndexChanged += async (_, _) => { if (IsExceptions) await LoadAsync(exceptionsGrid.Query with { PageNumber = 1 }); };
        assignManyButton.Visible = services.Can(Permission.BookingAssign);
        assignManyButton.Click += async (_, _) => await RunMutationAsync(assignManyButton, AssignManyAsync, ReloadAsync, CanAssignMany);
        createTaskButton.Click += (_, _) => { if (exceptionsLive.Current is {} x && x.AllowedActions.Contains(BookingExceptionAction.CreateTask)) services.Navigate("F19", new TaskBoardSelection(x.BookingId)); };
        Disposed += (_, _) => exceptionsLive.Dispose();
        if (selection is not null) viewTabs.SelectedTab = exceptionsTab;
    }
    private static string ExceptionName(BookingExceptionKind kind) => kind switch {
        BookingExceptionKind.ConfirmationOverdue => "Chờ chủ quá hạn", BookingExceptionKind.HoldExpiring => "Giữ phòng sắp hết",
        BookingExceptionKind.ReceiptWaiting => "Phiếu thu chờ lâu", _ => "Đơn chưa giao"
    };
    private static string Duration(long seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"d\d\ hh\:mm\:ss");
    private BookingExceptionItem[] SelectedExceptions() => exceptionsGrid.Grid.SelectedRows.Cast<DataGridViewRow>()
        .Select(r => exceptionsLive?.AtRow(r.Index)).OfType<BookingExceptionItem>().ToArray();
    private bool CanAssignMany() => IsExceptions && services!.Can(Permission.BookingAssign) &&
        SelectedExceptions() is { Length: > 0 } selected && selected.All(x => x.AllowedActions.Contains(BookingExceptionAction.Assign));
    private void RefreshExceptionActions()
    {
        if (!IsExceptions) return;
        claimButton.Enabled = assignButton.Enabled = false;
        detailButton.Enabled = exceptionsLive?.Current?.AllowedActions.Contains(BookingExceptionAction.OpenBooking) == true;
        createTaskButton.Enabled = exceptionsLive?.Current?.AllowedActions.Contains(BookingExceptionAction.CreateTask) == true;
        assignManyButton.Enabled = CanAssignMany();
    }
    private static BookingAssignmentItem[] NormalizeBatch(IEnumerable<BookingAssignmentItem> items){var groups=items.GroupBy(x=>x.BookingId).ToArray();if(groups.Any(g=>g.Any(x=>!x.RowVer.SequenceEqual(g.First().RowVer))))throw new BusinessException("Phiên bản của đơn trong các cảnh báo không khớp. Vui lòng tải lại.");return groups.Select(g=>g.First() with{RowVer=g.First().RowVer.ToArray()}).ToArray();}private async Task AssignManyAsync()
    {
        var items = IsExceptions ? SelectedExceptions().Select(x => new BookingAssignmentItem(x.BookingId, x.RowVer.ToArray()) { BookingCode = x.BookingCode })
            : Current.Grid.SelectedRows.Cast<DataGridViewRow>().Select(r => rows[r.Index]).Select(x => new BookingAssignmentItem(x.Id, x.RowVer.ToArray()) { BookingCode = x.Code });
        var batch = NormalizeBatch(items);
        if (batch.Length is < 1 or > 100) throw new BusinessException("Chọn từ 1 đến 100 đơn khác nhau.");
        IReadOnlyList<Lookup>? people = null;
        foreach (var item in batch) {
            var available = await services!.Bookings.ListAssigneesAsync(item.BookingId);
            people = people is null ? available : people.Where(p => available.Any(a => a.Id == p.Id)).ToArray();
        }
        if (people is null || people.Count == 0) throw new BusinessException("Không có người nhận phù hợp với tất cả đơn đã chọn.");
        using var dialog = new FieldsDialog("Giao " + batch.Length + " đơn", new FieldSpec("employee", "Người nhận đủ phạm vi tất cả đơn", FieldKind.Lookup, Choices: people.Select(p => new Choice(p.Id, p.Name)).ToArray()));
        dialog.SetSummary("Toàn bộ đơn được giao cùng lúc. Nếu một đơn vừa thay đổi hoặc không còn hợp lệ, không đơn nào được giao.");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await services!.Bookings.AssignManyAsync(new(batch, LiveUi.Id(dialog, "employee"))); await ReloadAsync();
    }
}
