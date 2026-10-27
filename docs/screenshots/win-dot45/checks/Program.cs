using System.Reflection;
using Homestay.GUI;
using Homestay.GUI.Common;
using Homestay.GUI.Staff;
using Homestay.GUI.Manager;
using Homestay.GUI.Accountant;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Enums;
internal static class Program{
    [STAThread] static int Main(string[] args){
        ApplicationConfiguration.Initialize();var lines=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);lines.Add("PASS "+message);}
        try{
            var required=new (Type Type,string[] Names)[]{
                (typeof(FrmBookings),["filterPanelField0","grid0","grid7","rows","clearFilters","viewTabs","filterPanelField1","filterPanelField2","filterPanelField6"]),
                (typeof(FrmBookingCreate),["editorFieldsField3","editorFieldsField4","editorFieldsField5","editorFieldsField6","saveButton","created","nightlyPrices"]),
                (typeof(FrmHomestayProfile),["detail","grid4","grid5","viewTabs","roomGrid","blockGrid"]),
                (typeof(FrmOwners),["grid0","addButton"]),(typeof(FrmHomestays),["grid0","addButton"]),(typeof(FrmContracts),["grid0"]),
                (typeof(FrmRefundQueue),["grid0","rows","payButton","accountButton"]),
                (typeof(FrmStaff),["grid0","roleButton","resignButton","endSessionButton"])
            };
            foreach(var item in required)Check(item.Names.All(n=>item.Type.GetField(n,BindingFlags.NonPublic|BindingFlags.Instance) is not null),"UI automation bindings match "+item.Type.Name);
            var registry=typeof(AppServices).Assembly.GetType("Homestay.GUI.Shell.ScreenRegistry")!;
            foreach(var role in new[]{Position.Staff,Position.Manager,Position.Accountant}){
                var app=Fake(role);var entries=(Array)registry.GetMethod("Production",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[app,app])!;
                var ids=entries.Cast<object>().Select(e=>(string)e.GetType().GetProperty("Id")!.GetValue(e)!).ToArray();
                Check(ids.Contains("F14")&&ids.Contains("F22"),"Own tasks and owner read menu for "+role);
                Check(ids.Contains("F15")== (role!=Position.Accountant)&&ids.Contains("F19")== (role==Position.Manager)&&ids.Contains("F23")== (role==Position.Manager),"Listing contract and manager task menus respect role "+role);
                using var owners=new FrmOwners(app);var tabs=(TabControl)typeof(FrmOwners).GetField("viewTabs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owners)!;
                Check(tabs.TabCount==(role==Position.Accountant?1:2),"Accountant cannot navigate into homestay-only owner tab: "+role);
            }
            CheckDot45(Check);lines.Add("RESULT PASS");
        }catch(Exception e){lines.Add("FAIL "+e);Environment.ExitCode=1;}
        File.WriteAllLines(args[0],lines);foreach(var line in lines)Console.WriteLine(line);return Environment.ExitCode;
    }
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(target,args);
    static void CheckDot45(Action<bool,string> check){
        var manager=Fake(Position.Manager);var staff=Fake(Position.Staff);var accountant=Fake(Position.Accountant);
        foreach(var app in new[]{manager,staff,accountant}){
            using var form=new FrmBookings(app);var tabs=Field<TabControl>(form,"viewTabs");
            check(tabs.TabPages.Cast<TabPage>().Any(t=>t.Text=="Ngoại lệ")== (app==manager),"Exception grid is restricted to manager: "+app.Session!.Position);
            check(!Field<Button>(form,"assignManyButton").Enabled,"Batch write starts disabled: "+app.Session.Position);
        }
        using(var form=new FrmBookings(manager,exceptions:new(Homestay.DTO.Bookings.BookingExceptionKind.HoldExpiring))){
            check(Field<TabControl>(form,"viewTabs").SelectedTab!.Text=="Ngoại lệ","Dashboard exception navigation selects separate queue");
            check(Field<ComboBox>(form,"exceptionKind").SelectedIndex==2,"Dashboard preserves exact exception kind");
            var v=new byte[]{1,2};var a=new Homestay.DTO.Bookings.BookingAssignmentItem(5,v){BookingCode="TEST"};
            var batch=(Homestay.DTO.Bookings.BookingAssignmentItem[])Call(form,"NormalizeBatch",new object[]{new[]{a,a,a with{BookingId=6}}})!;
            check(batch.Length==2&&batch[0].BookingCode=="TEST","Batch de-duplicates booking IDs and preserves displayed code");
            v[0]=9;check(batch[0].RowVer[0]==1,"Batch copies RowVer before async lookups");
            try{Call(form,"NormalizeBatch",new object[]{new[]{a,a with{RowVer=new byte[]{8}}}});throw new Exception("Mixed versions accepted");}catch(TargetInvocationException e)when(e.InnerException is BusinessException){check(true,"Mixed versions across exceptions require reload without sending");}
        }
        using(var form=new FrmHomestays(staff,new ListingOwnerSelection(777,"Chủ đã biết từ bước tạo")))
            check(Field<ListingOwnerSelection>(form,"selectedOwner").OwnerId==777,"F15 navigation preserves known approved owner independent of current lookup");
        using(var form=new FrmAvailability(manager))check(!Field<Button>(form,"blockButton").Enabled&&!Field<Button>(form,"unblockButton").Enabled,"Calendar writes disabled before calendar load");
        using(var form=new FrmAvailability(staff))check(!Field<Button>(form,"blockButton").Visible&&!Field<Button>(form,"unblockButton").Visible,"Staff calendar has no blocking controls");
        using(var form=new FrmStaff(manager)){
            var self=new Homestay.DTO.Staff.EmployeeListItem{EmployeeId=manager.Session!.EmployeeId,UserId=manager.Session.UserId};
            Set(form,"targetEmployee",self);Call(form,"RefreshManagerButtons");
            check(!Field<Button>(form,"roleButton").Enabled&&!Field<Button>(form,"resignButton").Enabled,"Manager cannot change own position or employment from UI");
            Set(form,"targetEmployee",self with{EmployeeId=self.EmployeeId+1});Call(form,"RefreshManagerButtons");
            check(Field<Button>(form,"roleButton").Enabled,"Selected other employee enables staff management");
            check(!Field<Button>(form,"endSessionButton").Enabled,"Force logout needs a selected open session");
            check(Field<CheckBox>(form,"allSessions").Checked,"Open-session list defaults to all employees");
        }
        using(var form=new FrmHomestayProfile(manager,1)){
            Set(form,"detail",new Homestay.DTO.Homestays.HomestayDetail{Homestay=new(){Status=Homestay.DTO.Enums.HomestayStatus.Suspended},AllowedActions=[Homestay.DTO.Homestays.HomestayAction.Reopen]});
            Call(form,"SetEnabled");check(Field<Button>(form,"suspendButton").Enabled&&Field<Button>(form,"suspendButton").Text=="Mở lại","Suspended profile follows Reopen allowed action");
            Set(form,"detail",null);Call(form,"SetEnabled");check(!Field<Button>(form,"suspendButton").Enabled&&!Field<Button>(form,"approveButton").Enabled,"Reload clears profile write permissions immediately");
        }
    }
    static AppServices Fake(Position role){
        var app=new AppServices(new Homestay.GUI.Configuration.AppSettings{Logging=new(){Folder=Path.Combine(Path.GetTempPath(),"ReportGuiChecksLogs")}},Path.Combine(Path.GetTempPath(),"unused-report-profile.dat"));
        var current=new CurrentSession();current.Set(new(){UserId=800001,EmployeeId=800001,Position=role,EmployeeCode="SYNTHETIC",FullName="Synthetic",HasAllAreas=role==Position.Manager,LoginAtUtc=DateTime.UtcNow});
        var auth=new Authorizer(current);var runtime=typeof(AppServices).GetNestedType("Runtime",BindingFlags.NonPublic)!;var ctor=runtime.GetConstructors().Single(c=>c.GetParameters().Length>1);
        var values=ctor.GetParameters().Select(p=>p.ParameterType==typeof(CurrentSession)?(object)current:p.ParameterType==typeof(Authorizer)?auth:null).ToArray();typeof(AppServices).GetField("runtime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(app,ctor.Invoke(values));return app;
    }
}