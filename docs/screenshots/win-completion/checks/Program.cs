using System.Reflection;
using Homestay.GUI.Common;
using Homestay.GUI.Staff;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Notifications;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
internal static class Program{
    static readonly List<string> lines=[];
    [STAThread] static int Main(string[] args){
        ApplicationConfiguration.Initialize();
        using var runner=new Form{Width=500,Height=500,ShowInTaskbar=false,Opacity=0};
        runner.Shown+=async(_,_)=>{
            try{await Run(runner);lines.Add("RESULT PASS");}
            catch(Exception e){lines.Add("FAIL "+e);Environment.ExitCode=1;}
            finally{File.WriteAllLines(args[0],lines);runner.Close();}
        };
        Application.Run(runner);foreach(var line in lines)Console.WriteLine(line);return Environment.ExitCode;
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);lines.Add("PASS "+message);}
    static T Field<T>(object obj,string name){for(Type? t=obj.GetType();t is not null;t=t.BaseType){var field=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);if(field is not null)return (T)field.GetValue(obj)!;}throw new Exception("Missing field "+name);}
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(obj,value);
    static object? Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(obj,args);
    static async Task Until(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(5);while(!ready()){if(DateTime.UtcNow>end)throw new Exception("UI wait timed out");await Task.Delay(20);}}
    static async Task Run(Form runner){
        var now=new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc);
        Check(DeadlineDisplay.Format(null,now)=="","Missing deadline stays empty");
        Check(DeadlineDisplay.Format(now.AddSeconds(60),now).EndsWith("Còn 00:01:00"),"Deadline countdown handles minute boundary");
        Check(DeadlineDisplay.Format(now.AddDays(2).AddSeconds(1),now).EndsWith("Còn 2 ngày 00:00:01"),"Countdown displays days without wrapping to zero");
        Check(DeadlineDisplay.Format(now,now).Contains("Đã quá hạn"),"Deadline at server time is shown overdue");
        var folder=Path.Combine(Path.GetTempPath(),"HomestayGuiEvidence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            var pdf=Path.Combine(folder,"renamed.jpg");await File.WriteAllBytesAsync(pdf,"%PDF-1.7\nsynthetic"u8.ToArray());
            var file=await EvidencePresentation.ReadAsync(pdf);
            Check(file.ContentType=="application/pdf"&&EvidencePresentation.Extension(file.ContentType)==".pdf","Evidence uses file signature rather than renamed extension");
            var webp=Path.Combine(folder,"test.webp");await File.WriteAllBytesAsync(webp,new byte[]{82,73,70,70,0,0,0,0,87,69,66,80});
            Check((await EvidencePresentation.ReadAsync(webp)).ContentType=="image/webp","WEBP evidence accepted by existing BLL validation");
            var invalid=Path.Combine(folder,"bad.png");await File.WriteAllTextAsync(invalid,"not an image");
            try{await EvidencePresentation.ReadAsync(invalid);throw new Exception("Invalid evidence accepted");}catch(BusinessException){Check(true,"Invalid evidence rejected before saving receipt");}
            var big=Path.Combine(folder,"large.pdf");await File.WriteAllBytesAsync(big,new byte[5*1024*1024+1]);
            try{await EvidencePresentation.ReadAsync(big);throw new Exception("Oversized evidence accepted");}catch(BusinessException){Check(true,"Evidence over 5 MB rejected");}
        }finally{foreach(var path in Directory.GetFiles(folder))File.Delete(path);Directory.Delete(folder);}
        using(var form=new FrmBookingCreate()){
            var picker=Field<SearchPicker>(form,"editorFieldsField4");
            picker.SetItems([new Lookup{Id=0,Name="Auto"},new Lookup{Id=101,Name="A1"},new Lookup{Id=102,Name="A2"}]);
            Call(form,"SelectPreferredRoom",102);
            Check(picker.SelectedItem?.Id==102,"Calendar draft selects the clicked room instead of automatic assignment");
            try{Call(form,"SelectPreferredRoom",999);throw new Exception("Missing room silently replaced");}
            catch(TargetInvocationException e)when(e.InnerException is BusinessException){Check(picker.SelectedItem is null&&!Field<Button>(form,"saveButton").Enabled,"Sold-out preferred room cannot silently fall back to another room");}
        }
        using(var form=new FrmAvailability()){
            form.Show();
            var late=new TaskCompletionSource<int>();var applied=false;
            var loading=Field<AsyncLoader>(form,"loader").LoadAsync(_=>late.Task,_=>applied=true);
            Call(form,"MarkSearchDirty");late.SetResult(1);await loading;
            Check(!applied,"Editing search filters cancels a pending old search result");
            var date=new DateOnly(2026,10,28);
            RoomCalendarRow Room(int id,string name)=>new(id,name,id,"Type",id,"R"+id,[new(date,RoomCalendarState.Available,null,null,null,null,null,null)]);
            Set(form,"loadedCalendar",new RoomCalendar(date,1,now,[Room(1,"Homestay A"),Room(2,"Homestay B")]));
            var picker=Field<SearchPicker>(form,"filterPanelField8");picker.SetItems([new Lookup{Id=1,Name="Homestay A"},new Lookup{Id=2,Name="Homestay B"}]);
            picker.SelectId(2);Call(form,"RenderCalendar");
            var grid=Field<PagedGrid>(form,"grid1");
            Check(grid.Grid.Rows.Count==1&&grid.Grid.Rows[0].Cells[0].Value?.ToString()?.Contains("Homestay B")==true,"Calendar homestay filter preserves correct room rows");
            picker.SelectId(null);Call(form,"RenderCalendar");
            Check(grid.Grid.Rows.Count==2,"Clearing calendar homestay filter restores all rooms; selected="+picker.SelectedItem?.Id+" rows="+grid.Grid.Rows.Count);
        }
        using(var form=new FrmBookingCreate(FakeApp(Homestay.DTO.Enums.Position.Staff))){
            Set(form,"quote",new BookingQuote(new(),null!,0,null,[],null!));
            var room=Field<SearchPicker>(form,"editorFieldsField4");
            room.SetItems([new Lookup{Id=0,Name="Auto"}]);room.SelectId(0);
            Check(!Field<Button>(form,"saveButton").Enabled,"A stale automatic-room choice cannot enable saving when quote has no available rooms");
        }
        using(var form=new RoomRefreshForm(FakeApp(Homestay.DTO.Enums.Position.Staff))){
            AvailabilityItem Type(int id)=>new(){RoomTypeId=id,HomestayName="Synthetic",RoomTypeName="Type "+id,AvailableRooms=2,AveragePrice=100000};
            form.Read=(_,_)=>Task.FromResult<IReadOnlyList<AvailabilityItem>>([Type(11)]);
            form.Show();
            var types=Field<SearchPicker>(form,"editorFieldsField3");
            var choices=Field<ComboBox>(types,"choices");
            var checkin=Field<DateTimePicker>(form,"editorFieldsField5");var checkout=Field<DateTimePicker>(form,"editorFieldsField6");
            var status=Field<Label>(form,"roomStatus");
            await Until(()=>choices.Items.Count==1);types.SelectId(11);
            var before=form.Requests.Count;var arrival=checkin.Value.AddDays(2);var departure=arrival.AddDays(2);
            checkout.Value=departure;checkin.Value=arrival;
            Check(choices.Items.Count==0&&!Field<Button>(form,"saveButton").Enabled,"Changing booking dates immediately invalidates old rooms and saving");
            await Until(()=>form.Requests.Count>before&&choices.Items.Count==1);
            Check(form.Requests.Count==before+1&&form.Requests[^1].CheckIn==DateOnly.FromDateTime(arrival)&&form.Requests[^1].CheckOut==DateOnly.FromDateTime(departure),"Editing both booking dates coalesces into one automatic search for final dates");
            Check(types.SelectedItem?.Id==11&&Field<Button>(form,"quoteButton").Enabled,"Automatic refresh retains previous room type only when still available");
            var late=new TaskCompletionSource<IReadOnlyList<AvailabilityItem>>();form.Read=(_,_)=>late.Task;
            before=form.Requests.Count;checkout.Value=checkout.Value.AddDays(1);await Until(()=>form.Requests.Count>before);
            form.Read=(_,_)=>Task.FromResult<IReadOnlyList<AvailabilityItem>>([Type(22)]);
            before=form.Requests.Count;checkout.Value=checkout.Value.AddDays(1);await Until(()=>form.Requests.Count>before&&choices.Items.Count==1);
            late.SetResult([Type(11)]);await Task.Delay(30);
            Check(choices.Items[0] is Lookup {Id:22}&&types.SelectedItem is null,"Late search cannot restore old room types or silently replace selected type");
            Check(status.Text.Contains("chọn lại"),"Sold-out previous type explains that another type must be selected");
            form.Read=(_,_)=>Task.FromResult<IReadOnlyList<AvailabilityItem>>([]);
            before=form.Requests.Count;checkout.Value=checkout.Value.AddDays(1);await Until(()=>form.Requests.Count>before&&status.Text.Contains("Không có loại phòng"));
            Check(choices.Items.Count==0&&!types.Enabled&&!Field<Button>(form,"quoteButton").Enabled&&!Field<Button>(form,"saveButton").Enabled,"No availability has an explicit message and prevents quote or saving");
            before=form.Requests.Count;checkin.Value=checkout.Value;await Task.Delay(550);
            Check(form.Requests.Count==before&&status.Text.Contains("Ngày trả phòng phải sau"),"Invalid date range shows inline correction without querying or leaving unexplained blank picker");
            form.Read=(_,_)=>throw new BusinessException("Synthetic availability unavailable");
            checkout.Value=checkin.Value.AddDays(1);await Until(()=>status.Text.Contains("Synthetic availability unavailable"));
            Check(!Field<Button>(form,"saveButton").Enabled,"Automatic availability error remains visible with saving disabled");
            var pendingSearch=new TaskCompletionSource<IReadOnlyList<AvailabilityItem>>();form.Read=(_,_)=>pendingSearch.Task;
            before=form.Requests.Count;checkout.Value=checkout.Value.AddDays(1);await Until(()=>form.Requests.Count>before);
            form.Close();pendingSearch.SetResult([Type(33)]);await Task.Delay(30);
            Check(form.IsDisposed,"Closing booking form safely cancels automatic room refresh");
        }
        var data=Enumerable.Range(1,45).Select(i=>new NotificationItem(i,"Thông báo "+i,"Booking",i.ToString(),false,now)).ToList();
        bool fail=false,block=false;
        TaskCompletionSource<PagedResult<NotificationItem>>? delayed=null;
        PagedResult<NotificationItem> Page(PageRequest q)=>new(){Items=data.Skip((q.PageNumber-1)*q.PageSize).Take(q.PageSize).ToArray(),TotalCount=data.Count,PageNumber=q.PageNumber,PageSize=q.PageSize};
        using(var inbox=new NotificationInbox((q,_)=>{
            if(fail)throw new IOException("Synthetic read failure");
            if(block){block=false;return delayed!.Task;}
            return Task.FromResult(Page(q));
        },_=>Task.FromResult(data.Count(n=>!n.IsRead)),(id,_)=>{
            var i=data.FindIndex(x=>x.Id==id);data[i]=data[i] with{IsRead=true};return Task.CompletedTask;
        },_=>{for(var i=0;i<data.Count;i++)data[i]=data[i] with{IsRead=true};return Task.CompletedTask;})){
            inbox.Dock=DockStyle.Fill;runner.Controls.Add(inbox);await inbox.RefreshAsync();
            var list=Field<ListBox>(inbox,"list");var next=Field<Button>(inbox,"next");var readAll=Field<Button>(inbox,"readAll");
            Check(inbox.UnreadCount==45&&list.Items.Count==20&&inbox.CurrentPage==1,"Notification inbox shows unread count and first server page");
            delayed=new();block=true;next.PerformClick();next.PerformClick();
            Check(inbox.CurrentPage==2&&!next.Enabled,"Rapid paging cannot skip an unrequested notification page");
            var again=inbox.RefreshAsync();delayed.SetResult(Page(new(){PageNumber=2,PageSize=20}));await again;
            await Until(()=>next.Enabled);
            Check(inbox.CurrentPage==2&&((NotificationItem)list.Items[0]).Id==21,"Queued refresh preserves requested notification page");
            int opened=0;inbox.BookingRequested+=id=>opened=id;list.SelectedIndex=0;
            await (Task)Call(inbox,"OpenSelectedAsync")!;
            Check(opened==21&&data.Single(x=>x.Id==21).IsRead&&inbox.UnreadCount==44,"Opening booking notification marks only selected message read and navigates");
            readAll.PerformClick();await Until(()=>inbox.UnreadCount==0);
            Check(data.All(x=>x.IsRead)&&!readAll.Enabled&&inbox.CurrentPage==1,"Read all updates counter and keeps read-all button disabled");
            var paymentOpened=0;inbox.PaymentRequested+=id=>paymentOpened=id;
            data[0]=data[0] with{RefType="Payment",RefId="701"};await inbox.RefreshAsync();list.SelectedIndex=0;
            await (Task)Call(inbox,"OpenSelectedAsync")!;
            Check(paymentOpened==701,"Refund notification opens payment workflow instead of only showing text");
            fail=true;await inbox.RefreshAsync();
            Check(Field<Label>(inbox,"status").Text.Contains("Không tải được")&&Field<Button>(inbox,"reload").Enabled,"Failed inbox read offers retry without clearing existing data");
            fail=false;next.PerformClick();await Until(()=>inbox.CurrentPage==2);
            data.Clear();await inbox.RefreshAsync();
            Check(inbox.CurrentPage==1&&list.Items.Count==0&&!next.Enabled,"Shrinking notification results clamp to first valid page");
        }
        using(var form=new FrmBookingDetail(FakeApp(Homestay.DTO.Enums.Position.Manager),1)){
            Check(new[]{"confirmButton","rejectButton","roomButton","checkinButton","checkoutButton","noShowButton","unavailableButton"}.All(name=>!Field<Button>(form,name).Enabled),
                "Lifecycle buttons start disabled until booking detail is loaded");
        }
        using(var form=new DelayedDetailForm(FakeApp(Homestay.DTO.Enums.Position.Manager))){
            var first=new TaskCompletionSource<BookingDetail>();form.Read=_=>first.Task;
            form.Show();await Until(()=>form.ReadCount==1);
            Check(!Field<Button>(form,"unavailableButton").Enabled,"Slow initial detail read cannot expose an actionable cancellation button");
            var detail=new BookingDetail{Booking=new(){Id=1,Code="SYNTHETIC",Status=Homestay.DTO.Enums.BookingStatus.Deposited},
                Customer=new(1,"Synthetic","0900000000",null,null,true,null,true,[]),
                CancellationPolicy=new(1,null,"Synthetic",Homestay.DTO.Enums.CancellationScope.All,true,[]),AllowedActions=[BookingAction.CancelHomestayUnavailable]};
            first.SetResult(detail);await Until(()=>Field<Button>(form,"unavailableButton").Visible&&Field<Button>(form,"unavailableButton").Enabled);
            Check(true,"Cancellation button becomes actionable only after loaded BLL actions permit it");
            var second=new TaskCompletionSource<BookingDetail>();form.Read=_=>second.Task;var loading=form.RefreshFromServerAsync();
            Check(!Field<Button>(form,"unavailableButton").Enabled&&Field<BookingDetail?>(form,"detail") is null,"Reload clears old booking snapshot and disables stale mutation buttons");
            second.SetResult(detail with{AllowedActions=[]});await loading;
            Check(!Field<Button>(form,"unavailableButton").Enabled&&!Field<Button>(form,"unavailableButton").Visible,"Reload does not retain a cancellation action removed by the BLL");
            var closing=new TaskCompletionSource<BookingDetail>();form.Read=_=>closing.Task;loading=form.RefreshFromServerAsync();
            form.Close();closing.SetResult(detail);await loading;
            Check(form.IsDisposed,"Closing detail during reload safely discards its delayed actions");
        }
        foreach(var role in new[]{Homestay.DTO.Enums.Position.Staff,Homestay.DTO.Enums.Position.Manager,Homestay.DTO.Enums.Position.Accountant}){
            var app=FakeApp(role);
            var registry=typeof(Homestay.GUI.Shell.FrmMain).Assembly.GetType("Homestay.GUI.Shell.ScreenRegistry")!;
            var entries=(Array)registry.GetMethod("Production",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[app,app])!;
            var ids=entries.Cast<object>().Select(e=>(string)e.GetType().GetProperty("Id")!.GetValue(e)!).ToArray();
            Check(ids.Contains("F27")==(role==Homestay.DTO.Enums.Position.Accountant)&&ids.Contains("F29")==(role==Homestay.DTO.Enums.Position.Accountant)&&
                ids.Contains("F20")==(role==Homestay.DTO.Enums.Position.Manager),"M31 read menus respect role "+role);
            if(role==Homestay.DTO.Enums.Position.Staff){
                using var booking=new Homestay.GUI.Staff.FrmBookingDetail(app,1);
                Check(new[]{"confirmButton","rejectButton","roomButton","checkinButton","checkoutButton","noShowButton","unavailableButton"}
                    .All(name=>Field<Button>(booking,name).Tag is null),"Integrated booking buttons do not retain placeholder message handlers");
            }
            if(role==Homestay.DTO.Enums.Position.Accountant){
                using var receipts=new Homestay.GUI.Accountant.FrmDepositVerification(app);
                using var refunds=new Homestay.GUI.Accountant.FrmRefundQueue(app);
                Check(!Field<Button>(receipts,"verifyButton").Enabled&&!Field<Button>(receipts,"rejectButton").Enabled&&
                    Field<Label>(receipts,"pendingNotice").Text.Contains("đang được hoàn thiện"),"Receipt actions require a loaded eligible selection");
                Check(!Field<Button>(refunds,"payButton").Enabled,"Refund payment requires a selected pending refund");
                var refund=new Homestay.DTO.Payments.PaymentQueueItem{Id=1,Type=Homestay.DTO.Enums.PaymentType.Refund,Status=Homestay.DTO.Enums.PaymentStatus.Pending,Method=Homestay.DTO.Enums.PaymentMethod.BankTransfer};
                void SetRefund(Homestay.DTO.Payments.PaymentQueueItem item){
                    Set(refunds,"rows",(IReadOnlyList<Homestay.DTO.Payments.PaymentQueueItem>)new[]{item});
                    Field<PagedGrid>(refunds,"grid0").SetPage(new PagedResult<Homestay.DTO.Payments.PaymentQueueItem>{Items=[item],PageNumber=1,PageSize=50,TotalCount=1},_=>new object?[]{"PC","B","C","H","10","C",null,"Reason","Now"});
                    Field<PagedGrid>(refunds,"grid0").Grid.Rows[0].Selected=true;
                }
                SetRefund(refund);
                Check((bool)Call(refunds,"CanPaySelected")!,"Actual refund can be recorded when legacy destination account is not stored");
                SetRefund(refund with{IsRecordedByCurrentUser=true});
                Check(!(bool)Call(refunds,"CanPaySelected")!,"Refund creator cannot record their own refund payment");
                SetRefund(refund with{Status=Homestay.DTO.Enums.PaymentStatus.Verified});
                Check(!(bool)Call(refunds,"CanPaySelected")!,"Already paid refund cannot be submitted again");
            }
            if(role==Homestay.DTO.Enums.Position.Manager){
                using var approvals=new Homestay.GUI.Manager.FrmCancellationApproval(app);
                using var request=new Homestay.GUI.Staff.FrmCancellation(app,1);
                Check(!Field<Button>(approvals,"approveButton").Enabled&&!Field<Button>(approvals,"rejectButton").Enabled,"Cancellation review requires loaded eligible request");
                Check(!Field<Button>(request,"saveButton").Enabled,"Cancellation request stays disabled before a current quote is loaded");
                Set(approvals,"selected",new Homestay.DTO.ChangeRequests.CancelRequestDetail(new(){Status=Homestay.DTO.Enums.ChangeRequestStatus.Pending,CanReview=false,IsRequestedByCurrentUser=true},new()));
                Call(approvals,"UpdateSelection");
                Check(!Field<Button>(approvals,"approveButton").Enabled&&Field<Label>(approvals,"selectionStatus").Text.Contains("Quản lý khác"),"Own cancellation request explains why another manager must review");
                Set(approvals,"selected",new Homestay.DTO.ChangeRequests.CancelRequestDetail(new(){Status=Homestay.DTO.Enums.ChangeRequestStatus.Pending,CanReview=true,QuotedRefundAmount=1000},new()));
                Call(approvals,"UpdateSelection");
                Check(Field<Button>(approvals,"approveButton").Enabled&&Field<Button>(approvals,"rejectButton").Enabled&&Field<Label>(approvals,"selectionStatus").Text.Contains("1.000"),"Eligible pending cancellation enables review and displays server quote");
            }
        }
        var capability=typeof(Homestay.GUI.AppServices).Assembly.GetType("Homestay.GUI.Common.LifecycleUi")!;
        var canWrite=capability.GetMethod("CanWrite",BindingFlags.Public|BindingFlags.Static)!;
        Check((bool)canWrite.Invoke(null,[Homestay.DTO.Bookings.BookingAction.ConfirmOnBehalf])!&&
            (bool)canWrite.Invoke(null,[Homestay.DTO.Bookings.BookingAction.CheckOut])!,"M33A implemented booking actions are available");
        Check((bool)canWrite.Invoke(null,[Homestay.DTO.Bookings.BookingAction.CancelHomestayUnavailable])!&&
            (bool)canWrite.Invoke(null,[Homestay.DTO.Bookings.BookingAction.SubmitCancel])!,"M33B implemented cancellation actions are available");
        var pending=new TaskCompletionSource<PagedResult<NotificationItem>>();
        using(var inbox=new NotificationInbox((_,_)=>pending.Task,_=>Task.FromResult(0),(_,_)=>Task.CompletedTask,_=>Task.CompletedTask)){
            var task=inbox.RefreshAsync();inbox.Dispose();pending.SetResult(new(){Items=[],TotalCount=0,PageNumber=1,PageSize=20});await task;
            Check(true,"Closing notification inbox discards pending results safely");
        }
    }

    static Homestay.GUI.AppServices FakeApp(Homestay.DTO.Enums.Position role){
        var app=new Homestay.GUI.AppServices(new Homestay.GUI.Configuration.AppSettings{
            Logging=new(){Folder=Path.Combine(Path.GetTempPath(),"HomestayGuiCompletionLogs")}},Path.Combine(Path.GetTempPath(),"unused-completion-connection.dat"));
        var current=new CurrentSession();
        current.Set(new Homestay.DTO.Auth.SessionInfo{UserId=800001,EmployeeId=800001,Position=role,EmployeeCode="SYNTHETIC",FullName="Synthetic",LoginAtUtc=DateTime.UtcNow});
        var authorizer=new Authorizer(current);
        var runtime=typeof(Homestay.GUI.AppServices).GetNestedType("Runtime",BindingFlags.NonPublic)!;
        var ctor=runtime.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single(c=>c.GetParameters().Length>1);
        var args=ctor.GetParameters().Select(p=>p.ParameterType==typeof(CurrentSession)?(object)current:p.ParameterType==typeof(Authorizer)?authorizer:null).ToArray();
        typeof(Homestay.GUI.AppServices).GetField("runtime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(app,ctor.Invoke(args));
        return app;
    }
}

internal sealed class RoomRefreshForm(Homestay.GUI.AppServices services):FrmBookingCreate(services){
    public List<AvailabilitySearchRequest> Requests { get; }=[];
    public Func<AvailabilitySearchRequest,CancellationToken,Task<IReadOnlyList<AvailabilityItem>>> Read=(_,_)=>Task.FromResult<IReadOnlyList<AvailabilityItem>>([]);
    protected override Task<IReadOnlyList<AvailabilityItem>> FindRoomTypesAsync(AvailabilitySearchRequest request,CancellationToken ct){Requests.Add(request);return Read(request,ct);}
}
internal sealed class DelayedDetailForm(Homestay.GUI.AppServices services):FrmBookingDetail(services,1){
    public int ReadCount;
    public Func<CancellationToken,Task<BookingDetail>> Read=_=>Task.FromResult(new BookingDetail());
    protected override Task<BookingDetail> ReadDetailAsync(CancellationToken ct){ReadCount++;return Read(ct);}
}