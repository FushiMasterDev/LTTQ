using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Tasks;
using Homestay.GUI.Staff;
namespace Homestay.GUI.Common;
public sealed record TaskBoardSelection(int? BookingId=null,bool OnlyOverdue=false);
internal sealed class WorkTaskBoard:ScreenForm
{
    private readonly AppServices services;private readonly bool manager;private readonly PagedGrid grid=new(){Dock=DockStyle.Fill};private readonly TextBox search=new(){Width=230,PlaceholderText="Tìm tiêu đề / mô tả / mã đơn"};private readonly ComboBox status=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=160};private readonly CheckBox overdue=new(){Text="Chỉ quá hạn",AutoSize=true};
    private readonly Button reload=new(){Text="Tải lại",AutoSize=true},create=new(){Text="Giao việc",AutoSize=true},edit=new(){Text="Sửa việc",AutoSize=true},cancel=new(){Text="Hủy việc",AutoSize=true},start=new(){Text="Bắt đầu",AutoSize=true},done=new(){Text="Hoàn thành",AutoSize=true};
    private readonly LiveGrid<WorkTaskItem> live;private readonly TaskBoardSelection? selection;
    public WorkTaskBoard(AppServices s,bool managing,TaskBoardSelection? selection=null){this.selection=selection;overdue.Checked=selection?.OnlyOverdue==true;
        services=s;manager=managing;if(!s.Can(managing?Permission.WorkTaskAssign:Permission.OwnWorkTaskManage))throw new ForbiddenException();
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(16)};layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.Controls.Add(new Label{Text=managing?"F19 · Giao việc":"F14 · Việc của tôi",AutoSize=true,Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=UiTheme.Ink,Padding=new Padding(0,8,0,12)},0,0);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};bar.Controls.AddRange([reload,create,edit,cancel,start,done,search,status,overdue]);layout.Controls.Add(bar,0,1);layout.Controls.Add(grid,0,2);Controls.Add(layout);LiveUi.EnumFilter<WorkTaskStatus>(status);
        create.Visible=edit.Visible=cancel.Visible=manager;start.Visible=done.Visible=!manager;grid.SetColumns("Tiêu đề","Người nhận","Mã đơn","Hạn","Ưu tiên","Trạng thái","Kết quả");grid.SetSortKeys("Title",null,null,"DueAtUtc","Priority","Status",null);grid.SetPreferenceIdentity(s.Session!.UserId.ToString(),manager?"F19.tasks":"F14.mine");
        live=new(grid,()=>{var text=search.Text;var state=LiveUi.EnumValue<WorkTaskStatus>(status);var late=overdue.Checked;return(q,ct)=>{var request=new WorkTaskSearchRequest{Page=q with{SearchText=text},Status=state,OnlyOverdue=late};return manager?s.Tasks.ListAssignedAsync(request,ct):s.Tasks.ListMineAsync(request,ct);};},r=>[r.Title,r.AssigneeName,r.BookingCode,EmployeeUi.Time(r.DueAtUtc),LiveUi.Status(r.Priority),LiveUi.Status(r.Status),r.ResultNote]);
        live.Changed+=()=>{var r=live.Current;edit.Enabled=r?.Status==WorkTaskStatus.Open;cancel.Enabled=r?.Status==WorkTaskStatus.Open&&r.CreatedByUserId==services.Session!.UserId;start.Enabled=r?.Status==WorkTaskStatus.Open&&r.StartedAtUtc is null;done.Enabled=r?.Status==WorkTaskStatus.Open;};
        grid.Grid.CellFormatting+=(_,e)=>{if(e.RowIndex>=0&&e.ColumnIndex==3&&live.AtRow(e.RowIndex)?.IsOverdue==true)e.CellStyle!.BackColor=Color.MistyRose;};
        reload.Click+=async(_,_)=>await live.LoadAsync();create.Click+=async(_,_)=>await Write(create,()=>Edit(false));edit.Click+=async(_,_)=>await Write(edit,()=>Edit(true));
        cancel.Click+=async(_,_)=>await Write(cancel,async()=>{var r=live.Selected;var note=LiveUi.Note(this,"Hủy việc "+r.Title);if(note is not null)await s.Tasks.CancelAsync(new(r.Id,note,r.RowVer));});
        start.Click+=async(_,_)=>await Write(start,async()=>{var r=live.Selected;await s.Tasks.StartAsync(new(r.Id,r.RowVer));});
        done.Click+=async(_,_)=>await Write(done,async()=>{var r=live.Selected;var note=LiveUi.Note(this,"Kết quả hoàn thành "+r.Title);if(note is not null)await s.Tasks.CompleteAsync(new(r.Id,note,r.RowVer));});
        Shown+=async(_,_)=>await live.LoadAsync();Disposed+=(_,_)=>live.Dispose();edit.Enabled=cancel.Enabled=start.Enabled=done.Enabled=false;
    }
    private Task Write(Button b,Func<Task> apply)=>RunMutationAsync(b,async()=>{await apply();await live.LoadAsync();},()=>live.LoadAsync(),()=>CanWrite(b));
    private bool CanWrite(Button button){
        if(button==create)return manager;
        var item=live.Current;if(item?.Status!=WorkTaskStatus.Open)return false;
        if(button==cancel)return manager&&item.CreatedByUserId==services.Session!.UserId;
        if(button==edit)return manager;
        if(button==start)return !manager&&item.StartedAtUtc is null;
        return button==done&&!manager;
    }
    private async Task Edit(bool updating){
        var old=updating?await services.Tasks.GetAsync(live.Selected.Id):null;var linked=old?.BookingId??selection?.BookingId;var bookingDetail=linked is int bookingId?await services.Bookings.GetAsync(bookingId):null;var homes=await services.Homestays.ListHomestaysAsync();using var links=new FieldsDialog("Liên kết công việc",new("booking","Mã đơn (không bắt buộc)",Initial:bookingDetail?.Booking.Code,Required:false,MaxLength:30),new("home","Homestay (không bắt buộc)",FieldKind.Lookup,old?.HomestayId,Required:false,Choices:homes.Select(h=>new Choice(h.Id,h.Name)).ToArray()));if(selection?.BookingId is not null)links.Controls.Find("booking",true).Single().Enabled=false;if(links.ShowDialog(this)!=DialogResult.OK)return;var bookingCode=LiveUi.Optional(links,"booking");bookingDetail=bookingCode is null?null:await services.Bookings.GetByCodeAsync(bookingCode);linked=bookingDetail?.Booking.Id;var linkedHome=links.Values["home"] as int?;var people=await services.Tasks.ListAssigneesAsync(linked,linkedHome);var local=old?.DueAtUtc?.AddHours(7);
        using var d=new FieldsDialog(updating?"Sửa việc":"Giao việc mới",new("title","Tiêu đề",Initial:old?.Title,MaxLength:200),new("description","Mô tả",FieldKind.Multiline,old?.Description,Required:false,MaxLength:2000),new("assignee","Người nhận",FieldKind.Lookup,old?.AssigneeEmployeeId,Choices:people.Select(p=>new Choice(p.Id,p.Name)).ToArray()),new("booking","Mã đơn liên quan",Initial:bookingDetail?.Booking.Code,Required:false,MaxLength:30),new("home","Homestay liên quan",FieldKind.Lookup,linkedHome,Required:false,Choices:homes.Select(h=>new Choice(h.Id,h.Name)).ToArray()),new("priority","Ưu tiên",FieldKind.Choice,old?.Priority??WorkTaskPriority.Medium,Choices:Enum.GetValues<WorkTaskPriority>().Select(p=>new Choice(p,LiveUi.Status(p))).ToArray()),new("date","Hạn: ngày (để trống nếu không có hạn)",FieldKind.Date,local is DateTime t?DateOnly.FromDateTime(t):null,Required:false),new("time","Hạn: giờ Việt Nam",FieldKind.Time,local is DateTime time?TimeOnly.FromDateTime(time):new TimeOnly(17,0)));
        d.Controls.Find("booking",true).Single().Enabled=false;d.Controls.Find("home",true).Single().Enabled=false;if(d.ShowDialog(this)!=DialogResult.OK)return;int? booking=linked;var code=LiveUi.Optional(d,"booking");if(booking is null&&code is not null)booking=(await services.Bookings.GetByCodeAsync(code)).Booking.Id;
        var v=new WorkTaskValues{Title=LiveUi.Text(d,"title"),Description=LiveUi.Optional(d,"description"),AssigneeEmployeeId=LiveUi.Id(d,"assignee"),BookingId=booking,HomestayId=d.Values["home"] as int?,Priority=(WorkTaskPriority)d.Values["priority"]!,DueAtVietnam=d.Values["date"] is DateOnly date?DateTime.SpecifyKind(date.ToDateTime((TimeOnly)d.Values["time"]!),DateTimeKind.Unspecified):null};
        if(old is null)await services.Tasks.CreateAsync(v);else await services.Tasks.UpdateAsync(new(old.Id,v,old.RowVer));
    }
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(k==Keys.F5||k==Keys.Enter&&search.Focused){reload.PerformClick();return true;}if(k==(Keys.Control|Keys.F)){search.Focus();return true;}return base.ProcessCmdKey(ref m,k);}
}