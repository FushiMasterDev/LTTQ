using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmBookingDetail : ScreenForm
{
    private AppServices? services;
    private BookingDetail? detail;
    private int bookingId;
    private readonly Button reload=new(){Text="Tải lại",AutoSize=true};
    private readonly Button claim=new(){Text="Nhận phụ trách",AutoSize=true};
    private readonly Label totals=new(){AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(8)};
    public FrmBookingDetail(){InitializeComponent();HelpText="Thông tin và giá là snapshot của đơn. Chỉ gửi địa chỉ/hướng dẫn cho khách sau xác nhận. Bấm đúp phiếu thu để xem chứng từ.";}
    public FrmBookingDetail(AppServices services,int bookingId):this(){
        this.services=services;this.bookingId=bookingId;EmployeeUi.Activate(pendingNotice,receiptButton,cancelButton);
        foreach(var b in new[]{confirmButton,rejectButton,roomButton,checkinButton,checkoutButton,noShowButton,unavailableButton})b.Visible=false;
        toolbar.Controls.AddRange([reload,claim]);toolbar.Controls.Add(totals);
        grid0.SetColumns("Nội dung","Giá trị");grid1.SetColumns("Ngày","Loại ngày","Giá");
        grid2.SetColumns("Số phiếu","Loại","Số tiền","Phương thức","Trạng thái","Người ghi","Ghi lúc","Khách chuyển lúc");
        grid3.SetColumns("Loại yêu cầu","Trạng thái","Lý do","Ngày gửi");
        reload.Click+=async(_,_)=>await ErrorHandler.RunAsync(reload,ReloadAsync);
        claim.Click+=async(_,_)=>await RunMutationAsync(claim,async()=>{await services.Bookings.ClaimAsync(new(bookingId,detail!.Booking.RowVer));await ReloadAsync();},ReloadAsync);
        receiptButton.Click+=(_,_)=>services.Navigate("F10",bookingId);
        cancelButton.Text="Hủy đơn chưa có tiền";
        cancelButton.Click+=async(_,_)=>await RunMutationAsync(cancelButton,async()=>{
            using var d=new FieldsDialog("Hủy đơn chưa có tiền",new FieldSpec("reason","Lý do hủy",FieldKind.Multiline));
            if(d.ShowDialog(this)==DialogResult.OK){await services.Bookings.CancelUnpaidAsync(new(bookingId,(string)d.Values["reason"]!,detail!.Booking.RowVer));await ReloadAsync();}
        },ReloadAsync);
        grid2.DetailRequested+=async(_,_)=>await ErrorHandler.RunAsync(reload,async()=>{
            var i=(grid2.Query.PageNumber-1)*grid2.Query.PageSize+EmployeeUi.Selected(grid2);var p=detail!.Payments[i];
            if(p.EvidenceFileId is not Guid id)throw new BusinessException("Phiếu này không có chứng từ.");
            var file=await services.Files.GetEvidenceAsync(bookingId,id);
            using var stream=new MemoryStream(file.Content);using var picture=Image.FromStream(stream);using var form=new Form{Text="Chứng từ "+p.VoucherNo,Width=850,Height=650,StartPosition=FormStartPosition.CenterParent};
            form.Controls.Add(new PictureBox{Dock=DockStyle.Fill,Image=picture,SizeMode=PictureBoxSizeMode.Zoom});form.ShowDialog(this);
        });
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(reload,ReloadAsync);
    }
    public Task RefreshFromServerAsync()=>ReloadAsync();
    private async Task ReloadAsync(){
        detail=await services!.Bookings.GetAsync(bookingId);var d=detail;var b=d.Booking;
        filterPanelField0.Text=b.Code;filterPanelField1.Text=EmployeeUi.Status(b.Status);filterPanelField2.Text=b.PaymentStateName;filterPanelField3.Text=EmployeeUi.Time(b.DeadlineAtUtc);
        Text="F09 · "+b.Code;
        receiptButton.Enabled=d.AllowedActions.Contains(BookingAction.RecordDeposit);cancelButton.Enabled=d.AllowedActions.Contains(BookingAction.CancelUnpaid);claim.Enabled=d.AllowedActions.Contains(BookingAction.Claim);
        totals.Text="Tổng: "+EmployeeUi.Money(d.TotalAmount)+" · Cọc: "+EmployeeUi.Money(d.DepositAmount)+" · Cần thu: "+EmployeeUi.Money(d.AmountDue);
        List<object?[]> info=[["Khách",d.Customer.FullName+" / "+d.Customer.Phone],["Người ở",b.ContactName+" / "+b.ContactPhone],
            ["Homestay / phòng",b.HomestayName+" / "+b.RoomTypeName+" / "+b.RoomCode],["Ngày ở",EmployeeUi.Date(b.CheckIn)+" – "+EmployeeUi.Date(b.CheckOut)],
            ["Số khách",d.Guests],["Yêu cầu",d.SpecialRequests],["Địa chỉ",d.Address],["Hướng dẫn",d.CheckinGuide],
            ["Cung cấp thông tin cho khách",d.CanProvideArrivalDetails?"Đã xác nhận, được cung cấp":"Chưa được cung cấp địa chỉ/hướng dẫn cho khách"],
            ["Chính sách hủy",d.CancellationPolicy.Name],["Phụ trách",b.AssignedEmployeeName]];
        if(d.CommissionRate is decimal rate)info.Add(["Hoa hồng",rate.ToString("P0")]);
        EmployeeUi.Local(grid0,info,x=>x);
        EmployeeUi.Local(grid1,d.Nights,n=>[EmployeeUi.Date(n.Date),n.DayType switch{Homestay.DTO.Enums.DayType.Holiday=>"Ngày lễ",Homestay.DTO.Enums.DayType.Weekend=>"Cuối tuần",_=>"Ngày thường"},EmployeeUi.Money(n.Price)]);
        EmployeeUi.Local(grid2,d.Payments,p=>[p.VoucherNo,EmployeeUi.PaymentKind(p.Type),EmployeeUi.Money(p.Amount),EmployeeUi.PaymentMethodName(p.Method),EmployeeUi.PaymentStatusName(p.Status),p.RecordedByName,EmployeeUi.Time(p.RecordedAtUtc),EmployeeUi.Time(p.TransferredAtUtc)]);
        EmployeeUi.Local(grid3,d.ChangeRequests,x=>[EmployeeUi.ChangeKind(x.RequestType),EmployeeUi.ChangeStatus(x.Status),x.Reason,EmployeeUi.Time(x.RequestedAtUtc)]);
        EmployeeUi.Local(grid4,d.History,x=>[EmployeeUi.Time(x.ChangedAtUtc),x.FromStatus is null?"":EmployeeUi.Status(x.FromStatus.Value),EmployeeUi.Status(x.ToStatus),x.ActorName,x.Note]);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys k){if(services is not null&&k==Keys.F5){reload.PerformClick();return true;}return base.ProcessCmdKey(ref msg,k);}
}