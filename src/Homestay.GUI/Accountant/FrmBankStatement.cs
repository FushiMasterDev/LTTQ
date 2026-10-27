using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Accountant;

public partial class FrmBankStatement : ScreenForm
{
    private readonly AsyncLoader loader=new();
    private AppServices? services;
    private IReadOnlyList<BankTransactionItem> rows=[];
    public FrmBankStatement()
    {
        InitializeComponent();
        HelpText = "Nhập giao dịch ngân hàng thủ công, tự khớp mã đơn hoặc xử lý khớp tay. Giao dịch trùng bị database từ chối.";
        Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmBankStatement(AppServices services):this(){this.services=services;EmployeeUi.Activate(pendingNotice,reloadButton,manualButton,autoButton,matchButton,refundButton,ignoreButton);var canWrite=services.Session?.Position==Homestay.DTO.Enums.Position.Accountant;manualButton.Enabled=autoButton.Enabled=matchButton.Enabled=refundButton.Enabled=ignoreButton.Enabled=canWrite;
        filterPanelField2.Items.AddRange(Enum.GetNames<MatchStatus>().Cast<object>().ToArray());filterPanelField2.SelectedIndex=0;
        grid0.SetColumns("Mã giao dịch","Thời gian","Số tiền","Người chuyển","Nội dung","Đơn khớp","Trạng thái");
        reloadButton.Click+=async(_,_)=>await ReloadAsync();manualButton.Click+=async(_,_)=>await ManualAsync();autoButton.Click+=async(_,_)=>await AutoAsync();matchButton.Click+=async(_,_)=>await MatchAsync();ignoreButton.Click+=async(_,_)=>await IgnoreAsync();refundButton.Click+=async(_,_)=>await RefundAsync();
        Shown+=async(_,_)=>await ReloadAsync();grid0.DetailRequested+=async(_,_)=>await MatchAsync();}
    private DateTime Utc(DateTime local)=>DateTime.SpecifyKind(local.Date.AddHours(-7),DateTimeKind.Utc);
    private async Task ReloadAsync(){try{var status=filterPanelField2.SelectedItem?.ToString();if(status=="Tất cả")status=null;await loader.LoadAsync(ct=>services!.Accounting.ListBankTransactionsAsync(Utc(filterPanelField0.Value),Utc(filterPanelField1.Value).AddDays(1),Enum.TryParse<MatchStatus>(status,out var s)?s:null,filterPanelField3.Text,ct),x=>{rows=x;var table=new System.Data.DataTable();for(var i=0;i<7;i++)table.Columns.Add("Column"+i,typeof(object));foreach(var r in x)table.Rows.Add(r.BankReference,UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(r.TransactionTimeUtc,DateTimeKind.Utc))).ToString("dd/MM/yyyy HH:mm"),UiFormat.Money(r.Amount),r.SenderName,r.TransferContent,r.BookingCode,r.MatchStatus);grid0.SetPage(table,x.Count,1);});}catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}}
    private BankTransactionItem? Selected(){if(grid0.Grid.SelectedRows.Count!=1)return null;var i=grid0.Grid.SelectedRows[0].Index;return i>=0&&i<rows.Count?rows[i]:null;}
    private async Task ManualAsync(){using var d=new FieldsDialog("Nhập giao dịch ngân hàng",new FieldSpec("account","Tài khoản Fuji",FieldKind.Text,Required:true,MaxLength:30),new FieldSpec("reference","Mã giao dịch",FieldKind.Text,Required:true,MaxLength:100),new FieldSpec("amount","Số tiền (VND)",FieldKind.Money,Required:true,Minimum:1),new FieldSpec("time","Thời gian chuyển (dd/MM/yyyy HH:mm)",FieldKind.Text,Required:true,MaxLength:16),new FieldSpec("sender","Tên người chuyển",FieldKind.Text,Required:false,MaxLength:100),new FieldSpec("senderAccount","Tài khoản người chuyển",FieldKind.Text,Required:false,MaxLength:30),new FieldSpec("content","Nội dung chuyển khoản",FieldKind.Multiline,Required:false,MaxLength:500));if(d.ShowDialog(this)!=DialogResult.OK)return;try{if(!DateTime.TryParseExact((string)d.Values["time"]!,"dd/MM/yyyy HH:mm",UiFormat.Vietnamese,System.Globalization.DateTimeStyles.None,out var local))throw new InvalidDataException("Ngày giờ phải theo dd/MM/yyyy HH:mm.");var txTime=DateTime.SpecifyKind(local.AddHours(-7),DateTimeKind.Utc);await services!.Accounting.RecordBankTransactionAsync(new((string)d.Values["account"]!,(string)d.Values["reference"]!,Convert.ToDecimal(d.Values["amount"]),Str(d,"content"),Str(d,"sender"),Str(d,"senderAccount"),txTime));filterPanelField0.Value=local.Date;filterPanelField1.Value=local.Date;await services.Accounting.AutoMatchAsync(txTime.AddSeconds(-1),txTime.AddSeconds(1));await ReloadAsync();}catch(Exception e){ErrorHandler.Show(e,this);}}
    private static string? Str(FieldsDialog d,string key)=>string.IsNullOrWhiteSpace(Convert.ToString(d.Values[key]))?null:Convert.ToString(d.Values[key]);
    private async Task AutoAsync(){try{await services!.Accounting.AutoMatchAsync(Utc(filterPanelField0.Value),Utc(filterPanelField1.Value).AddDays(1));await ReloadAsync();}catch(Exception e){ErrorHandler.Show(e,this);}}
    private async Task MatchAsync(){try{var tx=Selected();if(tx is null)throw new InvalidDataException("Chọn một giao dịch.");using var d=new FieldsDialog("Khớp giao dịch với đơn",new FieldSpec("code","Mã đơn",FieldKind.Text,Required:true,MaxLength:20));if(d.ShowDialog(this)!=DialogResult.OK)return;var b=await services!.Bookings.GetByCodeAsync((string)d.Values["code"]!);await services.Accounting.MatchBankTransactionAsync(new(tx.Id,b.Booking.Id,tx.RowVer));await ReloadAsync();}catch(Exception e){ErrorHandler.Show(e,this);}}
    private async Task IgnoreAsync(){try{var tx=Selected()??throw new InvalidDataException("Chọn một giao dịch.");using var d=new FieldsDialog("Bỏ qua giao dịch",new FieldSpec("reason","Lý do",FieldKind.Multiline,Required:true,MaxLength:500));if(d.ShowDialog(this)!=DialogResult.OK)return;await services!.Accounting.IgnoreBankTransactionAsync(new(tx.Id,tx.RowVer,(string)d.Values["reason"]!));await ReloadAsync();}catch(Exception e){ErrorHandler.Show(e,this);}}
    private async Task RefundAsync(){try{var tx=Selected()??throw new InvalidDataException("Chọn một giao dịch.");using var d=new FieldsDialog("Ghi đã hoàn trả",new FieldSpec("reference","Mã giao dịch hoàn",FieldKind.Text,Required:true,MaxLength:100),new FieldSpec("time","Thời gian hoàn (dd/MM/yyyy HH:mm)",FieldKind.Text,Required:true,MaxLength:16),new FieldSpec("note","Ghi chú",FieldKind.Multiline,MaxLength:500));if(d.ShowDialog(this)!=DialogResult.OK)return;if(!DateTime.TryParseExact((string)d.Values["time"]!,"dd/MM/yyyy HH:mm",UiFormat.Vietnamese,System.Globalization.DateTimeStyles.None,out var local))throw new InvalidDataException("Ngày giờ phải theo dd/MM/yyyy HH:mm.");await services!.Accounting.RecordUnmatchedRefundAsync(new(tx.Id,tx.RowVer,(string)d.Values["reference"]!,DateTime.SpecifyKind(local.AddHours(-7),DateTimeKind.Utc),Str(d,"note")));await ReloadAsync();}catch(Exception e){ErrorHandler.Show(e,this);}}
}
