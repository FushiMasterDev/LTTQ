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
            lines.Add("RESULT PASS");
        }catch(Exception e){lines.Add("FAIL "+e);Environment.ExitCode=1;}
        File.WriteAllLines(args[0],lines);foreach(var line in lines)Console.WriteLine(line);return Environment.ExitCode;
    }
    static AppServices Fake(Position role){
        var app=new AppServices(new Homestay.GUI.Configuration.AppSettings{Logging=new(){Folder=Path.Combine(Path.GetTempPath(),"ReportGuiChecksLogs")}},Path.Combine(Path.GetTempPath(),"unused-report-profile.dat"));
        var current=new CurrentSession();current.Set(new(){UserId=800001,EmployeeId=800001,Position=role,EmployeeCode="SYNTHETIC",FullName="Synthetic",HasAllAreas=role==Position.Manager,LoginAtUtc=DateTime.UtcNow});
        var auth=new Authorizer(current);var runtime=typeof(AppServices).GetNestedType("Runtime",BindingFlags.NonPublic)!;var ctor=runtime.GetConstructors().Single(c=>c.GetParameters().Length>1);
        var values=ctor.GetParameters().Select(p=>p.ParameterType==typeof(CurrentSession)?(object)current:p.ParameterType==typeof(Authorizer)?auth:null).ToArray();typeof(AppServices).GetField("runtime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(app,ctor.Invoke(values));return app;
    }
}