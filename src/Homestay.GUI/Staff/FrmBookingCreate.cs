using Homestay.BLL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmBookingCreate : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader quoteLoader=new();
    private readonly AsyncLoader roomLoader=new();
    private readonly AsyncLoader customerLoader=new();
    private BookingQuote? quote;
    private QuoteRequest? quoteRequest;
    private CustomerItem? customer;
    private CreateCustomerRequest? quickCustomer;
    private BookingDetail? created;
    private TimeSpan remaining;
    private readonly System.Diagnostics.Stopwatch holdWatch=new();
    private readonly Button searchRooms=new(){Text="Tìm loại phòng",AutoSize=true};
    private readonly Button findCustomer=new(){Text="Tìm khách theo SĐT",AutoSize=true};
    private readonly Button quickButton=new(){Text="Khách mới",AutoSize=true};
    private readonly System.Windows.Forms.Timer countdown;
    private readonly System.Windows.Forms.Timer roomSearchDelay;
    private readonly Label roomStatus=new(){Name="roomStatus",AutoSize=true,MaximumSize=new Size(600,0),Padding=new Padding(4,8,4,4)};
    private int? previousRoomTypeId;
    private bool settingInitialStay;
    private readonly CancellationTokenSource lifetime=new();
    private bool lifetimeDisposed;
    public FrmBookingCreate(){
        InitializeComponent();HelpText="Tìm khách hoặc tạo khách nhanh, chọn ngày/số khách rồi tìm loại phòng. Xem báo giá trước khi tạo. Chỉ giữ phòng sau khi tạo đơn thành công.";
        roomSearchDelay=new(components!){Interval=400};
        roomSearchDelay.Tick+=async(_,_)=>{roomSearchDelay.Stop();await RefreshRoomTypesAutomaticallyAsync();};
        countdown=new(components!){Interval=1000};countdown.Tick+=(_,_)=>UpdateCountdown();Disposed+=(_,_)=>DisposeLifetime();
    }
    public FrmBookingCreate(AppServices services,object? initial=null):this(){
        if(!services.Can(Permission.BookingCreate))throw new ForbiddenException();
        this.services=services;EmployeeUi.Activate(pendingNotice,quoteButton,receiptButton,saveButton);
        receiptButton.Enabled=saveButton.Enabled=false;editorFieldsField3.Enabled=editorFieldsField4.Enabled=false;editorFieldsField0.ReadOnly=true;toolbar.Controls.AddRange([searchRooms,findCustomer,quickButton,roomStatus]);quoteButton.Enabled=false;
        editorFieldsField2.Visible=editorFieldsField2Label.Visible=false;
        editorFieldsField1.Visible=editorFieldsField1Label.Visible=false;
        editorFieldsField5.Value=EmployeeUi.Today.ToDateTime(TimeOnly.MinValue);editorFieldsField6.Value=editorFieldsField5.Value.AddDays(1);
        editorFieldsField7.Minimum=1;editorFieldsField7.Maximum=1000;editorFieldsField7.Value=1;
        editorFieldsField10.DisplayMember=nameof(Choice.Label);editorFieldsField10.Items.Clear();
        editorFieldsField10.Items.AddRange(new object[]{new Choice(SourceChannel.Phone,"Điện thoại"),new Choice(SourceChannel.Zalo,"Zalo"),new Choice(SourceChannel.Messenger,"Messenger"),new Choice(SourceChannel.WalkIn,"Tại văn phòng"),new Choice(SourceChannel.Other,"Khác")});editorFieldsField10.SelectedIndex=0;
        nightlyPrices.SetColumns("Ngày","Loại ngày","Đơn giá");
        searchRooms.Click+=async(_,_)=>await ErrorHandler.RunAsync(searchRooms,()=>SearchRoomsAsync());
        findCustomer.Click+=async(_,_)=>await ErrorHandler.RunAsync(findCustomer,async()=>{
            using var d=new FieldsDialog("Tìm khách",new FieldSpec("phone","Số điện thoại",Initial:editorFieldsField0.Text,MaxLength:20));
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            var phone=(string)d.Values["phone"]!;
            await customerLoader.LoadAsync(ct=>services.Customers.FindByPhoneAsync(phone,ct),found=>{
                customer=found;quickCustomer=null;
                if(customer is null){
                    editorFieldsField0.Text=phone;editorFieldsField8.Clear();editorFieldsField9.Clear();
                    MessageBox.Show(this,"Chưa tìm thấy khách. Chọn Khách mới để nhập hồ sơ.");return;
                }
                ShowCustomer();
            });
        });
        quickButton.Click+=(_,_)=>{
            using var d=new FieldsDialog("Khách mới",new("name","Họ tên",MaxLength:100),new("phone","SĐT",Initial:editorFieldsField0.Text,MaxLength:20));
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            customerLoader.CancelPending();customer=null;quickCustomer=new(){FullName=(string)d.Values["name"]!,Phone=(string)d.Values["phone"]!};
            editorFieldsField0.Text=quickCustomer.Phone;editorFieldsField8.Text=quickCustomer.FullName;editorFieldsField9.Text=quickCustomer.Phone;
        };
        quoteButton.Click+=async(_,_)=>await ErrorHandler.RunAsync(quoteButton,QuoteAsync,enabledAfter:()=>created is null&&editorFieldsField3.SelectedItem is not null);
        saveButton.Click+=async(_,_)=>{await RunMutationAsync(saveButton,CreateAsync);if(created is not null)saveButton.Enabled=false;};
        receiptButton.Click+=(_,_)=>{if(created is not null)services.Navigate("F10",created.Booking.Id);};
        foreach(var c in new DateTimePicker[]{editorFieldsField5,editorFieldsField6})c.ValueChanged+=(_,_)=>StayChanged();
        editorFieldsField7.ValueChanged+=(_,_)=>StayChanged();
        editorFieldsField3.SelectionChanged+=(_,_)=>{InvalidateQuote();quoteButton.Enabled=created is null&&editorFieldsField3.SelectedItem is not null;};
        editorFieldsField4.SelectionChanged+=(_,_)=>saveButton.Enabled=created is null&&quote is {AvailableRooms.Count:>0}&&editorFieldsField4.SelectedItem is not null;
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(searchRooms,async()=>{
            if(initial is CustomerItem x){customer=x;ShowCustomer();}
            var request=initial switch{QuoteRequest quoteInput=>quoteInput,BookingDraftSelection draft=>draft.Quote,_=>null};
            if(request is {} r){
                settingInitialStay=true;
                try{editorFieldsField5.Value=r.CheckIn.ToDateTime(TimeOnly.MinValue);editorFieldsField6.Value=r.CheckOut.ToDateTime(TimeOnly.MinValue);editorFieldsField7.Value=r.Guests;}
                finally{settingInitialStay=false;}
                await SearchRoomsAsync(r.RoomTypeId);await QuoteAsync();
                if(initial is BookingDraftSelection {PreferredRoomId: int roomId})SelectPreferredRoom(roomId);
            }
            else await SearchRoomsAsync();
        });
    }
    private void SelectPreferredRoom(int roomId){
        editorFieldsField4.SelectId(roomId);
        if(editorFieldsField4.SelectedItem is null){
            saveButton.Enabled=false;
            throw new BusinessException("Phòng đã chọn không còn trống. Vui lòng chọn phòng khác hoặc tìm lại; chưa tạo đơn.");
        }
    }
    private void ShowCustomer(){editorFieldsField0.Text=customer!.Phone;editorFieldsField8.Text=customer.FullName;editorFieldsField9.Text=customer.Phone;}
    private QuoteRequest Request()=>new(editorFieldsField3.SelectedItem?.Id??throw new BusinessException("Vui lòng chọn loại phòng."),DateOnly.FromDateTime(editorFieldsField5.Value),DateOnly.FromDateTime(editorFieldsField6.Value),(int)editorFieldsField7.Value);
    private void InvalidateQuote(){
        if(created is not null)return;
        quoteLoader.CancelPending();quote=null;quoteRequest=null;editorFieldsField4.SetItems([]);editorFieldsField4.Enabled=false;saveButton.Enabled=false;
        EmployeeUi.Local(nightlyPrices,Array.Empty<object?[]>(),x=>x);
        foreach(var field in new[]{filterPanelField1,filterPanelField2,filterPanelField3,filterPanelField4,filterPanelField5})field.Clear();
    }
    private AvailabilitySearchRequest StayRequest()=>new(){CheckIn=DateOnly.FromDateTime(editorFieldsField5.Value),CheckOut=DateOnly.FromDateTime(editorFieldsField6.Value),Guests=(int)editorFieldsField7.Value};
    private void StayChanged(){
        if(created is not null||lifetimeDisposed)return;
        roomSearchDelay.Stop();previousRoomTypeId=editorFieldsField3.SelectedItem?.Id??previousRoomTypeId;
        roomLoader.CancelPending();editorFieldsField3.SetItems([]);editorFieldsField3.Enabled=false;InvalidateQuote();quoteButton.Enabled=false;
        if(StayRequest() is var request&&request.CheckOut<=request.CheckIn){
            roomStatus.Text="Ngày trả phòng phải sau ngày nhận phòng. Vui lòng chỉnh lại ngày.";return;
        }
        roomStatus.Text="Đang cập nhật loại phòng theo ngày và số khách mới…";
        if(services is not null&&!settingInitialStay)roomSearchDelay.Start();
    }
    private async Task RefreshRoomTypesAutomaticallyAsync(){
        if(IsDisposed||lifetimeDisposed||created is not null)return;
        try{await SearchRoomsAsync();}
        catch(SessionExpiredException e){if(!IsDisposed)ErrorHandler.Show(e,this);}
        catch(Exception e){
            if(IsDisposed||lifetimeDisposed)return;
            roomStatus.Text=e is BusinessException?e.Message:"Không tải được loại phòng. Bấm Tìm loại phòng để thử lại.";
            if(e is not BusinessException)services?.Logger.Error(e);
        }
    }
    protected virtual Task<IReadOnlyList<AvailabilityItem>> FindRoomTypesAsync(AvailabilitySearchRequest request,CancellationToken ct)=>services!.Availability.SearchAsync(request,ct);
    private async Task SearchRoomsAsync(int? selected=null){
        if(created is not null||lifetimeDisposed)return;
        roomSearchDelay.Stop();
        var preferred=selected??editorFieldsField3.SelectedItem?.Id??previousRoomTypeId;
        InvalidateQuote();roomLoader.CancelPending();editorFieldsField3.SetItems([]);editorFieldsField3.SelectId(null);editorFieldsField3.Enabled=false;quoteButton.Enabled=false;
        var request=StayRequest();
        if(request.CheckOut<=request.CheckIn){roomStatus.Text="Ngày trả phòng phải sau ngày nhận phòng. Vui lòng chỉnh lại ngày.";return;}
        roomStatus.Text="Đang tìm loại phòng trống…";
        await roomLoader.LoadAsync(ct=>FindRoomTypesAsync(request,ct),list=>{
            if(created is not null)return;
            var available=list.Where(x=>x.AvailableRooms>0).ToArray();
            editorFieldsField3.SetItems(available.Select(x=>new Lookup{Id=x.RoomTypeId,Name=x.HomestayName+" / "+x.RoomTypeName+" — "+EmployeeUi.Money(x.AveragePrice)+"/đêm"}));
            editorFieldsField3.Enabled=available.Length>0;
            if(preferred.HasValue)editorFieldsField3.SelectId(preferred);
            previousRoomTypeId=editorFieldsField3.SelectedItem?.Id;
            quoteButton.Enabled=previousRoomTypeId.HasValue;
            roomStatus.Text=available.Length==0?"Không có loại phòng trống cho ngày và số khách đã chọn. Hãy đổi ngày hoặc số khách."
                :previousRoomTypeId.HasValue?"Đã cập nhật phòng trống. Bấm Xem báo giá để tính lại theo ngày mới."
                :preferred.HasValue?"Loại phòng trước không còn phù hợp. Vui lòng chọn lại trong "+available.Length+" loại phòng trống."
                :"Có "+available.Length+" loại phòng trống. Chọn loại phòng rồi bấm Xem báo giá.";
        });
    }
    private async Task QuoteAsync(){
        if(created is not null)return;
        var r=Request();InvalidateQuote();
        await quoteLoader.LoadAsync(ct=>services!.Bookings.QuoteAsync(r,ct),result=>{
        if(created is not null)return;quote=result;quoteRequest=r;saveButton.Enabled=result.AvailableRooms.Count>0;
        EmployeeUi.Local(nightlyPrices,result.Pricing.Nights,n=>[EmployeeUi.Date(n.Date),n.DayType switch{DayType.Holiday=>"Ngày lễ",DayType.Weekend=>"Cuối tuần",_=>"Ngày thường"},EmployeeUi.Money(n.Price)]);
        editorFieldsField4.Enabled=result.AvailableRooms.Count>0;
        editorFieldsField4.SetItems(result.AvailableRooms.Count>0?new[]{new Lookup{Id=0,Name="Tự động chọn phòng"}}.Concat(result.AvailableRooms.Select(x=>new Lookup{Id=x.RoomId,Name=x.RoomCode})):[]);
        if(result.AvailableRooms.Count>0)editorFieldsField4.SelectId(0);
        else roomStatus.Text="Phòng vừa hết chỗ cho kỳ ở này. Bấm Tìm loại phòng để cập nhật.";
        filterPanelField1.Text=EmployeeUi.Money(result.Pricing.TotalAmount);filterPanelField2.Text=EmployeeUi.Money(result.Pricing.DepositAmount);filterPanelField3.Text=EmployeeUi.Money(result.AmountDue);
        filterPanelField4.Text=result.RoomType.CancellationPolicy.Name;
        filterPanelField5.Text=result.Bank.BankName+" / "+result.Bank.AccountNo+" / "+result.Bank.AccountName;
        quoteTabs.SelectedTab=quoteTab;
        });
    }
    private async Task CreateAsync(){
        if(created is not null)return;
        if(quote is null||quoteRequest!=Request())throw new BusinessException("Vui lòng xem lại báo giá cho thông tin hiện tại.");
        if(editorFieldsField4.SelectedItem is null)throw new BusinessException("Vui lòng chọn phòng cụ thể hoặc Tự động chọn phòng.");
        if(customer is null&&quickCustomer is null)throw new BusinessException("Vui lòng tìm khách theo SĐT hoặc nhập Khách mới.");
        customerLoader.CancelPending();roomSearchDelay.Stop();roomLoader.CancelPending();
        var r=quoteRequest!;
        created=await services!.Bookings.CreateAsync(new(){CustomerId=customer?.Id,QuickCustomer=customer is null?quickCustomer:null,
            RoomTypeId=r.RoomTypeId,RoomId=editorFieldsField4.SelectedItem?.Id is >0?editorFieldsField4.SelectedItem.Id:null,
            CheckIn=r.CheckIn,CheckOut=r.CheckOut,Guests=r.Guests,ContactName=editorFieldsField8.Text,ContactPhone=editorFieldsField9.Text,
            SourceChannel=(SourceChannel)((Choice)editorFieldsField10.SelectedItem!).Value!,SpecialRequests=editorFieldsField11.Text},lifetime.Token);
        var t=created.TransferInstructions??await services.Bookings.GetTransferInstructionsAsync(created.Booking.Id,lifetime.Token);
        filterPanelField0.Text=created.Booking.Code;filterPanelField1.Text=EmployeeUi.Money(created.TotalAmount);filterPanelField2.Text=EmployeeUi.Money(created.DepositAmount);filterPanelField3.Text=EmployeeUi.Money(created.AmountDue);
        filterPanelField5.Text=t.Bank.BankName+" / "+t.Bank.AccountNo+" / "+t.Bank.AccountName+"; nội dung: "+t.TransferContent;
        editorFields.Enabled=false;saveButton.Enabled=false;quoteButton.Enabled=false;searchRooms.Enabled=false;findCustomer.Enabled=false;quickButton.Enabled=false;
        try{remaining=created.HoldExpiresAtUtc-await services.ServerNowAsync(lifetime.Token);holdWatch.Restart();}
        catch(Exception e){services.Logger.Error(e);remaining=TimeSpan.Zero;filterPanelField6.Text="Giữ đến "+EmployeeUi.Time(created.HoldExpiresAtUtc);}
        receiptButton.Enabled=created.AllowedActions.Contains(BookingAction.RecordDeposit);if(remaining>TimeSpan.Zero){countdown.Start();UpdateCountdown();}
        ShowSuccess("Đã tạo đơn "+created.Booking.Code+". Phòng đã được giữ. Khách cần cọc "+EmployeeUi.Money(created.AmountDue)+".","Tạo đơn thành công");
    }
    // Form.Close disposes the form; a host or using scope may dispose it again.
    private void DisposeLifetime()
    {
        if(lifetimeDisposed)return;
        lifetimeDisposed=true;
        quoteLoader.Dispose();roomLoader.Dispose();customerLoader.Dispose();
        try{lifetime.Cancel();}
        finally{lifetime.Dispose();}
    }
    protected virtual void ShowSuccess(string text,string title)=>MessageBox.Show(this,text,title);
    private void UpdateCountdown(){
        if(created is null)return;var left=remaining-holdWatch.Elapsed;
        filterPanelField6.Text=left>TimeSpan.Zero?((int)left.TotalMinutes).ToString("00")+":"+left.Seconds.ToString("00"):"Hết hạn hiển thị — tải lại đơn";
        if(left<=TimeSpan.Zero){countdown.Stop();receiptButton.Enabled=false;}
    }
}