using System.Text.RegularExpressions;
using Homestay.GUI;
using Homestay.GUI.Configuration;
using Homestay.GUI.Workflow;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.BLL.Infrastructure;
using Microsoft.Data.SqlClient;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) {
        ApplicationConfiguration.Initialize();
        using var runner=new Form{ShowInTaskbar=false,Opacity=0,Width=10,Height=10};
        runner.Shown+=async(_,_)=>{try{await Run(args);}catch(Exception e){Console.WriteLine("HARNESS FAIL "+e.GetType().Name);Environment.ExitCode=1;}finally{runner.Close();}};
        Application.Run(runner);
    }
    static async Task Run(string[] args) {
var root=Path.GetFullPath(args[0]);
var lines=new List<string>();
var output=Path.GetFullPath(args.Length>1?args[1]:Path.Combine(root,"outputs"));
var privateRoot=Path.Combine(root,"work/private/live-flow-checks",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);Directory.CreateDirectory(privateRoot);
var sourceProfile=new ConnectionStore(Path.Combine(root,"work/private/sql-win/connection.dat")).Load();
var dbName="HomestayWinCheck_"+Guid.NewGuid().ToString("N")[..16];
bool created=false;
await using var admin=new SqlConnection(new SqlConnectionStringBuilder{
    DataSource=@"lpc:.\SQLEXPRESS",InitialCatalog="master",IntegratedSecurity=true,
    Encrypt=true,TrustServerCertificate=true,Pooling=false,ConnectTimeout=8
}.ConnectionString);
var progressPath=Path.Combine(output,"WIN-DOT45-PROGRESS.txt");File.WriteAllText(progressPath,"WIN DOT45 started"+Environment.NewLine);
void Pass(bool value,string name){if(!value)throw new InvalidDataException("Kiểm tra không đạt: "+name);lines.Add("PASS "+name);File.AppendAllText(progressPath,"PASS "+name+Environment.NewLine);Console.WriteLine("PASS "+name);}
async Task Execute(SqlConnection connection,string sql){
    await using var cmd=connection.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=120;await cmd.ExecuteNonQueryAsync();
}
string Password()=>"Q"+Guid.NewGuid().ToString("N")+"a8!";
try{
    // Windows authentication is only used for the newly created fixture DB.
    // No existing DB is dropped, no SQL login is created or changed.
    lines.Add("Build=WIN-DOT45-20261028; WindowsAdministrator="+ConnectionStore.IsAdministrator());
    if (!ConnectionStore.IsAdministrator())
        throw new UnauthorizedAccessException("Cần chạy launcher bằng Run as administrator trong Windows.");
    await admin.OpenAsync();
    await using(var check=admin.CreateCommand()){
        check.CommandText="SELECT SUSER_ID(N'homestay_app')";
        if(await check.ExecuteScalarAsync() is null or DBNull)throw new InvalidDataException("Thiếu SQL login homestay_app; dừng trước khi tạo database.");
    }
    await Execute(admin,"IF DB_ID(N'"+dbName+"') IS NOT NULL THROW 50001,'Test database already exists',1; CREATE DATABASE ["+dbName+"] COLLATE Vietnamese_CI_AS;");
    created=true;
    await Execute(admin,"ALTER DATABASE ["+dbName+"] SET READ_COMMITTED_SNAPSHOT ON;");
    await admin.ChangeDatabaseAsync(dbName);
    var managerPassword=Password();var demoPassword=Password();
    foreach(var file in new[]{"001_schema.sql","002_indexes.sql","003_views.sql","004_security.sql","010_seed_reference.sql","020_seed_demo.sql"}){
        var sql=await File.ReadAllTextAsync(Path.Combine(root,"work/LTTQ/database",file));
        sql=sql.Replace("$(AppSqlPassword)",sourceProfile.Password.Replace("'","''"))
            .Replace("$(FirstManagerPassword)",managerPassword).Replace("$(DemoPassword)",demoPassword);
        foreach(var batch in Regex.Split(sql,@"^\s*GO\s*(?:--[^\r\n]*)?$",RegexOptions.Multiline|RegexOptions.IgnoreCase)){
            if(!string.IsNullOrWhiteSpace(batch))await Execute(admin,batch);
        }
    }
    lines.Add("Fixture database="+dbName);
    var profile=new ConnectionProfile{Server=sourceProfile.Server,Database=dbName,Username=sourceProfile.Username,
        Password=sourceProfile.Password,Encrypt=sourceProfile.Encrypt,TrustServerCertificate=sourceProfile.TrustServerCertificate};
    var settings=new AppSettings{Logging=new LoggingSettings{Folder=Path.Combine(privateRoot,"logs")}};
    AppServices App(string name){
        var store=new ConnectionStore(Path.Combine(privateRoot,name+".dat"));store.Save(profile);
        return new AppServices(settings,store.Path);
    }
    var manager=App("manager");await manager.CheckConnectionAsync(profile,default);
    Pass((await manager.ReadSchemaVersionsAsync(profile,default)).SequenceEqual(new[]{1,2,3,4,10,20}),"F04 schema versions on separate SQL database");
    var saved=manager.Connections.Load();
    Pass(saved.Database==dbName&&saved.Password==profile.Password,"F04 connection check then encrypted save/load");saved.Password="";
    await manager.InitializeAsync();
    var managerNext=Password();
    var session=await new LoginFlow(manager).SignInAsync(new LoginRequest{Username="NV0001",Password=managerPassword,MachineName=manager.MachineName,AppVersion=manager.AppVersion},async()=>{
        await manager.ChangePasswordAsync(new ChangePasswordRequest{OldPassword=managerPassword,NewPassword=managerNext,ConfirmPassword=managerNext});return true;});
    Pass(session is{Position:Position.Manager,MustChangePassword:false},"Manager login with required password change");
    Pass(await manager.HeartbeatAsync(),"Manager heartbeat remains valid after password change");
    var staff=App("staff");await staff.InitializeAsync();
    await staff.LoginAsync(new LoginRequest{Username="NV0002",Password=demoPassword,MachineName=staff.MachineName,AppVersion=staff.AppVersion});
    Pass(staff.Session?.Position==Position.Staff&&!staff.Can(Permission.EmployeeManage),"Staff login and permissions");
    var accountant=App("accountant");await accountant.InitializeAsync();
    await accountant.LoginAsync(new LoginRequest{Username="NV0006",Password=demoPassword,MachineName=accountant.MachineName,AppVersion=accountant.AppVersion});
    Pass(accountant.Session?.Position==Position.Accountant&&!accountant.Can(Permission.EmployeeManage),"Accountant login and permissions");
    var list=await manager.Staff.ListAsync(new EmployeeSearchRequest{Page=new PageRequest{PageSize=10,SortColumn="EmployeeCode"}});
    Pass(list.Items.Count>0&&list.TotalCount>=7,"F18 list through SQL service with server paging");
    var createdEmployee=await manager.Staff.CreateAsync(new CreateEmployeeRequest{FullName="KIỂM THỬ WIN - chỉ ở database tạm",Phone="+84987654321",Position=Position.Staff});
    var newStaff=App("new-staff");await newStaff.InitializeAsync();var newPassword=Password();
    var newSession=await new LoginFlow(newStaff).SignInAsync(new LoginRequest{Username=createdEmployee.EmployeeCode,Password=createdEmployee.TemporaryPassword,MachineName=newStaff.MachineName,AppVersion=newStaff.AppVersion},async()=>{
        await newStaff.ChangePasswordAsync(new ChangePasswordRequest{OldPassword=createdEmployee.TemporaryPassword,NewPassword=newPassword,ConfirmPassword=newPassword});return true;});
    Pass(newSession is{MustChangePassword:false},"F18 created employee signs in and changes temporary password");
    await newStaff.UnlockAsync(newPassword);Pass(await newStaff.HeartbeatAsync(),"Unlock and heartbeat for created employee");
    await newStaff.LogoutAsync();
    var detail=await manager.Staff.GetAsync(createdEmployee.EmployeeId);
    await manager.Staff.LockAccountAsync(new AccountActionRequest{UserId=detail.Employee.UserId,RowVer=detail.Employee.RowVer,Reason="Kiểm thử trên database tạm"});
    detail=await manager.Staff.GetAsync(createdEmployee.EmployeeId);
    Pass(detail.Employee.AccountStatus==UserStatus.Locked,"F18 lock account");
    await manager.Staff.UnlockAccountAsync(new AccountActionRequest{UserId=detail.Employee.UserId,RowVer=detail.Employee.RowVer});
    detail=await manager.Staff.GetAsync(createdEmployee.EmployeeId);
    Pass(detail.Employee.AccountStatus==UserStatus.Active,"F18 unlock account");
    var reset=await manager.Staff.ResetPasswordAsync(new AccountActionRequest{UserId=detail.Employee.UserId,RowVer=detail.Employee.RowVer});
    await newStaff.LoginAsync(new LoginRequest{Username=createdEmployee.EmployeeCode,Password=reset.TemporaryPassword,MachineName=newStaff.MachineName,AppVersion=newStaff.AppVersion});
    Pass(newStaff.Session is{MustChangePassword:true},"F18 reset password requires another change");
    await newStaff.LogoutAsync();
    Pass((await manager.Catalog.ListAreasAsync()).Count>0&&
        (await manager.Catalog.ListItemsAsync(CatalogKind.Amenity)).Count>0&&
        (await manager.Catalog.ListItemsAsync(CatalogKind.RoomCategory)).Count>0&&
        (await manager.Catalog.ListHolidaysAsync(2026)).Count>0&&
        (await manager.Catalog.ListPoliciesAsync()).Count>0&&
        (await manager.Catalog.ListSettingsAsync()).Count>0,"F24 reads six catalog tabs");
    var amenityId=await manager.Catalog.CreateItemAsync(CatalogKind.Amenity,new("Tiện nghi kiểm thử WIN"));
    var item=(await manager.Catalog.ListItemsAsync(CatalogKind.Amenity)).Single(x=>x.Id==amenityId);
    await manager.Catalog.UpdateItemAsync(CatalogKind.Amenity,item,new("Tiện nghi kiểm thử đã sửa"));
    await manager.Catalog.SetActiveAsync(CatalogKind.Amenity,item.Id,true,false);
    Pass((await manager.Catalog.ListItemsAsync(CatalogKind.Amenity)).Single(x=>x.Id==amenityId) is{Name:"Tiện nghi kiểm thử đã sửa",IsActive:false},"F24 creates, edits and deactivates catalog item");
    var setting=(await manager.Catalog.ListSettingsAsync()).Single(x=>x.Key=="Session.IdleMinutes");
    await manager.Catalog.UpdateSettingAsync(new(setting.Key,setting.Value=="30"?"31":"30",setting.Value));
    Pass((await manager.Catalog.ListSettingsAsync()).Single(x=>x.Key==setting.Key).Value!=setting.Value,"F24 updates setting with original value");

    var tomorrow=DateOnly.FromDateTime((await staff.ServerNowAsync()).AddHours(7));
    var available=await staff.Availability.SearchAsync(new(){CheckIn=tomorrow,CheckOut=tomorrow.AddDays(1),Guests=1});
    var sellable=available.OrderByDescending(x=>x.AvailableRooms).First(x=>x.AvailableRooms>1);
    Pass(available.Count>0,"W2 availability search uses real SQL");
    var calendar=await staff.Availability.GetCalendarAsync(new(null,sellable.AreaId,tomorrow,14));
    Pass(calendar.Days==14&&calendar.Rooms.Count>0,"W2 calendar returns real room-night cells");
    var client=await staff.Customers.CreateAsync(new(){FullName="Khách kiểm thử W2",Phone="+84987654444",IdNumber="123456789012"});
    Pass(client.Customer.IsIdentityMasked&&!client.Customer.IdNumber!.Contains("12345678"),"W2 staff customer identity is masked");
    Pass((await staff.Customers.FindByPhoneAsync("0987654444"))?.Id==client.Customer.Id,"W2 customer phone lookup");
    var quote=await staff.Bookings.QuoteAsync(new(sellable.RoomTypeId,tomorrow,tomorrow.AddDays(1),1));
    var booking=await staff.Bookings.CreateAsync(new(){CustomerId=client.Customer.Id,RoomTypeId=sellable.RoomTypeId,
        CheckIn=tomorrow,CheckOut=tomorrow.AddDays(1),Guests=1,ContactName=client.Customer.FullName,ContactPhone=client.Customer.Phone,SourceChannel=SourceChannel.Phone});
    Pass(booking.Booking.Status==BookingStatus.AwaitingPayment&&booking.TotalAmount==quote.Pricing.TotalAmount&&booking.TransferInstructions is not null,"W2 create booking commits real hold and pricing");
    Pass((await staff.Bookings.ListAsync(new(){Code=booking.Booking.Code})).Items.Single().Id==booking.Booking.Id,"W2 exact booking search");
    Pass((await staff.Bookings.QuickSearchAsync(booking.Booking.Code)).Bookings.Any(x=>x.Id==booking.Booking.Id),"W2 shell quick search service");
    System.Reflection.FieldInfo Info(object form,string name){
        for(Type? t=form.GetType();t is not null;t=t.BaseType){var f=t.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);if(f is not null)return f;}
        throw new InvalidDataException("Missing UI field: "+name);
    }
    T Field<T>(object form,string name)=>(T)Info(form,name).GetValue(form)!;
    async Task CallAsync(object form,string name){
        var method=form.GetType().GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(form,null)!;
    }
    object? calendarDraft=null;
    async Task Until(Func<bool> ready,string stage) {
        var end=DateTime.UtcNow.AddSeconds(20);
        while(!ready()){if(DateTime.UtcNow>end)throw new InvalidDataException("UI timeout: "+stage);await Task.Delay(100);}
    }
    async Task Ui(Form form,Func<Form,Task> inspect) {
        using(form){form.Show();try{await inspect(form);}finally{form.Close();}}
    }
    await Ui(new Homestay.GUI.Staff.FrmDashboard(staff),async f=>{
        await Until(()=>Field<Homestay.GUI.Common.PagedGrid>(f,"grid1").Grid.Rows.Count>0,"dashboard");
        Pass(Field<Label>(f,"pendingNotice").Visible==false,"W2 F05 reads live queues, no placeholder banner");
    });
    await Ui(new Homestay.GUI.Staff.FrmAvailability(staff),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");
        await Until(()=>grid.Grid.Rows.Count>0,"availability");
        Pass(grid.Grid.Rows.Count>0,"W2 F06 loads real availability in GUI");
        Field<DateTimePicker>(f,"filterPanelField2").Value=tomorrow.AddDays(3).ToDateTime(TimeOnly.MinValue);
        Pass(!Field<Button>(f,"bookButton").Enabled&&!Field<Button>(f,"priceButton").Enabled,"Completion changed search filters block stale booking and quote actions");
        Field<Homestay.GUI.Common.SearchPicker>(f,"filterPanelField0").SelectId(sellable.AreaId);
        Field<DateTimePicker>(f,"filterPanelField1").Value=tomorrow.ToDateTime(TimeOnly.MinValue);
        Field<TabControl>(f,"viewTabs").SelectedIndex=1;
        var calendarGrid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid1");
        await Until(()=>calendarGrid.Grid.Rows.Count>0,"calendar GUI");
        Field<Homestay.GUI.Common.SearchPicker>(f,"filterPanelField8").SelectId(sellable.HomestayId);
        var rooms=Field<IReadOnlyList<Homestay.DTO.Availability.RoomCalendarRow>>(f,"calendar");
        Pass(rooms.Count>0&&rooms.All(r=>r.HomestayId==sellable.HomestayId),"Completion calendar filters correct homestay");
        var index=rooms.Select((r,i)=>(r,i)).First(x=>x.r.RoomTypeId==sellable.RoomTypeId&&x.r.Cells[0].State==Homestay.DTO.Availability.RoomCalendarState.Available).i;
        void Capture(string id,object? arg){if(id=="F07")calendarDraft=arg;}
        staff.NavigationRequested+=Capture;
        try{
            typeof(Homestay.GUI.Staff.FrmAvailability).GetMethod("OpenCalendarCell",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(f,[index,1]);
        }finally{staff.NavigationRequested-=Capture;}
        Pass(calendarDraft is Homestay.GUI.Staff.BookingDraftSelection selection&&selection.PreferredRoomId==rooms[index].RoomId&&selection.Quote.CheckIn==tomorrow,
            "Completion calendar opens draft for exact room and date");
    });
    await Ui(new Homestay.GUI.Staff.FrmCustomers(staff,client.Customer),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");
        await Until(()=>grid.Grid.Rows.Count>0,"customers");
        Pass(grid.Grid.Rows.Count==1&&grid.Grid.Rows[0].Cells[2].Value?.ToString()==client.Customer.Phone,"W2 F13 applies selected-customer phone filter");
        Pass(Field<Button>(f,"phoneButton").Enabled,"Audit staff can request phone change; BLL protects customers with bookings");
    });
    await Ui(new Homestay.GUI.Staff.FrmBookings(staff,new(){Code=booking.Booking.Code}),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");
        await Until(()=>grid.Grid.Rows.Count>0,"bookings");
        Pass(grid.Grid.Rows.Count==1,"W2 F08 loads exact matching booking in GUI");
        Pass(Field<TabControl>(f,"viewTabs").TabPages.Count==11,"Completion all eleven booking queues are exposed");
        Pass(Field<TextBox>(f,"filterPanelField0").Text==booking.Booking.Code,"Completion initial booking search is visible and editable");
        Field<TextBox>(f,"filterPanelField0").Text=client.Customer.FullName;
        Field<Button>(f,"reloadButton").PerformClick();await Until(()=>grid.Grid.Rows.Count>0,"customer name booking filter");
        Pass(grid.Grid.Rows.Cast<DataGridViewRow>().All(r=>r.Cells[1].Value?.ToString()==client.Customer.FullName),"Completion booking customer-name filter uses SQL");
        grid.Grid.Rows[0].Selected=true;await CallAsync(f,"RefreshSelectionActionsAsync");
        var allowed=await staff.Bookings.GetAsync(Field<IReadOnlyList<Homestay.DTO.Bookings.BookingListItem>>(f,"rows")[0].Id);
        Pass(Field<Button>(f,"claimButton").Enabled==allowed.AllowedActions.Contains(Homestay.DTO.Bookings.BookingAction.Claim),
            "Completion booking action button matches BLL allowed actions");
        var deadline=grid.Grid.Rows[0].Cells[10].FormattedValue?.ToString()??"";
        Pass(deadline.Contains("Còn")||deadline.Contains("Đã quá hạn"),"Completion booking deadline displays server countdown");
        Field<Button>(f,"clearFilters").PerformClick();await Until(()=>Field<TextBox>(f,"filterPanelField0").Text=="","clear filters");
        Pass(Field<TextBox>(f,"filterPanelField0").Text=="","Completion clear filters removes implicit exact-code filter");
    });
    await Ui(new Homestay.GUI.Staff.FrmBookingDetail(staff,booking.Booking.Id),async f=>{
        await Until(()=>Field<Homestay.GUI.Common.PagedGrid>(f,"grid0").Grid.Rows.Count>0,"detail");
        Pass(Field<Button>(f,"receiptButton").Enabled,"W2 F09 exposes deposit only when BLL allows");
    });
    await Ui(new TestBookingForm(staff,calendarDraft??throw new InvalidDataException("Missing calendar draft")),async f=>{
        await Until(()=>!string.IsNullOrEmpty(Field<TextBox>(f,"filterPanelField1").Text),"create quote");
        Pass(Field<Homestay.GUI.Common.PagedGrid>(f,"nightlyPrices").Grid.Rows.Count==1,"W2 F07 shows actual nightly quote without creating another booking");
        Info(f,"customer").SetValue(f,client.Customer);
        Field<TextBox>(f,"editorFieldsField8").Text=client.Customer.FullName;
        Field<TextBox>(f,"editorFieldsField9").Text=client.Customer.Phone;
        Field<Button>(f,"saveButton").PerformClick();
        await Until(()=>((TestBookingForm)f).Completed,"GUI booking save");
        booking=Field<Homestay.DTO.Bookings.BookingDetail>(f,"created");
        Pass(booking.Booking.Status==BookingStatus.AwaitingPayment,"W2 F07 Save button creates a real booking");
        Pass(booking.Booking.RoomId==((Homestay.GUI.Staff.BookingDraftSelection)calendarDraft!).PreferredRoomId,"Completion saved booking keeps room selected from calendar");
    });
    await Ui(new TestReceiptForm(staff,booking.Booking.Id),async f=>{
        await Until(()=>Field<Button>(f,"saveButton").Enabled,"receipt");
        Pass(Field<NumericUpDown>(f,"editorFieldsField1").Value==booking.AmountDue,"W2 F10 loads actual amount due");
        var pdf=Path.Combine(privateRoot,"receipt.pdf");await File.WriteAllBytesAsync(pdf,"%PDF-1.7\nSynthetic receipt"u8.ToArray());
        var evidence=await Homestay.GUI.Common.EvidencePresentation.ReadAsync(pdf);
        Info(f,"evidence").SetValue(f,evidence.Content);Info(f,"evidenceName").SetValue(f,evidence.FileName);
        Field<ComboBox>(f,"editorFieldsField3").SelectedIndex=0;
        Field<Button>(f,"saveButton").PerformClick();
        await Until(()=>f.IsDisposed,"GUI receipt save");
    });
    var afterGuiReceipt=await staff.Bookings.GetAsync(booking.Booking.Id);
    Pass(afterGuiReceipt.Booking.Status==BookingStatus.PaymentSubmitted&&afterGuiReceipt.Payments.Any(p=>p.TransferredAtUtc is not null),"W2 F10 Save button records real receipt and moves booking to verification");
    var refreshed=await staff.Bookings.GetAsync(booking.Booking.Id);
    Pass(!refreshed.AllowedActions.Contains(Homestay.DTO.Bookings.BookingAction.RecordDeposit),"W2 prevents duplicate deposit through allowed actions");
    Pass(await accountant.Notifications.CountUnreadAsync()>0,"W2 notifies accountant after receipt");
    var stored=afterGuiReceipt.Payments.Last(p=>p.EvidenceFileId is not null);
    var storedFile=await staff.Files.GetEvidenceAsync(booking.Booking.Id,stored.EvidenceFileId!.Value);
    Pass(storedFile.ContentType=="application/pdf","Completion PDF evidence is stored and read through FileService");
    var staffUnread=await staff.Notifications.CountUnreadAsync();
    using(var host=new Form()){
        var inbox=new Homestay.GUI.Common.NotificationInbox((q,ct)=>accountant.Notifications.ListAsync(q,ct),
            ct=>accountant.Notifications.CountUnreadAsync(ct),(id,ct)=>accountant.Notifications.MarkReadAsync(id,ct),
            ct=>accountant.Notifications.MarkAllReadAsync(ct)){Dock=DockStyle.Fill};
        host.Controls.Add(inbox);host.Show();await inbox.RefreshAsync();
        Pass(inbox.UnreadCount>0,"Completion notification inbox reads real accountant messages");
        Field<Button>(inbox,"readAll").PerformClick();await Until(()=>inbox.UnreadCount==0,"mark accountant notifications read");
        Pass(await accountant.Notifications.CountUnreadAsync()==0&&!Field<Button>(inbox,"readAll").Enabled,"Completion read-all persists through SQL and disables button");
        Pass(await staff.Notifications.CountUnreadAsync()==staffUnread,"Completion accountant read-all preserves staff notifications");
        host.Close();
    }
    await staff.Jobs.RunOnceAsync();
    Pass(await staff.Jobs.RunOnceAsync()==0,"W2 expired-hold job is repeatable");

    var recipients=await staff.Bookings.ListAssigneesAsync(booking.Booking.Id);
    Pass(recipients.Count>0&&recipients.All(x=>x.Id!=staff.Session!.EmployeeId),"M31 staff assignee lookup works without EmployeeManage");
    var beforeAssignment=await staff.Bookings.GetAsync(booking.Booking.Id);
    var reassigned=await staff.Bookings.ReassignAsync(new(booking.Booking.Id,recipients[0].Id,beforeAssignment.Booking.RowVer));
    Pass(reassigned.Booking.AssignedEmployeeId==recipients[0].Id,"M31 reassignment validates recipient through real SQL service");
    await Ui(new Homestay.GUI.Accountant.FrmDepositVerification(accountant),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");
        await Until(()=>grid.Grid.Rows.Count>0,"M31 receipt queue");
        var queue=Field<IReadOnlyList<Homestay.DTO.Payments.PaymentQueueItem>>(f,"rows");
        var index=queue.Select((x,i)=>(x,i)).Single(x=>x.x.Id==stored.Id).i;
        grid.Grid.Rows[index].Selected=true;await CallAsync(f,"SelectAsync");
        Pass(Field<TextBox>(f,"filterPanelField1").Text==booking.Booking.Code,"M31 F27 reads actual receipt and booking details");
        Pass(Field<Button>(f,"zoomButton").Enabled&&Field<Button>(f,"verifyButton").Enabled&&Field<Button>(f,"rejectButton").Enabled,
            "M33A F27 enables eligible receipt review and evidence");
        Field<Button>(f,"verifyButton").PerformClick();
        await Until(()=>!Field<IReadOnlyList<Homestay.DTO.Payments.PaymentQueueItem>>(f,"rows").Any(x=>x.Id==stored.Id),"verify receipt UI");
        Pass((await staff.Bookings.GetAsync(booking.Booking.Id)).Booking.Status==BookingStatus.Deposited,"M33A F27 Verify button commits verified receipt and deposited booking");
    });
    await Ui(new Homestay.GUI.Staff.FrmBookingDetail(staff,booking.Booking.Id),async f=>{
        await Until(()=>Field<Button>(f,"confirmButton").Enabled,"confirm action");
        var replacements=await staff.Bookings.ListConfirmationRoomsAsync(booking.Booking.Id);
        var change=replacements.Count>0;
        using var automatic=new System.Windows.Forms.Timer{Interval=30};
        string expectedDialog="";
        automatic.Tick+=(_,_)=>{
            foreach(var dialog in Application.OpenForms.OfType<Homestay.GUI.Common.FieldsDialog>().ToArray()){
                if(dialog.Text!=expectedDialog)continue;
                var note=dialog.Controls.Find("note",true).OfType<TextBox>().Single();note.Text="Kiểm thử giao diện trên database tạm";
                var room=dialog.Controls.Find("room",true).OfType<ComboBox>().FirstOrDefault();if(room is not null)room.SelectedIndex=0;
                automatic.Stop();Field<Button>(dialog,"saveButton").PerformClick();
            }
        };
        async Task Act(string button,string dialog,string status){
            expectedDialog=dialog;automatic.Start();Field<Button>(f,button).PerformClick();
            try{await Until(()=>Field<TextBox>(f,"filterPanelField1").Text==status,"lifecycle "+status);}
            finally{automatic.Stop();}
        }
        var originalTotal=booking.TotalAmount;
        await Act(change?"roomButton":"confirmButton",change?"Đổi phòng và xác nhận thay chủ":"Xác nhận thay chủ","Đã xác nhận");
        var confirmed=await staff.Bookings.GetAsync(booking.Booking.Id);
        Pass(confirmed.Booking.Status==BookingStatus.Confirmed&&(!change||confirmed.Booking.RoomId==replacements[0].Id),"M33A F09 confirms on behalf and applies selected replacement room");
        Pass(!Field<Button>(f,"noShowButton").Visible,"M33A no-show is not offered on the check-in date");
        await Until(()=>Field<Button>(f,"checkinButton").Enabled,"checkin enabled");
        await Act("checkinButton","Ghi chú nhận phòng thay chủ","Đang ở");
        Pass((await staff.Bookings.GetAsync(booking.Booking.Id)).Booking.Status==BookingStatus.CheckedIn,"M33A F09 check-in button commits real state change");
        await Until(()=>Field<Button>(f,"checkoutButton").Enabled,"checkout enabled");
        await Act("checkoutButton","Ghi chú trả phòng thay chủ","Hoàn thành");
        var complete=await staff.Bookings.GetAsync(booking.Booking.Id);
        Pass(complete.Booking.Status==BookingStatus.Completed&&complete.TotalAmount==originalTotal&&!complete.Payments.Any(p=>p.Type==Homestay.DTO.Enums.PaymentType.Refund),
            "M33A F09 early checkout completes without repricing or refund");
    });
    await Ui(new Homestay.GUI.Staff.FrmBookingCreate(staff),async f=>{
        var types=Field<Homestay.GUI.Common.SearchPicker>(f,"editorFieldsField3");
        var choices=Field<ComboBox>(types,"choices");
        await Until(()=>choices.Items.Count>0,"F07 initial room types");
        var from=tomorrow.AddDays(14);var to=from.AddDays(2);
        Field<DateTimePicker>(f,"editorFieldsField6").Value=to.ToDateTime(TimeOnly.MinValue);
        Field<DateTimePicker>(f,"editorFieldsField5").Value=from.ToDateTime(TimeOnly.MinValue);
        await Until(()=>choices.Items.Count>0&&Field<Label>(f,"roomStatus").Text.StartsWith("Có "),"F07 automatic date refresh");
        var expected=(await staff.Availability.SearchAsync(new(){CheckIn=from,CheckOut=to,Guests=1})).Where(x=>x.AvailableRooms>0).Select(x=>x.RoomTypeId).Order().ToArray();
        Pass(choices.Items.Cast<Lookup>().Select(x=>x.Id).Order().SequenceEqual(expected)&&!Field<Button>(f,"saveButton").Enabled,
            "F07 changing dates automatically reloads matching SQL room types without creating a booking");
    });
    async Task<Homestay.DTO.Bookings.BookingDetail> Deposited(int offset){
        var date=tomorrow.AddDays(offset);
        var option=(await staff.Availability.SearchAsync(new(){CheckIn=date,CheckOut=date.AddDays(1),Guests=1})).OrderByDescending(x=>x.AvailableRooms).First(x=>x.AvailableRooms>0);
        var draft=await staff.Bookings.CreateAsync(new(){CustomerId=client.Customer.Id,RoomTypeId=option.RoomTypeId,CheckIn=date,CheckOut=date.AddDays(1),Guests=1,ContactName=client.Customer.FullName,ContactPhone=client.Customer.Phone,SourceChannel=SourceChannel.Phone});
        var receipt=await staff.Payments.RecordReceiptAsync(new(){BookingId=draft.Booking.Id,BookingCode=draft.Booking.Code,Amount=draft.AmountDue,TransferredAtUtc=await staff.ServerNowAsync(),Method=PaymentMethod.Cash,PayerName=client.Customer.FullName,RowVer=draft.Booking.RowVer});
        var review=await accountant.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
        return await accountant.Payments.VerifyReceiptAsync(new(receipt.Receipt.Id,review.Receipt.RowVer,review.Booking.Booking.RowVer));
    }
    async Task DialogAction(Form form,string button,string title,Action<Homestay.GUI.Common.FieldsDialog> fill,Func<Task<bool>> complete){
        var dialogSeen=false;var dialogSubmitted=false;
        using var timer=new System.Windows.Forms.Timer{Interval=30};
        timer.Tick+=(_,_)=>{
            var dialog=Application.OpenForms.OfType<Homestay.GUI.Common.FieldsDialog>().FirstOrDefault(d=>d.Text.StartsWith(title,StringComparison.Ordinal));
            if(dialog is null)return;
            timer.Stop();dialogSeen=true;fill(dialog);Field<Button>(dialog,"saveButton").PerformClick();dialogSubmitted=dialog.DialogResult==DialogResult.OK;
        };
        await Until(()=>Field<Button>(form,button).Visible&&Field<Button>(form,button).Enabled&&Field<Button>(form,button).CanSelect,"Ready UI action: "+button);
        timer.Start();Field<Button>(form,button).PerformClick();
        try{var end=DateTime.UtcNow.AddSeconds(25);while(!await complete()){if(DateTime.UtcNow>end)throw new InvalidDataException("UI timeout: "+title+"; dialogSeen="+dialogSeen+"; submitted="+dialogSubmitted+"; visible="+Field<Button>(form,button).Visible+"; enabled="+Field<Button>(form,button).Enabled);await Task.Delay(100);}}
        finally{timer.Stop();}
        await Until(()=>!Field<bool>(form,"mutationBusy"),"Completed UI action: "+button);
    }
    var cancelBooking=await Deposited(40);
    Homestay.DTO.ChangeRequests.CancelRequestDetail? sent=null;
    await Ui(new Homestay.GUI.Staff.FrmCancellation(staff,cancelBooking.Booking.Id),async f=>{
        await Until(()=>Field<Button>(f,"saveButton").Enabled,"M33B cancellation preview");
        var preview=await staff.Changes.PreviewCancelAsync(cancelBooking.Booking.Id);
        Pass(Field<TextBox>(f,"filterPanelField2").Text.Contains(Homestay.GUI.Common.UiFormat.Money(preview.Calculation.RefundAmount)),"M33B F11 shows BLL refund preview before submission");
        Field<TextBox>(f,"editorFieldsField1").Text="Khách yêu cầu hủy - dữ liệu kiểm thử tạm";
        Field<Button>(f,"saveButton").PerformClick();await Until(()=>f.IsDisposed,"M33B cancellation submitted");
        var requests=await manager.Changes.ListAsync(new(){BookingId=cancelBooking.Booking.Id});
        sent=await manager.Changes.GetAsync(requests.Items.Single().Id);
        Pass(sent.Request.Status==ChangeRequestStatus.Pending&&sent.Request.QuotedRefundAmount==preview.Calculation.RefundAmount&&sent.Booking.Booking.Status==BookingStatus.Deposited,
            "M33B F11 Save persists quoted cancellation without immediately cancelling booking");
    });
    await Ui(new Homestay.GUI.Manager.FrmCancellationApproval(manager,cancelBooking.Booking.Id),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");await Until(()=>grid.Grid.Rows.Count>0,"M33B review queue");
        grid.Grid.Rows[0].Selected=true;await CallAsync(f,"SelectAsync");
        Pass(Field<Button>(f,"approveButton").Enabled&&Field<Button>(f,"rejectButton").Enabled&&!Field<Label>(f,"pendingNotice").Visible,"M33B F20 enables cancellation review for another manager");
        await DialogAction(f,"approveButton","Duyệt hủy ",d=>d.Controls.Find("note",true).OfType<TextBox>().Single().Text="Duyệt trên database tạm",
            async()=> (await manager.Changes.GetAsync(sent!.Request.Id)).Request.Status==ChangeRequestStatus.Applied);
        await Until(()=>grid.Grid.Rows.Count==0,"M33B approved request leaves pending queue");
        var approved=await manager.Changes.GetAsync(sent!.Request.Id);
        Pass(approved.Booking.Booking.Status==BookingStatus.Cancelled&&approved.Booking.Payments.Any(p=>p.Type==PaymentType.Refund&&p.Amount==sent.Request.QuotedRefundAmount),
            "M33B F20 Approve cancels booking and creates refund using saved quote");
    });
    await Ui(new Homestay.GUI.Accountant.FrmRefundQueue(accountant),async f=>{
        Field<TextBox>(f,"filterPanelField0").Text=cancelBooking.Booking.Code;Field<Button>(f,"reloadButton").PerformClick();
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");
        await Until(()=>Field<IReadOnlyList<Homestay.DTO.Payments.PaymentQueueItem>>(f,"rows").Any(p=>p.BookingId==cancelBooking.Booking.Id),"M33B refund queue");
        grid.Grid.Rows[0].Selected=true;var refund=Field<IReadOnlyList<Homestay.DTO.Payments.PaymentQueueItem>>(f,"rows").Single();
        Pass(Field<Button>(f,"payButton").Enabled&&refund.RefundAccount is null,"M33B F29 allows recording real payment when legacy recipient account is not stored");
        await DialogAction(f,"payButton","Ghi nhận đã chi ",d=>{
            d.Controls.Find("confirmed",true).OfType<CheckBox>().Single().Checked=true;
            d.Controls.Find("reference",true).OfType<TextBox>().Single().Text="WIN-M33B-TEMP-REFUND";
        },async()=> !(await accountant.Payments.ListPendingRefundsAsync(new(){BookingId=cancelBooking.Booking.Id})).Items.Any());
        await Until(()=>grid.Grid.Rows.Count==0,"M33B paid refund leaves queue");
        var paid=(await manager.Bookings.GetAsync(cancelBooking.Booking.Id)).Payments.Single(p=>p.Id==refund.Id);
        Pass(paid.Status==PaymentStatus.Verified&&!Field<Button>(f,"payButton").Enabled,"M33B F29 Pay records real refund and prevents repeated payment");
    });
    var manager2=App("manager2");await manager2.InitializeAsync();
    await manager2.LoginAsync(new LoginRequest{Username="NV0005",Password=demoPassword,MachineName=manager2.MachineName,AppVersion=manager2.AppVersion});
    var secondBooking=await Deposited(41);
    var own=await manager.Changes.SubmitCancelAsync(new(secondBooking.Booking.Id,"Yêu cầu của Quản lý trên database tạm",secondBooking.Booking.RowVer));
    await Ui(new Homestay.GUI.Manager.FrmCancellationApproval(manager,secondBooking.Booking.Id),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");await Until(()=>grid.Grid.Rows.Count>0,"M33B own request");grid.Grid.Rows[0].Selected=true;await CallAsync(f,"SelectAsync");
        Pass(!Field<Button>(f,"approveButton").Enabled&&!Field<Button>(f,"rejectButton").Enabled&&Field<Label>(f,"selectionStatus").Text.Contains("Quản lý khác"),"M33B F20 blocks own cancellation review and explains separation");
    });
    await Ui(new Homestay.GUI.Manager.FrmCancellationApproval(manager2,secondBooking.Booking.Id),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");await Until(()=>grid.Grid.Rows.Count>0,"M33B second manager");grid.Grid.Rows[0].Selected=true;await CallAsync(f,"SelectAsync");
        await DialogAction(f,"rejectButton","Từ chối yêu cầu ",d=>d.Controls.Find("note",true).OfType<TextBox>().Single().Text="Khách giữ lại đặt phòng - kiểm thử tạm",
            async()=> (await manager2.Changes.GetAsync(own.Request.Id)).Request.Status==ChangeRequestStatus.Rejected);
        await Until(()=>grid.Grid.Rows.Count==0,"M33B rejected request leaves queue");
        Pass((await staff.Bookings.GetAsync(secondBooking.Booking.Id)).Booking.Status==BookingStatus.Deposited,"M33B F20 Reject preserves booking state");
    });
    var currentSecond=await staff.Bookings.GetAsync(secondBooking.Booking.Id);
    var pendingSecond=await staff.Changes.SubmitCancelAsync(new(secondBooking.Booking.Id,"Khách yêu cầu hủy lần mới - kiểm thử tạm",currentSecond.Booking.RowVer));
    await Ui(new Homestay.GUI.Staff.FrmBookingDetail(manager,secondBooking.Booking.Id),async f=>{
        await Until(()=>Field<Button>(f,"unavailableButton").Visible&&Field<Button>(f,"unavailableButton").Enabled&&Field<Homestay.DTO.Bookings.BookingDetail?>(f,"detail") is not null,"M33B unavailable action loaded");
        await DialogAction(f,"unavailableButton","Lý do homestay không phục vụ được",d=>d.Controls.Find("note",true).OfType<TextBox>().Single().Text="Homestay bảo trì - kiểm thử tạm",
            async()=> (await manager.Bookings.GetAsync(secondBooking.Booking.Id)).Booking.Status==BookingStatus.Cancelled);
        var cancelled=await manager.Bookings.GetAsync(secondBooking.Booking.Id);
        Pass(cancelled.Payments.Any(p=>p.Type==PaymentType.Refund&&p.Amount==cancelled.AmountDue),"M33B F09 homestay unavailable cancels and refunds full deposit");
        Pass((await manager.Changes.GetAsync(pendingSecond.Request.Id)).Request.Status==ChangeRequestStatus.Expired,"M33B homestay cancellation closes existing pending request");
    });
    var captures=Path.Combine(output,"anh-bao-cao-DOT45");Directory.CreateDirectory(captures);
    void Capture(Form f,string name){f.PerformLayout();using var bitmap=new Bitmap(f.Width,f.Height);f.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(captures,name+".png"));}
    var reportOwnerName="WIN Report Owner";
    await Ui(new Homestay.GUI.Manager.FrmOwners(manager),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");await Until(()=>grid.Grid.Rows.Count>0,"F22 owners");Capture(f,"F22_QuanLy_DanhSachChu");
        Pass(grid.Grid.Rows.Count>0&&!Field<Label>(f,"pendingNotice").Visible,"Report F22 loads real scoped owners");
        await DialogAction(f,"addButton","Thông tin chủ homestay",d=>{
            d.Controls.Find("name",true).OfType<TextBox>().Single().Text=reportOwnerName;
            d.Controls.Find("phone",true).OfType<TextBox>().Single().Text="0987654001";
        },async()=> (await manager.Owners.ListAsync(new(){Page=new(){SearchText=reportOwnerName}})).Items.Any());
        Pass((await manager.Owners.ListAsync(new(){Page=new(){SearchText=reportOwnerName}})).Items.Single().Status==OwnerStatus.Pending,"Report F22 Create saves a pending owner through BLL");
    });
    var newOwner=(await manager.Owners.ListAsync(new(){Page=new(){SearchText=reportOwnerName}})).Items.Single();
    await Ui(new Homestay.GUI.Manager.FrmHomestays(manager),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");await Until(()=>grid.Grid.Rows.Count>0,"F15 homestays");Capture(f,"F15_QuanLy_DanhSachHomestay");
        var area=(await manager.Homestays.ListAreasAsync()).First();
        await DialogAction(f,"addButton","Thông tin homestay nháp",d=>{
            d.Controls.Find("owner",true).OfType<Homestay.GUI.Common.SearchPicker>().Single().SelectId(newOwner.Id);
            d.Controls.Find("area",true).OfType<Homestay.GUI.Common.SearchPicker>().Single().SelectId(area.Id);
            d.Controls.Find("name",true).OfType<TextBox>().Single().Text="WIN Report Homestay";
            d.Controls.Find("address",true).OfType<TextBox>().Single().Text="Địa chỉ kiểm thử trên database tạm";
        },async()=> (await manager.Homestays.ListAsync(new(){Page=new(){SearchText="WIN Report Homestay"}})).Items.Any());
        Pass((await manager.Homestays.ListAsync(new(){Page=new(){SearchText="WIN Report Homestay"}})).Items.Single().Status==HomestayStatus.Draft,"Report F15 Create saves a real homestay draft");
    });
    var home=(await manager.Homestays.ListAsync(new(){Status=HomestayStatus.Active})).Items.First();
    await Ui(new Homestay.GUI.Manager.FrmHomestayProfile(manager,home.Id),async f=>{
        await Until(()=>Field<Homestay.DTO.Homestays.HomestayDetail?>(f,"detail") is not null,"F16 profile");
        var tabs=Field<TabControl>(f,"viewTabs");Capture(f,"F16_QuanLy_ThongTin");
        foreach(var pair in new[]{(4,"LoaiPhong"),(5,"BangGia"),(11,"DieuKienDuyet")}){tabs.SelectedIndex=pair.Item1;Capture(f,"F16_QuanLy_"+pair.Item2);}
        Pass(Field<Homestay.GUI.Common.PagedGrid>(f,"grid4").Grid.Rows.Count>0&&tabs.TabPages.Count==12,"Report F16 loads real rooms prices contracts documents and approval checklist");
    });
    await Ui(new Homestay.GUI.Manager.FrmContracts(manager),async f=>{await Until(()=>Field<Homestay.GUI.Common.PagedGrid>(f,"grid0").Grid.Rows.Count>0,"F23 contracts");Capture(f,"F23_QuanLy_HopDong");Pass(true,"Report F23 loads current contracts");});
    var person=(await manager.Tasks.ListAssigneesAsync()).First(x=>x.Id==staff.Session!.EmployeeId);
    var reportTask=await manager.Tasks.CreateAsync(new(){Title="Việc minh họa báo cáo WIN",AssigneeEmployeeId=person.Id,Priority=WorkTaskPriority.High,DueAtVietnam=DateTime.SpecifyKind((await manager.ServerNowAsync()).AddHours(8),DateTimeKind.Unspecified)});
    foreach(var view in new[]{(true,(Form)new Homestay.GUI.Manager.FrmTasks(manager)),(false,(Form)new Homestay.GUI.Staff.FrmMyWork(staff))}){
        await Ui(view.Item2,async f=>{
            await Until(()=>f.Controls.OfType<Form>().Any(),"embedded task board");var board=f.Controls.OfType<Form>().Single();
            var grid=Field<Homestay.GUI.Common.PagedGrid>(board,"grid");await Until(()=>grid.Grid.Rows.Count>0,"task rows");Capture(f,view.Item1?"F19_QuanLy_GiaoViec":"F14_NhanVien_ViecCuaToi");Pass(true,view.Item1?"Report F19 loads assigned work tasks":"Report F14 loads only current employee tasks");
        });
    }
    await Ui(new Homestay.GUI.Manager.FrmStaff(manager),async f=>{await Until(()=>Field<Homestay.GUI.Common.PagedGrid>(f,"grid0").Grid.Rows.Count>0,"F18 manager");Capture(f,"F18_QuanLy_NhanVien");});
    await Ui(new Homestay.GUI.Accountant.FrmBankStatement(accountant),async f=>{await Task.Delay(700);Capture(f,"F28_KeToan_SaoKe");});
    await Ui(new Homestay.GUI.Accountant.FrmSettlements(accountant),async f=>{await Task.Delay(700);Capture(f,"F30_KeToan_DoiSoat");});
    await Ui(new Homestay.GUI.Accountant.FrmCashBook(accountant),async f=>{await Task.Delay(700);Capture(f,"F32_KeToan_SoThuChi");});
    lines.Add("Report screenshots="+captures);
    var onboardingOwner=await staff.Owners.CreateAsync(new(){FullName="WIN DOT45 Chủ đưa vào bán",Phone="+84987123456",IdNumber="111223334445",BankName="Ngân hàng kiểm thử",BankAccountNo="123450987",BankAccountName="CHU KIEM THU"});
    await manager.Owners.ChangeStatusAsync(new(onboardingOwner.Id,OwnerStatus.Approved,null,onboardingOwner.RowVer));
    Pass(!(await staff.Owners.LookupAsync()).Any(x=>x.Id==onboardingOwner.Id),"Dot45 approved owner without homestay remains outside staff read lookup as contracted");
    var onboardingArea=(await staff.Homestays.ListAreasAsync()).First();
    await Ui(new Homestay.GUI.Manager.FrmHomestays(staff,new(onboardingOwner.Id,onboardingOwner.FullName)),async f=>{
        await Until(()=>Field<Button>(f,"addButton").Enabled,"Dot45 staff draft create");
        await DialogAction(f,"addButton","Thông tin homestay nháp",d=>{
            Pass(((Homestay.GUI.Common.SearchPicker)d.Controls.Find("owner",true).Single()).SelectedItem?.Id==onboardingOwner.Id,"Dot45 F15 preserves known owner ID after separate manager approval");
            ((Homestay.GUI.Common.SearchPicker)d.Controls.Find("area",true).Single()).SelectId(onboardingArea.Id);
            d.Controls.Find("name",true).Single().Text="WIN DOT45 Đưa homestay vào bán";d.Controls.Find("address",true).Single().Text="Địa chỉ mẫu database tạm";
            d.Controls.Find("phone",true).Single().Text="0987123456";d.Controls.Find("description",true).Single().Text="Mô tả homestay mẫu để kiểm tra đủ điều kiện duyệt";
        },async()=> (await staff.Homestays.ListAsync(new(){Page=new(){SearchText="WIN DOT45 Đưa homestay vào bán"}})).Items.Any());
    });
    var onboardingHome=(await staff.Homestays.ListAsync(new(){Page=new(){SearchText="WIN DOT45 Đưa homestay vào bán"}})).Items.Single();
    var onboardingDetail=await staff.Homestays.SubmitAsync(new(onboardingHome.Id,onboardingHome.RowVer));
    var missing=onboardingDetail.Approval.Conditions.Where(c=>!c.IsSatisfied).Select(c=>c.Message).ToArray();
    try{await manager.Homestays.ApproveAsync(new(onboardingHome.Id,onboardingDetail.Homestay.RowVer));throw new InvalidDataException("Missing checklist accepted");}
    catch(BusinessException e){Pass(e.Message==string.Join(Environment.NewLine,missing),"Dot45 approval failure lists exact missing checklist conditions");}
    await manager.Homestays.ReturnAsync(new(onboardingHome.Id,onboardingDetail.Homestay.RowVer,"Bổ sung đủ hồ sơ kiểm thử"));
    onboardingDetail=await staff.Homestays.GetAsync(onboardingHome.Id);
    var onboardingAmenity=(await staff.Homestays.ListAmenitiesAsync()).First();
    onboardingDetail=await staff.Homestays.SetAmenitiesAsync(new(onboardingHome.Id,[onboardingAmenity.Id],onboardingDetail.Homestay.RowVer));
    using(var bitmap=new Bitmap(12,12)){bitmap.SetPixel(0,0,Color.DarkCyan);using var bytes=new MemoryStream();bitmap.Save(bytes,System.Drawing.Imaging.ImageFormat.Png);onboardingDetail=await staff.Homestays.AddImageAsync(new(onboardingHome.Id,bytes.ToArray(),"sample-cover.png",null,"Ảnh mẫu kiểm thử",0,true,onboardingDetail.Homestay.RowVer));}
    foreach(var type in new[]{DocumentType.BusinessLicense,DocumentType.FireSafety})onboardingDetail=await staff.Homestays.AddDocumentAsync(new(onboardingHome.Id,type,System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nWIN DOT45 SAMPLE ONLY "+type),"sample-"+type+".pdf",null,onboardingDetail.Homestay.RowVer));
    var onboardingCategory=(await staff.Homestays.ListRoomCategoriesAsync()).First();
    var onboardingType=await staff.Rooms.CreateTypeAsync(new(onboardingHome.Id,onboardingCategory.Id,"Phòng mẫu DOT45",2,20,"Loại phòng kiểm thử",onboardingDetail.Homestay.RowVer));
    onboardingDetail=await staff.Homestays.GetAsync(onboardingHome.Id);await staff.Rooms.CreateRoomAsync(new(onboardingType.Id,"DOT45-01",null,onboardingDetail.Homestay.RowVer));
    onboardingDetail=await staff.Homestays.GetAsync(onboardingHome.Id);await staff.Rooms.AddPriceAsync(new(onboardingType.Id,onboardingDetail.Approval.Today,500000m,600000m,700000m,onboardingDetail.Homestay.RowVer));
    onboardingDetail=await manager.Homestays.GetAsync(onboardingHome.Id);await manager.Contracts.CreateAsync(new(onboardingHome.Id,"WIN-DOT45-CONTRACT",onboardingDetail.Approval.Today,onboardingDetail.Approval.Today.AddYears(1),0.125m,onboardingDetail.Homestay.RowVer));
    onboardingDetail=await staff.Homestays.GetAsync(onboardingHome.Id);await staff.Homestays.SubmitAsync(new(onboardingHome.Id,onboardingDetail.Homestay.RowVer));
    await Ui(new Homestay.GUI.Manager.FrmHomestayProfile(manager,onboardingHome.Id),async f=>{
        await Until(()=>Field<Button>(f,"approveButton").Enabled,"Dot45 full checklist ready for other manager");var tabs=Field<TabControl>(f,"viewTabs");tabs.SelectedIndex=tabs.TabCount-1;Capture(f,"F16_QuanLy_DuDieuKien");
        await DialogAction(f,"approveButton","Duyệt hồ sơ",d=>{},async()=> (await manager.Homestays.GetAsync(onboardingHome.Id)).Homestay.Status==HomestayStatus.Active);
        Pass(true,"Dot45 F16 Approve button activates fully prepared staff-created homestay");Capture(f,"F16_QuanLy_DaDuyet");
    });
    var onboardingStay=tomorrow.AddDays(160);var onboardingAvailable=(await staff.Availability.SearchAsync(new(){CheckIn=onboardingStay,CheckOut=onboardingStay.AddDays(1),Guests=1})).Single(x=>x.RoomTypeId==onboardingType.Id);
    Pass(onboardingAvailable.AvailableRooms==1,"Dot45 approved homestay appears in F06 availability");
    var onboardingBooking=await staff.Bookings.CreateAsync(new(){CustomerId=client.Customer.Id,RoomTypeId=onboardingType.Id,CheckIn=onboardingStay,CheckOut=onboardingStay.AddDays(1),Guests=1,ContactName=client.Customer.FullName,ContactPhone=client.Customer.Phone,SourceChannel=SourceChannel.Phone});
    Pass(onboardingBooking.Booking.HomestayId==onboardingHome.Id,"Dot45 onboarding completes with a real booking at newly approved homestay");

    // WIN-only end-to-end checks for the newly connected M4/M5 forms.
    var dot45Date=tomorrow.AddDays(175);
    await Ui(new Homestay.GUI.Staff.FrmAvailability(manager),async f=>{
        await Until(()=>Field<Homestay.GUI.Common.PagedGrid>(f,"grid0").Grid.Rows.Count>0,"Dot45 availability initial read");
        Field<Homestay.GUI.Common.SearchPicker>(f,"filterPanelField0").SelectId(home.AreaId);
        Field<DateTimePicker>(f,"filterPanelField1").Value=dot45Date.ToDateTime(TimeOnly.MinValue);
        Field<TabControl>(f,"viewTabs").SelectedIndex=1;
        await Until(()=>Field<Homestay.DTO.Availability.RoomCalendar?>(f,"loadedCalendar")?.FromDate==dot45Date,"Dot45 calendar read");
        Field<Homestay.GUI.Common.SearchPicker>(f,"filterPanelField8").SelectId(home.Id);
        var rooms=Field<IReadOnlyList<Homestay.DTO.Availability.RoomCalendarRow>>(f,"calendar");
        var index=rooms.Select((r,i)=>(r,i)).First(x=>x.r.Cells[0].State==Homestay.DTO.Availability.RoomCalendarState.Available).i;
        var room=rooms[index];var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid1");grid.Grid.CurrentCell=grid.Grid.Rows[index].Cells[1];
        await DialogAction(f,"blockButton","Khoảng khóa phòng",d=>{d.Controls.Find("note",true).Single().Text="WIN DOT45 temporary block";},async()=> (await manager.Rooms.ListBlocksAsync(new(home.Id,dot45Date,dot45Date.AddDays(1)))).Any(b=>b.RoomId==room.RoomId));
        await Until(()=>Field<IReadOnlyList<Homestay.DTO.Availability.RoomCalendarRow>>(f,"calendar").Any(r=>r.RoomId==room.RoomId&&r.Cells[0].RoomBlockId is not null),"Dot45 blocked calendar refresh");
        Pass(true,"Dot45 F06 Block button persists real block and refreshes calendar");Capture(f,"F06_QuanLy_KhoaPhong");
        rooms=Field<IReadOnlyList<Homestay.DTO.Availability.RoomCalendarRow>>(f,"calendar");index=rooms.Select((r,i)=>(r,i)).Single(x=>x.r.RoomId==room.RoomId).i;grid.Grid.CurrentCell=grid.Grid.Rows[index].Cells[1];
        await DialogAction(f,"unblockButton","Mở khóa phòng",d=>d.Controls.Find("note",true).Single().Text="Đã hoàn tất bảo trì kiểm thử",async()=>!(await manager.Rooms.ListBlocksAsync(new(home.Id,dot45Date,dot45Date.AddDays(1)))).Any(b=>b.RoomId==room.RoomId));
        Pass(true,"Dot45 F06 Unblock button removes full block using refreshed parent token");
    });
    var batchCustomer=await manager.Customers.CreateAsync(new(){FullName="Khách kiểm thử giao nhiều DOT45",Phone="+84987654555"});
    var batchBookings=new List<Homestay.DTO.Bookings.BookingDetail>();
    var holdSetting=(await manager.Catalog.ListSettingsAsync()).Single(x=>x.Key=="Booking.HoldMinutes");
    await manager.Catalog.UpdateSettingAsync(new(holdSetting.Key,"5",holdSetting.Value));
    try{
        for(var n=0;n<2;n++){
            var date=dot45Date.AddDays(n+5);var choice=(await staff.Availability.SearchAsync(new(){CheckIn=date,CheckOut=date.AddDays(1),Guests=1})).First(x=>x.AvailableRooms>0);
            batchBookings.Add(await staff.Bookings.CreateAsync(new(){CustomerId=batchCustomer.Customer.Id,RoomTypeId=choice.RoomTypeId,CheckIn=date,CheckOut=date.AddDays(1),Guests=1,ContactName=batchCustomer.Customer.FullName,ContactPhone=batchCustomer.Customer.Phone,SourceChannel=SourceChannel.Phone}));
        }
    }finally{await manager.Catalog.UpdateSettingAsync(new(holdSetting.Key,holdSetting.Value,"5"));}
    var batchIds=batchBookings.Select(b=>b.Booking.Id).ToArray();
    await Ui(new Homestay.GUI.Staff.FrmBookings(manager,exceptions:new(Homestay.DTO.Bookings.BookingExceptionKind.HoldExpiring)),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"exceptionsGrid");
        await Until(()=>grid.Grid.Rows.Cast<DataGridViewRow>().Count(r=>batchBookings.Any(b=>b.Booking.Code==Convert.ToString(r.Cells[0].Value)))==2,"Dot45 exception batch rows");
        grid.Grid.ClearSelection();foreach(DataGridViewRow row in grid.Grid.Rows)if(batchBookings.Any(b=>b.Booking.Code==Convert.ToString(row.Cells[0].Value)))row.Selected=true;
        await DialogAction(f,"assignManyButton","Giao 2 đơn",d=>((Homestay.GUI.Common.SearchPicker)d.Controls.Find("employee",true).Single()).SelectId(manager.Session!.EmployeeId),async()=>{
            foreach(var id in batchIds)if((await manager.Bookings.GetAsync(id)).Booking.AssignedEmployeeId!=manager.Session!.EmployeeId)return false;return true;
        });
        Pass(true,"Dot45 F08 batch button assigns two distinct exception bookings atomically");Capture(f,"F08_QuanLy_NgoaiLe");
    });
    await Ui(new Homestay.GUI.Manager.FrmTasks(manager,new Homestay.GUI.Common.TaskBoardSelection(batchIds[0])),async f=>{
        await Until(()=>f.Controls.OfType<Form>().Any(),"Dot45 linked task board");var board=f.Controls.OfType<Form>().Single();
        using var timer=new System.Windows.Forms.Timer{Interval=30};var linkDone=false;var taskDone=false;
        timer.Tick+=(_,_)=>{
            var dialog=Application.OpenForms.OfType<Homestay.GUI.Common.FieldsDialog>().FirstOrDefault();if(dialog is null)return;
            if(dialog.Text=="Liên kết công việc"&&!linkDone){linkDone=true;Pass(!dialog.Controls.Find("booking",true).Single().Enabled&&dialog.Controls.Find("booking",true).Single().Text==batchBookings[0].Booking.Code,"Dot45 F19 carries selected booking into linked task editor");Field<Button>(dialog,"saveButton").PerformClick();}
            else if(dialog.Text=="Giao việc mới"&&!taskDone){taskDone=true;dialog.Controls.Find("title",true).Single().Text="WIN DOT45 linked task";((Homestay.GUI.Common.SearchPicker)dialog.Controls.Find("assignee",true).Single()).SelectId(manager.Session!.EmployeeId);Field<Button>(dialog,"saveButton").PerformClick();}
        };
        await Until(()=>Field<Button>(board,"create").Visible&&Field<Button>(board,"create").Enabled&&Field<Button>(board,"create").CanSelect,"Dot45 linked task create ready");timer.Start();Field<Button>(board,"create").PerformClick();
        try{var end=DateTime.UtcNow.AddSeconds(25);while(!(await manager.Tasks.ListAssignedAsync(new(){Page=new(){SearchText="WIN DOT45 linked task"}})).Items.Any()){if(DateTime.UtcNow>end)throw new InvalidDataException("Dot45 linked task save timeout; link="+linkDone+"; task="+taskDone+"; open="+string.Join(" | ",Application.OpenForms.Cast<Form>().Select(x=>x.Text)));await Task.Delay(100);}}
        finally{timer.Stop();}
        await Until(()=>!Field<bool>(board,"mutationBusy"),"Dot45 task create finished");
        Pass((await manager.Tasks.ListAssignedAsync(new(){Page=new(){SearchText="WIN DOT45 linked task"}})).Items.Single().BookingId==batchIds[0],"Dot45 F19 create saves booking link through TaskService");
    });
    await Ui(new Homestay.GUI.Staff.FrmMyWork(staff),async f=>{
        await Until(()=>f.Controls.OfType<Form>().Any(),"Dot45 own task board");var board=f.Controls.OfType<Form>().Single();var grid=Field<Homestay.GUI.Common.PagedGrid>(board,"grid");
        await Until(()=>grid.Grid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells[0].Value)==reportTask.Title),"Dot45 own task visible");
        var row=grid.Grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Cells[0].Value)==reportTask.Title);grid.Grid.ClearSelection();row.Selected=true;
        Field<Button>(board,"start").PerformClick();var until=DateTime.UtcNow.AddSeconds(20);while((await staff.Tasks.GetAsync(reportTask.Id)).StartedAtUtc is null){if(DateTime.UtcNow>until)throw new InvalidDataException("Dot45 start task timeout");await Task.Delay(100);}
        await Until(()=>!Field<bool>(board,"mutationBusy"),"Dot45 start finished");
        Pass((await staff.Tasks.GetAsync(reportTask.Id)).StartedAtUtc is not null,"Dot45 F14 Start persists real start time");
        grid.Grid.ClearSelection();grid.Grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Cells[0].Value)==reportTask.Title).Selected=true;
        await DialogAction(board,"done","Kết quả hoàn thành",d=>d.Controls.Find("note",true).Single().Text="Đã đối chiếu và hoàn thành công việc",async()=> (await staff.Tasks.GetAsync(reportTask.Id)).Status==WorkTaskStatus.Done);
        Pass(true,"Dot45 F14 starts and completes assigned work with required result");Capture(f,"F14_NhanVien_HoanThanhViec");
    });
    var endPassword=Password();await new LoginFlow(newStaff).SignInAsync(new LoginRequest{Username=createdEmployee.EmployeeCode,Password=reset.TemporaryPassword,MachineName=newStaff.MachineName,AppVersion=newStaff.AppVersion},async()=>{await newStaff.ChangePasswordAsync(new(){OldPassword=reset.TemporaryPassword,NewPassword=endPassword,ConfirmPassword=endPassword});return true;});
    await Ui(new Homestay.GUI.Manager.FrmStaff(manager),async f=>{
        var grid=Field<Homestay.GUI.Common.PagedGrid>(f,"grid0");await Until(()=>grid.Grid.Rows.Count>0,"Dot45 staff list");
        grid.Grid.ClearSelection();var row=grid.Grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Cells[0].Value)==createdEmployee.EmployeeCode);row.Selected=true;
        var tabs=Field<TabControl>(f,"viewTabs");tabs.SelectedIndex=2;var sessions=Field<Homestay.GUI.Common.PagedGrid>(f,"grid2");
        await Until(()=>sessions.Grid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells[0].Value)==createdEmployee.EmployeeCode),"Dot45 all employee open sessions");
        sessions.Grid.ClearSelection();sessions.Grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Cells[0].Value)==createdEmployee.EmployeeCode).Selected=true;
        await DialogAction(f,"endSessionButton","Buộc đăng xuất",d=>d.Controls.Find("note",true).Single().Text="Kiểm thử buộc đăng xuất phiên tạm",async()=>!await newStaff.HeartbeatAsync());
        Pass(true,"Dot45 F18 force logout ends only selected other employee session");
        Field<CheckBox>(f,"historySessions").Checked=true;
        await Until(()=>sessions.Grid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells[6].Value)!=""),"Dot45 selected employee session history");
        Pass(sessions.Grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[0].Value)==createdEmployee.EmployeeCode),"Dot45 F18 history is filtered by selected employee");Capture(f,"F18_QuanLy_LichSuPhien");
        tabs.SelectedIndex=1;Field<Homestay.GUI.Common.SearchPicker>(f,"filterPanelField3").SelectId(home.AreaId);
        await Until(()=>Field<Homestay.GUI.Common.PagedGrid>(f,"grid1").Grid.Rows.Count>0,"Dot45 employees by area");Capture(f,"F18_QuanLy_TheoKhuVuc");Pass(true,"Dot45 F18 area roster reads Staff.ListByAreaAsync");
    });

    await manager2.LogoutAsync();

    await staff.LogoutAsync();await accountant.LogoutAsync();await manager.LogoutAsync();
    Pass(manager.Session is null&&staff.Session is null&&accountant.Session is null,"Logout clears all three sessions");
    if(!Regex.IsMatch(dbName,@"^HomestayWinCheck_[0-9a-f]{16}$"))throw new InvalidDataException("Unsafe fixture name.");
    var testDirectory=Path.Combine(root,"work/LTTQ/src/Homestay.GUI/bin/WinDot45/bin/Homestay.BLL.Tests/release");
    var testAssembly=Path.Combine(testDirectory,"Homestay.BLL.Tests.dll");
    if(!File.Exists(testAssembly))throw new InvalidDataException("Build SQL tests before running this helper.");
    await File.WriteAllTextAsync(Path.Combine(testDirectory,"xunit.runner.json"),"{\"parallelizeTestCollections\":false,\"maxParallelThreads\":1}");
    var run=new System.Diagnostics.ProcessStartInfo{
        FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet/dotnet.exe"),
        WorkingDirectory=Path.Combine(root,"work/LTTQ"),UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    run.ArgumentList.Add("vstest");run.ArgumentList.Add(testAssembly);run.ArgumentList.Add("/Logger:console;verbosity=minimal");
    run.Environment["HOMESTAY_TEST_CONNECTION"]=new SqlConnectionStringBuilder{
        DataSource=profile.Server,InitialCatalog=dbName,UserID=profile.Username,Password=profile.Password,
        Encrypt=profile.Encrypt,TrustServerCertificate=profile.TrustServerCertificate,Pooling=true}.ConnectionString;
    Console.WriteLine("Running all current xUnit SQL tests on this temporary database...");
    using(var process=System.Diagnostics.Process.Start(run)??throw new InvalidDataException("Cannot start SQL test runner.")){
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new InvalidDataException("SQL test runner timed out; child process stopped.");}
        var log=(await stdout)+"\n"+(await stderr);
        var logPath=Path.Combine(privateRoot,"BLL-SQL-TESTS.txt");await File.WriteAllTextAsync(logPath,log);
        foreach(var line in log.Split('\n').Where(line=>line.TrimStart().StartsWith("Passed!")||line.TrimStart().StartsWith("Failed!")||line.TrimStart().StartsWith("Skipped!")))lines.Add(line.Trim());
        lines.Add("Detailed SQL test log="+logPath);
        var executed=Regex.Match(log,@"Passed:\s*(\d+).*Skipped:\s*0");
        Pass(process.ExitCode==0&&executed.Success&&int.Parse(executed.Groups[1].Value)>=231,"Completion full xUnit SQL suite executes on temporary database");
    }
    profile.Password="";lines.Add("RESULT PASS - service integration on SQL; manual UI acceptance remains.");
}catch(Exception e){
    Environment.ExitCode=1;
    var message=e is SqlException sql?"SQL "+sql.Number:e is BusinessException or InvalidDataException or UnauthorizedAccessException?e.Message:e.GetType().Name;
    var status = created ? "FAIL " : "BLOCKED before database creation: ";
    lines.Add(status+message);Console.WriteLine(status+message);
    var frames=new System.Diagnostics.StackTrace(e,false).GetFrames()??[];
    foreach(var frame in frames){var method=frame.GetMethod();if(method is not null)lines.Add("AT "+method.DeclaringType?.FullName+"."+method.Name);}
    if(!created)lines.Add("Chưa tạo database thử. Nếu lỗi SSPI/quyền, chạy lại launcher bằng Run as administrator trong Windows.");
}finally{
    sourceProfile.Password="";
    if(created&&Regex.IsMatch(dbName,@"^HomestayWinCheck_[0-9a-f]{16}$")){
        try{
            if(admin.State!=System.Data.ConnectionState.Open)await admin.OpenAsync();
            await Execute(admin,"IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            await admin.ChangeDatabaseAsync("master");
            await Execute(admin,"ALTER DATABASE ["+dbName+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+dbName+"];");
            lines.Add("Cleanup PASS - removed only this run's newly created fixture database.");
        }catch(Exception e){Environment.ExitCode=1;lines.Add("Cleanup FAIL "+e.GetType().Name+" database="+dbName);}
    }
    lines.Add("Only a new fixture database was used; main database was not changed.");
    await File.WriteAllLinesAsync(Path.Combine(output,"WIN-DOT45-SQL.txt"),lines);
    Console.WriteLine("Kết quả: "+Path.Combine(output,"WIN-DOT45-SQL.txt"));
}

    }
}
internal sealed class TestBookingForm(AppServices services,object initial) : Homestay.GUI.Staff.FrmBookingCreate(services,initial)
{
    public bool Completed { get; private set; }
    protected override void ShowSuccess(string text,string title) { Completed=true; }
}
internal sealed class TestReceiptForm(AppServices services,int bookingId) : Homestay.GUI.Staff.FrmReceipt(services,bookingId)
{
    public bool Completed { get; private set; }
    protected override void ShowSuccess(string text,string title) { Completed=true; }
}