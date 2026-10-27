using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.DTO.Finance;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Accountant;

public partial class FrmSettlements : ScreenForm
{
    private readonly AsyncLoader loader=new(); private AppServices? services; private IReadOnlyList<SettlementListItem> rows=[];
    public FrmSettlements()
    {
        InitializeComponent();
        HelpText = "Đối soát theo kỳ 1–15 và 16–cuối tháng, căn cứ ngày trả phòng. Kế toán lập/gửi; Quản lý chỉ xem.";Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmSettlements(AppServices services):this(){this.services=services;EmployeeUi.Activate(pendingNotice,reloadButton,createButton,detailButton);createButton.Enabled=services.Session?.Position==Homestay.DTO.Enums.Position.Accountant;grid0.SetColumns("Mã","Homestay","Chủ","Kỳ","Tổng giữ","Hoa hồng","Phải trả","Trạng thái","Ngày gửi","Ngày trả");reloadButton.Click+=async(_,_)=>await ReloadAsync();createButton.Click+=async(_,_)=>await CreateAsync();detailButton.Click+=(_,_)=>OpenDetail();grid0.DetailRequested+=(_,_)=>OpenDetail();Shown+=async(_,_)=>await ReloadAsync();}
    private async Task ReloadAsync(){try{var year=(int)filterPanelField0.Value;var month=int.TryParse(filterPanelField1.Text,out var m)?m:0;var period=filterPanelField2.SelectedIndex+1;await loader.LoadAsync(ct=>services!.Settlements.ListAsync(year,month,period,ct),items=>{rows=items;var table=new System.Data.DataTable();for(var i=0;i<10;i++)table.Columns.Add("Column"+i,typeof(object));foreach(var r in rows)table.Rows.Add(r.Id,r.HomestayName,r.OwnerName,$"{r.Year}/{r.Month:00} · {(r.PeriodNo==1?"1–15":"16–cuối")}",UiFormat.Money(r.Totals.TotalRetained),UiFormat.Money(r.Totals.TotalCommission),UiFormat.Money(r.Totals.PayoutAmount),r.Status,r.SentAtUtc is null?"":UiFormat.Date(r.SentAtUtc.Value),r.PaidAtUtc is null?"":UiFormat.Date(r.PaidAtUtc.Value));grid0.SetPage(table,rows.Count,1);});}catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}}
    private async Task CreateAsync(){using var d=new FieldsDialog("Lập đối soát nháp",new("home","Mã homestay",FieldKind.Integer,Required:true,Minimum:1),new("year","Năm",FieldKind.Integer,Required:true,Minimum:2020,Maximum:2100),new("month","Tháng",FieldKind.Integer,Required:true,Minimum:1,Maximum:12),new("period","Kỳ (1 hoặc 2)",FieldKind.Integer,Required:true,Minimum:1,Maximum:2));if(d.ShowDialog(this)!=DialogResult.OK)return;try{var result=await services!.Settlements.CreateOrRecreateDraftAsync(Convert.ToInt32(d.Values["home"]),Convert.ToInt32(d.Values["year"]),Convert.ToInt32(d.Values["month"]),Convert.ToInt32(d.Values["period"]));if(result.UnfinalizedBookingCodes.Count>0)MessageBox.Show(this,"Bảng nháp đã lập. Chưa thể gửi vì còn đơn chưa chốt: "+string.Join(", ",result.UnfinalizedBookingCodes));await ReloadAsync();services.Navigate("F31",result.Id);}catch(Exception e){ErrorHandler.Show(e,this);}}
    private void OpenDetail(){try{if(grid0.Grid.SelectedRows.Count!=1)throw new InvalidDataException("Chọn một bảng đối soát.");var i=grid0.Grid.SelectedRows[0].Index;services!.Navigate("F31",rows[i].Id);}catch(Exception e){ErrorHandler.Show(e,this);}}
}
