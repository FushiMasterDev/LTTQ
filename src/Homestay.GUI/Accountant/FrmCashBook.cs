using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.DTO.Finance;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Accountant;

public partial class FrmCashBook : ScreenForm
{
    private readonly AsyncLoader loader=new();private AppServices? services;private IReadOnlyList<CashBookItem> cash=[];private IReadOnlyList<PayableItem> payables=[];
    public FrmCashBook()
    {
        InitializeComponent();
        HelpText = "Sổ chỉ hiển thị tiền đã xác minh; công nợ lấy từ view kế toán. In phiếu dùng số liệu và số tiền bằng chữ.";Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmCashBook(AppServices services):this(){this.services=services;EmployeeUi.Activate(pendingNotice,reloadButton,printButton);grid0.SetColumns("Ngày","Số phiếu","Loại","Đối tượng","Thu","Chi","Mã đơn","Người ghi");grid1.SetColumns("Chủ","Homestay","Đối soát","Số tiền","Ngày gửi");grid2.SetColumns("Khách","Mã đơn","Số phiếu","Số tiền");grid3.SetColumns("Đối tượng","Mã liên quan","Số tiền","Lý do");reloadButton.Click+=async(_,_)=>await ReloadAsync();printButton.Click+=(_,_)=>Print();Shown+=async(_,_)=>await ReloadAsync();}
    private static DateTime Utc(DateTime x)=>DateTime.SpecifyKind(x.Date.AddHours(-7),DateTimeKind.Utc);
    private async Task ReloadAsync(){try{await loader.LoadAsync(async ct=>{var from=Utc(filterPanelField0.Value);var to=Utc(filterPanelField1.Value).AddDays(1);var ledger=await services!.Accounting.ListCashBookAsync(from,to,ct);var debts=await services.Accounting.ListPayablesAsync(ct);return(ledger,debts);},x=>{cash=x.ledger;payables=x.debts;var a=new System.Data.DataTable();for(var i=0;i<8;i++)a.Columns.Add("Column"+i,typeof(object));foreach(var r in cash)a.Rows.Add(UiFormat.Date(r.EntryAtUtc),r.ReferenceNo,r.EntryType,r.PartyName,UiFormat.Money(r.IncomeAmount),UiFormat.Money(r.ExpenseAmount),r.BookingId?.ToString(),r.RecordedByUsername);grid0.SetPage(a,cash.Count,1);EmployeeUi.Local(grid1,payables.Where(p=>p.PayableType=="OwnerPayout").ToArray(),p=>[p.PartyName,p.HomestayId,p.ReferenceNo,UiFormat.Money(p.Amount),UiFormat.Date(p.PayableAtUtc)]);EmployeeUi.Local(grid2,payables.Where(p=>p.PayableType=="Refund").ToArray(),p=>[p.PartyName,p.BookingId,p.ReferenceNo,UiFormat.Money(p.Amount)]);EmployeeUi.Local(grid3,payables.Where(p=>p.PayableType is not ("OwnerPayout" or "Refund")).ToArray(),p=>[p.PartyName,p.ReferenceNo,UiFormat.Money(p.Amount),p.Note]);});}catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}}
    private static void SetPayableGrid(PagedGrid grid,IEnumerable<PayableItem> values){var rows=values.ToArray();var t=new System.Data.DataTable();for(var i=0;i<5;i++)t.Columns.Add("Column"+i,typeof(object));foreach(var x in rows)t.Rows.Add(x.PartyName,x.ReferenceNo,UiFormat.Money(x.Amount),x.Note,UiFormat.Date(x.PayableAtUtc));grid.SetPage(t,rows.Length,1);}
    private void Print(){try{if(viewTabs.SelectedTab==tab0){if(grid0.Grid.SelectedRows.Count!=1)throw new InvalidDataException("Chọn một phiếu thu hoặc chi.");var r=cash[grid0.Grid.SelectedRows[0].Index];var amount=r.IncomeAmount>0?r.IncomeAmount:r.ExpenseAmount;DocumentPrinter.Preview(this,new PrintContent(r.IncomeAmount>0?"PHIẾU THU":"PHIẾU CHI",[$"Số phiếu: {r.ReferenceNo}",$"Ngày: {UiFormat.Date(r.EntryAtUtc)}",$"Đối tượng: {r.PartyName}",$"Mã đơn: {r.BookingId?.ToString()??"—"}",$"Số tiền: {UiFormat.Money(amount)}",$"Viết bằng chữ: {Homestay.BLL.Rules.VietnameseAmountWords.Spell(amount)}"],["Nội dung"],[new[]{r.Note??r.EntryType}],$"Người ghi: {r.RecordedByUsername}"));return;}throw new InvalidDataException("Chọn một dòng trong tab Sổ thu chi để in chứng từ.");}catch(Exception e){ErrorHandler.Show(e,this);}}
}
