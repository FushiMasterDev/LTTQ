using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

public partial class FrmAvailability : ScreenForm
{
    public FrmAvailability()
    {
        InitializeComponent();
        HelpText = "Chọn ngày nhận và trả, số khách, khu vực. Lịch 14/31 ngày sẽ lấy trạng thái từ BLL. Chỉ Quản lý được khóa phòng. Chưa nối AvailabilityService/RoomService.";
    }
}
