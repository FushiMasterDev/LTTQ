using System.Reflection;
using Homestay.GUI;
using Homestay.GUI.Shell;
using Homestay.GUI.Common;
using Homestay.DTO.Enums;
using Homestay.BLL.Infrastructure;

internal static class Program
{
    static readonly List<string> lines=[];
    [STAThread] static int Main(string[] args){
        ApplicationConfiguration.Initialize();
        using var runner=new Form{Opacity=0,ShowInTaskbar=false};
        runner.Shown+=async(_,_)=>{try{await CheckMenu(args[1]);lines.Add("RESULT PASS");}catch(Exception e){Environment.ExitCode=1;lines.Add("FAIL "+e);}finally{File.WriteAllLines(args[0],lines);runner.Close();}};
        Application.Run(runner);foreach(var line in lines)Console.WriteLine(line);return Environment.ExitCode;
    }
    static void Check(bool value,string name){if(!value)throw new Exception(name);lines.Add("PASS "+name);}
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o)!;
    static object? Call(object o,string name,params object?[] args)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    static async Task CheckMenu(string folder){
        Directory.CreateDirectory(folder);
        using var shell=new FrmMain();shell.Show();await Task.Delay(150);
        var panel=Field<FlowLayoutPanel>(shell,"navigation");var tabs=Field<TabControl>(shell,"workspaceTabs");
        var registry=typeof(AppServices).Assembly.GetType("Homestay.GUI.Shell.ScreenRegistry")!;
        Check(panel.AutoScroll&&!panel.WrapContents&&panel.FlowDirection==FlowDirection.TopDown,"Menu is a scrolling column of actual buttons");
        Check(panel.Controls.OfType<Button>().Count()==28,"All 28 screen buttons exist in Designer");
        foreach(var role in new[]{Position.Staff,Position.Manager,Position.Accountant}){
            var app=Fake(role);var entries=(Array)registry.GetMethod("Production",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[app,app])!;
            var wanted=entries.Cast<object>().Where(e=>!new[]{"F09","F10","F11","F16"}.Contains((string)e.GetType().GetProperty("Id")!.GetValue(e)!)).ToArray();
            var entryType=entries.GetType().GetElementType()!;var menu=Array.CreateInstance(entryType,wanted.Length);
            for(var i=0;i<wanted.Length;i++){
                var e=wanted[i];var title=(string)entryType.GetProperty("Title")!.GetValue(e)!;var id=(string)entryType.GetProperty("Id")!.GetValue(e)!;
                Func<Form> create=()=>new Form{Text=id+" · "+title};
                menu.SetValue(Activator.CreateInstance(entryType,id,title,"All",create),i);
            }
            Field<ComboBox>(shell,"roleBox").SelectedItem=UiNames.Position(role);
            Call(shell,"SetMenu",menu);
            var visible=panel.Controls.OfType<Button>().Where(b=>b.Visible).ToArray();
            Check(visible.Length==wanted.Length,"Buttons match authorized menu for "+role);
            Check(visible.All(b=>b.TabStop&&b.FlatStyle==FlatStyle.Flat&&b.FlatAppearance.BorderSize==1&&!b.UseMnemonic),"Buttons have borders keyboard focus and full ampersand captions for "+role);
            Check(visible.All(b=>b.Tag is not null&&b.Height>=44),"Every visible button has a screen action and comfortable height for "+role);
            var target=visible.Single(b=>b.Name=="menuF05");target.PerformClick();await Task.Delay(30);
            Check((string?)tabs.SelectedTab?.Tag=="F05"&&target.BackColor==UiTheme.Accent&&target.ForeColor==Color.White,"Click navigates and highlights active button for "+role);
            target.PerformClick();Check(tabs.TabPages.Cast<TabPage>().Count(p=>(string?)p.Tag=="F05")==1,"Repeated menu click reuses the existing tab for "+role);
            if(role==Position.Manager){
                shell.OpenScreen("F16:123",()=>new Form{Text="Hồ sơ homestay"});
                Check(Field<Button>(shell,"menuF15").BackColor==UiTheme.Accent,"Deep-linked profile highlights its homestay menu");
                target.PerformClick();
                foreach(var size in new[]{new Size(1320,720),new Size(900,600)}){
                    shell.ClientSize=size;shell.PerformLayout();await Task.Delay(50);
                    Check(visible.All(b=>b.Width>0&&b.Left+b.Width<=panel.ClientSize.Width),"Buttons stay inside resized sidebar "+size.Width);
                }
                shell.ClientSize=new Size(1320,720);shell.PerformLayout();await Task.Delay(50);
                using var bitmap=new Bitmap(shell.Width,shell.Height);shell.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(folder,"Menu_QuanLy.png"));
            }
        }
        shell.Dispose();
    }
    static AppServices Fake(Position role){
        var app=new AppServices(new Homestay.GUI.Configuration.AppSettings{Logging=new(){Folder=Path.Combine(Path.GetTempPath(),"MenuChecksLogs")}},Path.Combine(Path.GetTempPath(),"unused-menu-profile.dat"));
        var current=new CurrentSession();current.Set(new(){UserId=800001,EmployeeId=800001,Position=role,EmployeeCode="PREVIEW",FullName="Xem trước menu",HasAllAreas=role==Position.Manager,LoginAtUtc=DateTime.UtcNow});
        var auth=new Authorizer(current);var runtime=typeof(AppServices).GetNestedType("Runtime",BindingFlags.NonPublic)!;var ctor=runtime.GetConstructors().Single(c=>c.GetParameters().Length>1);
        var values=ctor.GetParameters().Select(p=>p.ParameterType==typeof(CurrentSession)?(object)current:p.ParameterType==typeof(Authorizer)?auth:null).ToArray();typeof(AppServices).GetField("runtime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(app,ctor.Invoke(values));return app;
    }
}
