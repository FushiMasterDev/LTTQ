using Homestay.BLL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Homestays;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Manager;
namespace Homestay.GUI.Staff;
public partial class FrmAvailability
{
    private readonly Button unblockButton = new() { Text = "Mở khóa ô đã chọn", AutoSize = true, Enabled = false };
    private void InitializeRoomBlocking()
    {
        toolbar.Controls.Add(unblockButton);
        blockButton.Tag = null;blockButton.Text = "Khóa phòng đã chọn";
        blockButton.Visible = unblockButton.Visible = false;blockButton.Enabled=unblockButton.Enabled=false;
        grid1.Grid.CurrentCellChanged += (_, _) => UpdateBlockButtons();
        blockButton.Click += async (_, _) => await RunMutationAsync(blockButton, async () => {
            var (room, cell) = SelectedCalendarCell();
            var home = await services!.Homestays.GetAsync(room.HomestayId);
            if (!home.AllowedActions.Contains(HomestayAction.BlockRoom)) throw new ForbiddenException();
            using var d = ListingDraftEditors.Block([new() { Id = room.RoomId, Name = room.HomestayName + " / " + room.RoomCode }],home.Approval.Today);
            LiveUi.Fill(d,"room",room.RoomId);LiveUi.Fill(d,"start",cell.Date);LiveUi.Fill(d,"end",cell.Date.AddDays(1));
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            await services.Rooms.BlockAsync(new(room.RoomId,(DateOnly)d.Values["start"]!,(DateOnly)d.Values["end"]!,Enum.Parse<RoomBlockReason>(LiveUi.Text(d,"reason")),LiveUi.Optional(d,"note"),home.Homestay.RowVer));
            await services.Homestays.GetAsync(room.HomestayId);await ReloadAsync();
        },ReloadAsync,CanBlockSelection);
        unblockButton.Click += async (_, _) => await RunMutationAsync(unblockButton, async () => {
            var (room,cell)=SelectedCalendarCell();
            if(cell.RoomBlockId is not int blockId)throw new BusinessException("Chọn một ô đang khóa để mở.");
            var home=await services!.Homestays.GetAsync(room.HomestayId);
            if(!home.AllowedActions.Contains(HomestayAction.BlockRoom))throw new ForbiddenException();
            var note=LiveUi.Note(this,"Mở khóa phòng "+room.RoomCode+" (toàn bộ khoảng khóa)");if(note is null)return;
            await services.Rooms.UnblockAsync(new(blockId,note,home.Homestay.RowVer));
            await services.Homestays.GetAsync(room.HomestayId);await ReloadAsync();
        },ReloadAsync,CanUnblockSelection);
    }
    private (RoomCalendarRow Room,RoomCalendarCell Cell) SelectedCalendarCell()
    {
        var selected=grid1.Grid.CurrentCell;
        if(loadedCalendar is null||selected is null||selected.ColumnIndex<1)throw new BusinessException("Chọn một ô ngày của phòng trên lịch.");
        var index=(grid1.Query.PageNumber-1)*grid1.Query.PageSize+selected.RowIndex;
        if(index<0||index>=calendar.Count)throw new BusinessException("Vui lòng chọn phòng trên lịch.");
        return(calendar[index],calendar[index].Cells[selected.ColumnIndex-1]);
    }
    private bool CanBlockSelection()=>viewTabs.SelectedIndex==1&&loadedCalendar is not null&&services!.Can(Permission.RoomBlockManage)&&grid1.Grid.CurrentCell is {ColumnIndex:>0,RowIndex:>=0};
    private bool CanUnblockSelection(){if(!CanBlockSelection())return false;try{return SelectedCalendarCell().Cell.RoomBlockId is not null;}catch(BusinessException){return false;}}
    private void UpdateBlockButtons(){blockButton.Enabled=CanBlockSelection();unblockButton.Enabled=CanUnblockSelection();}
}
