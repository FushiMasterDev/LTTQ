using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmBookingDetail : ScreenForm
{
    private AppServices? services;
    private BookingDetail? detail;
    private readonly AsyncLoader detailLoader=new();
    private int bookingId;
    private readonly Button reload=new(){Text="Tải lại",AutoSize=true};
    private readonly Button claim=new(){Text="Nhận phụ trách",AutoSize=true};
    private readonly Button reassign=new(){Text="Giao lại đơn",AutoSize=true};
    private readonly Button requestCancel=new(){Text="Xem hoàn / yêu cầu hủy",AutoSize=true};
    private readonly Label totals=new(){AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(8)};
    public FrmBookingDetail(){InitializeComponent();Disposed+=(_,_)=>detailLoader.Dispose();HelpText="Thông tin và giá là snapshot của đơn. Chỉ gửi địa chỉ/hướng dẫn cho khách sau xác nhận. Bấm đúp phiếu thu để xem chứng từ.";}
    public FrmBookingDetail(AppServices services,int bookingId):this(){
        this.services=services;this.bookingId=bookingId;EmployeeUi.Activate(pendingNotice,receiptButton,cancelButton,confirmButton,rejectButton,roomButton,checkinButton,checkoutButton,noShowButton,unavailableButton);
        foreach(var b in new[]{confirmButton,rejectButton,roomButton,checkinButton,checkoutButton,noShowButton,unavailableButton}){b.Visible=false;b.Enabled=false;}
        toolbar.Controls.AddRange([reload,claim,reassign,requestCancel]);toolbar.Controls.Add(totals);
        receiptButton.Enabled=cancelButton.Enabled=claim.Enabled=reassign.Enabled=requestCancel.Enabled=false;
        reassign.Visible=services.Can(Permission.BookingAssign);requestCancel.Visible=services.Can(Permission.BookingCancellationRequest);
        grid0.SetColumns("Nội dung","Giá trị");grid1.SetColumns("Ngày","Loại ngày","Giá");
        grid2.SetColumns("Số phiếu","Loại","Số tiền","Phương thức","Trạng thái","Người ghi","Ghi lúc","Khách chuyển lúc");
        grid3.SetColumns("Loại yêu cầu","Trạng thái","Lý do","Ngày gửi");
        reload.Click+=async(_,_)=>await ErrorHandler.RunAsync(reload,ReloadAsync);
        claim.Click+=async(_,_)=>await RunMutationAsync(claim,async()=>{await services.Bookings.ClaimAsync(new(bookingId,detail!.Booking.RowVer));await ReloadAsync();},ReloadAsync,()=>detail?.AllowedActions.Contains(BookingAction.Claim)==true);
        receiptButton.Click+=(_,_)=>services.Navigate("F10",bookingId);
        reassign.Click+=async(_,_)=>await RunMutationAsync(reassign,async()=>{await EmployeeUi.ReassignAsync(services,detail!.Booking,this);await ReloadAsync();},ReloadAsync,()=>detail?.AllowedActions.Contains(BookingAction.Reassign)==true);
        requestCancel.Click+=(_,_)=>services.Navigate("F11",bookingId);
        confirmButton.Click+=async(_,_)=>await ConfirmAsync(false);
        roomButton.Text="Đổi phòng và xác nhận";roomButton.Click+=async(_,_)=>await ConfirmAsync(true);
        rejectButton.Click+=async(_,_)=>await LifecycleActionAsync(rejectButton,BookingAction.RejectOnBehalf,"Lý do chủ từ chối",
            (d,note)=>services.Bookings.RejectOnBehalfAsync(new(bookingId,note,d.Booking.RowVer)));
        checkinButton.Click+=async(_,_)=>await LifecycleActionAsync(checkinButton,BookingAction.CheckIn,"Ghi chú nhận phòng thay chủ",
            (d,note)=>services.Bookings.CheckInAsync(new(bookingId,note,d.Booking.RowVer)));
        checkoutButton.Click+=async(_,_)=>await LifecycleActionAsync(checkoutButton,BookingAction.CheckOut,"Ghi chú trả phòng thay chủ",
            (d,note)=>services.Bookings.CheckOutAsync(new(bookingId,note,d.Booking.RowVer)));
        noShowButton.Click+=async(_,_)=>await LifecycleActionAsync(noShowButton,BookingAction.MarkNoShow,"Ghi chú khách không đến",
            (d,note)=>services.Bookings.MarkNoShowAsync(new(bookingId,note,d.Booking.RowVer)));
        unavailableButton.Click+=async(_,_)=>await LifecycleActionAsync(unavailableButton,BookingAction.CancelHomestayUnavailable,"Lý do homestay không phục vụ được",
            (d,note)=>services.Bookings.CancelHomestayUnavailableAsync(new(bookingId,note,d.Booking.RowVer)));
        cancelButton.Text="Hủy đơn chưa có tiền";
        cancelButton.Click+=async(_,_)=>await RunMutationAsync(cancelButton,async()=>{
            using var d=new FieldsDialog("Hủy đơn chưa có tiền",new FieldSpec("reason","Lý do hủy",FieldKind.Multiline));
            if(d.ShowDialog(this)==DialogResult.OK){await services.Bookings.CancelUnpaidAsync(new(bookingId,(string)d.Values["reason"]!,detail!.Booking.RowVer));await ReloadAsync();}
        },ReloadAsync,()=>detail?.AllowedActions.Contains(BookingAction.CancelUnpaid)==true);
        grid2.DetailRequested+=async(_,_)=>await ErrorHandler.RunAsync(reload,async()=>{
            var i=(grid2.Query.PageNumber-1)*grid2.Query.PageSize+EmployeeUi.Selected(grid2);var p=detail!.Payments[i];
            if(p.EvidenceFileId is not Guid id)throw new BusinessException("Phiếu này không có chứng từ.");
            var file=await services.Files.GetEvidenceAsync(bookingId,id);
            if(!IsDisposed)EvidencePresentation.Show(this,file,"Chứng từ "+p.VoucherNo);
        });
        grid3.DetailRequested+=async(_,_)=>await ErrorHandler.RunAsync(reload,async()=>{
            var current=detail??throw new BusinessException("Chưa tải được đơn.");
            var index=(grid3.Query.PageNumber-1)*grid3.Query.PageSize+EmployeeUi.Selected(grid3);
            var change=current.ChangeRequests[index];
            if(change.RequestType!=Homestay.DTO.Enums.ChangeRequestType.Cancel)throw new BusinessException("Đợt này chỉ hỗ trợ yêu cầu Hủy.");
            if(services.Can(Permission.BookingChangeReview)){services.Navigate("F20",bookingId);return;}
            var request=(await services.Changes.GetAsync(change.Id)).Request;
            MessageBox.Show(this,request.Reason+"\nTrạng thái: "+EmployeeUi.ChangeStatus(request.Status)+
                "\nHoàn chốt lúc gửi: "+(request.QuotedRefundAmount is decimal amount?EmployeeUi.Money(amount):"Chưa có mức chốt")+
                "\nNgười quyết định: "+request.DecidedByName+"\nGhi chú: "+request.DecisionNote,"Yêu cầu hủy "+request.BookingCode);
        });
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(reload,ReloadAsync);
    }
    private Task LifecycleActionAsync(Button button,BookingAction action,string title,Func<BookingDetail,string,Task<BookingDetail>> apply)=>
        RunMutationAsync(button,async()=>{
            var current=detail??throw new BusinessException("Chưa tải được đơn.");
            using var dialog=new FieldsDialog(title,new FieldSpec("note",title,FieldKind.Multiline));
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            await apply(current,(string)dialog.Values["note"]!);await ReloadAsync();
        },ReloadAsync,()=>LifecycleUi.CanWrite(action)&&detail?.AllowedActions.Contains(action)==true);
    private Task ConfirmAsync(bool changeRoom)=>RunMutationAsync(changeRoom?roomButton:confirmButton,async()=>{
        var current=detail??throw new BusinessException("Chưa tải được đơn.");
        var fields=new List<FieldSpec>{new("note","Ghi chú xác nhận thay chủ",FieldKind.Multiline)};
        if(changeRoom){
            var rooms=await services!.Bookings.ListConfirmationRoomsAsync(bookingId);
            if(rooms.Count==0)throw new BusinessException("Không có phòng khác cùng loại trống cho toàn bộ kỳ ở.");
            fields.Add(new("room","Phòng thay thế",FieldKind.Choice,Choices:rooms.Select(r=>new Choice(r.Id,r.Name)).ToArray()));
        }
        using var dialog=new FieldsDialog(changeRoom?"Đổi phòng và xác nhận thay chủ":"Xác nhận thay chủ",fields.ToArray());
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        await services!.Bookings.ConfirmOnBehalfAsync(new(bookingId,(string)dialog.Values["note"]!,current.Booking.RowVer,changeRoom?(int)dialog.Values["room"]!:null));
        await ReloadAsync();
    },ReloadAsync,()=>LifecycleUi.BookingLifecycleAvailable&&detail?.AllowedActions.Contains(BookingAction.ConfirmOnBehalf)==true);
    public Task RefreshFromServerAsync()=>ReloadAsync();
    protected virtual Task<BookingDetail> ReadDetailAsync(CancellationToken ct)=>services!.Bookings.GetAsync(bookingId,ct);
    private async Task ReloadAsync(){
        detail=null;
        foreach(var button in new[]{receiptButton,cancelButton,claim,reassign,requestCancel,confirmButton,rejectButton,roomButton,checkinButton,checkoutButton,noShowButton,unavailableButton})button.Enabled=false;
        await detailLoader.LoadAsync(ReadDetailAsync,d=>{
        detail=d;var b=d.Booking;
        filterPanelField0.Text=b.Code;filterPanelField1.Text=EmployeeUi.Status(b.Status);filterPanelField2.Text=b.PaymentStateName;filterPanelField3.Text=EmployeeUi.Time(b.DeadlineAtUtc);
        Text="F09 · "+b.Code;
        receiptButton.Enabled=d.AllowedActions.Contains(BookingAction.RecordDeposit);cancelButton.Enabled=d.AllowedActions.Contains(BookingAction.CancelUnpaid);claim.Enabled=d.AllowedActions.Contains(BookingAction.Claim);
        reassign.Enabled=d.AllowedActions.Contains(BookingAction.Reassign);
        requestCancel.Enabled=d.AllowedActions.Contains(BookingAction.SubmitCancel);
        foreach(var pair in new[]{(confirmButton,BookingAction.ConfirmOnBehalf),(roomButton,BookingAction.ConfirmOnBehalf),(rejectButton,BookingAction.RejectOnBehalf),
            (checkinButton,BookingAction.CheckIn),(checkoutButton,BookingAction.CheckOut),(noShowButton,BookingAction.MarkNoShow),(unavailableButton,BookingAction.CancelHomestayUnavailable)}){
            pair.Item1.Visible=d.AllowedActions.Contains(pair.Item2);pair.Item1.Enabled=LifecycleUi.CanWrite(pair.Item2)&&pair.Item1.Visible;
        }
        pendingNotice.Visible=d.AllowedActions.Any(a=>(int)a>=(int)BookingAction.ConfirmOnBehalf&&!LifecycleUi.CanWrite(a));
        pendingNotice.Text=LifecycleUi.ReadOnlyNotice;
        totals.Text="Tổng: "+EmployeeUi.Money(d.TotalAmount)+" · Cọc: "+EmployeeUi.Money(d.DepositAmount)+" · Mức cọc cần có: "+EmployeeUi.Money(d.AmountDue);
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
        });
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys k){if(services is not null&&k==Keys.F5){reload.PerformClick();return true;}return base.ProcessCmdKey(ref msg,k);}
}