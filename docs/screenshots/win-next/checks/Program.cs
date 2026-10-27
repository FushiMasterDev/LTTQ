using System.Reflection;
using Homestay.GUI.Common;
using Homestay.GUI.Manager;
using Homestay.GUI.Accountant;
using Homestay.DTO.Common;
using Homestay.DTO.Payments;

internal static class Program{
    static readonly List<string> results=[];
    static string output="";
    [STAThread] static int Main(string[] args){
        ApplicationConfiguration.Initialize();output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        using var runner=new Form{Opacity=0,ShowInTaskbar=false};
        runner.Shown+=(_,_)=>{
            try{Run();results.Add("RESULT PASS");}
            catch(Exception e){results.Add("FAIL "+e);Environment.ExitCode=1;}
            finally{File.WriteAllLines(Path.Combine(output,"WIN-NEXT-CHECKS.txt"),results);runner.Close();}
        };
        Application.Run(runner);foreach(var line in results)Console.WriteLine(line);return Environment.ExitCode;
    }
    static T Input<T>(Form dialog,string key)where T:Control=>dialog.Controls.Find(key,true).OfType<T>().Single();
    static T Field<T>(object value,string key)=>(T)typeof(FieldsDialog).GetField(key,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value)!;
    static void Save(FieldsDialog d)=>Input<Button>(d,"saveButton").PerformClick();
    static void Check(bool test,string text){if(!test)throw new Exception(text);results.Add("PASS "+text);}
    static string Error(FieldsDialog d)=>Field<Label>(d,"validationLabel").Text;
    static void Capture(FieldsDialog d,string name){d.PerformLayout();using var bmp=new Bitmap(d.Width,d.Height);d.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(output,name+".png"));}
    static void Run(){
        var now=new DateTime(2026,10,27,11,20,0,DateTimeKind.Utc);
        using(var d=RefundPaymentEditor.Create(new(){VoucherNo="PC-SYNTHETIC",BookingCode="FJ-SYNTHETIC",Amount=468000,PayerOrPayeeName="Người nhận tổng hợp"},now)){
            d.Show();Input<TextBox>(d,"reference").Text="REF-001";
            Save(d);
            Check(!d.IsDisposed&&d.DialogResult!=DialogResult.OK&&Error(d).Contains("xác nhận"),"Unconfirmed refund stays open with inline explanation");
            Check(Input<TextBox>(d,"reference").Text=="REF-001","Unconfirmed refund preserves already entered transaction reference");
            Capture(d,"F29_UNCONFIRMED_SYNTHETIC");
            Input<CheckBox>(d,"confirmed").Checked=true;Input<TextBox>(d,"reference").Text="Mã giao dịch";Save(d);
            Check(!d.IsDisposed&&Error(d).Contains("ASCII"),"Invalid reference is corrected in the same refund dialog");
            Input<TextBox>(d,"reference").Text="REF-001";Save(d);
            Check(d.Values["confirmed"] is true&&(string)d.Values["reference"]! =="REF-001"&&d.Values["time"] is TimeOnly t&&t==new TimeOnly(18,20),"Confirmed refund returns preserved inputs and Vietnam local time");
        }
        var choices=new[]{new Lookup{Id=1,Name="Hồ sơ tổng hợp"}};
        using(var d=ListingDraftEditors.Contract(choices,new DateOnly(2026,10,27))){
            d.Show();Input<SearchPicker>(d,"homestay").SelectId(1);Input<TextBox>(d,"number").Text="HD-SYNTHETIC";
            Input<TextBox>(d,"end").Text="26/10/2026";Save(d);
            Check(!d.IsDisposed&&Error(d).Contains("sau ngày bắt đầu"),"Contract rejects end before start without closing inputs");
            var rate=Input<NumericUpDown>(d,"commissionPercent");
            Check(rate.Minimum==10&&rate.Maximum==15&&rate.DecimalPlaces==2,"Contract editor presents commission in percent with explicit 10 to 15 bounds");
            Input<TextBox>(d,"end").Text="27/10/2027";rate.Value=12.5m;Save(d);
            Check(d.Values["homestay"] is int id&&id==1&&d.Values["commissionPercent"] is decimal pct&&pct==12.5m,"Contract returns selected lookup ID and decimal percentage without a database write");
        }
        using(var d=ListingDraftEditors.Homestay(choices,choices)){
            d.SetPreviewMode();d.Show();Input<SearchPicker>(d,"owner").SelectId(1);Input<SearchPicker>(d,"area").SelectId(1);
            Input<TextBox>(d,"name").Text="Homestay tổng hợp";Input<TextBox>(d,"address").Text="Địa chỉ tổng hợp";Save(d);
            Check(d.Values["checkin"] is TimeOnly arrive&&arrive==new TimeOnly(14,0)&&d.Values["checkout"] is TimeOnly leave&&leave==new TimeOnly(12,0),"Homestay editor keeps check-in and checkout as typed times");
            Check(!d.IsDisposed&&d.DialogResult!=DialogResult.OK&&Error(d).Contains("Chưa lưu"),"Draft preview validates without reporting a saved homestay");
            Check(!Field<Panel>(d,"viewport").HorizontalScroll.Visible,"Long draft forms wrap guidance and avoid horizontal scrolling");
            Capture(d,"F16_DRAFT_SYNTHETIC");
            d.Size=d.MinimumSize;d.PerformLayout();
            Check(!Field<Panel>(d,"viewport").HorizontalScroll.Visible,"Draft editor remains usable at its minimum window size");
        }
        using(var d=ListingDraftEditors.RoomType(choices)){
            d.Show();Input<SearchPicker>(d,"category").SelectId(1);Input<TextBox>(d,"name").Text="Loại tổng hợp";Input<TextBox>(d,"guests").Text="0";Save(d);
            Check(!d.IsDisposed&&Error(d).Contains("từ 1"),"Room type rejects zero capacity before returning form values");
            Input<TextBox>(d,"guests").Text="2";Save(d);
            Check(d.Values["guests"] is int guests&&guests==2&&d.Values["area"] is null,"Optional room area can stay empty while capacity is typed integer");
        }
        using(var d=ListingDraftEditors.Price(choices,new DateOnly(2026,10,27))){
            d.Show();Input<SearchPicker>(d,"type").SelectId(1);Save(d);
            Check(!d.IsDisposed&&Error(d).Contains("ba mức giá"),"Room price requires all three positive price levels");
            Input<NumericUpDown>(d,"weekday").Value=1234000;Input<NumericUpDown>(d,"weekend").Value=1500000;Input<NumericUpDown>(d,"holiday").Value=1800000;Save(d);
            Check(d.Values["weekday"] is decimal price&&price==1234000,"Room price preserves whole VND amounts without parsing thousands separators");
        }
        using(var d=ListingDraftEditors.Block(choices,new DateOnly(2026,10,27))){
            d.Show();Input<SearchPicker>(d,"room").SelectId(1);Input<TextBox>(d,"end").Text="27/10/2026";Save(d);
            Check(!d.IsDisposed&&Error(d).Contains("sau ngày bắt đầu"),"Room block explains exclusive end date and rejects empty interval");
        }
        using(var d=ListingDraftEditors.Owner()){
            d.Show();Input<TextBox>(d,"name").Text="Chủ tổng hợp";Input<TextBox>(d,"phone").Text="0900000000";Input<TextBox>(d,"email").Text="invalid";Save(d);
            Check(!d.IsDisposed&&Error(d).Contains("Email"),"Owner email error keeps owner draft open");
            Input<TextBox>(d,"email").Clear();Save(d);
            Check(d.Values["name"] is string name&&name=="Chủ tổng hợp"&&d.Values["account"] is string bank&&bank=="","Pending owner draft does not fabricate bank details or an approved status");
        }
        using(var d=ListingDraftEditors.Room(choices)){d.Show();Capture(d,"F16_ROOM_SYNTHETIC");Check(Input<TextBox>(d,"code").MaxLength==20,"Room code respects schema input length");}
    }
}