using Homestay.DTO.Staff;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Staff;
namespace Homestay.GUI.Manager;
public partial class FrmStaff
{
    private LiveGrid<EmployeeSessionItem>? sessions;
    private LiveGrid<EmployeeListItem>? areaMembers;
    private readonly Button areasButton=new(){Text="Gán khu vực",AutoSize=true};
    private readonly CheckBox historySessions=new(){Text="Lịch sử phiên",AutoSize=true};
    private readonly CheckBox allSessions=new(){Text="Mọi nhân viên (phiên mở)",AutoSize=true,Checked=true};
    private readonly Label employeeScope=new(){AutoSize=true,Padding=new Padding(5,8,5,0)};
    private EmployeeListItem? targetEmployee;
    private void InitializeManagerFeatures()
    {
        var s=services!;toolbar.Controls.AddRange([areasButton,historySessions,allSessions,employeeScope]);
        grid1.SetColumns("Mã","Nhân viên","Chức vụ","Trạng thái","Khu vực");grid1.SetSortKeys("EmployeeCode","FullName",null,null,null);
        grid2.SetColumns("Mã","Nhân viên","Máy","Phiên bản","Đăng nhập","Hoạt động cuối","Kết thúc","Lý do kết thúc");
        grid2.SetSortKeys("EmployeeCode",null,"MachineName",null,"LoginAtUtc","LastSeenAtUtc",null,null);
        sessions=new(grid2,()=>{var employee=allSessions.Checked&&!historySessions.Checked?null:targetEmployee?.EmployeeId;var history=historySessions.Checked;return(q,ct)=>history?s.Sessions.ListSessionHistoryAsync(new(){Page=q,EmployeeId=employee},ct):s.Sessions.ListOpenSessionsAsync(new(){Page=q,EmployeeId=employee},ct);},r=>[r.EmployeeCode,r.EmployeeName,r.MachineName,r.AppVersion,EmployeeUi.Time(r.LoginAtUtc),EmployeeUi.Time(r.LastSeenAtUtc),EmployeeUi.Time(r.EndedAtUtc),r.EndReason?.ToString()]);
        areaMembers=new(grid1,()=>{var area=filterPanelField3.SelectedItem?.Id??0;return(q,ct)=>s.Staff.ListByAreaAsync(new(){Page=q,AreaId=area},ct);},r=>[r.EmployeeCode,r.FullName,UiNames.Position(r.Position),UiNames.Employment(r.EmploymentStatus),r.HasAllAreas?"Toàn hệ thống":r.AreaNames]);
        sessions.Changed+=RefreshManagerButtons;areaMembers.Changed+=()=>{if(viewTabs.SelectedTab==tab1){targetEmployee=areaMembers.Current;RefreshManagerButtons();}};
        grid0.Grid.SelectionChanged+=(_,_)=>{targetEmployee=grid0.Grid.SelectedRows.Count==1&&grid0.Grid.SelectedRows[0].Index<rows.Count?Selected():null;RefreshManagerButtons();};
        viewTabs.SelectedIndexChanged+=async(_,_)=>{RefreshManagerButtons();await ReloadManagerTabAsync();};
        filterPanelField3.Visible=filterPanelField3Label.Visible=true;
        filterPanelField3.SelectionChanged+=async(_,_)=>{if(viewTabs.SelectedTab==tab1&&filterPanelField3.SelectedItem is not null)await areaMembers.LoadAsync(grid1.Query with{PageNumber=1});};
        historySessions.CheckedChanged+=async(_,_)=>{allSessions.Enabled=!historySessions.Checked;if(viewTabs.SelectedTab==tab2)await ReloadManagerTabAsync();};
        allSessions.CheckedChanged+=async(_,_)=>{if(viewTabs.SelectedTab==tab2)await ReloadManagerTabAsync();};
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(reloadButton,async()=>{filterPanelField3.SetItems(await s.Homestays.ListAreasAsync());},enabledAfter:()=>true);
        roleButton.Click+=async(_,_)=>await RunMutationAsync(roleButton,async()=>{
            var r=ManagerTarget();using var d=new FieldsDialog("Đổi chức vụ "+r.EmployeeCode,new("position","Chức vụ mới",FieldKind.Choice,r.Position,Choices:UiNames.Positions()),new("reason","Lý do",FieldKind.Multiline));
            d.SetSummary("Chuyển sang Kế toán sẽ trả các đơn đang mở về chưa giao và hủy các việc đang mở. Không thể tự đổi chức vụ hoặc làm mất Quản lý cuối cùng.");
            if(d.ShowDialog(this)==DialogResult.OK){await s.Staff.ChangePositionAsync(new(r.EmployeeId,(Position)d.Values["position"]!,LiveUi.Text(d,"reason"),r.RowVer));await ReloadStaffManagementAsync();}
        },ReloadStaffManagementAsync,()=>CanChangeEmployee());
        resignButton.Click+=async(_,_)=>await RunMutationAsync(resignButton,async()=>{
            var r=ManagerTarget();using var d=new FieldsDialog("Cho nghỉ / tạm nghỉ "+r.EmployeeCode,new("status","Tình trạng",FieldKind.Choice,EmploymentStatus.Resigned,Choices:[new(EmploymentStatus.Resigned,"Nghỉ việc"),new(EmploymentStatus.Suspended,"Tạm nghỉ")]),new("reason","Lý do",FieldKind.Multiline),new("confirm","Tôi xác nhận đã đọc tác động bên dưới",FieldKind.Confirmation));
            d.SetSummary("Đơn đang mở sẽ trả về chưa giao; việc đang mở bị hủy và Quản lý nhận thông báo. Tài khoản bị khóa, các phiên hiện tại bị kết thúc. Không thể tự cho nghỉ hoặc làm mất Quản lý cuối cùng.");
            if(d.ShowDialog(this)==DialogResult.OK){await s.Staff.TerminateEmploymentAsync(new(r.EmployeeId,LiveUi.Text(d,"reason"),r.RowVer){Status=(EmploymentStatus)d.Values["status"]!});await ReloadStaffManagementAsync();}
        },ReloadStaffManagementAsync,CanChangeEmployee);
        areasButton.Click+=async(_,_)=>await RunMutationAsync(areasButton,async()=>{
            var employee=ManagerTarget();var r=await s.Staff.GetAreasAsync(employee.EmployeeId);
            if(r.Position==Position.Accountant)throw new Homestay.BLL.Infrastructure.BusinessException("Kế toán không có phạm vi khu vực.");
            var all=await s.Homestays.ListAreasAsync();var fields=all.Select(a=>new FieldSpec("area"+a.Id,a.Name,FieldKind.Confirmation,r.Areas.Any(x=>x.Id==a.Id),Required:false)).ToList();
            if(r.Position==Position.Manager)fields.Insert(0,new("all","Toàn hệ thống",FieldKind.Confirmation,r.HasAllAreas,Required:false));
            using var d=new FieldsDialog("Gán khu vực "+employee.FullName,fields.ToArray());d.SetSummary("Thay toàn bộ khu vực; có hiệu lực ngay với cả phiên đang đăng nhập.");
            if(d.ShowDialog(this)==DialogResult.OK){await s.Staff.SetAreasAsync(new(r.EmployeeId,all.Where(a=>d.Values["area"+a.Id] is true).Select(a=>a.Id).ToArray(),r.Position==Position.Manager&&d.Values["all"] is true,r.RowVer));await ReloadStaffManagementAsync();}
        },ReloadStaffManagementAsync,()=>targetEmployee is {} selected&&selected.Position!=Position.Accountant);
        endSessionButton.Click+=async(_,_)=>await RunMutationAsync(endSessionButton,async()=>{
            var r=sessions.Selected;var note=LiveUi.Note(this,"Buộc đăng xuất "+r.EmployeeCode+" trên "+r.MachineName);if(note is not null){await s.Sessions.ForceLogoutAsync(new(r.SessionId,note));await sessions.LoadAsync();}
        },()=>sessions.LoadAsync(),()=>CanForceSession());
        Disposed+=(_,_)=>{sessions.Dispose();areaMembers.Dispose();};RefreshManagerButtons();
    }
    private EmployeeListItem ManagerTarget()=>targetEmployee??throw new Homestay.BLL.Infrastructure.BusinessException("Chọn nhân viên trong tab Nhân viên hoặc Khu vực trước.");
    private bool CanChangeEmployee()=>targetEmployee is {} x&&x.EmployeeId!=services!.Session!.EmployeeId;
    private bool CanForceSession()=>viewTabs.SelectedTab==tab2&&sessions?.Current is {} x&&x.EndedAtUtc is null&&x.SessionId!=services!.Session!.SessionId;
    private void RefreshManagerButtons(){
        roleButton.Enabled=resignButton.Enabled=CanChangeEmployee();areasButton.Enabled=targetEmployee is {} selected&&selected.Position!=Position.Accountant;endSessionButton.Enabled=CanForceSession();
        historySessions.Visible=allSessions.Visible=viewTabs.SelectedTab==tab2;
        employeeScope.Text=targetEmployee is {} e?"Đang chọn: "+e.EmployeeCode+" · "+e.FullName:"Chọn nhân viên để xem lịch sử hoặc sửa phạm vi";
    }
    private async Task ReloadManagerTabAsync(){
        if(viewTabs.SelectedTab==tab2){if(historySessions.Checked&&targetEmployee is null){ErrorHandler.Show(new Homestay.BLL.Infrastructure.BusinessException("Chọn nhân viên trước khi xem lịch sử phiên."),this);return;}await sessions!.LoadAsync();}
        else if(viewTabs.SelectedTab==tab1&&filterPanelField3.SelectedItem is not null)await areaMembers!.LoadAsync();
    }
    private async Task ReloadStaffManagementAsync(){var id=targetEmployee?.EmployeeId;targetEmployee=null;RefreshManagerButtons();await ReloadAsync();if(id is int selected){targetEmployee=(await services!.Staff.GetAsync(selected)).Employee;RefreshManagerButtons();}await ReloadManagerTabAsync();}
}
