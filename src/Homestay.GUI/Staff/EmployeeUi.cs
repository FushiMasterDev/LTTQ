using System.Data;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

internal static class EmployeeUi
{
    public static async Task ReassignAsync(AppServices services,BookingListItem booking,IWin32Window owner){
        var lookups=await services.Bookings.ListAssigneesAsync(booking.Id);
        if(lookups.Count==0)throw new BusinessException("Chưa có người nhận đang hoạt động phù hợp khu vực của đơn.");
        var choices=lookups.Select(x=>new Choice(x.Id,x.Name)).ToArray();
        using var dialog=new FieldsDialog("Giao lại đơn "+booking.Code,new FieldSpec("employee","Người phụ trách",FieldKind.Choice,Choices:choices));
        if(dialog.ShowDialog(owner)==DialogResult.OK)
            await services.Bookings.ReassignAsync(new(booking.Id,(int)dialog.Values["employee"]!,booking.RowVer));
    }
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    public static string Date(DateOnly x) => x.ToString("dd/MM/yyyy");
    public static string Time(DateTime? x) => x is null ? "" : DateTime.SpecifyKind(x.Value,DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy HH:mm");
    public static string Money(decimal x) => UiFormat.Money(x);
    public static string Status(BookingStatus x) => x switch {
        BookingStatus.AwaitingPayment=>"Chờ thanh toán",BookingStatus.PaymentSubmitted=>"Chờ xác minh cọc",
        BookingStatus.Deposited=>"Chờ chủ xác nhận",BookingStatus.Confirmed=>"Đã xác nhận",
        BookingStatus.CheckedIn=>"Đang ở",BookingStatus.Completed=>"Hoàn thành",BookingStatus.Expired=>"Hết hạn",
        BookingStatus.Rejected=>"Từ chối",BookingStatus.Cancelled=>"Đã hủy",_=>"Không đến" };
    public static string PaymentKind(PaymentType x)=>x switch{PaymentType.Deposit=>"Cọc",PaymentType.AdditionalDeposit=>"Cọc bổ sung",PaymentType.Refund=>"Hoàn tiền",_=>"Khoản bù"};
    public static string PaymentMethodName(PaymentMethod x)=>x==PaymentMethod.Cash?"Tiền mặt":"Chuyển khoản";
    public static string PaymentStatusName(PaymentStatus x)=>x switch{PaymentStatus.Pending=>"Chờ xác minh",PaymentStatus.Verified=>"Đã xác minh",_=>"Đã từ chối"};
    public static string ChangeKind(ChangeRequestType x)=>x switch{ChangeRequestType.Cancel=>"Hủy",ChangeRequestType.ChangeDates=>"Đổi ngày",ChangeRequestType.ChangeRoomType=>"Đổi loại phòng",_=>"Đổi ngày và loại phòng"};
    public static string ChangeStatus(ChangeRequestStatus x)=>x switch{ChangeRequestStatus.Pending=>"Chờ duyệt",ChangeRequestStatus.AwaitingPayment=>"Chờ thanh toán",ChangeRequestStatus.Applied=>"Đã áp dụng",ChangeRequestStatus.Rejected=>"Từ chối",_=>"Hết hạn"};
    public static string Queue(BookingQueue x) => x switch {
        BookingQueue.Mine=>"Đơn của tôi",BookingQueue.Unassigned=>"Chưa giao",BookingQueue.AwaitingPayment=>"Chờ thu cọc",
        BookingQueue.DepositVerification=>"Chờ xác minh",BookingQueue.OwnerConfirmation=>"Chờ chủ xác nhận",
        BookingQueue.ArrivalsToday=>"Nhận hôm nay",BookingQueue.DeparturesToday=>"Trả hôm nay",
        BookingQueue.Upcoming=>"Sắp đến",BookingQueue.InHouse=>"Đang ở",BookingQueue.OpenChangeRequests=>"Yêu cầu đang mở",_=>"Tất cả" };
    public static object?[] BookingRow(BookingListItem b) => [b.Code,b.CustomerName,b.CustomerPhone,b.HomestayName,
        b.RoomTypeName+" / "+b.RoomCode,Date(b.CheckIn),Date(b.CheckOut),Status(b.Status),b.PaymentStateName,b.AssignedEmployeeName,Time(b.DeadlineAtUtc)];
    public static void BookingColumns(PagedGrid g) {
        g.SetColumns("Mã đơn","Khách hàng","SĐT","Homestay","Loại / phòng","Nhận phòng","Trả phòng","Trạng thái đơn","Thanh toán","Phụ trách","Hạn xử lý");
        g.SetSortKeys("Code","CustomerName","CustomerPhone","HomestayName",null,"CheckIn","CheckOut","Status","PaymentState","AssignedEmployeeName","DeadlineAtUtc");
    }
    public static void Local<T>(PagedGrid grid,IReadOnlyList<T> items,Func<T,object?[]> map,Action? bound=null) {
        void Apply(PageRequest q) {
            var page=Math.Clamp(q.PageNumber,1,Math.Max(1,(int)Math.Ceiling((double)items.Count/q.PageSize)));
            grid.SetPage(new PagedResult<T>{Items=items.Skip((page-1)*q.PageSize).Take(q.PageSize).ToArray(),TotalCount=items.Count,PageNumber=page,PageSize=q.PageSize},map);bound?.Invoke();
        }
        if(grid.Tag is EventHandler<PageQueryEventArgs> old)grid.QueryRequested-=old;
        EventHandler<PageQueryEventArgs> handler=(_,e)=>Apply(e.Request);
        grid.Tag=handler;grid.QueryRequested+=handler;
        foreach(DataGridViewColumn c in grid.Grid.Columns)c.Tag=false;
        grid.ExportFilteredAsync=_=>Task.FromResult(Table(grid,items.Select(map)));
        Apply(grid.Query with{PageNumber=1});
    }
    public static DataTable Table(PagedGrid grid,IEnumerable<object?[]> rows) {
        var t=new DataTable();foreach(DataGridViewColumn c in grid.Grid.Columns)t.Columns.Add(c.DataPropertyName,typeof(object));
        foreach(var row in rows)t.Rows.Add(row);return t;
    }
    public static int Selected(PagedGrid g) {
        if(g.Grid.SelectedRows.Count!=1)throw new BusinessException("Vui lòng chọn một dòng.");
        return g.Grid.SelectedRows[0].Index;
    }
    public static void Activate(Label notice,params Button[] buttons) {notice.Visible=false;foreach(var b in buttons)b.Tag=null;}
}