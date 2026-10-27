using System.Reflection;
using Homestay.GUI;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Staff;
using Homestay.DTO.Common;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
internal static class Program {
    static readonly List<string> results=[];
    [STAThread] static int Main(string[] args) {
        ApplicationConfiguration.Initialize();
        using var runner=new Form{ShowInTaskbar=false,Opacity=0,Width=10,Height=10};
        runner.Shown+=async(_,_)=>{
            try {await Run();results.Add("RESULT PASS");}
            catch(Exception e){results.Add("FAIL "+e);Environment.ExitCode=1;}
            finally {File.WriteAllLines(args[0],results);runner.Close();}
        };
        Application.Run(runner);foreach(var line in results)Console.WriteLine(line);return Environment.ExitCode;
    }
    static void Check(bool value,string name){if(!value)throw new Exception(name);results.Add("PASS "+name);}
    static T Field<T>(object form,string name)=>(T)form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
    static void Set(object form,string name,object? value)=>form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,value);
    static void Invoke(object form,string method)=>form.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null);
    static async Task Run(){
        using(var trigger=new Button()){
            await ErrorHandler.RunAsync(trigger,()=>Task.CompletedTask,enabledAfter:()=>false);
            Check(!trigger.Enabled,"Business eligibility stays disabled after asynchronous operation");
            trigger.Enabled=true;await ErrorHandler.RunAsync(trigger,()=>Task.CompletedTask);
            Check(trigger.Enabled,"Ordinary asynchronous trigger is restored");
        }
        using(var form=new MutationForm()){
            form.Show();var gate=new TaskCompletionSource();
            var running=form.Run(async()=>await gate.Task,()=>false);
            form.Close();Check(!form.IsDisposed&&form.Visible,"Cannot close a form while its write is unfinished");
            var calls=0;await form.Run(()=>{calls++;return Task.CompletedTask;});
            Check(calls==0,"Concurrent mutation in the same form is rejected");
            gate.SetResult();await running;
            Check(!form.Trigger.Enabled,"Mutation completion preserves current allowed action");
            form.Close();Check(form.IsDisposed,"Form can close after write completion");
        }
        using(var form=new MutationForm()){
            form.Show();await form.Run(()=>{form.Complete();return Task.CompletedTask;});
            Check(form.IsDisposed,"Successful receipt-style operation can close intentionally");
        }
        using(var loader=new AsyncLoader()){
            var delayed=new TaskCompletionSource<int>();var applied=0;
            var first=loader.LoadAsync(_=>delayed.Task,n=>applied=n);
            loader.CancelPending();delayed.SetResult(1);await first;
            Check(applied==0,"Changing booking input invalidates an old quote even when cancellation is ignored");
            var stale=new TaskCompletionSource<int>();
            first=loader.LoadAsync(_=>stale.Task,n=>applied=n);
            await loader.LoadAsync(_=>Task.FromResult(2),n=>applied=n);
            stale.SetResult(1);await first;
            Check(applied==2,"Newest search wins over a slower old response");
            var disposed=new TaskCompletionSource<int>();first=loader.LoadAsync(_=>disposed.Task,n=>applied=n);
            loader.Dispose();loader.Dispose();disposed.SetResult(3);await first;
            Check(applied==2,"Closing form discards pending results and repeated dispose is safe");
        }
        using(var form=new FrmBookingCreate()){
            Set(form,"quote",new BookingQuote(null!,null!,100,null,[],null!));
            Set(form,"quoteRequest",new QuoteRequest(1,new(2026,10,28),new(2026,10,29),1));
            Field<Button>(form,"saveButton").Enabled=true;
            foreach(var name in new[]{"filterPanelField1","filterPanelField2","filterPanelField3","filterPanelField4","filterPanelField5"})Field<TextBox>(form,name).Text="old quote";
            Field<SearchPicker>(form,"editorFieldsField4").SetItems([new Lookup{Id=1,Name="old room"}]);
            Field<SearchPicker>(form,"editorFieldsField4").SelectId(1);
            Invoke(form,"InvalidateQuote");
            Check(!Field<Button>(form,"saveButton").Enabled,"Saving requires a current quote");
            Check(Field<SearchPicker>(form,"editorFieldsField4").SelectedItem is null,"Old assigned room is cleared with invalid quote");
            Check(Field<TextBox>(form,"filterPanelField1").Text=="","Old quote totals disappear after input changes");
            Check(Field<BookingQuote?>(form,"quote") is null&&Field<QuoteRequest?>(form,"quoteRequest") is null,"Invalidation clears quote and its input snapshot");
            Set(form,"created",new BookingDetail{Booking=new BookingListItem{Code="SYNTHETIC"}});
            Field<TextBox>(form,"filterPanelField1").Text="committed total";
            Invoke(form,"InvalidateQuote");
            Check(Field<TextBox>(form,"filterPanelField1").Text=="committed total","Committed booking cannot be reset as a fresh quote");
            form.Dispose();form.Dispose();
        }
        using(var form=new ShortcutForm()){
            form.Show();form.Grid.SetColumns("Code");
            form.Grid.SetPage(new PagedResult<int>{Items=[1],TotalCount=1,PageNumber=1,PageSize=10},x=>[x]);
            form.Grid.Grid.Rows[0].Selected=true;
            var opened=0;form.Grid.DetailRequested+=(_,_)=>opened++;
            Check(form.Key(Keys.F2)&&opened==1,"F2 opens the selected row using the existing detail action");
            form.Filter.Text="query";Check(form.Key(Keys.Control|Keys.F)&&form.Filter.Focused,"Ctrl+F focuses the visible filter");
            form.Close();
        }
        using(var form=new FrmBookingCreate()){form.Show();form.Close();form.Dispose();}
        Check(true,"F07 Close then repeated Dispose regression");
    }
}
internal sealed class MutationForm:ScreenForm{
    public Button Trigger{get;}=new(){Text="Save"};
    public MutationForm(){var root=new Panel{Dock=DockStyle.Fill};Controls.Add(root);root.Controls.Add(Trigger);}
    public Task Run(Func<Task> action,Func<bool>? enabled=null)=>RunMutationAsync(Trigger,action,enabledAfter:enabled);
    public void Complete()=>CloseAfterMutation();
}
internal sealed class ShortcutForm:ScreenForm{
    public PagedGrid Grid{get;}=new(){Dock=DockStyle.Fill};
    public TextBox Filter{get;}=new(){Name="filterPanelField0",Dock=DockStyle.Top};
    public ShortcutForm(){Controls.Add(Grid);Controls.Add(Filter);}
    public bool Key(Keys key){var msg=new Message();return base.ProcessCmdKey(ref msg,key);}
}
