using Homestay.GUI.Common;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Manager;

public partial class FrmHomestayProfile : ScreenForm
{
    public FrmHomestayProfile()
    {
        InitializeComponent();
        ListingDraftPreview.Add(toolbar,("Biểu mẫu thông tin",()=>ListingDraftEditors.Homestay([],[])),("Loại phòng",()=>ListingDraftEditors.RoomType([])),("Phòng",()=>ListingDraftEditors.Room([])),("Phiên bản giá",()=>ListingDraftEditors.Price([],EmployeeUi.Today)),("Khoảng khóa phòng",()=>ListingDraftEditors.Block([],EmployeeUi.Today)));
        HelpText = "Sửa địa chỉ/giấy tờ pháp lý có thể đưa hồ sơ về chờ duyệt. Giá đã hiệu lực không sửa. Quyền và điều kiện do hệ thống kiểm; chưa nối các service đợt 4.";
    }
}
