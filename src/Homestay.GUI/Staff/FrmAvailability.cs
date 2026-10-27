using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;

namespace Homestay.GUI.Staff;
public partial class FrmAvailability : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new();
    private IReadOnlyList<AvailabilityItem> results=[];
    private IReadOnlyList<RoomCalendarRow> calendar=[];
    private RoomCalendar? loadedCalendar;
    private AvailabilitySearchRequest? lastSearch;
    private bool changingHomestays;
    private readonly FlowLayoutPanel legend=new(){Dock=DockStyle.Bottom,AutoSize=true,WrapContents=true};
    public FrmAvailability(){
        InitializeComponent();
        HelpText="Báo giá không giữ phòng. Lịch lọc theo khu vực và homestay. Bấm đúp ô trống để đặt đúng phòng/ngày; ô có đơn để xem chi tiết.";
        Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmAvailability(AppServices services):this(){
        this.services=services;EmployeeUi.Activate(pendingNotice,searchButton,bookButton,priceButton,policyButton);
        blockButton.Visible=false;
        filterPanelField8Label.Text="Homestay trong lịch (trống: tất cả)";
        filterPanelField8.Enabled=false;filterPanelField8.Visible=filterPanelField8Label.Visible=filterPanelField9.Visible=filterPanelField9Label.Visible=false;
        bookButton.Enabled=priceButton.Enabled=policyButton.Enabled=false;
        filterPanelField1.Value=EmployeeUi.Today.ToDateTime(TimeOnly.MinValue);filterPanelField2.Value=filterPanelField1.Value.AddDays(1);
        filterPanelField3.Minimum=1;filterPanelField3.Value=1;filterPanelField3.Maximum=1000;
        filterPanelField9.Items.Clear();filterPanelField9.Items.AddRange([14,31]);filterPanelField9.SelectedIndex=0;
        grid0.SetSortKeys(null,null,null,null,null,null,null,null,null);
        grid0.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F06.search");
        foreach(var state in Enum.GetValues<RoomCalendarState>()){
            legend.Controls.Add(new Label{Text=State(state),BackColor=StateColor(state),AutoSize=true,Padding=new Padding(6),Margin=new Padding(3)});
        }
        tab1.Controls.Add(legend);legend.BringToFront();
        searchButton.Click+=async(_,_)=>await ReloadAsync();
        bookButton.Click+=(_,_)=>{try{
            var item=Selected();if(item.AvailableRooms==0)throw new BusinessException("Loại phòng này đã hết chỗ.");
            services.Navigate("F07",new QuoteRequest(item.RoomTypeId,lastSearch!.CheckIn,lastSearch.CheckOut,lastSearch.Guests));
        }catch(Exception e){ErrorHandler.Show(e,this);}};
        grid0.DetailRequested+=(_,_)=>bookButton.PerformClick();
        priceButton.Click+=async(_,_)=>await ErrorHandler.RunAsync(priceButton,async()=>{
            var item=Selected();var q=await services.Availability.QuoteAsync(new(item.RoomTypeId,lastSearch!.CheckIn,lastSearch.CheckOut,lastSearch.Guests));
            MessageBox.Show(this,string.Join("\n",q.Pricing.Nights.Select(n=>EmployeeUi.Date(n.Date)+": "+EmployeeUi.Money(n.Price))),"Giá từng đêm");
        });
        policyButton.Click+=(_,_)=>{try{
            var p=Selected().CancellationPolicy;
            MessageBox.Show(this,p.Name+"\n"+string.Join("\n",p.Rules.Select(r=>r.MinDaysBefore+" ngày: hoàn "+r.RefundPercent+"%")),"Chính sách hủy");
        }catch(Exception e){ErrorHandler.Show(e,this);}};
        viewTabs.SelectedIndexChanged+=async(_,_)=>{
            var search=viewTabs.SelectedIndex==0;
            bookButton.Visible=priceButton.Visible=policyButton.Visible=search;
            filterPanelField8.Visible=filterPanelField8Label.Visible=filterPanelField9.Visible=filterPanelField9Label.Visible=!search;
            foreach(var control in new Control[]{filterPanelField2,filterPanelField2Label,filterPanelField4,filterPanelField4Label,filterPanelField5,filterPanelField5Label,filterPanelField6,filterPanelField6Label,filterPanelField7,filterPanelField7Label})control.Visible=search;
            filterPanelField3Label.Text=search?"Số khách":"Số khách khi đặt";
            filterPanelField8.Enabled=!search&&loadedCalendar is not null;
            await ReloadAsync();
        };
        filterPanelField0.SelectionChanged+=(_,_)=>{
            MarkSearchDirty();loader.CancelPending();loadedCalendar=null;
            changingHomestays=true;filterPanelField8.SetItems([]);changingHomestays=false;
            filterPanelField8.Enabled=false;
            calendar=[];EmployeeUi.Local(grid1,calendar,_=>[]);
        };
        filterPanelField1.ValueChanged+=(_,_)=>{MarkSearchDirty();InvalidateCalendar();};
        filterPanelField2.ValueChanged+=(_,_)=>MarkSearchDirty();
        foreach(var number in new[]{filterPanelField3,filterPanelField4,filterPanelField5})number.ValueChanged+=(_,_)=>MarkSearchDirty();
        filterPanelField6.SelectionChanged+=(_,_)=>MarkSearchDirty();
        filterPanelField7.ItemCheck+=(_,_)=>MarkSearchDirty();
        filterPanelField9.SelectedIndexChanged+=(_,_)=>InvalidateCalendar();
        filterPanelField8.SelectionChanged+=(_,_)=>{if(!changingHomestays&&loadedCalendar is not null)RenderCalendar();};
        grid1.Grid.CellDoubleClick+=(_,e)=>OpenCalendarCell(e.RowIndex,e.ColumnIndex);
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(searchButton,async()=>{
            filterPanelField0.SetItems((await services.Catalog.ListAreasAsync()).Where(x=>x.IsActive).Select(x=>new Lookup{Id=x.Id,Name=x.Name}));
            filterPanelField6.SetItems((await services.Catalog.ListItemsAsync(CatalogKind.RoomCategory)).Where(x=>x.IsActive).Select(x=>new Lookup{Id=x.Id,Name=x.Name}));
            filterPanelField7.DisplayMember=nameof(Lookup.Name);
            filterPanelField7.Items.AddRange((await services.Catalog.ListItemsAsync(CatalogKind.Amenity)).Where(x=>x.IsActive).Select(x=>(object)new Lookup{Id=x.Id,Name=x.Name}).ToArray());
            await ReloadAsync();
        });
    }
    private void MarkSearchDirty(){if(viewTabs.SelectedIndex==0)loader.CancelPending();bookButton.Enabled=priceButton.Enabled=policyButton.Enabled=false;}
    private void InvalidateCalendar(){
        loader.CancelPending();loadedCalendar=null;calendar=[];filterPanelField8.Enabled=false;grid1.Enabled=false;
        EmployeeUi.Local(grid1,calendar,_=>[]);
    }
    private AvailabilityItem Selected()=>results[(grid0.Query.PageNumber-1)*grid0.Query.PageSize+EmployeeUi.Selected(grid0)];
    private void OpenCalendarCell(int row,int column){
        try{
            if(row<0||column<=0)return;
            var index=(grid1.Query.PageNumber-1)*grid1.Query.PageSize+row;
            if(index>=calendar.Count||column>calendar[index].Cells.Count)return;
            var room=calendar[index];var cell=room.Cells[column-1];
            if(cell.BookingId is int id){services!.Navigate("F09",id);return;}
            if(cell.State!=RoomCalendarState.Available||!services!.Can(Permission.BookingCreate))return;
            if(loadedCalendar is null||cell.Date<DateOnly.FromDateTime(loadedCalendar.ServerNowUtc.AddHours(7)))
                throw new BusinessException("Vui lòng chọn ngày từ hôm nay để tạo đơn.");
            services.Navigate("F07",new BookingDraftSelection(new(room.RoomTypeId,cell.Date,cell.Date.AddDays(1),(int)filterPanelField3.Value),room.RoomId));
        }catch(Exception e){ErrorHandler.Show(e,this);}
    }
    private async Task ReloadAsync(){
        if(services is null)return;
        try{
            if(viewTabs.SelectedIndex==0){
                var r=new AvailabilitySearchRequest{AreaId=filterPanelField0.SelectedItem?.Id,CategoryId=filterPanelField6.SelectedItem?.Id,
                    CheckIn=DateOnly.FromDateTime(filterPanelField1.Value),CheckOut=DateOnly.FromDateTime(filterPanelField2.Value),Guests=(int)filterPanelField3.Value,
                    MinAveragePrice=filterPanelField4.Value==0?null:filterPanelField4.Value,MaxAveragePrice=filterPanelField5.Value==0?null:filterPanelField5.Value,
                    AmenityIds=filterPanelField7.CheckedItems.Cast<Lookup>().Select(x=>x.Id).ToArray()};
                await loader.LoadAsync(ct=>services.Availability.SearchAsync(r,ct),rows=>{
                    results=rows;lastSearch=r;bookButton.Enabled=priceButton.Enabled=policyButton.Enabled=rows.Count>0;EmployeeUi.Local(grid0,rows,x=>[x.HomestayName,x.AreaName,x.RoomTypeName,x.MaxGuests,x.AvailableRooms+" / "+x.TotalActiveRooms,EmployeeUi.Money(x.AveragePrice),EmployeeUi.Money(x.TotalAmount),EmployeeUi.Money(x.DepositAmount),x.CancellationPolicy.Name]);
                });
            }else{
                if(filterPanelField0.SelectedItem is not Lookup area)throw new BusinessException("Vui lòng chọn khu vực để xem lịch.");
                var days=(int)filterPanelField9.SelectedItem!;
                await loader.LoadAsync(ct=>services.Availability.GetCalendarAsync(new(null,area.Id,DateOnly.FromDateTime(filterPanelField1.Value),days),ct),r=>{
                    var selected=filterPanelField8.SelectedItem?.Id;
                    loadedCalendar=r;changingHomestays=true;
                    try{
                        filterPanelField8.SetItems(r.Rooms.GroupBy(x=>x.HomestayId).Select(g=>new Lookup{Id=g.Key,Name=g.First().HomestayName}).OrderBy(x=>x.Name));
                        filterPanelField8.SelectId(selected);
                    }finally{changingHomestays=false;}
                    filterPanelField8.Enabled=true;RenderCalendar();
                });
            }
        }catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private void RenderCalendar(){
        if(loadedCalendar is not {} r)return;
        grid1.Enabled=true;
        var homestay=filterPanelField8.SelectedItem?.Id;
        calendar=r.Rooms.Where(x=>homestay is null||x.HomestayId==homestay).ToArray();
        grid1.SetColumns(new[]{"Homestay / loại / phòng"}.Concat(Enumerable.Range(0,r.Days).Select(i=>r.FromDate.AddDays(i).ToString("dd/MM"))).ToArray());
        EmployeeUi.Local(grid1,calendar,x=>new object?[]{x.HomestayName+" / "+x.RoomTypeName+" / "+x.RoomCode}.Concat(x.Cells.Select(c=>(object?)State(c.State))).ToArray(),ColorCalendar);
        grid1.Grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        foreach(DataGridViewColumn c in grid1.Grid.Columns)c.Width=c.Index==0?260:110;
    }
    private void ColorCalendar(){
        foreach(DataGridViewRow row in grid1.Grid.Rows){
            var room=calendar[(grid1.Query.PageNumber-1)*grid1.Query.PageSize+row.Index];
            for(var i=0;i<room.Cells.Count;i++){
                var c=room.Cells[i];var cell=row.Cells[i+1];
                cell.Style.BackColor=StateColor(c.State);
                cell.ToolTipText=c.BookingCode is null?State(c.State):c.BookingCode+" · "+c.CustomerName+(c.HoldExpiresAtUtc is null?"":" · Giữ đến "+EmployeeUi.Time(c.HoldExpiresAtUtc));
            }
        }
    }
    private static Color StateColor(RoomCalendarState s)=>s switch{
        RoomCalendarState.Available=>Color.Honeydew,RoomCalendarState.Held or RoomCalendarState.ChangeRequestHold=>Color.LemonChiffon,
        RoomCalendarState.Booked=>Color.LightBlue,RoomCalendarState.Occupied=>Color.PaleTurquoise,RoomCalendarState.Stayed=>Color.Lavender,
        RoomCalendarState.MaintenanceBlock=>Color.MistyRose,_=>Color.Gainsboro};
    private static string State(RoomCalendarState s)=>s switch{RoomCalendarState.Available=>"Trống",RoomCalendarState.Held=>"Đang giữ",RoomCalendarState.Booked=>"Đã đặt",RoomCalendarState.Occupied=>"Có khách",RoomCalendarState.Stayed=>"Đã ở",RoomCalendarState.OtherChannelBlock=>"Khóa kênh khác",RoomCalendarState.MaintenanceBlock=>"Bảo trì",RoomCalendarState.ChangeRequestHold=>"Giữ đổi đơn",_=>"Ngừng bán"};
    protected override bool ProcessCmdKey(ref Message msg,Keys k){if(services is not null&&k==Keys.F5){searchButton.PerformClick();return true;}return base.ProcessCmdKey(ref msg,k);}
}
